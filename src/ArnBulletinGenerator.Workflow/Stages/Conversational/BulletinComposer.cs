using System.Diagnostics;
using System.Globalization;
using System.Text;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;
using Playbook = ArnBulletinGenerator.Domain.Models.Playbook;
using PlaybookApplyResult = ArnBulletinGenerator.Domain.Models.PlaybookApplyResult;
using PlaybookOverlay = ArnBulletinGenerator.Domain.Models.PlaybookOverlay;
using WorkflowLlmCallRecord = ArnBulletinGenerator.Workflow.Trace.LlmCallRecord;

namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     Writes the read from the writer's story briefs. This role sees no source data: the
///     conversational workflow keeps that expensive context in the writer and editor, avoiding the
///     roughly 20,000-token, 36-second citation-heavy path measured on 2026-08-26.
///     <para>
///         It receives briefs rather than finished sentences precisely so the prose is composed
///         here rather than paraphrased from upstream, and the briefs keep facts and editorial
///         reasoning in separate blocks so only the former can be voiced.
///     </para>
/// </summary>
/// <summary>
///     Half-width of the word band. The prompt states aim +/- this, and the correction loop accepts
///     aim +/- this. One constant, so the instruction the model is given and the rule it is judged
///     by cannot drift apart.
/// </summary>
public sealed class BulletinComposer(
    IWorkflowLlm llm,
    ConversationalWorkflowOptions? options = null)
{
    private readonly ConversationalWorkflowOptions _options = options ?? new();

    private const int BandTolerance = 4;
    // `aimWords` is one rung around the caller's exact BODY budget. The station bookends were
    // already subtracted before this workflow was called, so the prompt must not inflate the aim
    // to the audio compression ceiling. That ceiling remains available to the deterministic slot
    // fitter after the composer returns.
    public Task<ComposerReply> ComposeAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        int aimWords,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(request,
            RenderPrompt(request, input, briefs, aimWords, previousRead: null, correction: null),
            aimWords, trace, cancellationToken);

    public Task<ComposerReply> RecomposeAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        int aimWords,
        string previousRead,
        EditorVerdict rejection,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(request,
            RenderPrompt(request, input, briefs, aimWords, previousRead, rejection.AsCorrectionMessage()),
            aimWords, trace, cancellationToken);

    /// <summary>
    ///     Revises the read by CONTINUING the conversation that produced it, sending only the
    ///     editor's reasons and the new band.
    ///     <para>
    ///         The alternative, and what this replaced, was a fresh call re-rendering the entire
    ///         prompt: measured at 18,560 characters on dev operation a73962e5, of which the briefs,
    ///         station lessons, voice policy and board state are byte-identical to the call that
    ///         just happened. It also quoted the composer's own output back at it under a
    ///         "PREVIOUS READ:" heading, so the model was re-deriving from a transcript of itself
    ///         instead of editing what it had just written. Continuing the thread sends about 300
    ///         characters instead, keeps the read in its own assistant turn where "fix these
    ///         problems" is the natural next instruction, and leaves the cacheable prefix untouched.
    ///     </para>
    ///     <para>
    ///         The fresh path remains for the case with no thread to continue: an agent route that
    ///         returned no response id, or a first revision after a candidate whose id was lost.
    ///     </para>
    /// </summary>
    public Task<ComposerReply> ReviseInThreadAsync(
        WorkflowRequest request,
        string threadResponseId,
        int aimWords,
        EditorVerdict rejection,
        TraceRecorder trace,
        CancellationToken cancellationToken = default)
    {
        int minimumWords = Math.Max(1, aimWords - BandTolerance);
        string followUp =
            rejection.AsCorrectionMessage()
            + "\n\nRewrite the read you just gave me, fixing every reason above and preserving the "
            + "facts. Keep the station's configured section order.\n\n"
            + $"BEFORE YOU REPLY: count the words in your read. It must be between "
            + $"{minimumWords.ToString(CultureInfo.InvariantCulture)} and "
            + $"{(aimWords + BandTolerance).ToString(CultureInfo.InvariantCulture)} words. "
            + "If it is outside that range, rewrite it to fit before answering.";

        return ContinueOnceAsync(request, threadResponseId, followUp, trace, cancellationToken);
    }

    /// <summary>
    ///     One continuation turn on an existing thread, traced and stripped like a compose.
    ///     <para>
    ///         The call is recorded on the trace as an <c>LlmCall</c> event, exactly as the
    ///         word-loop rounds are, because that event is the ONLY route into the durable call
    ///         captures (<c>ConversationalScriptGenerator.CaptureCallsAsync</c> replays trace events
    ///         into the blob capture). Before this was recorded, an editor-driven revision produced a
    ///         read that the next editor pass reviewed and yet left no capture at all, and when the
    ///         following revision came back empty the reason died with the trace: the panel run on
    ///         2026-09-02 (adelaide a41ed02d) showed only "Composer produced no read while revising"
    ///         with nothing to diagnose it from. The failure reason is now also carried on the reply
    ///         so the workflow can put it in the operator's job log.
    ///     </para>
    /// </summary>
    private async Task<ComposerReply> ContinueOnceAsync(
        WorkflowRequest request,
        string threadResponseId,
        string followUp,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        trace.Decision("Compose", "Revising in the composer's own thread");
        var stopwatch = Stopwatch.StartNew();
        LlmReply reply;
        try
        {
            reply = await llm.ContinueAsync("compose2", threadResponseId, followUp, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            trace.Failure("Compose", "Composer revision continuation failed", exception.Message);
            return new ComposerReply(string.Empty, null) { Error = exception.Message };
        }

        stopwatch.Stop();
        trace.LlmCall("Compose", new WorkflowLlmCallRecord(
            "compose2",
            reply.Model,
            string.Empty,
            followUp,
            reply.Text,
            stopwatch.Elapsed.TotalMilliseconds,
            reply.PromptTokens,
            reply.CompletionTokens,
            reply.Error,
            reply.CachedInputTokens,
            reply.ReasoningTokens,
            reply.Attempt,
            reply.StatusCode,
            reply.ViaFallback,
            reply.DeadlineUtc,
            reply.AttemptElapsedMs,
            reply.Stalled,
            reply.ResponseId));

        if (!reply.IsSuccess)
        {
            string reason = reply.Error ?? "The model returned a failure.";
            trace.Failure("Compose", "Composer revision failed", reason);
            return new ComposerReply(string.Empty, null) { Error = reason };
        }

        string text = StripWrapper(reply.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            const string reason = "The model returned an empty read for the revision.";
            trace.Failure("Compose", "Composer revision failed", reason);
            return new ComposerReply(string.Empty, reply.ResponseId) { Error = reason };
        }

        RecordRead(trace, text);
        return new ComposerReply(text, reply.ResponseId);
    }

    private async Task<ComposerReply> CompleteAsync(
        WorkflowRequest request,
        string userPrompt,
        int targetWords,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        const string role = "compose2";
        string systemPrompt = ResolveComposerSystemPrompt(request, targetWords, trace);
        var stopwatch = Stopwatch.StartNew();
        LlmReply reply;

        try
        {
            // Prose is intentionally unwrapped. A JSON envelope costs tokens and creates escaping
            // failure modes without adding anything the downstream workflow needs.
            reply = await llm.CompleteAsync(role, systemPrompt, userPrompt, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            reply = LlmReply.Failure(exception.Message);
            trace.LlmCall("Compose", new WorkflowLlmCallRecord(
                role,
                reply.Model,
                systemPrompt,
                userPrompt,
                reply.Text,
                stopwatch.Elapsed.TotalMilliseconds,
                reply.PromptTokens,
                reply.CompletionTokens,
                exception.Message,
                reply.CachedInputTokens,
                reply.ReasoningTokens,
                reply.Attempt,
                reply.StatusCode,
                reply.ViaFallback,
                reply.DeadlineUtc,
                reply.AttemptElapsedMs,
                reply.Stalled,
                reply.ResponseId));
            trace.Failure("Compose", "Composer LLM call failed", exception.Message);
            return new ComposerReply(string.Empty, null);
        }

        stopwatch.Stop();
        trace.LlmCall("Compose", new WorkflowLlmCallRecord(
            role,
            reply.Model,
            systemPrompt,
            userPrompt,
            reply.Text,
            stopwatch.Elapsed.TotalMilliseconds,
            reply.PromptTokens,
            reply.CompletionTokens,
            reply.Error,
            reply.CachedInputTokens,
            reply.ReasoningTokens,
            reply.Attempt,
            reply.StatusCode,
            reply.ViaFallback,
            reply.DeadlineUtc,
            reply.AttemptElapsedMs,
            reply.Stalled,
            reply.ResponseId));

        if (!reply.IsSuccess)
        {
            trace.Failure("Compose", "Composer LLM call failed", reply.Error ?? "The model returned a failure.");
            return new ComposerReply(string.Empty, null);
        }

        string text = StripWrapper(reply.Text);
        RecordRead(trace, text);

        // The word loop deliberately does NOT run here. Compose is called once per candidate, so
        // correcting inside it corrected drafts that were about to be discarded: observed on dev
        // 2026-08-31, operation 825770ab, one bulletin made ELEVEN compose calls — three parallel
        // candidates plus eight sequential corrections, for a single read that ships. The loop now
        // runs once, in the workflow, against the candidate SelectCandidate actually chose.
        return new ComposerReply(text, reply.ResponseId);
    }

    /// <summary>
    ///     Corrects one already-chosen read, at most <c>MaxWordLoopRounds</c> times, and ONLY when
    ///     it exceeds the word band the prompt asked for.
    /// </summary>
    /// <param name="aimedWords">
    ///     The aim this read was composed against. The acceptance band is this number plus
    ///     <see cref="BandTolerance" />, which is character-for-character the band the prompt stated,
    ///     so a read that obeyed its instruction is never cut.
    ///     <para>
    ///         This used to be a SECONDS band from the duration oracle, and that made the loop
    ///         destroy correct bulletins. Dev operation a73962e5 on 2026-09-01: the prompt asked for
    ///         81-89 words, the composer returned 85, and the loop deleted two stories and then a
    ///         third, ending on a two-sentence 43-word read. Nothing misbehaved - the model obeyed
    ///         every instruction exactly - the two sides simply disagreed about the voice. The word
    ///         target is built from WordTargetCalculator at a calibrated ~181 wpm once the 1.15
    ///         overshoot is folded in, while CalibratedDurationOracle prices the same voice from
    ///         syllables at about 129 wpm (it reported a 39-word read as 18.1 seconds). Those two
    ///         estimates are 40% apart, so an in-band read priced ~39s against a 28.2s slot and the
    ///         loop read that as eleven seconds of overrun. Any read obeying the band was guaranteed
    ///         to be cut. Measuring the read against the band it was given makes the loop
    ///         self-consistent: it can only fire on a read that ignored its instruction.
    ///     </para>
    /// </param>
    public Task<ComposerReply> CorrectChosenReadAsync(
        WorkflowRequest request,
        string text,
        int aimedWords,
        string? responseId,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        CorrectOverlongReadAsync(request, text, aimedWords, responseId, trace, cancellationToken);

    /// <summary>
    ///     Repairs only an over-ceiling read before it reaches the editor. The correction is
    ///     deliberately a same-conversation turn and names whole sentences, because the measured
    ///     alternative — asking the model to count or trim a word delta — landed in band only 2 of
    ///     9 times. Naming the item to delete landed 4 of 5, then 12 of 12 in validation.
    ///     <para>
    ///         The word band is calibrated from the caller's slot word count: at the measured
    ///         2.40 words/second, a 30-second slot is 71-83 words (the 0.3-second silence pad sets
    ///         the floor and the 1.15 speed ceiling sets the top). Short reads are not corrected:
    ///         measured continuation could not reliably extend them, so they remain for the
    ///         existing workflow underrun handling.
    ///     </para>
    /// </summary>
    private async Task<ComposerReply> CorrectOverlongReadAsync(
        WorkflowRequest request,
        string text,
        int aimedWords,
        string? responseId,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        // The ceiling is the band the PROMPT stated, not a seconds figure from the duration oracle.
        // See CorrectChosenReadAsync for the measurement that forced this: the two disagreed by 40%
        // and the loop shredded reads that had obeyed their instruction exactly.
        int ceilingWords = aimedWords + BandTolerance;
        if (ceilingWords <= 0)
            return new ComposerReply(text, responseId);

        int maxRounds = Math.Max(0, _options.MaxWordLoopRounds);
        string current = text;
        string? currentResponseId = responseId;

        Stopwatch? fitStopwatch = null;
        void StartFitWords()
        {
            if (fitStopwatch is null)
            {
                fitStopwatch = Stopwatch.StartNew();
                trace.StageStart("FitWords", "Fitting the chosen read to its word band.");
            }
        }

        void FinishFitWords(string detail)
        {
            if (fitStopwatch is null)
                return;
            fitStopwatch.Stop();
            trace.StageEnd("FitWords", detail, fitStopwatch.Elapsed.TotalMilliseconds);
            fitStopwatch = null;
        }

        for (int round = 1; round <= maxRounds; round++)
        {
            int beforeWords = CountWords(current);
            if (beforeWords <= ceilingWords)
            {
                FinishFitWords($"Word loop completed at {beforeWords} words.");
                return new ComposerReply(current, currentResponseId);
            }

            StartFitWords();


            IReadOnlyList<string> sentences = SplitSentences(current);
            IReadOnlyList<string> body = sentences.Where(sentence => !IsWeatherOrFuelCloser(sentence)).ToList();
            if (body.Count <= 1)
            {
                FinishFitWords($"Word loop stopped at {beforeWords} words because no body item was removable.");

                trace.Decision("Compose",
                    $"Word-loop stopped at round {round}: the overlong read has no removable body item",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["beforeWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                        ["ceilingWords"] = ceilingWords.ToString(CultureInfo.InvariantCulture),
                        ["round"] = round.ToString(CultureInfo.InvariantCulture)
                    });

                return new ComposerReply(current, currentResponseId);
            }

            // Price drops in the read's OWN words-per-sentence, so a board of long sentences drops
            // fewer of them, and never drop below the floor: the read has to stay inside the band it
            // was given, not merely fall under its ceiling.
            double meanWordsPerSentence = beforeWords / (double)Math.Max(1, sentences.Count);
            int floorWords = Math.Max(1, aimedWords - BandTolerance);
            int requestedDrops = Math.Max(1, (int)Math.Ceiling((beforeWords - ceilingWords) / Math.Max(1.0, meanWordsPerSentence)));
            int affordableDrops = Math.Max(1, (int)Math.Floor((beforeWords - floorWords) / Math.Max(1.0, meanWordsPerSentence)));

            int dropCount = Math.Min(Math.Min(requestedDrops, affordableDrops), body.Count - 1);
            IReadOnlyList<string> drops = [.. body.TakeLast(dropCount)];
            string quotedDrops = string.Join(", ", drops.Select(QuoteSentence));
            string followUpPrompt =
                $"Remove {dropCount} whole item(s) from that read: {quotedDrops}.\n"
                + "Delete them entirely - do not summarise them or fold them into other sentences.\n"
                + "Leave every remaining sentence exactly as written, in the same order.";

            // A missing response id used to abandon the correction outright. That made the loop
            // silently conditional on a provider detail it does not control: the read still
            // overran, nothing in the narrative said the loop had declined, and the deterministic
            // trimmer downstream took the blame for cutting 15-27 words of approved copy. The
            // continuation is an OPTIMISATION - it saves resending the read - not a requirement, so
            // without an id the same instruction goes out as a standalone turn carrying the read.
            var stopwatch = Stopwatch.StartNew();
            LlmReply correction;
            try
            {
                correction = string.IsNullOrWhiteSpace(currentResponseId)
                    ? await llm.CompleteAsync(
                            "compose2",
                            ResolveComposerSystemPrompt(request, aimedWords, trace),
                            "Here is a read you wrote:\n\n" + current + "\n\n" + followUpPrompt,
                            null,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await llm.ContinueAsync(
                            "compose2", currentResponseId, followUpPrompt, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                trace.Failure("Compose", "Composer word-loop continuation failed", exception.Message);
                trace.Measurement("FitWords",
                    $"Word-loop round {round}: {beforeWords} words before, {beforeWords} after",
                    stopwatch.Elapsed.TotalMilliseconds,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["round"] = round.ToString(CultureInfo.InvariantCulture),
                        ["beforeWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                        ["afterWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                        ["floorWords"] = floorWords.ToString(CultureInfo.InvariantCulture),
                        ["ceilingWords"] = ceilingWords.ToString(CultureInfo.InvariantCulture)
                    });
                FinishFitWords($"Word loop stopped at {beforeWords} words after a continuation failure.");

                return new ComposerReply(current, currentResponseId);
            }

            stopwatch.Stop();
            trace.LlmCall("Compose", new WorkflowLlmCallRecord(
                "compose2",
                correction.Model,
                string.Empty,
                followUpPrompt,
                correction.Text,
                stopwatch.Elapsed.TotalMilliseconds,
                correction.PromptTokens,
                correction.CompletionTokens,
                correction.Error,
                correction.CachedInputTokens,
                correction.ReasoningTokens,
                correction.Attempt,
                correction.StatusCode,
                correction.ViaFallback,
                correction.DeadlineUtc,
                correction.AttemptElapsedMs,
                correction.Stalled,
                correction.ResponseId));

            if (!correction.IsSuccess)
            {
                trace.Failure("Compose", "Composer word-loop continuation failed",
                    correction.Error ?? "The model returned a failure.");
                trace.Measurement("FitWords",
                    $"Word-loop round {round}: {beforeWords} words before, {beforeWords} after",
                    stopwatch.Elapsed.TotalMilliseconds,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["round"] = round.ToString(CultureInfo.InvariantCulture),
                        ["beforeWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                        ["afterWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                        ["floorWords"] = floorWords.ToString(CultureInfo.InvariantCulture),
                        ["ceilingWords"] = ceilingWords.ToString(CultureInfo.InvariantCulture)
                    });
                FinishFitWords($"Word loop stopped at {beforeWords} words after a failed correction.");

                return new ComposerReply(current, currentResponseId);
            }

            string corrected = StripWrapper(correction.Text);
            int afterWords = CountWords(corrected);
            trace.Measurement("FitWords",
                $"Word-loop round {round}: {beforeWords} words before, {afterWords} after",
                stopwatch.Elapsed.TotalMilliseconds,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["round"] = round.ToString(CultureInfo.InvariantCulture),
                    ["beforeWords"] = beforeWords.ToString(CultureInfo.InvariantCulture),
                    ["afterWords"] = afterWords.ToString(CultureInfo.InvariantCulture),
                    ["floorWords"] = floorWords.ToString(CultureInfo.InvariantCulture),
                    ["ceilingWords"] = ceilingWords.ToString(CultureInfo.InvariantCulture)
                });

            if (string.IsNullOrWhiteSpace(corrected))
            {
                FinishFitWords($"Word loop stopped at {beforeWords} words after an empty correction.");
                return new ComposerReply(current, currentResponseId);
            }

            current = corrected;
            currentResponseId = correction.ResponseId;
            RecordRead(trace, current);
        }

        FinishFitWords($"Word loop completed at {CountWords(current)} words.");
        return new ComposerReply(current, currentResponseId);
    }

    /// <summary>
    ///     The slot the corrected read must land in, in SECONDS, measured by the same duration
    ///     oracle the rest of the workflow trusts.
    ///     <para>
    ///         This was a WORD band derived from the caller's target word count until 2026-08-31,
    ///         and that could not work: the target handed to the workflow is already inflated twice
    ///         over — the engine sizes it at 150 words per minute against a measured 144, then
    ///         multiplies by a 1.15 WordTargetOvershoot — so a 30-second slot arrives as ~84 words
    ///         when the voice actually delivers ~72. Banding off that number put the ceiling near 97
    ///         words, so 87-91 word reads measured as in-band, the loop never fired, and the slot
    ///         fitter was left compressing 35-40 seconds of speech into 30.
    ///     </para>
    ///     <para>
    ///         Seconds avoid the whole chain. The floor is the slot less the 0.3s silence pad the
    ///         audio path can absorb. The ceiling is the SLOT, not EffectiveMaxSeconds.
    ///     </para>
    ///     <para>
    ///         Banding to EffectiveMaxSeconds - "what the fitter can genuinely compress" - was
    ///         measured on dev 2026-08-31 and is a self-fulfilling ceiling: it licenses every read
    ///         between the slot and the compression limit, so the fitter is guaranteed the work it
    ///         was being spared. For a 30s slot the loop only engaged above 34.5s, roughly 98 words,
    ///         while the voice fits about 75. All three bulletins in that run passed the loop
    ///         untouched and were then rescued by the deterministic trimmer, which cut 90 to 75, 108
    ///         to 92 and 103 to 76 words - 15 to 27 words of drafted, edited, approved copy thrown
    ///         away per bulletin, chosen by a text cut rather than by the model that knows which
    ///         story matters least.
    ///     </para>
    ///     <para>
    ///         Correcting to the SLOT instead was tried on dev 2026-09-01 and reverted the same day.
    ///         It does remove the trimmer from the picture, but combined with a live loop it
    ///         overshot downward - 75-word targets returned 43 to 54 words, which the audio path can
    ///         only pad with silence. The 0.3s-wide band it produced also left the loop no room to
    ///         stop. The ceiling is therefore the compression limit again: an overrun the fitter can
    ///         absorb is a recoverable failure and a short read is not.
    ///     </para>
    /// </summary>
    private (double Floor, double Ceiling) SecondBand(WorkflowRequest request)
    {
        if (request.TargetSeconds <= 0)
            return (0, 0);

        double ceiling = request.EffectiveMaxSeconds > request.TargetSeconds
            ? request.EffectiveMaxSeconds
            : request.TargetSeconds;
        return (Math.Max(0, request.TargetSeconds - 0.3), ceiling);
    }

    private static void RecordRead(TraceRecorder trace, string text) =>
        trace.Artifact("Compose", "Read", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = text,
            ["wordCount"] = CountWords(text).ToString(CultureInfo.InvariantCulture)
        });

    private static IReadOnlyList<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('.' or '!' or '?'))
                continue;

            AddSentence(text[start..(index + 1)], sentences);
            start = index + 1;
        }

        AddSentence(text[start..], sentences);
        return sentences;
    }

    private static void AddSentence(string text, List<string> sentences)
    {
        string trimmed = text.Trim();
        if (trimmed.Length > 0)
            sentences.Add(trimmed);
    }

    private static bool IsWeatherOrFuelCloser(string sentence) =>
        sentence.Contains("degrees", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains('°')
        || sentence.Contains("unleaded", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains("diesel", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains("cloud", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains("rain", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains("sunny", StringComparison.OrdinalIgnoreCase)
        || sentence.Contains("clear", StringComparison.OrdinalIgnoreCase);

    private static string QuoteSentence(string sentence)
    {
        string snippet = sentence.Length > 60 ? sentence[..60].TrimEnd() + "..." : sentence;
        return $"'{snippet}'";
    }

    private static string ResolveComposerSystemPrompt(
        WorkflowRequest request,
        int targetWords,
        TraceRecorder trace)
    {
        string systemPrompt = StationPlaybook.Resolve(request, "compose2", trace, "Compose");
        return systemPrompt.Replace(
            "{{ATN_EXEMPLARS}}",
            ExemplarSelector.Render(targetWords, "metro", null, request.Station),
            StringComparison.Ordinal);
    }
    private static string RenderPrompt(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        int targetWords,
        string? previousRead,
        string? correction)
    {
        var prompt = new StringBuilder();
        // null, not targetWords: the composer states its word count exactly once, at the tail.
        // The header "Slot: about N spoken words of air." was the FIRST of three competing counts
        // and the one the model appeared to act on. The writer still receives it, because sizing
        // its briefs is a separate job from sizing the read.
        prompt.AppendLine(SourceDataRenderer.RenderBrief(request, null));

        string voicePolicy = PolicyRenderer.ForComposer(request.EffectivePolicy.Voice);
        if (!string.IsNullOrWhiteSpace(voicePolicy))
        {
            prompt.AppendLine();
            prompt.AppendLine(voicePolicy);
        }

        prompt.AppendLine("STORY BRIEFS (your colleague's decisions; source data is not repeated here):");
        prompt.AppendLine(briefs.Render(input));

        if (request.FutureLessons is { Count: > 0 })
        {
            prompt.AppendLine();
            prompt.AppendLine("STATION LESSONS (standing wording preferences; follow these unless they conflict with evidence):");
            foreach (FutureLessonEntry lesson in request.FutureLessons)
                prompt.AppendLine($"- {lesson.Text.Trim()}");
        }

        if (request.Guidance.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("STATION GUIDANCE:");
            foreach (string guidance in request.Guidance)
                prompt.AppendLine($"- {guidance.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.PreviousScript))
        {
            prompt.AppendLine();
            prompt.AppendLine("PREVIOUS READ (aired earlier today)");
            prompt.AppendLine(request.PreviousScript);
            prompt.AppendLine("This is what listeners heard last; do not reuse its opening phrase, its idioms for speed or state, or its sentence shapes.");
            prompt.AppendLine("Continue the story where facts are unchanged, using the station's continuity wording.");
        }

        if (correction is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine(correction);
            prompt.AppendLine();
            prompt.AppendLine("PREVIOUS READ:");
            prompt.AppendLine(previousRead ?? string.Empty);
        }

        int storyBudget = SourceDataRenderer.StoryBudget(input, targetWords);
        (bool isQuiet, int maxDelay) = SourceDataRenderer.BoardState(input);
        if (isQuiet)
        {
            prompt.AppendLine();
            prompt.AppendLine(
                $"BOARD STATE: quiet run — no closures, nothing above {(maxDelay > 0 ? $"{maxDelay} minutes" : "a trivial delay")}. "
                + "The quiet-board rule applies: lead with the network state, not the biggest obstruction.");
        }

        prompt.AppendLine();
        // A numeric "HARD LIMITS: never exceed N words … land between N and M" block was added here
        // on 2026-08-30 and WITHDRAWN the same day. It was cheap to try and it backfired: the style
        // gate started rejecting reads for speaker labels ("Remove speaker labels like 'Alex:'"),
        // measured 0 occurrences across 20 pre-change jobs and 4 across 24 jobs after. Each
        // rejection re-drafts, and a generation attempt (~25s of agent plus TTS) costs more than the
        // ~14.4s re-render the cap was meant to avoid, so the cure was worse than the disease.
        // Length is governed where it belongs instead: the band handed to the workflow and the
        // ceiling-aware candidate selection, which need no prompt pressure to work.
        // The word count is stated ONCE, at the tail, and deliberately not here. It used to be
        // stated three times - "Slot: about N spoken words" in the brief header, "about N words"
        // in this sentence, and the self-check below - and the model acted on the earliest one it
        // read, which is why moving only the tail (measured slope 1.028 in isolation) did not move
        // production output. Three numbers are three instructions. This sentence keeps the STORY
        // budget, which is a different quantity and does not compete with a word count.
        prompt.AppendLine(
            $"The air holds about {storyBudget.ToString(CultureInfo.InvariantCulture)} of the optional stories told properly, " +
            "plus the required ones: a lead with room to breathe, then quick calls. Drop the rest whole.");

        // Measured 2026-08-31: the same length instruction mid-prompt steered at an aim-to-output
        // slope of 0.083, while placing it at the tail restored a 1.028 slope. An inline "HARD
        // LIMIT" at the buried position was worse than the plain baseline (7% vs 23% steering
        // efficiency), so this is deliberately the final instruction and its position, not its
        // wording, does the work. It is now also the ONLY place a word count appears.
        int minimumWords = Math.Max(1, targetWords - BandTolerance);
        int maximumWords = targetWords + BandTolerance;
        prompt.AppendLine();
        prompt.AppendLine(
            $"BEFORE YOU REPLY: count the words in your read. It must be between " +
            $"{minimumWords.ToString(CultureInfo.InvariantCulture)} and " +
            $"{maximumWords.ToString(CultureInfo.InvariantCulture)} words. " +
            "If it is outside that range, rewrite it to fit before answering.");
        return prompt.ToString();
    }

    private static string StripWrapper(string raw)
    {
        string text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = text.IndexOf('\n');
            text = firstLineBreak >= 0 ? text[(firstLineBreak + 1)..] : text[3..];
            if (text.TrimEnd().EndsWith("```", StringComparison.Ordinal))
                text = text.TrimEnd()[..^3];
            text = text.Trim();
        }

        if (text.Length >= 2 &&
            ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
            text = text[1..^1].Trim();

        return text;
    }

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    /// <summary>
    ///     Builds one mechanical correction from the duration actually measured for a read and sends
    ///     it as a single continuation turn. Overruns reuse the existing whole-item deletion shape;
    ///     underruns lengthen existing sentences for a small shortfall, or add one brief item for a
    ///     larger shortfall while voicing remaining unvoiced facts in the existing sentences.
    ///     <para>
    ///         Dev Gold Coast job 69965aa3 (2026-09-03) had a 2.2s shortfall on a board of
    ///         approximately 16-word sentences. The composer answered with a whole new item, then
    ///         the ceiling and fitter removed a whole item, so attempt 2 reproduced attempt 1
    ///         exactly.
    ///     </para>
    /// </summary>
    /// <param name="request">The station request that supplied the composer thread.</param>
    /// <param name="threadResponseId">Provider response id returned for the current composer read.</param>
    /// <param name="currentRead">The read whose natural duration was measured.</param>
    /// <param name="measuredSeconds">Natural spoken duration of <paramref name="currentRead" />.</param>
    /// <param name="trace">Trace receiving the continuation and duration arithmetic.</param>
    /// <param name="cancellationToken">Cancellation requested by the workflow host.</param>
    public Task<ComposerReply> ReviseForMeasuredDurationAsync(
        WorkflowRequest request,
        string threadResponseId,
        string currentRead,
        double measuredSeconds,
        TraceRecorder trace,
        CancellationToken cancellationToken = default)
    {
        int currentWords = CountWords(currentRead);
        if (currentWords == 0 || measuredSeconds <= 0 || !double.IsFinite(measuredSeconds))
            return Task.FromResult(new ComposerReply(string.Empty, null));

        string followUp;
        if (measuredSeconds > request.TargetSeconds)
        {
            IReadOnlyList<string> sentences = SplitSentences(currentRead);
            IReadOnlyList<string> body = sentences.Where(sentence => !IsWeatherOrFuelCloser(sentence)).ToList();
            if (body.Count <= 1)
                return Task.FromResult(new ComposerReply(currentRead, threadResponseId));

            double secondsPerWord = measuredSeconds / currentWords;
            int wordsToRemove = Math.Max(1, (int)Math.Ceiling(
                (measuredSeconds - request.TargetSeconds) / Math.Max(0.001, secondsPerWord)));
            double meanWordsPerSentence = currentWords / (double)Math.Max(1, sentences.Count);
            int requestedDrops = Math.Max(1, (int)Math.Ceiling(
                wordsToRemove / Math.Max(1.0, meanWordsPerSentence)));
            int dropCount = Math.Min(requestedDrops, body.Count - 1);
            string quotedDrops = string.Join(", ", body.TakeLast(dropCount).Select(QuoteSentence));

            followUp =
                $"Remove {dropCount} whole item(s) from that read: {quotedDrops}.\n"
                + "Delete them entirely - do not summarise them or fold them into other sentences.\n"
                + "Leave every remaining sentence exactly as written, in the same order.";
            trace.Decision("Compose",
                $"Measured overrun correction removes {dropCount} whole item(s) for {measuredSeconds:F1}s "
                + $"against {request.TargetSeconds:F1}s",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["words"] = currentWords.ToString(CultureInfo.InvariantCulture),
                    ["secondsPerWord"] = secondsPerWord.ToString("F3", CultureInfo.InvariantCulture),
                    ["meanWordsPerSentence"] = meanWordsPerSentence.ToString("F2", CultureInfo.InvariantCulture),
                    ["wordsToRemove"] = wordsToRemove.ToString(CultureInfo.InvariantCulture),
                    ["dropCount"] = dropCount.ToString(CultureInfo.InvariantCulture)
                });
        }
        else
        {
            double secondsPerWord = measuredSeconds / currentWords;
            double shortfallSeconds = Math.Max(0, request.TargetSeconds - measuredSeconds);
            int additionalWords = Math.Max(1, (int)Math.Ceiling(
                shortfallSeconds / Math.Max(0.001, secondsPerWord)));
            IReadOnlyList<string> sentences = SplitSentences(currentRead);
            double meanWordsPerSentence = currentWords / (double)Math.Max(1, sentences.Count);

            if (additionalWords < meanWordsPerSentence)
            {
                followUp =
                    $"Lengthen the existing read by about "
                    + $"{additionalWords.ToString(CultureInfo.InvariantCulture)} words by voicing facts "
                    + "the briefs supply but the read left unvoiced, such as a direction, an extent, "
                    + "a cross street, or the cause behind a delay. Spread those facts across one or "
                    + "two existing sentences. Every sentence keeps its road name and its single "
                    + "location anchor. Do not add a new item, do not restate anything, do not "
                    + "reorder or drop sentences.";
                trace.Decision("Compose",
                    $"Measured underrun correction lengthens existing sentences by about {additionalWords} words "
                    + $"for {measuredSeconds:F1}s against {request.TargetSeconds:F1}s "
                    + $"(meanWordsPerSentence={meanWordsPerSentence:F2})",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["words"] = currentWords.ToString(CultureInfo.InvariantCulture),
                        ["secondsPerWord"] = secondsPerWord.ToString("F3", CultureInfo.InvariantCulture),
                        ["shortfallSeconds"] = shortfallSeconds.ToString("F2", CultureInfo.InvariantCulture),
                        ["additionalWords"] = additionalWords.ToString(CultureInfo.InvariantCulture),
                        ["meanWordsPerSentence"] = meanWordsPerSentence.ToString("F2", CultureInfo.InvariantCulture),
                        ["repairMode"] = "lengthen-existing-sentences"
                    });
            }
            else
            {
                int itemWords = Math.Min(additionalWords, Math.Max(1, (int)Math.Round(meanWordsPerSentence)));
                followUp =
                    $"Add exactly one additional item from the story briefs, about "
                    + $"{itemWords.ToString(CultureInfo.InvariantCulture)} words. "
                    + "Append it in the station's configured section order. Close any remaining "
                    + "shortfall by voicing facts the briefs supply but the existing sentences left "
                    + "unvoiced. Leave everything else verbatim: do not rewrite, reorder, shorten, "
                    + "or repeat existing sentences.";
                trace.Decision("Compose",
                    $"Measured underrun correction adds one brief item of about {itemWords} words "
                    + $"for {measuredSeconds:F1}s against {request.TargetSeconds:F1}s; any remaining "
                    + $"shortfall is closed by voicing unvoiced facts in existing sentences "
                    + $"(meanWordsPerSentence={meanWordsPerSentence:F2})",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["words"] = currentWords.ToString(CultureInfo.InvariantCulture),
                        ["secondsPerWord"] = secondsPerWord.ToString("F3", CultureInfo.InvariantCulture),
                        ["shortfallSeconds"] = shortfallSeconds.ToString("F2", CultureInfo.InvariantCulture),
                        ["additionalWords"] = additionalWords.ToString(CultureInfo.InvariantCulture),
                        ["itemWords"] = itemWords.ToString(CultureInfo.InvariantCulture),
                        ["meanWordsPerSentence"] = meanWordsPerSentence.ToString("F2", CultureInfo.InvariantCulture),
                        ["repairMode"] = "add-item-and-lengthen-existing-sentences"
                    });
            }
        }

        return ContinueOnceAsync(request, threadResponseId, followUp, trace, cancellationToken);
    }

}

/// <summary>
///     A composed read together with the provider response id that produced it, so a later
///     correction can continue THAT conversation rather than a sibling candidate's. Three
///     candidates are composed in parallel, so the id cannot live in shared state on the composer.
///     <para>
///         <see cref="Error" /> is set only when <see cref="Text" /> is empty because the model
///         call failed or returned nothing; the workflow puts it in the failure it reports so the
///         operator's job log names the cause instead of "produced no read".
///     </para>
/// </summary>
public sealed record ComposerReply(string Text, string? ResponseId)
{
    /// <summary>Why <see cref="Text" /> is empty, when it is; otherwise <c>null</c>.</summary>
    public string? Error { get; init; }
}
