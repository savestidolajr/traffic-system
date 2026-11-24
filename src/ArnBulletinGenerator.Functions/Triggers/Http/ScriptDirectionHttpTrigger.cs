using System.Text;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Returns the DIRECTED TEXT for a draft so an operator can iterate on delivery before
///     spending a re-voice.
///
///     <see cref="IScriptDirector" /> has always been able to do this — it adds v3 audio tags,
///     capitalisation and ellipses to a plain script without changing a single word — but it only
///     ever ran deep inside synthesis, where its output was invisible. An operator could hear a
///     style preview and nothing more. This exposes the same agent as a tool: describe the read in
///     plain English, get the tagged text back, edit it, audition it, then re-voice.
///
///     Words are guaranteed unchanged. The director is fail-open by contract, and this endpoint
///     re-checks <see cref="DeliveryTags.WordsPreserved" /> before returning, so the caller can
///     never receive text that would be refused at synthesis.
/// </summary>
public sealed class ScriptDirectionHttpTrigger(
    IJobRepository jobRepository,
    IMarketAccessService marketAccess,
    IScriptDirector scriptDirector,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    ILogger<ScriptDirectionHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null)
{
    private readonly IInteractiveWorkGate _interactiveWorkGate =
        interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance;
    private const int MaxDirectChars = 3000;

    /// <summary>
    ///     NO tags by default - punctuation only.
    ///
    ///     Every audio tag that measurably works on our voices inserts a non-speech artefact: a
    ///     sigh, a breath, a throat clear, or a mechanically abrupt pause. A newsreader does none
    ///     of those, and on a fatality they read worse than a flat delivery. The tags that WOULD
    ///     express tone ([sad], [solemn], [softly]) do nothing on a designed voice.
    ///
    ///     So the caller must ask for tags explicitly and name them. Omitted or empty means the
    ///     director works with ellipses, pacing and capitalisation alone (decided 2026-08-03).
    /// </summary>
    private static readonly string[] NoTags = [];

    [Function("DirectScript")]
    public async Task<HttpResponseData> Direct(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/direct")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        var cancellationToken = ctx.CancellationToken;
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, cancellationToken) is { } forbidden)
            return forbidden;

        var parsed = await HttpRequestParser.ParseAsync<DirectScriptRequestDto>(req, cancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);
        var dto = parsed.Value;

        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, $"Job '{jobId}' not found.");
        var job = jobResult.Value;

        // The caller may direct an unsaved draft; fall back to what the job aired.
        string plain = DeliveryTags.Strip(
            string.IsNullOrWhiteSpace(dto.Text)
                ? job.ScriptText.GetValueOr(string.Empty)
                : dto.Text!);
        if (string.IsNullOrWhiteSpace(plain))
            return await HttpResponseHelper.BadRequestAsync(req, "There is no script to direct.");
        if (plain.Length > MaxDirectChars)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Script must be at most {MaxDirectChars} characters to direct.");
        if (dto.Intent is { Length: > ConfigValidationLimits.MaxDeliveryIntentLength })
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Intent must be at most {ConfigValidationLimits.MaxDeliveryIntentLength} characters to direct.");
        if (string.IsNullOrWhiteSpace(dto.Intent) && string.IsNullOrWhiteSpace(dto.Tone))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Describe the read you want - for example \"grave, slow, the fatality is the lead\".");

        var tags = dto.Tags is { Count: > 0 } ? dto.Tags.ToArray() : NoTags;
        var policy = new DeliveryStylePolicy(
            dto.Tone ?? dto.Intent ?? string.Empty,
            tags,
            Math.Clamp(dto.Intensity ?? 3, DeliveryStylePolicy.MinIntensity, DeliveryStylePolicy.MaxIntensity));

        string model = job.VoiceConfig.Match(v => v.Model, () => elevenLabsOptions.Value.ModelId);
        var key = InteractiveScriptWorkKeys.ForJob(
            req, job, "direct", plain, model,
            $"{policy.ToneDescription}|{policy.Intensity}|{string.Join(",", policy.AllowedTags)}|{dto.Intent}",
            style: dto.Tone);
        var directed = await _interactiveWorkGate.ExecuteLlmAsync(
            key,
            gateCancellationToken => scriptDirector.DirectAsync(
                plain, policy, dto.Intent, model, jobId, gateCancellationToken),
            cancellationToken);
        if (directed.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, directed.Error);

        string text = directed.Value;
        // The director is fail-open, so "unchanged" is a legitimate outcome, not an error - but
        // never hand back something synthesis would refuse.
        bool preserved = DeliveryTags.WordsPreserved(plain, text);
        if (!preserved)
        {
            logger.LogWarning("[Direct] Job {JobId} director altered the words; returning the plain read.", jobId);
            text = plain;
        }

        int tagCount = DeliveryTags.Extract(text).Count;
        logger.LogInformation("[Direct] Job {JobId} directed with {Tags} tag(s) (intensity {Intensity})",
            jobId, tagCount, policy.Intensity);

        return await HttpResponseHelper.OkJsonAsync(req,
            new DirectScriptResponseDto(text, tagCount, !string.Equals(text, plain, StringComparison.Ordinal)));
    }
}

/// <summary>
///     Rewrites a draft into a register the operator described in one sentence, and returns the
///     text for review. It does NOT re-voice: the operator reads the rewrite, edits it, auditions
///     it, then re-voices when happy. Separating those steps is the point — this changes the words,
///     so a human sees them before they can air, and the safety reviewer sees them on submit.
///
///     The station's own top-and-tail (bookend lines, filler statements) is peeled off first and
///     put back verbatim — see <see cref="StationLineSplitter" />. A read direction is about how the
///     TRAFFIC is read; a station's fixed promo is not the writer's to re-register.
/// </summary>
public sealed class ScriptRetoneHttpTrigger(
    IJobRepository jobRepository,
    IMarketAccessService marketAccess,
    IDeliveryWriter deliveryWriter,
    IVoiceCalibrationService voiceCalibration,
    IOptions<BroadcastOptions> broadcastOptions,
    IOptions<SpeechDurationOptions> speechOptions,
    IFillerStatementsRepository fillerStatementsRepository,
    IBookendRepository bookendRepository,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    ILogger<ScriptRetoneHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null)
{
    private readonly IInteractiveWorkGate _interactiveWorkGate =
        interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance;
    private const int MaxRetoneChars = 3000;

    private readonly JobStationLineResolver _stationLines =
        new(fillerStatementsRepository, bookendRepository, logger);

    [Function("RetoneScript")]
    public async Task<HttpResponseData> Retone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/retone")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        var cancellationToken = ctx.CancellationToken;
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, cancellationToken) is { } forbidden)
            return forbidden;

        var parsed = await HttpRequestParser.ParseAsync<RetoneScriptRequestDto>(req, cancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);
        var dto = parsed.Value;

        if (string.IsNullOrWhiteSpace(dto.Intent))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Describe the read you want, for example \"talk about this more calmly, it's about a death\".");

        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, $"Job '{jobId}' not found.");
        var job = jobResult.Value;

        string script = string.IsNullOrWhiteSpace(dto.Text)
            ? job.ScriptText.GetValueOr(string.Empty)
            : dto.Text!;
        if (string.IsNullOrWhiteSpace(script))
            return await HttpResponseHelper.BadRequestAsync(req, "There is no script to rewrite.");
        if (script.Length > MaxRetoneChars)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Script must be at most {MaxRetoneChars} characters to rewrite.");
        if (dto.Intent is { Length: > ConfigValidationLimits.MaxDeliveryIntentLength })
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Intent must be at most {ConfigValidationLimits.MaxDeliveryIntentLength} characters to rewrite.");

        // The station's bookend lines and filler statements are ARN's own editorial content and
        // never passed through an LLM stage on the way to air; a read direction must not be the
        // back door that reworders them. Peel, rewrite the body only, put them back verbatim.
        var split = StationLineSplitter.Split(script, await _stationLines.ResolveAsync(job, cancellationToken));
        int preservedWords = split.Leading.Concat(split.Trailing).Sum(WordCounter.Count);

        string model = job.VoiceConfig.Match(v => v.Model, () => elevenLabsOptions.Value.ModelId);
        var wordBudget = await RewriteWordBudgetBuilder.BuildAsync(
            job, preservedWords, voiceCalibration, broadcastOptions.Value, speechOptions.Value,
            cancellationToken);
        var key = InteractiveScriptWorkKeys.ForJob(
            req, job, "retone", split.Body, model,
            $"{dto.Intent}|{wordBudget.TargetWordCount}|{wordBudget.MinWordCount}|{wordBudget.MaxWordCount}",
            style: dto.Intent);
        var rewrite = await _interactiveWorkGate.ExecuteLlmAsync(
            key,
            gateCancellationToken => deliveryWriter.RewriteAsync(
                split.Body, dto.Intent!, model, jobId, gateCancellationToken, wordBudget),
            cancellationToken);
        if (rewrite.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, rewrite.Error);

        var value = rewrite.Value;
        logger.LogInformation("[Retone] Job {JobId}: {Before}->{After} words, {Tags} tag(s), {Preserved} station line(s) preserved",
            jobId, value.SpokenWordsBefore, value.SpokenWordsAfter, value.Tags.Count, split.PreservedLineCount);

        return await HttpResponseHelper.OkJsonAsync(req,
            new RetoneScriptResponseDto(split.Recompose(value.Script),
                value.SpokenWordsBefore + preservedWords, value.SpokenWordsAfter + preservedWords,
                value.Tags, split.PreservedLineCount));
    }
}

public sealed record RetoneScriptRequestDto(string? Text = null, string? Intent = null);

public sealed record RetoneScriptResponseDto(
    string Script, int WordsBefore, int WordsAfter, IReadOnlyList<string> Tags, int PreservedLines);
internal static class RewriteWordBudgetBuilder
{
    internal static async Task<RewriteWordBudget> BuildAsync(
        Job job,
        int preservedWords,
        IVoiceCalibrationService voiceCalibration,
        BroadcastOptions broadcastOptions,
        SpeechDurationOptions speechOptions,
        CancellationToken cancellationToken)
    {
        double wordsPerMinute = broadcastOptions.WordsPerMinute;
        var settings = VoiceSettings.Default;
        double paceBoostWpm = 0;

        if (job.VoiceConfig.HasValue)
        {
            var voice = job.VoiceConfig.Value;
            settings = voice.Settings.GetValueOr(VoiceSettings.Default);
            paceBoostWpm = voice.PaceBoostWpm;
            var metrics = await voiceCalibration.GetCalibrationMetricsAsync(
                voice.VoiceId, Option<VoiceSettings>.Some(settings), false, cancellationToken);
            if (metrics.WordsPerMinute > 0)
                wordsPerMinute = metrics.WordsPerMinute;
        }

        var band = BulletinEditingBand.Compute(
            wordsPerMinute, job.TargetDurationSeconds, settings.Speed,
            speechOptions.MaxSlowDownFactor, speechOptions.MaxSpeedUpFactor,
            paceBoostWpm, speechOptions.AbsolutePaceBoostSpeedUpCeiling,
            speechOptions.AbsolutePaceBoostSlowDownCeiling);
        return new RewriteWordBudget(
            band.TargetWordCount, band.MinWordCount, band.MaxWordCount, preservedWords);
    }
}

/// <summary>
///     Rewrites the CURRENT script in place to follow a free-form operator instruction and returns
///     the text as a PREVIEW. Where <see cref="ScriptRetoneHttpTrigger" /> reshapes only the
///     register around fixed facts, this endpoint may change the CONTENT — drop, reorder, reword,
///     re-emphasise — because the operator is editing the bulletin, not describing a read.
///
///     It never re-drafts from fresh traffic. The job's persisted generator snapshot (or its
///     persisted corridor/incident fallback) is supplied as compact reference data so the editor
///     can understand the names, places and figures already in play. The operator's instruction is
///     authoritative; this endpoint does not fact-check or refuse a requested content change.
///     Content safety (wordlist, then the AI reviewer) remains a separate 400 gate. The full LLM
///     review-refine loop is deliberately NOT re-run.
///
///     The station's own top-and-tail — bookend lines and filler statements — is peeled off before
///     the rewrite and put back verbatim afterwards (<see cref="StationLineSplitter" />). Those
///     lines are ARN's editorial content and are not part of the editable bulletin body.
///
///     Nothing is synthesised and the job is not mutated. The operator reads the preview, edits it,
///     and applies it through the normal verbatim re-voice path, where the safety reviewer sees the
///     changed words again on submit.
/// </summary>
public sealed class ScriptRewriteHttpTrigger(
    IJobRepository jobRepository,
    IMarketAccessService marketAccess,
    IScriptRewriter scriptRewriter,
    IVoiceCalibrationService voiceCalibration,
    IScriptSafetyReviewer scriptSafetyReviewer,
    IContentSafetyFilter contentSafetyFilter,
    IOptions<BroadcastOptions> broadcastOptions,
    IOptions<SpeechDurationOptions> speechOptions,
    IFillerStatementsRepository fillerStatementsRepository,
    IBookendRepository bookendRepository,
    ILogger<ScriptRewriteHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null)
{
    private readonly IInteractiveWorkGate _interactiveWorkGate =
        interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance;
    private const int MaxRewriteChars = 3000;

    private readonly JobStationLineResolver _stationLines =
        new(fillerStatementsRepository, bookendRepository, logger);

    [Function("RewriteScript")]
    public async Task<HttpResponseData> Rewrite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/rewrite")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        var cancellationToken = ctx.CancellationToken;
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, cancellationToken) is { } forbidden)
            return forbidden;

        var parsed = await HttpRequestParser.ParseAsync<RewriteScriptRequestDto>(req, cancellationToken);
        if (parsed.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);
        var dto = parsed.Value;

        if (string.IsNullOrWhiteSpace(dto.Instruction))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Describe the change you want, for example \"drop the weather and lead with the closure\" or \"make the crash the lead\".");

        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, $"Job '{jobId}' not found.");
        var job = jobResult.Value;

        // The caller may rewrite an unsaved draft; fall back to what the job aired. The rewrite is
        // a CONTENT pass, so it works on the plain words — audio tags are delivery, added by the
        // operator's tone palette or the delivery style after the words are settled.
        string script = DeliveryTags.Strip(string.IsNullOrWhiteSpace(dto.Text)
            ? job.ScriptText.GetValueOr(string.Empty)
            : dto.Text!);
        if (string.IsNullOrWhiteSpace(script))
            return await HttpResponseHelper.BadRequestAsync(req, "There is no script to rewrite.");
        if (script.Length > MaxRewriteChars)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Script must be at most {MaxRewriteChars} characters to rewrite.");
        if (dto.Instruction is { Length: > ConfigValidationLimits.MaxDeliveryIntentLength })
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Instruction must be at most {ConfigValidationLimits.MaxDeliveryIntentLength} characters to rewrite.");

        // Peel the station's own top-and-tail off BEFORE the rewrite. The editor cannot reword what
        // it is not shown, and station-authored copy remains verbatim.
        var split = StationLineSplitter.Split(script, await _stationLines.ResolveAsync(job, cancellationToken));
        int preservedWords = split.Leading.Concat(split.Trailing).Sum(WordCounter.Count);

        var context = BuildReferenceContext(job);
        var wordBudget = await RewriteWordBudgetBuilder.BuildAsync(
            job, preservedWords, voiceCalibration, broadcastOptions.Value, speechOptions.Value,
            cancellationToken);
        string model = job.VoiceConfig.Match(v => v.Model, () => string.Empty);
        var key = InteractiveScriptWorkKeys.ForJob(
            req, job, "rewrite", split.Body, model,
            $"{dto.Instruction}|{wordBudget.TargetWordCount}|{wordBudget.MinWordCount}|{wordBudget.MaxWordCount}",
            dictionary: $"{job.PronunciationDictionaryId.GetValueOr(string.Empty)}|{job.PronunciationVersionId.GetValueOr(string.Empty)}",
            style: $"{job.DeliveryStyleId.GetValueOr(string.Empty)}|{job.DeliveryStyleVersion.GetValueOr(0)}");
        var rewrite = await _interactiveWorkGate.ExecuteLlmAsync(
            key,
            gateCancellationToken => RewriteWithSafetyAsync(
                split.Body, dto.Instruction!, context, job, jobId, wordBudget, gateCancellationToken),
            cancellationToken);
        if (rewrite.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, rewrite.Error);

        var value = rewrite.Value;

        logger.LogInformation("[Rewrite] Job {JobId}: {Before}->{After} words, {Preserved} station line(s) preserved.",
            jobId, value.SpokenWordsBefore, value.SpokenWordsAfter, split.PreservedLineCount);

        return await HttpResponseHelper.OkJsonAsync(req,
            new RewriteScriptResponseDto(split.Recompose(value.Script),
                value.SpokenWordsBefore + preservedWords, value.SpokenWordsAfter + preservedWords,
                split.PreservedLineCount));
    }
    private async Task<Result<ScriptRewrite>> RewriteWithSafetyAsync(
        string body,
        string instruction,
        BroadcastWithTraffic context,
        Job job,
        string jobId,
        RewriteWordBudget wordBudget,
        CancellationToken cancellationToken)
    {
        var rewrite = await scriptRewriter.RewriteAsync(
            body, instruction, context, jobId, cancellationToken, wordBudget);
        if (rewrite.IsFailure)
            return rewrite;

        // Keep both semantic checks inside the same gated flight. A cancelled request therefore
        // cancels the rewrite and safety calls together, and a cached result is only ever one that
        // passed the safety boundary.
        var wordlist = await contentSafetyFilter.CheckAsync(
            rewrite.Value.Script, job.Market, cancellationToken);
        if (!wordlist.IsAllowed)
        {
            string summary = string.Join("; ", wordlist.Violations);
            logger.LogWarning("[Rewrite] Job {JobId} refused: content safety: {Violations}",
                jobId, summary);
            return Result<ScriptRewrite>.Failure($"Content safety: {summary}");
        }

        var safety = await scriptSafetyReviewer.ReviewScriptSafetyAsync(
            rewrite.Value.Script, jobId, cancellationToken);
        if (safety.IsFailure)
        {
            logger.LogWarning(
                "[Rewrite] Job {JobId} AI safety review unavailable ({Error}); wordlist passed, returning preview.",
                jobId, safety.Error);
        }
        else if (!safety.Value.IsSafe)
        {
            string summary = string.Join("; ", safety.Value.Violations);
            logger.LogWarning("[Rewrite] Job {JobId} refused: AI safety review: {Violations}",
                jobId, summary);
            return Result<ScriptRewrite>.Failure($"Content safety: {summary}");
        }

        return rewrite;
    }


    /// <summary>
    ///     The reference data supplied to the lightweight rewrite: the exact generator-input snapshot
    ///     when the job captured one, otherwise the corridor set rebuilt from the job's persisted
    ///     corridors and incidents. There is deliberately no HERE re-fetch.
    /// </summary>
    private static BroadcastWithTraffic BuildReferenceContext(Job job)
    {
        if (job.GeneratorInput is { } snapshot)
            return snapshot.ToContext(job.JobId);

        var incidentsByKey = job.AllIncidents.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var corridors = job.Corridors
            .Select(c => c.ToTrafficCorridor(incidentsByKey))
            .ToList();

        if (corridors.Count > 0)
        {
            return new BroadcastWithTraffic
            {
                TargetDurationSeconds = job.TargetDurationSeconds,
                JobId = job.JobId,
                Location = job.Market,
                MarketId = Option.OfWhitespace(job.Market),
                StationId = Option.OfWhitespace(job.Station),
                TimezoneId = job.TimezoneId,
                Content = job.ContentSelection.GetValueOr(BulletinContentSelection.Default),
                // TrafficNarrativeSummarizer regenerates the narrative from the corridors, so this
                // placeholder is never rendered on this path.
                Traffic = string.Empty,
                Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
                Corridors = corridors,
                Weather = job.Weather,
                Fuel = job.Fuel,
                GoodRunRoads = job.GoodRunRoads
            };
        }

        // Legacy job: incidents persisted without corridors. Compose the narrative from the
        // incident fields directly. Good runs are deliberately withheld here — the summarizer's
        // all-clear branch would otherwise blank the incident block entirely and every incident
        // claim would read as fabricated.
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = job.TargetDurationSeconds,
            JobId = job.JobId,
            Location = job.Market,
            MarketId = Option.OfWhitespace(job.Market),
            StationId = Option.OfWhitespace(job.Station),
            TimezoneId = job.TimezoneId,
            Content = job.ContentSelection.GetValueOr(BulletinContentSelection.Default),
            Traffic = ComposeIncidentEvidence(job.Incidents),
            Incidents = job.Incidents.Select(i => i.ToTrafficIncident()).ToList(),
            Weather = job.Weather,
            Fuel = job.Fuel
        };
    }

    private static string ComposeIncidentEvidence(IReadOnlyList<JobIncident> incidents)
    {
        if (incidents.Count == 0)
            return "No significant traffic incidents to report.";

        var sb = new StringBuilder();
        sb.AppendLine($"Traffic data ({incidents.Count} incident{(incidents.Count == 1 ? "" : "s")}):");
        foreach (var inc in incidents)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(inc.Headline))
                parts.Add(inc.Headline);
            if (inc.Direction.HasValue && !string.IsNullOrWhiteSpace(inc.Direction.Value))
                parts.Add(inc.Direction.Value);
            if (inc.LaneDetail.HasValue && !string.IsNullOrWhiteSpace(inc.LaneDetail.Value))
                parts.Add(inc.LaneDetail.Value);
            if (inc.RoadClosed)
                parts.Add("road closed");
            if (inc.Detail.HasValue && !string.IsNullOrWhiteSpace(inc.Detail.Value))
                parts.Add(inc.Detail.Value);
            sb.AppendLine("- " + string.Join(", ", parts));
        }
        return sb.ToString().TrimEnd();
    }
}

internal static class InteractiveScriptWorkKeys
{
    internal static InteractiveWorkKey ForJob(
        HttpRequestData request,
        Job job,
        string operation,
        string input,
        string? model = null,
        string? settings = null,
        string? dictionary = null,
        string? style = null)
    {
        string principalId = ClientPrincipal.Parse(request)?.ObjectId ?? "anonymous";
        string revision = string.IsNullOrWhiteSpace(job.ETag)
            ? job.LastUpdatedAt.ToUniversalTime().ToString("O")
            : job.ETag!;
        return new InteractiveWorkKey(
            principalId, operation, revision, input, model, settings, dictionary, style);
    }
}

public sealed record RewriteScriptRequestDto(string? Text = null, string? Instruction = null);

/// <param name="PreservedLines">
///     How many station-authored lines (bookend intro/outro, filler statements) were held out of the
///     rewrite and put back verbatim.
/// </param>
public sealed record RewriteScriptResponseDto(
    string Script, int WordsBefore, int WordsAfter, int PreservedLines);

public sealed record DirectScriptRequestDto(
    string? Text = null,
    string? Intent = null,
    string? Tone = null,
    IReadOnlyList<string>? Tags = null,
    int? Intensity = null);

public sealed record DirectScriptResponseDto(string DirectedText, int TagCount, bool Changed);
