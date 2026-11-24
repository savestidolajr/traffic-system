using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

    /// <summary>
    ///     Asks the planning role to decide the semantic content and order of a read before any prose
    ///     exists. The event-shaped prompt is intentional: facts that belong to one real-world event
    ///     must be visible together, otherwise a model can create a relationship by flattening unrelated
    ///     evidence lines. Groups are emitted as candidates with independently-priced members, not
    ///     commitments: on 2026-08-26 seven minor-road events fused into one indivisible 71-word story,
    ///     the fitter's all-or-nothing choice dropped all of them, and the read ran 15 seconds under
    ///     slot. Planning identifies what could be voiced together; fitting decides how many the slot
    ///     affords; composition decides the wording. The resulting plan is subsequently fitted by code,
    ///     not by this model.
    /// </summary>
public sealed class Planner(IWorkflowLlm llm)
{
    private const string Stage = "Plan";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Produces a defensive semantic plan. A malformed response is an observable workflow
    ///     failure, but never an exception escaping the stage: downstream code receives an empty
    ///     plan and the trace retains the complete request and model response for diagnosis.
    /// </summary>
    public async Task<BulletinPlan> PlanAsync(
        WorkflowRequest request,
        NormalizedInput input,
        TraceRecorder trace,
        CancellationToken cancellationToken = default)
    {
        trace.StageStart(Stage, $"Planning {input.Events.Count} events");
        var stopwatch = Stopwatch.StartNew();
        string userPrompt = RenderUserPrompt(request, input);
        string systemPrompt = string.Empty;
        string schema = string.Empty;
        LlmReply? reply = null;

        try
        {
            systemPrompt = PromptLibrary.System("plan");
            schema = PromptLibrary.Schema("plan");
            LlmReply result = await llm.CompleteAsync("plan", systemPrompt, userPrompt, schema, cancellationToken)
                .ConfigureAwait(false);
            reply = result;

            if (!result.IsSuccess)
            {
                string error = result.Error ?? "The planning model returned a failure.";
                trace.LlmCall(Stage, new LlmCallRecord(
                    "plan",
                    result.Model,
                    systemPrompt,
                    userPrompt,
                    result.Text,
                    stopwatch.Elapsed.TotalMilliseconds,
                    result.PromptTokens,
                    result.CompletionTokens,
                    result.Error,
                    result.CachedInputTokens,
                    result.ReasoningTokens,
                    result.Attempt,
                    result.StatusCode,
                    result.ViaFallback,
                    result.DeadlineUtc,
                    result.AttemptElapsedMs,
                    result.Stalled,
                    result.ResponseId));
                trace.Failure(Stage, "Planning model failed", error);
                return Finish(BulletinPlan.Empty, trace, stopwatch);
            }
            trace.LlmCall(Stage, new LlmCallRecord(
                "plan",
                result.Model,
                systemPrompt,
                userPrompt,
                result.Text,
                stopwatch.Elapsed.TotalMilliseconds,
                result.PromptTokens,
                result.CompletionTokens,
                result.Error,
                result.CachedInputTokens,
                result.ReasoningTokens,
                result.Attempt,
                result.StatusCode,
                result.ViaFallback,
                result.DeadlineUtc,
                result.AttemptElapsedMs,
                result.Stalled,
                result.ResponseId));

            BulletinPlan plan;
            try
            {
                plan = ParsePlan(result.Text, input, trace);
            }
            catch (Exception exception)
            {
                trace.Failure(Stage, "Could not parse planning response", exception.Message);
                return Finish(BulletinPlan.Empty, trace, stopwatch);
            }

            return Finish(plan, trace, stopwatch);

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // This also covers a transport exception and a missing prompt asset. Keep the prompts
            // that were successfully rendered, and make the failed call visible in the trace.
            trace.LlmCall(Stage, new LlmCallRecord(
                "plan",
                reply?.Model ?? string.Empty,
                systemPrompt,
                userPrompt,
                reply?.Text ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds,
                reply?.PromptTokens,
                reply?.CompletionTokens,
                reply?.Error ?? exception.Message,
                reply?.CachedInputTokens,
                reply?.ReasoningTokens,
                reply?.Attempt ?? 1,
                reply?.StatusCode,
                reply?.ViaFallback ?? false,
                reply?.DeadlineUtc,
                reply?.AttemptElapsedMs,
                reply?.Stalled ?? false,
                reply?.ResponseId));
            trace.Failure(Stage, "Planning call failed", exception.Message);
            return Finish(BulletinPlan.Empty, trace, stopwatch);
        }
    }

    private static BulletinPlan Finish(BulletinPlan plan, TraceRecorder trace, Stopwatch stopwatch)
    {
        var artifact = plan.Stories.ToDictionary(
            story => story.StoryId,
            story =>
            {
                string summary =
                    $"{RoleName(story.Role)} p{story.Priority} [{string.Join(", ", story.EventIds)}] " +
                    $"req={story.RequiredFacts.Count} opt={story.OptionalFacts.Count} {story.Relationship ?? string.Empty}";

                // A grouped story's whole point is that the fitter can voice a subset of its
                // members (on 2026-08-26 seven minor-road events fused into one indivisible
                // 71-word story and were dropped wholesale while the read ran 15s short), so
                // the trace artifact lists each member with its own price signal.
                if (story.Members is { Count: > 0 } members)
                {
                    summary += " members={" + string.Join(
                        "; ",
                        members.Select(member => $"{member.MemberId}: {member.EventId} p{member.Priority}")) +
                        "}";
                }

                return summary;
            },
            StringComparer.Ordinal);

        trace.Artifact(Stage, "Stories", artifact);
        trace.StageEnd(Stage, $"Planned {plan.Stories.Count} stories", stopwatch.Elapsed.TotalMilliseconds);
        return plan;
    }

    private static BulletinPlan ParsePlan(string rawJson, NormalizedInput input, TraceRecorder trace)
    {
        var payload = JsonSerializer.Deserialize<PlanPayload>(rawJson, JsonOptions)
            ?? throw new JsonException("The planning response was null.");
        if (payload.Stories is null)
        {
            throw new JsonException("The planning response did not contain stories.");
        }

        var knownEvents = input.Events
            .Select(eventItem => eventItem.EventId)
            .ToHashSet(StringComparer.Ordinal);
        var knownEvidence = input.Events
            .SelectMany(eventItem => eventItem.EvidenceIds)
            .ToHashSet(StringComparer.Ordinal);
        var stories = new List<PlannedStory>(payload.Stories.Count);
        var storyIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (StoryPayload? candidate in payload.Stories)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.StoryId))
            {
                trace.Decision(Stage, "Dropped story without a storyId");
                continue;
            }

            string storyId = candidate.StoryId!;

            var eventIds = (candidate.EventIds ?? [])
                .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (eventIds.Count == 0)
            {
                trace.Decision(Stage, $"Dropped story '{storyId}' without eventIds");
                continue;
            }

            if (!storyIds.Add(storyId))
            {
                trace.Decision(Stage, $"Dropped duplicate storyId '{storyId}'");
                continue;
            }

            string? unknownEventId = eventIds.FirstOrDefault(eventId => !knownEvents.Contains(eventId));
            if (unknownEventId is not null)
            {
                trace.Decision(Stage, $"Dropped story '{storyId}' citing unknown eventId '{unknownEventId}'");
                continue;
            }

            var requiredFacts = (candidate.RequiredFacts ?? [])
                .Where(knownEvidence.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var optionalFacts = new List<OptionalFact>();
            foreach (OptionalFactPayload? optional in candidate.OptionalFacts ?? [])
            {
                if (optional is null)
                {
                    continue;
                }

                var evidenceIds = (optional.EvidenceIds ?? [])
                    .Where(knownEvidence.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (evidenceIds.Count == 0)
                {
                    continue;
                }

                optionalFacts.Add(new OptionalFact(
                    evidenceIds,
                    Math.Max(0, optional.EstimatedWords),
                    ClampPriority(optional.Priority)));
            }

            List<StoryMember>? members = ParseMembers(storyId, candidate.Members, knownEvents, knownEvidence, trace);
            if (members is { Count: < 2 })
            {
                // Fewer than two independently affordable members cannot carry a genuine
                // grouping, but discarding the story would silently lose the remaining event.
                // Degrading to a plain story keeps the surviving material in play.
                trace.Decision(Stage, $"Group '{storyId}' has fewer than two valid members; degrading to a plain story");
                members = null;
            }

            stories.Add(new PlannedStory(
                storyId,
                eventIds,
                ParseRole(candidate.Role),
                ClampPriority(candidate.Priority),
                requiredFacts,
                optionalFacts,
                candidate.Relationship,
                members));
        }

        return new BulletinPlan(stories);
    }

    private static StoryRole ParseRole(JsonElement role)
    {
        if (role.ValueKind == JsonValueKind.String &&
            Enum.TryParse<StoryRole>(role.GetString(), ignoreCase: true, out StoryRole parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        return StoryRole.Traffic;
    }

    /// <summary>
    ///     Parses a story's member list into independently-priced units. Returns null for a
    ///     plain story. Every defect is repaired in place rather than rejected: the live run on
    ///     2026-08-26 showed an indivisible seven-event group dropped wholesale while the read
    ///     ran 15 seconds under slot, so the cost of losing a valid member exceeds the cost of
    ///     keeping a slightly imperfect one. A member with no usable requiredFacts stays: the
    ///     fitter prices it from the event alone.
    /// </summary>
    private static List<StoryMember>? ParseMembers(
        string storyId,
        List<MemberPayload?>? payload,
        HashSet<string> knownEvents,
        HashSet<string> knownEvidence,
        TraceRecorder trace)
    {
        if (payload is null || payload.Count == 0)
        {
            return null;
        }

        var members = new List<StoryMember>(payload.Count);
        foreach (MemberPayload? candidate in payload)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.MemberId))
            {
                trace.Decision(Stage, $"Dropped a member without a memberId from story '{storyId}'");
                continue;
            }

            if (!knownEvents.Contains(candidate.EventId ?? string.Empty))
            {
                trace.Decision(Stage, $"Dropped member '{candidate.MemberId}' from story '{storyId}' citing unknown eventId '{candidate.EventId ?? "(missing)"}'");
                continue;
            }

            var requiredFacts = (candidate.RequiredFacts ?? [])
                .Where(knownEvidence.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var optionalFacts = new List<OptionalFact>();
            foreach (OptionalFactPayload? optional in candidate.OptionalFacts ?? [])
            {
                if (optional is null)
                {
                    continue;
                }

                var evidenceIds = (optional.EvidenceIds ?? [])
                    .Where(knownEvidence.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (evidenceIds.Count == 0)
                {
                    continue;
                }

                optionalFacts.Add(new OptionalFact(
                    evidenceIds,
                    Math.Max(0, optional.EstimatedWords),
                    ClampPriority(optional.Priority)));
            }

            members.Add(new StoryMember(
                candidate.MemberId!,
                candidate.EventId!,
                requiredFacts,
                optionalFacts,
                ClampPriority(candidate.Priority)));
        }

        return members;
    }

    private static int ClampPriority(int priority) => Math.Clamp(priority, 0, 100);

    private static string RoleName(StoryRole role) => role.ToString().ToLowerInvariant();

    private static string RenderUserPrompt(WorkflowRequest request, NormalizedInput input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Create the editorial plan for this bulletin request.");
        builder.AppendLine("Return JSON matching the supplied schema. Do not write a script or broadcast prose.");
        builder.AppendLine($"Location: {request.Location}");
        builder.AppendLine($"Market: {request.Market}");
        builder.AppendLine($"Target seconds: {request.TargetSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Station required sections: {RenderList(request.RequiredSections)}");
        // Editorial policy governs MEANING and is the only configuration this stage may see. The
        // road-feel palette used to be rendered here too: that is VOICE configuration, and handing
        // wording guidance to a stage forbidden from writing prose is exactly the overlapping
        // authority this architecture removes. It now reaches the composer alone.
        string editorial = PolicyRenderer.ForPlanner(request.EffectivePolicy.Editorial);
        if (!string.IsNullOrWhiteSpace(editorial))
        {
            builder.AppendLine();
            builder.AppendLine(editorial);
            builder.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(request.HouseStyle))
        {
            // Legacy undifferentiated style string, retained until callers supply a StationPolicy.
            builder.AppendLine($"House style: {request.HouseStyle}");
        }

        if (request.PreviousScript is not null)
        {
            builder.AppendLine("Previous script (for continuity judgement only; do not copy its prose):");
            builder.AppendLine(request.PreviousScript);
        }

        builder.AppendLine();
        builder.AppendLine("EVENTS (facts are grouped under their event; cite only the supplied ids):");
        foreach (BulletinEvent eventItem in input.Events)
        {
            builder.AppendLine($"Event {eventItem.EventId}: label={eventItem.Label}; kind={eventItem.Kind}");
            foreach (EventFact fact in eventItem.Facts)
            {
                builder.AppendLine($"  [{fact.EvidenceId}] {fact.Field} = {fact.Value}");
            }
        }

        return builder.ToString();
    }

    private static string RenderList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private sealed class PlanPayload
    {
        [JsonPropertyName("stories")]
        public List<StoryPayload?>? Stories { get; set; }
    }

    private sealed class StoryPayload
    {
        [JsonPropertyName("storyId")]
        public string? StoryId { get; set; }

        [JsonPropertyName("eventIds")]
        public List<string>? EventIds { get; set; }

        [JsonPropertyName("role")]
        public JsonElement Role { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("requiredFacts")]
        public List<string>? RequiredFacts { get; set; }

        [JsonPropertyName("optionalFacts")]
        public List<OptionalFactPayload?>? OptionalFacts { get; set; }

        [JsonPropertyName("relationship")]
        public string? Relationship { get; set; }

        [JsonPropertyName("members")]
        public List<MemberPayload?>? Members { get; set; }
    }

    private sealed class OptionalFactPayload
    {
        [JsonPropertyName("evidenceIds")]
        public List<string>? EvidenceIds { get; set; }

        [JsonPropertyName("estimatedWords")]
        public int EstimatedWords { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }
    }

    private sealed class MemberPayload
    {
        [JsonPropertyName("memberId")]
        public string? MemberId { get; set; }

        [JsonPropertyName("eventId")]
        public string? EventId { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("requiredFacts")]
        public List<string>? RequiredFacts { get; set; }

        [JsonPropertyName("optionalFacts")]
        public List<OptionalFactPayload?>? OptionalFacts { get; set; }
    }
}
