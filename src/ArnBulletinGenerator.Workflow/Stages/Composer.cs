using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Writes the complete read after code has fixed both its semantic content and its order. Keeping
///     composition whole is deliberate: a connective, an ellipsis, or a carried subject is meaningful
///     only in the discourse around it, so sentence-level assembly would recreate the old repeated
///     road-name/suburb/condition rhythm and strand joins when the fitter changes the set.
/// </summary>
public sealed class Composer(IWorkflowLlm llm)
{
    public Task<ComposedRead> ComposeAsync(
        WorkflowRequest request,
        NormalizedInput input,
        FittedPlan plan,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(RenderPrompt(request, input, plan, current: null, problems: null), input, plan, trace, cancellationToken);

    public Task<ComposedRead> ReviseAsync(
        WorkflowRequest request,
        NormalizedInput input,
        FittedPlan plan,
        ComposedRead current,
        IReadOnlyList<string> problems,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(RenderPrompt(request, input, plan, current, problems), input, plan, trace, cancellationToken);

    private async Task<ComposedRead> CompleteAsync(
        string userPrompt,
        NormalizedInput input,
        FittedPlan plan,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        const string role = "compose";
        string systemPrompt = PromptLibrary.System(role);
        // Live evidence 2026-08-26: the model faithfully copied rendered ids but tacked on the
        // prompt's trailing colon ('r5:direction:'), the parser called them hallucinated, and
        // CitationAuthorised then failed because every citation had been stripped. Constraining
        // evidenceIds to an enum of exactly the authorized ids makes a malformed id structurally
        // impossible instead of relying on the model's punctuation instincts.
        HashSet<string> authorizedEvidence = AuthorizedEvidence(input, plan);
        string schema = BuildRequestSchema(authorizedEvidence);
        var stopwatch = Stopwatch.StartNew();
        LlmReply reply;

        try
        {
            reply = await llm.CompleteAsync(role, systemPrompt, userPrompt, schema, cancellationToken)
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
            trace.LlmCall("Compose", new LlmCallRecord(
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
            return ComposedRead.Empty;
        }

        stopwatch.Stop();
        trace.LlmCall("Compose", new LlmCallRecord(
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
            return ComposedRead.Empty;
        }

        try
        {
            return ParseRead(reply.Text, authorizedEvidence, trace);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            trace.Failure("Compose", "Composer response could not be parsed", exception.Message);
            return ComposedRead.Empty;
        }
    }

    private static ComposedRead ParseRead(string rawResponse, IReadOnlySet<string> authorizedEvidence, TraceRecorder trace)
    {
        using JsonDocument document = JsonDocument.Parse(rawResponse);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sentences", out JsonElement sentences) ||
            sentences.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Response must be an object with a sentences array.");
        }

        // The authorized set, not the whole input, is what the plan legally permits the read to
        // cite; the fitter already narrowed the world to these ids.
        List<ComposedSentence> parsed = [];
        List<string> unresolved = [];

        int sourcePosition = 0;

        foreach (JsonElement sentenceElement in sentences.EnumerateArray())
        {
            sourcePosition++;
            if (sentenceElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Sentence {sourcePosition} is not an object.");

            string text = string.Empty;
            if (sentenceElement.TryGetProperty("text", out JsonElement textElement))
            {
                if (textElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException($"Sentence {sourcePosition} has a non-string text value.");
                text = textElement.GetString() ?? string.Empty;
            }
            text = text.Trim();

            string? storyId = null;
            if (sentenceElement.TryGetProperty("storyId", out JsonElement storyElement))
            {
                if (storyElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new JsonException($"Sentence {sourcePosition} has a non-string storyId value.");
                storyId = storyElement.GetString();
            }

            List<string> evidenceIds = [];
            HashSet<string> seenEvidence = new(StringComparer.Ordinal);
            if (sentenceElement.TryGetProperty("evidenceIds", out JsonElement evidenceElement))
            {
                if (evidenceElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                    throw new JsonException($"Sentence {sourcePosition} has a non-array evidenceIds value.");

                if (evidenceElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement evidenceIdElement in evidenceElement.EnumerateArray())
                    {
                        if (evidenceIdElement.ValueKind != JsonValueKind.String)
                            throw new JsonException($"Sentence {sourcePosition} contains a non-string evidence id.");

                        string evidenceId = NormalizeCitationId(evidenceIdElement.GetString() ?? string.Empty);
                        if (evidenceId.Length == 0)
                            continue;
                        if (!seenEvidence.Add(evidenceId))
                            continue;

                        if (!authorizedEvidence.Contains(evidenceId))
                        {
                            // Live evidence 2026-08-26: stripping an unresolvable id kept the claim
                            // and destroyed its provenance, so CitationAuthorised failed later with
                            // "cites no evidence IDs" - backwards from a safety standpoint. A
                            // citation that survives normalisation but still does not resolve means
                            // the whole attempt is malformed: fail it loudly and let the caller
                            // retry or degrade. Never return a sentence whose citations were cut.
                            unresolved.Add($"sentence {sourcePosition}: '{evidenceId}'");
                            continue;
                        }

                        evidenceIds.Add(evidenceId);
                    }
                }
            }

            if (text.Length == 0)
                continue;
            parsed.Add(new ComposedSentence(parsed.Count + 1, text, evidenceIds, storyId));
        }
        if (unresolved.Count > 0)
        {
            trace.Failure(
                "Compose",
                "Composer cited evidence ids that do not resolve against the authorized set",
                string.Join("; ", unresolved));
            return ComposedRead.Empty;
        }


        var artifact = parsed.ToDictionary(
            sentence => sentence.Order.ToString(CultureInfo.InvariantCulture),
            sentence => $"{sentence.Text}  [{string.Join(", ", sentence.EvidenceIds)}]",
            StringComparer.Ordinal);
        trace.Artifact("Compose", "Read", artifact);
        return new ComposedRead(parsed);
    }

    /// <summary>
    ///     Live run 2026-08-26: the model copied rendered ids verbatim including the prompt's
    ///     trailing colon ('r5:direction:'), because the id sat flush against a bare colon in the
    ///     prompt text. Trimming whitespace and trailing ':' or ',' recovers a faithful copy that
    ///     merely absorbed boundary punctuation; only ids that STILL fail to match after this are
    ///     treated as malformed output.
    /// </summary>
    private static string NormalizeCitationId(string raw) =>
        raw.Trim().TrimEnd(':', ',').Trim();

    /// <summary>
    ///     The set of evidence ids the composed read may cite: exactly what the fitter authorized.
    ///     Falls back to every input fact only when the plan authorizes nothing, so an empty or
    ///     degenerate fit cannot silently widen the legal set.
    /// </summary>
    private static HashSet<string> AuthorizedEvidence(NormalizedInput input, FittedPlan plan)
    {
        HashSet<string> authorized = plan.Stories
            .SelectMany(story => story.AuthorizedEvidenceIds)
            .ToHashSet(StringComparer.Ordinal);
        if (authorized.Count > 0)
            return authorized;

        return input.Events
            .SelectMany(e => e.Facts)
            .Select(f => f.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Per-request copy of the static compose schema whose evidenceIds items carry an enum of
    ///     exactly this bulletin's authorized ids, so a malformed id is structurally impossible
    ///     rather than merely discouraged. Injection failure falls back to the static asset:
    ///     schema hardening must never be able to fail composition on its own.
    /// </summary>
    private static string BuildRequestSchema(IReadOnlySet<string> authorizedEvidence)
    {
        if (authorizedEvidence.Count == 0)
            return PromptLibrary.Schema("compose");

        try
        {
            JsonObject root = JsonNode.Parse(PromptLibrary.Schema("compose"))!.AsObject();
            JsonObject items = root["properties"]!["sentences"]!["properties"]!["evidenceIds"]!["items"]!.AsObject();
            items["type"] = "string";
            var allowed = new JsonArray();
            foreach (string id in authorizedEvidence)
                allowed.Add(JsonValue.Create(id));
            items["enum"] = allowed;
            return root.ToJsonString();
        }
        catch (Exception)
        {
            return PromptLibrary.Schema("compose");
        }
    }

    private static string RenderPrompt(
        WorkflowRequest request,
        NormalizedInput input,
        FittedPlan plan,
        ComposedRead? current,
        IReadOnlyList<string>? problems)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"TARGET WORD COUNT: {plan.TargetWords.ToString(CultureInfo.InvariantCulture)}");
        prompt.AppendLine($"SLOT SECONDS: {request.TargetSeconds.ToString("0.##", CultureInfo.InvariantCulture)}");

        // Voice policy governs LANGUAGE and is the only configuration this stage may see. It can
        // never change WHAT is voiced: the fitted plan decided that, and a wording preference must
        // not be able to delete a fact the slot has already paid for.
        string voice = PolicyRenderer.ForComposer(request.EffectivePolicy.Voice);
        if (!string.IsNullOrWhiteSpace(voice))
        {
            prompt.AppendLine();
            prompt.AppendLine(voice);
            prompt.AppendLine();
        }
        else
        {
            // Legacy undifferentiated strings, retained until callers supply a StationPolicy.
            if (!string.IsNullOrWhiteSpace(request.HouseStyle))
                prompt.AppendLine($"STATION HOUSE STYLE: {request.HouseStyle}");
            if (!string.IsNullOrWhiteSpace(request.RoadFeelPalette))
                prompt.AppendLine($"ROAD-FEEL PALETTE: {request.RoadFeelPalette}");
        }
        if (!string.IsNullOrWhiteSpace(request.PreviousScript))
        {
            prompt.AppendLine("PREVIOUS SCRIPT (continuity reference):");
            prompt.AppendLine(request.PreviousScript);
        }

        prompt.AppendLine();
        prompt.AppendLine("THE FINAL PLAN (ordered list; positions are 1-based):");
        IReadOnlyDictionary<string, EventFact> facts = BuildFactIndex(input);
        for (int index = 0; index < plan.Stories.Count; index++)
        {
            FittedStory story = plan.Stories[index];
            string relationship = string.IsNullOrWhiteSpace(story.Relationship)
                ? string.Empty
                : $"; relationship={story.Relationship}";
            prompt.AppendLine(
                $"{index + 1}. storyId={story.StoryId}; role={story.Role}{relationship}");
            prompt.AppendLine($"   eventIds: {string.Join(", ", story.EventIds)}");
            // Live evidence 2026-08-26: "evidence this story MAY cite" let the composer write 54
            // words against an 85-word budget and started a shrinking treadmill. The fitter spent
            // budget on MustExpressEvidenceIds, so they are an obligation, not an offer, and the
            // two sets are rendered separately and labelled by obligation.
            prompt.AppendLine("   MUST VOICE (the slot is paying for these facts; every one reaches the read):");
            AppendStoryFacts(prompt, facts, story.MustExpressEvidenceIds);
            List<string> optional = story.EvidenceIds
                .Except(story.MustExpressEvidenceIds, StringComparer.Ordinal)
                .ToList();
            if (optional.Count > 0)
            {
                prompt.AppendLine("   MAY ALSO CITE (optional colour; cite only if voiced):");
                AppendStoryFacts(prompt, facts, optional);
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("FINALITY: This is the final content in the final order. Write every listed story once, in this order.");
        prompt.AppendLine("Nothing downstream reorders stories, drops one, splits a sentence, or rewrites one sentence in isolation.");

        if (current is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("REVISION MODE: Rewrite the WHOLE read to fix every listed problem.");
            prompt.AppendLine("THE FACT SET IS FROZEN: use only the supplied evidence and do not add an unsupported fact or drop a supplied story.");
            prompt.AppendLine("You may re-order words, re-join, re-split, and re-frame sentences freely, while preserving the final story order above.");
            prompt.AppendLine("CURRENT READ:");
            foreach (ComposedSentence sentence in current.Sentences)
            {
                string story = sentence.StoryId is null ? string.Empty : $" [storyId={sentence.StoryId}]";
                prompt.AppendLine(
                    $"{sentence.Order}. {sentence.Text}{story} | cites: {string.Join(", ", sentence.EvidenceIds)}");
            }

            prompt.AppendLine("PROBLEMS TO FIX:");
            if (problems is null || problems.Count == 0)
                prompt.AppendLine("- (none supplied; preserve the read while respecting the frozen fact set)");
            else
                foreach (string problem in problems)
                    prompt.AppendLine($"- {problem}");

            // Live evidence 2026-08-26: whole-read revisions satisfied checks by simply saying
            // less (66 -> 59 -> 52 words). Re-rendering the must-voice set at the moment of repair
            // pins the constraint where the shrink actually happens.
            prompt.AppendLine("MUST VOICE IN THIS REWRITE (the fitted set is FROZEN; no problem may be fixed by dropping one):");
            AppendMustVoice(prompt, facts, plan.Stories);
        }

        return prompt.ToString();
    }

    /// <summary>
    ///     Renders each fact with its id quoted and never adjacent to a bare colon. Live run
    ///     2026-08-26 rendered ids like "- r5:direction: field=..." so the model copied the
    ///     trailing colon as part of the id and every citation was rejected. Quoted ids make a
    ///     boundary-punctuation mistake structurally unlikely.
    /// </summary>
    private static void AppendStoryFacts(
        StringBuilder prompt,
        IReadOnlyDictionary<string, EventFact> facts,
        IReadOnlyList<string> evidenceIds)
    {
        foreach (string evidenceId in evidenceIds)
        {
            if (facts.TryGetValue(evidenceId, out EventFact? fact))
                prompt.AppendLine($"   - id=\"{evidenceId}\"  field={fact.Field}  value=\"{fact.Value.Replace("\"", "'")}\"");
            else
                prompt.AppendLine($"   - id=\"{evidenceId}\"  field=<unresolved>  value=<unresolved>  DO NOT CITE");
        }
    }

    private static void AppendMustVoice(
        StringBuilder prompt,
        IReadOnlyDictionary<string, EventFact> facts,
        IReadOnlyList<FittedStory> stories)
    {
        foreach (FittedStory story in stories)
        {
            if (story.MustExpressEvidenceIds.Count == 0)
                continue;
            prompt.AppendLine($"   storyId={story.StoryId}:");
            AppendStoryFacts(prompt, facts, story.MustExpressEvidenceIds);
        }
    }

    private static IReadOnlyDictionary<string, EventFact> BuildFactIndex(NormalizedInput input)
    {
        var facts = new Dictionary<string, EventFact>(StringComparer.Ordinal);
        foreach (EventFact fact in input.Events.SelectMany(e => e.Facts))
            facts.TryAdd(fact.EvidenceId, fact);
        return facts;
    }
}
