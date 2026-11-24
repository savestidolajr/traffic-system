using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Provider for the non-drafting LLM roles — Review, sentence-scoped Patch, Safety, Direction,
///     Delivery-rewrite and Content-rewrite — each issued as a one-shot Azure OpenAI Responses API
///     call composed from transport and prompt collaborators. This class owns role-facing argument
///     assembly and response parsing.
/// </summary>
public sealed class LlmScriptGenerator(
    IHttpClientFactory httpClientFactory,
    IOptions<BroadcastOptions> options,
    IOptions<AzureAIOptions> azureOptions,
    TimeProvider timeProvider,
    ILogger<LlmScriptGenerator> logger,
    ILlmCallCapture llmCallCapture,
    TokenCredential? tokenCredential = null)
    : IScriptReviewer, IScriptRefiner, IScriptSafetyReviewer, IScriptDirector, IDeliveryWriter, IScriptRewriter
{
    // Fallback syllables-per-word used when converting predictor output to word count for the LLM prompt.
    private const double DefaultSyllablesPerWord = 1.5;

    private readonly AzureAIOptions _azureOptions = azureOptions.Value;
    private readonly double _defaultSyllablesPerSecond = options.Value.SyllablesPerSecond;
    private readonly BroadcastOptions _options = options.Value;

    // Collaborator built directly (not DI-registered) rather than injected: LlmScriptGenerator's
    // constructor is public (implements the public role interfaces, resolved by the default
    // ServiceProvider, which only considers public constructors), so an internal collaborator type
    // can never appear as one of its parameters — mirrors AudioPipelineService's
    // AudioDurationFineTuner field, built the same way from the same logger category.
    private readonly ResponsesTransport _transport =
        new(httpClientFactory, azureOptions, timeProvider, logger, llmCallCapture, tokenCredential);

    /// <summary>
    ///     Market-local daypart ("morning"/"midday"/"afternoon"/"evening"/"overnight") from the
    ///     schedule's IANA timezone, or null when no timezone is known (ad-hoc manual jobs) or the
    ///     id doesn't resolve — in which case the prompts BAN time-of-day words entirely.
    /// </summary>
    private string? ResolveTimeOfDay(Option<string> timezoneId)
    {
        if (!timezoneId.HasValue || string.IsNullOrWhiteSpace(timezoneId.Value))
            return null;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Value);
            int hour = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), tz).Hour;
            return hour switch
            {
                >= 5 and < 12 => "morning",
                >= 12 and < 14 => "midday",
                >= 14 and < 18 => "afternoon",
                >= 18 and < 22 => "evening",
                _ => "overnight"
            };
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    // The words ContentFormatters.FormatWeather / FormatFuel seed a read with. Prices and
    // temperatures are spoken as words ("ten degrees", "one ninety-seven"), so a numeric match
    // cannot be used to tell whether the line aired.
    private static readonly string[] WeatherVocabulary =
        ["degrees", "sunny", "rain", "shower", "storm", "cloud", "wind", "fog", "cool", "mild", "warm", "hot", "clear skies"];

    private static readonly string[] FuelVocabulary =
        ["unleaded", "diesel", "litre", "dollar", "cents", "fuel"];

    private static bool MentionsAny(string script, string[] vocabulary) =>
        vocabulary.Any(word => script.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool MentionsWholeWord(string script, string term)
    {
        int i = script.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            bool leftOk = i == 0 || !char.IsLetterOrDigit(script[i - 1]);
            int end = i + term.Length;
            bool rightOk = end >= script.Length || !char.IsLetterOrDigit(script[end]);
            if (leftOk && rightOk) return true;
            i = script.IndexOf(term, i + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    ///     Repairs a single flagged sentence using a narrow, sentence-scoped prompt.
    ///     Enforces deterministic word-count ceiling (+/- 2 words or 15%, max +3 words)
    ///     and validates that citations do not introduce unsupplied evidence ids.
    /// </summary>
    public async Task<Result<DraftedSentence>> PatchSentenceAsync(
        BroadcastWithTraffic context,
        DraftedSentence sentence,
        string instruction,
        IReadOnlyList<EvidenceUnit> allowedEvidence,
        int wordBudget,
        CancellationToken cancellationToken = default)
    {
        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure) return Result<DraftedSentence>.Failure(authHeader.Error);

        var effectiveEvidence = allowedEvidence is { Count: > 0 }
            ? allowedEvidence
            : (sentence.EvidenceIds is { Count: > 0 }
                ? TrafficNarrativeSummarizer.BuildEvidenceUnits(context)
                    .Where(u => sentence.EvidenceIds.Contains(u.Id, StringComparer.Ordinal))
                    .ToList()
                : TrafficNarrativeSummarizer.BuildEvidenceUnits(context));

        int effectiveWordBudget = wordBudget > 0 ? wordBudget : WordCounter.Count(sentence.Text);

        var arguments = new Dictionary<string, object?>
        {
            ["sentenceOrder"] = sentence.Order,
            ["sentenceText"] = sentence.Text,
            ["allowedEvidence"] = PromptLibrary.RenderEvidenceUnits(effectiveEvidence),
            ["instruction"] = instruction,
            ["wordBudget"] = effectiveWordBudget
        };


        string renderedPrompt = HandlebarsTemplateRenderer.Render(PromptLibrary.RefineSentenceTemplate, arguments);

        var result = await _transport.DispatchLlmAsync("RefineSentence", renderedPrompt, 1024, true, cancellationToken,
            authHeader.Value, hasProducerFeedback: context.RejectionFeedback.HasValue, jobId: context.JobId,
            jsonSchema: null, systemInstructionsOverride: PromptLibrary.RefineSentenceTemplate);
        if (result.IsFailure)
            return Result<DraftedSentence>.Failure(result.Error);

        var parsed = TryParseRefineSentence(result.Value, sentence, effectiveEvidence, text => NormalizeRefineText(text, context));
        if (parsed.IsFailure)
        {
            logger.LogWarning("[RefineSentence] Sentence {Order} parse failed: {Error}. Retaining original sentence.",
                sentence.Order, parsed.Error);
            return Result<DraftedSentence>.Failure(parsed.Error);
        }

        var repaired = parsed.Value;

        // One sentence in, one sentence out: a two-sentence return would grow the script when the
        // caller splices it, silently shifting every later sentence order mid-loop. Treat it as a
        // malformed patch and let the caller's drop path handle the defective sentence.
        if (ScriptSentenceSplitter.Split(repaired.Text).Count > 1)
        {
            const string multiError = "RefineSentence returned multiple sentences for a single-sentence patch. Retaining original sentence.";
            logger.LogWarning("[RefineSentence] Sentence {Order} {Error}", sentence.Order, multiError);
            return Result<DraftedSentence>.Failure(multiError);
        }

        int replacementWords = WordCounter.Count(repaired.Text);

        // Deterministic word-count tolerance check:
        // Allow up to Math.Max(2, (int)Math.Ceiling(effectiveWordBudget * 0.15)) words expansion, capped at +3 words.
        // A minor addition (e.g. restoring a missing suburb or direction) adds 1-3 words legitimately;
        // larger expansions cause runaway inflation (measured Cairns 68->100 words, 36.9s on 22s slot).
        int maxAllowedWords = effectiveWordBudget + Math.Min(3, Math.Max(2, (int)Math.Ceiling(effectiveWordBudget * 0.15)));
        if (replacementWords > maxAllowedWords)
        {
            string error = $"Refined sentence inflated beyond word tolerance: {replacementWords} words vs budget {effectiveWordBudget} (max {maxAllowedWords}). Retaining original sentence.";
            logger.LogWarning("[RefineSentence] Sentence {Order} {Error}", sentence.Order, error);
            return Result<DraftedSentence>.Failure(error);
        }

        return Result<DraftedSentence>.Success(repaired);
    }


    public async Task<Result<ScriptReviewResult>> ReviewScriptAsync(
        string script,
        int targetDurationSeconds,
        int? targetWordCountOverride = null,
        string? rejectionFeedback = null,
        BroadcastWithTraffic? context = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DraftedSentence>? draftedSentences = null,
        IReadOnlyList<string>? removedSentences = null)
    {
        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
            return Result<ScriptReviewResult>.Failure(authHeader.Error);

        logger.LogInformation("[Review] Starting script review. Target={Target}s, ScriptLength={Length}",
            targetDurationSeconds, script.Length);

        int wordsPerMinute = _options.WordsPerMinute;
        int targetWordCount = targetWordCountOverride
                              ?? (int)Math.Round(targetDurationSeconds * wordsPerMinute / 60.0);

        // Deliberately NOT sent: the assembled script, the duration, the word target, the current
        // word count. Review judges SENTENCES now — nothing is assembled when it runs, so there is
        // no read to time and no budget to hold it to.
        //
        // Measured on Foundry traces 2026-08-25: review raised 1 defect across 34 bulletins while
        // 38 of 54 refine calls were "extend this sentence toward the word target". Handing a judge
        // a slot budget invites it to reason about fit, which is the fitter's job and is settled
        // deterministically from measured spoken duration — a number an LLM cannot estimate.
        // Selection is free: the fitter drops what it cannot use, so "too many sentences" is not a
        // defect and a reviewer that trims for length is destroying options it cannot price.
        var arguments = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(rejectionFeedback))
            arguments["rejectionFeedback"] = PromptSafe.StripDelimiters(rejectionFeedback);
        // Anti-whipsaw: sentences the loop already removed this run after a review proved them
        // unsupported. Without this the next review demands their return (observed live on dev job
        // 76f4f8d0, 2026-08-21: flagged → dropped → flagged for being gone → gate failure).
        if (removedSentences is { Count: > 0 })
            arguments["removedSentences"] = PromptSafe.WrapData(JsonSerializer.Serialize(removedSentences));
        if (draftedSentences is { Count: > 0 } &&
            draftedSentences.Any(sentence =>
                sentence.EvidenceIds?.Any(id => !string.IsNullOrWhiteSpace(id)) == true))
        {
            string structuredJson = JsonSerializer.Serialize(new
            {
                sentences = draftedSentences.Select(sentence => new
                {
                    order = sentence.Order,
                    evidenceIds = sentence.EvidenceIds,
                    text = sentence.Text
                })
            });
            arguments["draftedSentences"] = PromptSafe.WrapData(structuredJson);
        }

        // The reviewer's fabrication check needs ALL the data the draft saw — traffic, weather,
        // fuel, and good-run status. Filler statements are never sent here: they are ARN's own
        // editorial content, applied deterministically AFTER review passes (BroadcastOrchestrator),
        // so the reviewer has no opinion on them and never sees them.
        if (context is not null)
        {
            arguments["traffic"] = PromptSafe.WrapData(BulletinDataComposer.ComposeAllData(context));
            if (context.Weather.HasValue)
                arguments["includesWeather"] = true;
            if (context.Fuel.HasValue)
                arguments["includesFuel"] = true;
            // Continuity: the reviewer enforces that a re-read acknowledges ongoing/cleared items
            // (a "Still ongoing" item must not be re-introduced as fresh) — needs the same delta the draft saw.
            if (context.PreviousScript.HasValue && !string.IsNullOrWhiteSpace(context.PreviousScript.Value))
                arguments["hasPreviousBulletin"] = true;
            if (context.IncidentChanges.HasValue)
            {
                string changesSummary = IncidentChangeFormatter.BuildSummary(context.IncidentChanges, context.EditorialSettings);
                if (!string.IsNullOrWhiteSpace(changesSummary))
                    arguments["incidentChanges"] = changesSummary;
            }
            if (ResolveTimeOfDay(context.TimezoneId) is { } timeOfDay)
                arguments["timeOfDay"] = timeOfDay;
            var requiredSections = new List<string>();
            if (context.Content.RequiresWeather && context.Weather.HasValue)
                requiredSections.Add("weather");
            if (context.Content.RequiresFuel && context.Fuel.HasValue)
                requiredSections.Add("fuel");
            if (requiredSections.Count > 0)
                arguments["requiredSections"] = string.Join(" and ", requiredSections);
        }



        string renderedPrompt = HandlebarsTemplateRenderer.Render(PromptLibrary.ReviewTemplate, arguments);

        var result = await _transport.DispatchLlmAsync("Review", renderedPrompt, 1536, true, cancellationToken,
            authHeader.Value, hasProducerFeedback: !string.IsNullOrWhiteSpace(rejectionFeedback), jobId: context?.JobId,
            jsonSchema: null, systemInstructionsOverride: PromptLibrary.ReviewTemplate);

        if (result.IsFailure)
            return Result<ScriptReviewResult>.Failure(result.Error);

        var parsedReview = ModelResponseParser.ParseReviewResult(result.Value!);

        logger.LogInformation("[Review] Result: {Outcome}. Feedback preview: \"{Preview}\"",
            parsedReview.IsApproved ? "APPROVED" : "NEEDS_REVISION", ModelResponseParser.Truncate(parsedReview.Feedback, 200));

        return parsedReview;
    }


    public async Task<Result<ScriptSafetyResult>> ReviewScriptSafetyAsync(
        string script,
        string? jobId = null,
        CancellationToken cancellationToken = default)
    {
        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
            return Result<ScriptSafetyResult>.Failure(authHeader.Error);

        logger.LogInformation("[Safety] Starting script safety review. ScriptLength={Length}", script.Length);

        string renderedPrompt = HandlebarsTemplateRenderer.Render(
            PromptLibrary.SafetyTemplate, new Dictionary<string, object?> { ["script"] = script });

        // Safety uses its own configured deployment (defaults to the shared Model) for a
        // consistent safety bar across stations.
        var result = await _transport.DispatchLlmAsync("Safety", renderedPrompt, 1024, true, cancellationToken,
            authHeader.Value, hasProducerFeedback: false, jobId: jobId, jsonSchema: null,
            systemInstructionsOverride: PromptLibrary.SafetyTemplate);

        if (result.IsFailure)
            return Result<ScriptSafetyResult>.Failure(result.Error);

        var parsed = ParseSafetyResult(result.Value!);
        if (parsed.IsFailure)
            return parsed;

        logger.LogInformation("[Safety] Result: {Outcome}. Violations={Count}",
            parsed.Value.IsSafe ? "SAFE" : "BLOCKED", parsed.Value.Violations.Count);

        return parsed;
    }

    public async Task<Result<string>> DirectAsync(
        string plainScript,
        DeliveryStylePolicy policy,
        string? operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default)
    {
        // Fail-safe by contract: a neutral policy means no direction at all.
        if (string.IsNullOrWhiteSpace(plainScript) || policy is null || policy.IsNeutral)
            return Result<string>.Success(plainScript);

        // Only eleven_v3 interprets audio tags; every other model SPEAKS them aloud. That gate used
        // to skip the director entirely, which made a delivery style a silent no-op on the ~half of
        // stations running professional (multilingual_v2) voices. Punctuation and capitalisation are
        // model-agnostic, so on a non-v3 voice we keep directing with an EMPTY tag palette — which
        // prompts/director.md treats as a deliberate "punctuation and CAPS only" instruction.
        bool tagsSupported = DeliveryTags.SupportsTags(ttsModel);
        IReadOnlyList<string> allowedTags = tagsSupported ? policy.AllowedTags : [];
        if (!tagsSupported)
            logger.LogInformation(
                "[Director] TTS model '{Model}' is not v3; directing with punctuation only (no tags).",
                ttsModel ?? "(none)");

        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
        {
            logger.LogWarning("[Director] Auth failed ({Error}); using plain script.", authHeader.Error);
            return Result<string>.Success(plainScript);
        }

        var arguments = new Dictionary<string, object?>
        {
            ["script"] = plainScript,
            ["tone"] = policy.ToneDescription,
            // An EMPTY palette must be rendered as an explicit sentinel, never a blank. Measured
            // 2026-08-04: shown a blank list the model invents a tag from adjectives in the tone
            // ("Firm, clear and direct" -> "[firmly]") on 3 of 3 runs. The non-v3 strip below catches
            // it, but only after a warning and a wasted attempt; naming the absence removes the cause.
            ["tags"] = allowedTags.Count > 0
                ? string.Join("\n", allowedTags)
                : "(none — this read gets punctuation and capitalisation only)",
            ["intensity"] = policy.Intensity.ToString(CultureInfo.InvariantCulture),
            ["intent"] = string.IsNullOrWhiteSpace(operatorIntent) ? null : operatorIntent.Trim()
        };

        string renderedPrompt = HandlebarsTemplateRenderer.Render(PromptLibrary.DirectorTemplate, arguments);

        // The director LLM uses its own configured deployment (defaults to the shared Model)
        // for a consistent delivery bar across stations.
        // It must add tags WITHOUT changing any spoken word; the LLM occasionally rewrites/drops a
        // word (more likely at high intensity), which the word-immutability guard rejects. Retry one
        // genuinely invalid take with an escalating corrective nudge before giving up — a single bad
        // take must not silently drop the whole delivery style.
        const int maxDirectorAttempts = 2;
        const string correctionNudge =
            "\n\nCRITICAL: a previous attempt CHANGED, ADDED, DROPPED, or REORDERED a spoken word, " +
            "which is invalid. Output EVERY word of the script verbatim and in order — insert ONLY " +
            "audio tags, capitalisation, and punctuation. Do not alter a single word.";
        bool tagRequired = tagsSupported && allowedTags.Count > 0 && policy.Intensity >= 1;

        for (int attempt = 1; attempt <= maxDirectorAttempts; attempt++)
        {
            string prompt = attempt > 1 ? renderedPrompt + correctionNudge : renderedPrompt;
            var result = await _transport.DispatchLlmAsync("Director", prompt, 2048, true,
                cancellationToken, authHeader.Value, hasProducerFeedback: false, jobId: jobId,
                jsonSchema: null, systemInstructionsOverride: PromptLibrary.DirectorTemplate);
            if (result.IsFailure)
            {
                logger.LogWarning("[Director] Model call failed ({Error}); using plain script.", result.Error);
                return Result<string>.Success(plainScript);
            }

            var tagged = ParseDirectorResult(result.Value!);
            if (tagged is null)
            {
                logger.LogWarning("[Director] Unparseable response; using plain script.");
                return Result<string>.Success(plainScript);
            }

            // On a non-v3 voice a stray tag would be READ ALOUD, so an empty palette in the prompt is
            // not sufficient assurance — strip anything the model emitted anyway. Word-immutability is
            // checked after the strip so removing a tag can never be mistaken for an altered word.
            if (!tagsSupported)
            {
                string stripped = DeliveryTags.Strip(tagged);
                if (!string.Equals(stripped, tagged, StringComparison.Ordinal))
                    logger.LogWarning(
                        "[Director] Model emitted tag(s) despite an empty palette on non-v3 model '{Model}'; stripped.",
                        ttsModel ?? "(none)");
                tagged = stripped;
            }

            // Word-immutability guard: the director may only ADD tags/CAPS/punctuation. If a spoken
            // word changed, retry once; after the final attempt fall back to the plain script.
            if (DeliveryTags.WordsPreserved(plainScript, tagged))
            {
                int tagCount = DeliveryTags.Extract(tagged).Count;

                // A missing tag is a deterministic, low-value model shortfall. Add the first suitable
                // configured tag directly rather than paying for another round trip. Grave reads keep
                // their existing exception: if every configured tag is energetic, return untagged.
                if (tagRequired && tagCount == 0)
                {
                    string? defaultTag = SelectDefaultDirectorTag(plainScript, allowedTags);
                    if (defaultTag is not null)
                    {
                        tagged = defaultTag + " " + tagged;
                        tagCount = DeliveryTags.Extract(tagged).Count;
                        logger.LogInformation(
                            "[Director] Added configured default tag '{Tag}' after a word-preserved zero-tag response.",
                            defaultTag);
                    }
                }

                logger.LogInformation("[Director] Applied {TagCount} audio tag(s) on attempt {Attempt}/{Max}.",
                    tagCount, attempt, maxDirectorAttempts);
                return Result<string>.Success(tagged);
            }

            logger.LogWarning(
                "[Director] Word-immutability guard tripped on attempt {Attempt}/{Max} — director altered spoken words.",
                attempt, maxDirectorAttempts);
        }

        logger.LogWarning("[Director] All {Max} attempts altered spoken words; using plain script.",
            maxDirectorAttempts);
        return Result<string>.Success(plainScript);
    }


    private static string? SelectDefaultDirectorTag(string plainScript, IReadOnlyList<string> allowedTags)
    {
        bool grave = IsGraveDirectorRead(plainScript);
        foreach (string configuredTag in allowedTags)
        {
            string tag = configuredTag.Trim();
            if (DeliveryTags.Extract(tag).Count != 1)
                continue;

            if (!grave || !IsEnergeticDirectorTag(tag))
                return tag;
        }

        return null;
    }

    private static bool IsGraveDirectorRead(string script)
    {
        string lower = script.ToLowerInvariant();
        return lower.Contains("death", StringComparison.Ordinal) ||
            lower.Contains("fatal", StringComparison.Ordinal) ||
            lower.Contains("serious injur", StringComparison.Ordinal) ||
            lower.Contains("killed", StringComparison.Ordinal) ||
            lower.Contains("died", StringComparison.Ordinal);
    }

    private static bool IsEnergeticDirectorTag(string tag)
    {
        string label = DeliveryTags.Extract(tag)[0].ToLowerInvariant();
        return label.Contains("bright", StringComparison.Ordinal) ||
            label.Contains("cheer", StringComparison.Ordinal) ||
            label.Contains("energet", StringComparison.Ordinal) ||
            label.Contains("excited", StringComparison.Ordinal) ||
            label.Contains("enthusias", StringComparison.Ordinal) ||
            label.Contains("happy", StringComparison.Ordinal) ||
            label.Contains("joy", StringComparison.Ordinal) ||
            label.Contains("laugh", StringComparison.Ordinal) ||
            label.Contains("upbeat", StringComparison.Ordinal);
    }

    /// <summary>Parses the director JSON (<c>{"script":"…"}</c>); returns null on any malformed output.</summary>

    // ─── IDeliveryWriter ────────────────────────────────────────────────────────────────────────
    // Rewrites the copy into a requested register. The director cannot do this: it may only add
    // tags, and on this voice the words carry most of the effect (verified 2026-08-03). Because
    // this DOES change words, every output is validated here and then reviewed by the operator and
    // the safety reviewer before it can air.



    public async Task<Result<DeliveryRewrite>> RewriteAsync(
        string approvedScript,
        string operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null)
    {
        if (string.IsNullOrWhiteSpace(approvedScript))
            return Result<DeliveryRewrite>.Failure("There is no script to rewrite.");
        if (string.IsNullOrWhiteSpace(operatorIntent))
            return Result<DeliveryRewrite>.Failure("Describe the read you want in a sentence.");

        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
            return Result<DeliveryRewrite>.Failure($"Delivery writer unavailable: {authHeader.Error}");

        string plain = DeliveryTags.Strip(approvedScript);
        int before = WordCounter.Count(plain);

        // Only eleven_v3 interprets tags; every other voice SPEAKS them. The writer's strongest
        // measured levers - sentence splitting, clause endings, ellipses, CAPS - are all
        // model-agnostic, so a non-v3 voice still gets a real rewrite, just a tagless one. The
        // prompt is told (prompts/delivery-writer.md {{#if noTags}}) AND the output is stripped
        // below, because an instruction is not a guarantee.
        bool tagsSupported = DeliveryTags.SupportsTags(ttsModel);
        if (!tagsSupported)
            logger.LogInformation(
                "[DeliveryWriter] TTS model '{Model}' is not v3; rewriting with structure only (no tags).",
                ttsModel ?? "(none)");

        int preservedWords = Math.Max(0, wordBudget?.PreservedWordCount ?? 0);
        int fullCurrentWords = before + preservedWords;
        int fullTargetWords = wordBudget?.TargetWordCount > 0 ? wordBudget.TargetWordCount : fullCurrentWords;
        int fullMinWords = wordBudget?.MinWordCount > 0 ? wordBudget.MinWordCount : fullCurrentWords;
        int fullMaxWords = wordBudget?.MaxWordCount > 0 ? wordBudget.MaxWordCount : fullCurrentWords;
        int bodyTargetWords = Math.Max(1, fullTargetWords - preservedWords);
        int bodyMinWords = Math.Max(1, fullMinWords - preservedWords);
        int bodyMaxWords = Math.Max(bodyTargetWords, fullMaxWords - preservedWords);
        string lineWordCounts = string.Join(", ",
            ScriptSentenceSplitter.Split(plain)
                .Select((line, index) => $"line {index + 1}: {WordCounter.Count(line)}"));

        string rendered = HandlebarsTemplateRenderer.Render(PromptLibrary.DeliveryWriterTemplate,
            new Dictionary<string, object?>
            {
                ["script"] = plain,
                ["intent"] = operatorIntent.Trim(),
                ["noTags"] = !tagsSupported,
                ["currentWordCount"] = fullCurrentWords,
                ["targetWordCount"] = fullTargetWords,
                ["minWordCount"] = fullMinWords,
                ["maxWordCount"] = fullMaxWords,
                ["lineWordCounts"] = lineWordCounts,
                ["bodyTargetWordCount"] = bodyTargetWords,
                ["bodyMinWordCount"] = bodyMinWords,
                ["bodyMaxWordCount"] = bodyMaxWords,
            });

        const int maxAttempts = 3;
        string? lastProblem = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string prompt = lastProblem is null ? rendered
                : rendered + "\n\nA previous attempt was REJECTED: " + lastProblem +
                  " Fix that specific problem and return the corrected script.";

            var result = await _transport.DispatchLlmAsync("DeliveryWriter", prompt, 2048, true,
                cancellationToken, authHeader.Value, hasProducerFeedback: false, jobId: jobId,
                jsonSchema: null, systemInstructionsOverride: PromptLibrary.DeliveryWriterSystem);
            if (result.IsFailure)
                return Result<DeliveryRewrite>.Failure($"Delivery writer failed: {result.Error}");

            string? script = ParseDirectorResult(result.Value!);
            if (script is null) { lastProblem = "the response was not valid JSON of the form {\"script\": \"...\"}."; continue; }

            // Strip before the tag list is read, so a non-v3 rewrite reports the tags it ACTUALLY
            // has (none) rather than the ones the model tried to add - the operator's UI shows this.
            if (!tagsSupported)
            {
                string stripped = DeliveryTags.Strip(script);
                if (!string.Equals(stripped, script, StringComparison.Ordinal))
                    logger.LogWarning(
                        "[DeliveryWriter] Model emitted tag(s) for non-v3 model '{Model}'; stripped.",
                        ttsModel ?? "(none)");
                script = stripped;
            }

            var tags = DeliveryTags.Extract(script);
            var banned = DeliveryRewriteValidator.FindBannedTags(script);
            if (banned.Count > 0)
            {
                lastProblem = $"it used banned tag(s): {string.Join(", ", banned)}. Those air as literal noises or do nothing.";
                logger.LogWarning("[DeliveryWriter] Rejected attempt {Attempt}: banned tags {Tags}", attempt, string.Join(",", banned));
                continue;
            }

            int after = WordCounter.Count(DeliveryTags.Strip(script));
            int fullAfter = after + preservedWords;
            bool outsideBudget = wordBudget is { } requestedBudget
                ? fullAfter < requestedBudget.MinWordCount || fullAfter > requestedBudget.MaxWordCount
                : DeliveryRewriteValidator.ExceedsSlot(before, after);
            if (outsideBudget)
            {
                lastProblem = wordBudget is { } budget
                    ? $"it produced {fullAfter} words against the complete-read range {budget.MinWordCount}-{budget.MaxWordCount}; rewrite within that range without dropping facts."
                    : $"it grew the script from {before} to {after} spoken words, which will not fit the slot. Tighten existing wording.";
                logger.LogWarning("[DeliveryWriter] Rejected attempt {Attempt}: {Before}->{After} body words ({FullAfter} complete words)",
                    attempt, before, after, fullAfter);
                continue;
            }

            var missing = DeliveryRewriteValidator.FindMissingFacts(plain, script);
            if (missing.Count > 0)
            {
                lastProblem = $"it dropped or altered these facts: {string.Join(", ", missing)}. Every road name and number must survive verbatim.";
                logger.LogWarning("[DeliveryWriter] Rejected attempt {Attempt}: lost facts {Facts}", attempt, string.Join(",", missing));
                continue;
            }

            logger.LogInformation("[DeliveryWriter] Rewrote {Before}->{After} words with {Tags} tag(s) on attempt {Attempt}.",
                before, after, tags.Count, attempt);
            return Result<DeliveryRewrite>.Success(new DeliveryRewrite(script, before, after, tags, []));
        }

        return Result<DeliveryRewrite>.Failure($"Could not produce a usable rewrite: {lastProblem}");
    }

    // ─── IScriptRewriter ────────────────────────────────────────────────────────────────────────
    // Content rewrite: a deliberately lightweight editor pass. The operator instruction owns the
    // requested change; the reference data is supplied only so names, places and figures are
    // intelligible in context. The caller owns safety and final human review.


    public async Task<Result<ScriptRewrite>> RewriteAsync(
        string currentScript,
        string operatorInstruction,
        BroadcastWithTraffic context,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null)
    {
        if (string.IsNullOrWhiteSpace(currentScript))
            return Result<ScriptRewrite>.Failure("There is no script to rewrite.");
        if (string.IsNullOrWhiteSpace(operatorInstruction))
            return Result<ScriptRewrite>.Failure("Describe the change you want in a sentence.");

        var authHeader = await _transport.GetAuthHeaderAsync(cancellationToken);
        if (authHeader.IsFailure)
            return Result<ScriptRewrite>.Failure($"Rewrite service unavailable: {authHeader.Error}");

        string plain = DeliveryTags.Strip(currentScript);
        if (string.IsNullOrWhiteSpace(plain))
            return Result<ScriptRewrite>.Failure("There is no script to rewrite.");
        int before = WordCounter.Count(plain);

        // Keep the payload aligned with the conversational workflow: the same atomic evidence units,
        // grouped by source and rendered as field/value facts. It is reference context, not a
        // grounding rubric or a second editorial policy.
        string referenceData = PromptSafe.WrapData(BulletinDataComposer.ComposeAllData(context));
        int fullCurrentWords = before + Math.Max(0, wordBudget?.PreservedWordCount ?? 0);
        int fullTargetWords = wordBudget?.TargetWordCount > 0 ? wordBudget.TargetWordCount : fullCurrentWords;
        int fullMinWords = wordBudget?.MinWordCount > 0 ? wordBudget.MinWordCount : fullCurrentWords;
        int fullMaxWords = wordBudget?.MaxWordCount > 0 ? wordBudget.MaxWordCount : fullCurrentWords;
        int preservedWords = Math.Max(0, wordBudget?.PreservedWordCount ?? 0);
        int bodyTargetWords = Math.Max(1, fullTargetWords - preservedWords);
        int bodyMinWords = Math.Max(1, fullMinWords - preservedWords);
        int bodyMaxWords = Math.Max(bodyTargetWords, fullMaxWords - preservedWords);
        string lineWordCounts = string.Join(", ",
            ScriptSentenceSplitter.Split(plain)
                .Select((line, index) => $"line {index + 1}: {WordCounter.Count(line)}"));
        string rendered = HandlebarsTemplateRenderer.Render(PromptLibrary.RewriteTemplate,
            new Dictionary<string, object?>
            {
                ["script"] = plain,
                ["instruction"] = PromptSafe.StripDelimiters(operatorInstruction).Trim(),
                ["evidence"] = referenceData,
                ["currentWordCount"] = fullCurrentWords,
                ["targetWordCount"] = fullTargetWords,
                ["minWordCount"] = fullMinWords,
                ["maxWordCount"] = fullMaxWords,
                ["lineWordCounts"] = lineWordCounts,
                ["currentBodyWordCount"] = before,
                ["bodyTargetWordCount"] = bodyTargetWords,
                ["bodyMinWordCount"] = bodyMinWords,
                ["bodyMaxWordCount"] = bodyMaxWords,
            });

        const int maxAttempts = 3;
        string? lastProblem = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string prompt = lastProblem is null ? rendered
                : rendered + "\n\nA previous attempt was REJECTED: " + lastProblem +
                  " Fix that specific problem and return the corrected script.";

            var result = await _transport.DispatchLlmAsync("Rewrite", prompt, 2048, true,
                cancellationToken, authHeader.Value, hasProducerFeedback: false, jobId: jobId,
                jsonSchema: null, systemInstructionsOverride: PromptLibrary.RewriteSystem);
            if (result.IsFailure)
                return Result<ScriptRewrite>.Failure($"Rewrite failed: {result.Error}");

            string? script = ParseDirectorResult(result.Value!);
            if (script is null)
            {
                lastProblem = "the response was not valid JSON of the form {\"script\": \"...\"}.";
                continue;
            }
            if (string.IsNullOrWhiteSpace(script))
            {
                lastProblem = "the response contained an empty script.";
                continue;
            }

            // This is a CONTENT pass: audio tags are delivery, applied separately by the tone
            // palette or the delivery style. Strip any the model volunteered so the preview is
            // always plain text for the operator to diff and edit.
            string stripped = DeliveryTags.Strip(script).Trim();
            if (!string.Equals(stripped, script, StringComparison.Ordinal))
                logger.LogWarning(
                    "[ScriptRewriter] Model emitted audio tag(s); stripped for content preview.");
            script = stripped;

            int after = WordCounter.Count(script);
            logger.LogInformation(
                "[ScriptRewriter] Rewrote {Before}->{After} words on attempt {Attempt}.",
                before, after, attempt);
            return Result<ScriptRewrite>.Success(new ScriptRewrite(script, before, after));
        }

        return Result<ScriptRewrite>.Failure($"Could not produce a usable rewrite: {lastProblem}");
    }


    private static string? ParseDirectorResult(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("script", out var scriptProp) &&
                scriptProp.ValueKind == JsonValueKind.String)
            {
                var value = scriptProp.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return null;
    }

    /// <summary>
    ///     Parses the safety-review JSON. Unparseable output is a Failure — the orchestrator
    ///     treats reviewer failure as fail-open (wordlist passed, human gate remains), so a
    ///     malformed response must never be misread as either "safe" or "blocked".
    /// </summary>
    private static Result<ScriptSafetyResult> ParseSafetyResult(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;

            if (!root.TryGetProperty("safe", out var safeProp) ||
                safeProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Result<ScriptSafetyResult>.Failure(
                    $"Safety review response missing boolean 'safe' property: {ModelResponseParser.Truncate(response, 200)}");

            if (safeProp.GetBoolean())
                return Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe());

            var violations = new List<string>();
            if (root.TryGetProperty("violations", out var violationsProp) &&
                violationsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in violationsProp.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        string? s = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) violations.Add(s);
                        continue;
                    }

                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string category = entry.TryGetProperty("category", out var c) ? c.GetString() ?? "unspecified" : "unspecified";
                    string reason = entry.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                    string text = entry.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    violations.Add(string.IsNullOrWhiteSpace(text)
                        ? $"{category}: {reason}"
                        : $"{category}: {reason} (\"{text}\")");
                }
            }

            if (violations.Count == 0)
                violations.Add("Safety reviewer blocked the script without naming a violation.");

            return Result<ScriptSafetyResult>.Success(new ScriptSafetyResult(false, violations));
        }
        catch (JsonException ex)
        {
            return Result<ScriptSafetyResult>.Failure(
                $"Safety review response was not valid JSON ({ex.Message}): {ModelResponseParser.Truncate(response, 200)}");
        }
    }




    private Result<DraftedSentence> TryParseRefineSentence(
        string? raw,
        DraftedSentence originalSentence,
        IReadOnlyList<EvidenceUnit> allowedEvidence,
        Func<string, string> normalize)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result<DraftedSentence>.Failure("RefineSentence returned empty output.");

        try
        {
            // Supports the single RefineSentence envelope {"order":1,...}, the wrapped
            // {"sentences":[...]} form, and the draft-shaped {"items":[...]} form the live
            // sentence agent intermittently answers with (observed 2026-08-21): the variant
            // content is identical, so rejecting the envelope would discard a correct repair.
            RefineSentence? sentence = null;
            if (raw.Contains("\"sentences\"", StringComparison.OrdinalIgnoreCase))
            {
                var wrapped = JsonSerializer.Deserialize<RefineResponse>(raw);
                sentence = wrapped?.Sentences?.FirstOrDefault(s => s.Order == originalSentence.Order)
                    ?? wrapped?.Sentences?.FirstOrDefault();
            }
            else if (raw.Contains("\"items\"", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(raw);
                var firstItem = doc.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
                if (firstItem.ValueKind == JsonValueKind.Object)
                {
                    var ids = firstItem.TryGetProperty("evidenceIds", out var idsEl)
                        ? idsEl.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!)
                            .ToList()
                        : [];
                    var itemVariants = firstItem.TryGetProperty("variants", out var vsEl)
                        ? vsEl.EnumerateArray()
                            .Where(v => v.ValueKind == JsonValueKind.Object
                                && v.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                            .Select(v => new RefineVariant(
                                v.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.String
                                    ? st.GetString() ?? "standard"
                                    : "standard",
                                v.GetProperty("text").GetString()!))
                            .ToList()
                        : [];
                    sentence = new RefineSentence(originalSentence.Order, ids, Variants: itemVariants);
                }
            }
            else
            {
                sentence = JsonSerializer.Deserialize<RefineSentence>(raw);
            }

            if (sentence is null)
                return Result<DraftedSentence>.Failure("RefineSentence returned null payload.");

            var allowedIds = allowedEvidence.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
            var citedIds = sentence.EvidenceIds ?? [];

            // A refine is scoped by the CALLER: this method supplied `allowedEvidence`, and the
            // sentence being repaired already carries the ids it asserts. The model's citation is
            // therefore our own input echoed back — it carries no information we do not already
            // hold, and asking for it only creates a way to be wrong.
            //
            // It was previously a hard reject, and it fired constantly. RenderEvidenceUnits prints a
            // human-readable group heading ("M5, Kingsgrove") on its own line directly above the
            // citable "[r3:suburb]" ids, so models cite the heading. Measured over the captured
            // corpus (scripts/agent-io-viewer.py): 12 of 29 refine calls — 41% — returned a correct,
            // well-grounded repair that was thrown away on that mismatch alone, and the DEFECTIVE
            // sentence was retained in its place. Job cb5ada8a spent 12 refine calls this way.
            //
            // Resolve instead of reject, in descending order of evidence: what the model cited and
            // we recognise; else what the sentence already asserted; else the scope we supplied.
            // Every branch stays inside `allowedIds`, so a citation can still never widen the
            // sentence's evidence beyond what the caller permitted.
            var validIds = citedIds
                .Distinct(StringComparer.Ordinal)
                .Where(allowedIds.Contains)
                .ToList();
            if (validIds.Count == 0)
                validIds = originalSentence.EvidenceIds
                    .Distinct(StringComparer.Ordinal)
                    .Where(allowedIds.Contains)
                    .ToList();
            if (validIds.Count == 0)
                validIds = allowedEvidence.Select(u => u.Id).Distinct(StringComparer.Ordinal).ToList();

            var variants = sentence.Variants?
                .Where(v => !string.IsNullOrWhiteSpace(v.Text))
                .Select(v => new DraftedVariant(
                    string.IsNullOrWhiteSpace(v.Style) ? "standard" : v.Style.Trim().ToLowerInvariant(),
                    v.Text.Trim()))
                // "continuation" is a phrasing that presents the item as following a related one ("and
                //  there's another on ..."). Without it on the menu no selection can produce a
                //  cohesive read, because every other variant is written as a standalone sentence.
                .Where(v => v.Style is "concise" or "standard" or "expanded" or "continuation")
                .ToList() ?? [];

            if (variants.Count == 0 && !string.IsNullOrWhiteSpace(sentence.Text))
                variants.Add(new DraftedVariant("standard", sentence.Text.Trim()));

            if (variants.Count == 0)
                return Result<DraftedSentence>.Failure("RefineSentence returned no text or variants.");

            var normalizedVariants = variants
                .Select(v => new DraftedVariant(v.Style, normalize(v.Text)))
                .ToList();
            string preferred = normalizedVariants.FirstOrDefault(v => v.Style == "standard")?.Text
                ?? normalizedVariants[0].Text;

            int order = sentence.Order > 0 ? sentence.Order : originalSentence.Order;
            return Result<DraftedSentence>.Success(new DraftedSentence(
                order,
                preferred,
                validIds,
                normalizedVariants,
                CorridorKeysFor(validIds, allowedEvidence)));
        }
        catch (JsonException ex)
        {
            return Result<DraftedSentence>.Failure($"RefineSentence returned malformed JSON: {ex.Message}");
        }
    }
    private static IReadOnlyList<string> CorridorKeysFor(
        IReadOnlyList<string> evidenceIds,
        IReadOnlyList<EvidenceUnit> evidenceUnits) =>
        evidenceUnits
            .Where(unit => evidenceIds.Contains(unit.Id, StringComparer.Ordinal)
                && unit.SourceKey.StartsWith("corridor:", StringComparison.OrdinalIgnoreCase))
            .Select(unit => unit.SourceKey["corridor:".Length..])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    private string NormalizeRefineText(string text, BroadcastWithTraffic context) =>
        SpokenFormNormalizer.Normalize(text.Trim(), ResolveTimeOfDay(context.TimezoneId));


}
