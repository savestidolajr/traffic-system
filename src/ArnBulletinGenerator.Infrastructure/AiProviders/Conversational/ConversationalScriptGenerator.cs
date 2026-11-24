using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Contracts;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;

/// <summary>
///     Runs the conversational bulletin workflow behind the deployed generator seam.
///     <para>
///         The workflow is not a drop-in generator: it is a writer that already owns its own review
///         and length loops, where <see cref="IScriptGenerator" /> is expected to return a draft and
///         let <c>ScriptGenerationEngine</c> do both. Both arrangements converge, so the engine's
///         LLM review is turned off for this path via <c>Broadcast:MaxReviewRefineRetries</c> rather
///         than being duplicated. The engine's DETERMINISTIC gates stay on: they are grounding and
///         safety, not a second opinion.
///     </para>
///     <para>
///         Everything after drafting belongs to the engine and is deliberately not reimplemented
///         here — bookends, filler, pronunciation, ceiling trim, TTS retiming, safety, fallback and
///         persistence. That is most of the surface area, and none of it needed to change.
///     </para>
/// </summary>
public sealed class ConversationalScriptGenerator(
    IWorkflowLlm llm,
    IDurationPredictor predictor,
    IDurationOracle fallbackOracle,
    ILlmCallCapture capture,
    BroadcastOptions options,
    ILogger<ConversationalScriptGenerator> logger) : IScriptGenerator
{
    public Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken = default) =>
        GenerateScriptAsync(context, 0, VoiceSettings.Default, null, cancellationToken);

    public Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        double syllablesPerSecond,
        VoiceSettings settings,
        CancellationToken cancellationToken = default) =>
        GenerateScriptAsync(context, syllablesPerSecond, settings, null, cancellationToken);

    public async Task<Result<DraftedScript>> GenerateScriptAsync(
        BroadcastWithTraffic context,
        double syllablesPerSecond,
        VoiceSettings settings,
        string? promptTemplate,
        CancellationToken cancellationToken = default)
    {
        // Built per request, not injected, because the oracle is voice-specific: the same word
        // count is a different number of seconds on a 190 wpm voice than on a 148 wpm one, and the
        // workflow has to aim at the target the engine will grade it against.
        IDurationOracle duration = CreateDurationOracle(context);

        var workflow = new ConversationalBulletinWorkflow(llm, duration);

        WorkflowRequest request = BuildRequest(context, duration);

        int requestWords = request.SlotWords > 0
            ? request.SlotWords
            : duration.WordsForSeconds(request.TargetSeconds);
        logger.LogInformation(
            "[Conversational] {Location} body target {Target:F1}s ({Words} words), {Evidence} evidence unit(s)",
            context.Location, request.TargetSeconds, requestWords, request.Evidence.Count);

        WorkflowOutcome outcome = await workflow.RunAsync(request, cancellationToken).ConfigureAwait(false);

        await CaptureCallsAsync(context.JobId, outcome, cancellationToken).ConfigureAwait(false);

        if (!outcome.IsSuccess || string.IsNullOrWhiteSpace(outcome.Script))
            return Result<DraftedScript>.Failure(outcome.Error ?? "Conversational workflow returned no script.");

        // An out-of-band or noted outcome still produced a read the engine can fit and air. It is
        // recorded rather than failed: the engine's own gates decide whether it ships, and failing
        // here would hand a market the filler cart over a read that only missed its slot.
        if (!string.Equals(outcome.Trace.Outcome, "succeeded", StringComparison.Ordinal))
            logger.LogWarning("[Conversational] {Location} returned {Outcome} after {Calls} LLM call(s)",
                context.Location, outcome.Trace.Outcome, outcome.Trace.LlmCallCount);

        return Result<DraftedScript>.Success(
            new DraftedScript(outcome.Script, ToDraftedSentences(outcome))
            {
                ComposerThreadId = outcome.ComposerThreadId
            });
    }

    public async Task<Result<DraftedScript>> ReviseForMeasuredDurationAsync(
        BroadcastWithTraffic context,
        string composerThreadId,
        string currentRead,
        double measuredSeconds,
        double syllablesPerSecond,
        VoiceSettings settings,
        CancellationToken cancellationToken = default)
    {
        IDurationOracle duration = CreateDurationOracle(context);
        var workflow = new ConversationalBulletinWorkflow(llm, duration);
        WorkflowRequest request = BuildRequest(context, duration);
        WorkflowOutcome outcome = await workflow.ReviseForMeasuredDurationAsync(
            request, composerThreadId, currentRead, measuredSeconds, cancellationToken)
            .ConfigureAwait(false);

        await CaptureCallsAsync(context.JobId, outcome, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess || string.IsNullOrWhiteSpace(outcome.Script))
            return Result<DraftedScript>.Failure(
                outcome.Error ?? "Composer duration revision returned no script.");

        return Result<DraftedScript>.Success(
            new DraftedScript(outcome.Script, ToDraftedSentences(outcome))
            {
                ComposerThreadId = outcome.ComposerThreadId
            });
    }

    private IDurationOracle CreateDurationOracle(BroadcastWithTraffic context) =>
        new CalibratedDurationOracle(
            predictor,
            new PredictionContext
            {
                VoiceId = context.VoiceId.GetValueOr(options.DefaultVoiceId),
                ModelId = context.Model.GetValueOr(string.Empty),
                Speed = context.VoiceSettings.HasValue ? context.VoiceSettings.Value.Speed : 1.0
            },
            fallbackOracle);

    /// <summary>
    ///     Translates the broadcast context into the workflow's request.
    ///     <para>
    ///         The word target is taken from the engine, never recomputed. The engine has already
    ///         subtracted the station's bookend cost and applied the voice's calibrated rate
    ///         (<c>ScriptGenerationEngine.cs:102-200</c>); deriving a second target from raw slot
    ///         seconds here would spend the bookend's words twice.
    ///     </para>
    /// </summary>
    internal static WorkflowRequest BuildRequest(BroadcastWithTraffic context, IDurationOracle duration)
    {
        double targetSeconds = context.TargetDurationSeconds;

        var requiredSections = new List<string>(2);
        if (context.Content.RequiresWeather)
            requiredSections.Add("weather");
        if (context.Content.RequiresFuel)
            requiredSections.Add("fuel");

        return new WorkflowRequest(
            context.JobId,
            context.MarketId.HasValue ? context.MarketId.Value : string.Empty,
            context.StationId.HasValue ? context.StationId.Value : string.Empty,
            context.Location,
            targetSeconds,
            // mirrorProseSelection: false - this pipeline renders no prose, so there is nothing for
            // the evidence to mirror. The tier budget exists to keep matrix citations resolvable
            // against its own bullets; applied here it just shortlists the board before the desks,
            // whose whole job is choosing from it. Cairns job 39b22098: 17 corridors fetched, 5 shown.
            TrafficNarrativeSummarizer.BuildEvidenceUnits(context, mirrorProseSelection: false),
            requiredSections)
        {
            ProgressReporter = context.ProgressReporter,
            SlotWords = context.TargetWordCountOverride.GetValueOr(0),
            PreviousScript = context.PreviousScript.HasValue ? context.PreviousScript.Value : null,
            Policy = StationPolicyFor(context),
            SegmentOrder = context.Content.EffectiveSegmentOrder.Select(segment => segment.ToString()).ToList(),
            TrafficOrder = context.Content.TrafficOrder,
            FutureLessons = context.FutureLessons
        };
    }

    /// <summary>
    ///     Carries ARN's console-authored voice configuration into the workflow.
    ///     <para>
    ///         Routed through <see cref="StationPolicy" /> rather than the request's legacy
    ///         <c>HouseStyle</c> and <c>RoadFeelPalette</c> fields, which the conversational stages
    ///         never read: they consult only <see cref="WorkflowRequest.EffectivePolicy" />, so a
    ///         value passed on those two is discarded silently.
    ///     </para>
    ///     <para>
    ///         <c>WriterPrompt</c> is deliberately NOT sent. It is the structured matrix's own
    ///         kernel, assembled from the console's writer sections, and shipping it here made it a
    ///         second voice with asserted precedence over <c>compose2.md</c> — the exact failure
    ///         <c>MarketContentFetcher.ResolveSurface</c> documents for seeds. Measured on a dev
    ///         capture 2026-08-26: it added a <c>magnitudeTier</c>/<c>voiceability</c> field legend
    ///         for a data shape the composer never receives, told it to "lead with the worst item,
    ///         in the order supplied" when the writer's briefs had already set the order, referred
    ///         to a matrix "fill order" that does not exist on this path, and leaked a raw HTML
    ///         comment into the prompt. Only surfaces that describe HOW something is said belong
    ///         here; a whole rival kernel does not.
    ///     </para>
    /// </summary>
    internal static StationPolicy StationPolicyFor(BroadcastWithTraffic context)
    {
        // NO EDITORIAL SURFACES ARE SENT. SharedRules, RoadFeelPalette, IncidentVoicing,
        // WeatherVoicing and FuelVoicing are the STRUCTURED MATRIX's ARN-editable surfaces, and on
        // this path they were a second editable voice competing with compose2.md - the same rival
        // kernel this class already refuses to ship WriterPrompt for, just smaller.
        //
        // The road-feel bands were the one surface doing real work, so they were not dropped: they
        // now live in compose2.md as the voice.road-feel-bands block, editable in the playbook
        // studio, which is this pipeline's own single surface. Measured on the captured prompt from
        // job 1f93189b, 10 runs per arm: severe wording on a one-to-three-minute board went 7/10
        // injected to 0/10 with the bands in the playbook and nothing injected. Injection was
        // actively worse - the palette arrived framed as "outranks your instincts", which primed
        // the very vocabulary it was listing.
        var voice = new VoicePolicy();

        var editorial = new EditorialPolicy
        {
            MaxStories = context.EditorialSettings.MaxCorridors ?? 0
        };

        // A station that configured neither must not look configured: StationPolicy.Empty is what
        // the stages check for when deciding whether to render a policy block at all. The playbook
        // is checked INDEPENDENTLY of the structured settings — a station whose only configuration
        // is a playbook edit previously fell into the Empty branch here and had that edit silently
        // dropped, which is the same dead-wire failure class as the unrendered future lessons.
        var playbooks = context.Playbook is { } overlay
            ? new Dictionary<string, PlaybookOverlay>(StringComparer.OrdinalIgnoreCase)
            {
                ["compose2"] = overlay
            }
            : new Dictionary<string, PlaybookOverlay>(StringComparer.OrdinalIgnoreCase);

        return voice.IsEmpty && editorial.IsEmpty && playbooks.Count == 0
            ? StationPolicy.Empty
            : new StationPolicy(editorial, voice) { Playbooks = playbooks };
    }

    /// <summary>
    ///     Maps composed sentences onto the deployed sentence contract.
    ///     <para>
    ///         Variants are empty by design rather than by omission: the authored-variant fitter
    ///         exists to hit a slot without another model call, and this workflow already converged
    ///         on length before returning. <c>SlotFitVariantSelector</c> reports Unavailable and the
    ///         engine falls through to its deterministic trim, which is the correct behaviour when
    ///         no alternates were authored.
    ///     </para>
    ///     <para>
    ///         Corridor keys are left to <c>ScriptProvenanceMapper</c>, which derives them from the
    ///         aired words against canonical road names. Attribution taken from what was said beats
    ///         attribution the model asserted, and it is the same path the deployed pipeline already
    ///         runs for every script.
    ///     </para>
    /// </summary>
    private static IReadOnlyList<DraftedSentence> ToDraftedSentences(WorkflowOutcome outcome) =>
        [.. outcome.Sentences.Select(sentence =>
            new DraftedSentence(sentence.Order, sentence.Text, sentence.EvidenceIds) { Section = sentence.Section })];

    /// <summary>
    ///     Writes the workflow's LLM calls to the same blob capture the matrix uses.
    ///     <para>
    ///         Without this the only capture on a conversational job is the engine's safety call,
    ///         and every existing diagnostic — <c>arn job captures</c>, <c>export-prompts.py</c>,
    ///         <c>arn prompt tune</c> — reports an empty draft. The workflow already keeps the full
    ///         system prompt, user prompt, response and timing per call in its trace; nothing new is
    ///         recorded here, it is only moved somewhere durable and already tooled.
    ///     </para>
    ///     <para>
    ///         Stage names are the workflow's own roles rather than the matrix's Draft/Review, so a
    ///         capture set answers which writer produced a job without cross-referencing anything.
    ///         Attempt is the trace iteration, which is what makes a recompose distinguishable from
    ///         the first pass.
    ///     </para>
    ///     <para>
    ///         Capture is a diagnostic. A failure here must never cost a bulletin that generated
    ///         cleanly, so it is logged and swallowed.
    ///     </para>
    /// </summary>
    private async Task CaptureCallsAsync(
        string? jobId,
        WorkflowOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var evt in outcome.Trace.Events.Where(e => e.LlmCall is not null))
            {
                var call = evt.LlmCall!;
                await capture.CaptureAsync(
                    new Domain.Models.LlmCallRecord(
                        jobId,
                        Stage: call.Role,
                        Attempt: call.Attempt > 0 ? call.Attempt : Math.Max(1, evt.Iteration),
                        Model: call.Model,
                        SystemInstructions: call.SystemPrompt,
                        RenderedPrompt: call.UserPrompt,
                        // The transport builds its own body from these two, so re-serialising it
                        // here would store a guess. The prompts above are the request.
                        RequestBody: string.Empty,
                        ResponseBody: call.RawResponse,
                        ElapsedMs: (long)call.DurationMs,
                        TimestampUtc: evt.At,
                        Usage: call.PromptTokens is null
                            && call.CompletionTokens is null
                            && call.CachedInputTokens is null
                            && call.ReasoningTokens is null
                            ? null
                            : new Domain.Models.LlmTokenUsage(
                                InputTokens: call.PromptTokens ?? 0,
                                CachedInputTokens: call.CachedInputTokens ?? 0,
                                OutputTokens: call.CompletionTokens ?? 0,
                                ReasoningTokens: call.ReasoningTokens ?? 0),
                        Error: call.Error,
                        StatusCode: call.StatusCode,
                        ViaFallback: call.ViaFallback,
                        DeadlineUtc: call.DeadlineUtc,
                        ResponseId: call.ResponseId),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "[Conversational] Could not capture workflow LLM calls for job {JobId}", jobId);
        }
    }
}
