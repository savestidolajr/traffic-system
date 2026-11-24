using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     Decides what the bulletin is about and briefs the composer, without writing a word of it.
///     <para>
///         This role used to hand over finished sentences. Measured live on 2026-08-26 the composer
///         then tracked that wording clause for clause, so the second call bought arrangement and
///         nothing else. Handing over briefs - subject, facts, priority, reasoning - leaves the
///         sentence genuinely unwritten, which is the only way the composer's creativity is real.
///     </para>
///     <para>
///         The briefs are also a PARTITION, enforced here in code rather than asked for in a
///         prompt: <see cref="EnforcePartition" /> drops any event a higher-priority story already
///         claimed. The old menu offered "South Road", "James Congdon Drive" and their combination
///         as three lines, and a composer taking two of them voiced one closure twice.
///     </para>
/// </summary>
public sealed class BulletinWriter(IWorkflowLlm llm)
{
    private const string Stage = "Write";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    ///     Writes story briefs; a bad model response becomes an empty set, never an exception.
    ///     <para>
    ///         ALWAYS split across the two desks - one holding what has HAPPENED, one holding how
    ///         the network is MOVING. They run concurrently, so it is one round-trip either way, and
    ///         a board carrying only one kind of event calls only that desk, because an empty desk
    ///         is never invoked. The split therefore costs a second call only when there genuinely
    ///         are both kinds, which is exactly when you want both specialists.
    ///     </para>
    ///     <para>
    ///         There was an event-count threshold here. It was a guess, and it was gated against a
    ///         supply the evidence builder capped at ten by construction, so the desks fired only on
    ///         the busiest boards - the opposite of always having an expert on the material. A
    ///         generalist prompt hedging closures AND delay banding is worse at five events than two
    ///         focused ones, not better.
    ///     </para>
    ///     <para>
    ///         Ordering and fixture-filling happen in the stitch below, once, so they never depend
    ///         on how many desks happened to run. They diverged once and it shipped a real defect:
    ///         a writer ranked Cairns weather 55 and fuel 48 against clear runs 25, and the clear
    ///         roads aired AFTER the weather and fuel (job 7d61df27).
    ///     </para>
    /// </summary>
    public Task<BulletinBriefs> WriteBriefsAsync(
        WorkflowRequest request,
        NormalizedInput input,
        int targetWords,
        TraceRecorder trace,
        CancellationToken cancellationToken = default) =>
        WriteByDeskAsync(request, input, targetWords, trace, cancellationToken);

    /// <summary>
    ///     Splits the evidence between the desks, runs them at once, and stitches the result.
    ///     <para>
    ///         Concurrent, so the split costs no latency: it is still one round-trip, exactly like
    ///         the composer race. A desk that fails contributes nothing rather than failing the
    ///         bulletin - a read carrying only the incidents is worse than one carrying both and far
    ///         better than none.
    ///     </para>
    ///     <para>
    ///         The stitch below is where fixtures are appended last and the running order is
    ///         settled, so neither depends on how many desks ran.
    ///     </para>
    /// </summary>
    private async Task<BulletinBriefs> WriteByDeskAsync(
        WorkflowRequest request,
        NormalizedInput input,
        int targetWords,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        Stopwatch phase = Stopwatch.StartNew();
        IReadOnlySet<string> incidentCorridors = BriefDivision.IncidentCorridors(input.Events);
        var byDesk = input.Events
            .GroupBy(evt => BriefDivision.DeskFor(evt, incidentCorridors))
            .ToDictionary(group => group.Key, group => group.ToList());

        var desks = new[]
        {
            (Desk: BriefDivision.Desk.Incidents, Role: "writeIncidents"),
            (Desk: BriefDivision.Desk.Flow, Role: "writeFlow")
        }
        // A desk with nothing to brief is never called, so a one-kind board still costs one call.
        .Where(desk => byDesk.ContainsKey(desk.Desk))
        .ToList();

        trace.StageStart(Stage, $"Briefing {input.Events.Count} events across {desks.Count} desk(s) "
            + $"plus {byDesk.GetValueOrDefault(BriefDivision.Desk.Fixture)?.Count ?? 0} fixture(s)");

        BulletinBriefs[] written = await Task.WhenAll(desks.Select(desk =>
            WriteWholeAsync(
                request,
                input with { Events = byDesk[desk.Desk] },
                targetWords,
                trace,
                desk.Role,
                cancellationToken)))
            .ConfigureAwait(false);

        // A desk that briefed nothing lost its events, so retry once before deciding whether the
        // loss is fatal. An incidents desk may intentionally drop a works record when the playbook
        // says it has no measured impact. This exception to the loss rule was forced by Sydney job
        // 9a66a239 (2026-09-03): HERE supplied one construction record on Joynton Ave, labelled
        // "Closed" but with no delay and no lane closure, and the desk correctly left it empty on
        // all eight calls. Treating that permitted omission as a transient desk failure redelivered
        // the same evidence three times.
        //
        // The retry remains asymmetric:
        //
        //   Flow lost:      degrade. The read loses good-run colour and still tells the truth about
        //                   every obstruction. This is the original desk-failure guarantee.
        //   Incidents lost: fail only when at least one event cannot be dropped. A positive delay,
        //                   lanesClosed, or a non-works blockage such as a closure or crash must
        //                   reach the bulletin. Works with no measured impact are droppable under
        //                   the incidents playbook, so an empty reply after the retry can continue.
        //
        // Dev Adelaide job 369b0c37 (2026-08-30) remains the fatal case: three non-works closures
        // were present in the desk prompt, but an empty completion led to a false "clear runs"
        // read. Failing that attempt gives the engine a retry and, failing that, the filler cart.
        // That is better on air than a bulletin that contradicts its own evidence.
        for (int i = 0; i < desks.Count; i++)
        {
            if (written[i].Stories.Count > 0)
                continue;

            var deskEvents = byDesk[desks[i].Desk];
            int lostEvents = deskEvents.Count;
            bool incidentsDesk = desks[i].Desk == BriefDivision.Desk.Incidents;
            bool fatal = incidentsDesk && deskEvents.Any(MustBrief);
            trace.Failure(Stage, $"{desks[i].Role} briefed none of its {lostEvents} event(s)",
                fatal
                    ? "empty desk reply; retrying once before synthesizing fallback briefs"
                    : incidentsDesk
                        ? "empty desk reply; retrying once before applying the droppable-works rule"
                        : "empty desk reply; retrying once, then continuing without its stories");
            written[i] = await WriteWholeAsync(
                request,
                input with { Events = deskEvents },
                targetWords,
                trace,
                desks[i].Role,
                cancellationToken).ConfigureAwait(false);

            if (written[i].Stories.Count > 0)
                continue;

            if (fatal)
            {
                var fallbackStories = CreateFallbackBriefs(deskEvents.Where(MustBrief).ToList());
                if (fallbackStories.Count > 0)
                {
                    trace.Decision(Stage,
                        $"{desks[i].Role} briefed nothing twice; synthesized {fallbackStories.Count} "
                        + $"fallback brief(s) for non-droppable event(s)");
                    written[i] = new BulletinBriefs(fallbackStories);
                    continue;
                }
            }

            if (incidentsDesk)
            {
                trace.Decision(Stage,
                    $"{desks[i].Role} briefed nothing twice; every one of its {lostEvents} event(s) "
                    + "is droppable under the desk playbook (works with no measured impact), "
                    + "continuing with zero incident stories");
            }
            else
            {
                trace.Decision(Stage,
                    $"{desks[i].Role} briefed nothing twice; continuing without its {lostEvents} "
                    + "flow event(s); the read still reports every obstruction");
            }
        }
        var collected = desks
            .Zip(written, (desk, briefs) => briefs.Stories.Select(story => (desk.Desk, Brief: story)))
            .SelectMany(pair => pair)
            .Concat(BriefDivision.FixtureBriefs(input.Events)
                .Select(brief => (BriefDivision.Desk.Fixture, Brief: brief)))
            .ToList();

        var ranks = input.Events.ToDictionary(evt => evt.EventId, evt => evt.Rank, StringComparer.Ordinal);
        BulletinBriefs stitched = BriefDivision.Stitch(
            collected, ranks, request.SegmentOrder, request.TrafficOrder);

        trace.Decision(Stage, $"Stitched {stitched.Stories.Count} brief(s) from "
            + string.Join(", ", desks.Zip(written, (d, b) => $"{d.Role} {b.Stories.Count}")));

        return Finish(MarkFixtures(EnforcePartition(stitched, trace), input), trace, phase);
    }

    /// <summary>
    ///     Whether an event must survive an empty incidents-desk response. The incidents prompt is
    ///     allowed to leave unremarkable works empty, but it must not make a measured delay,
    ///     lane closure, crash, or non-works closure disappear.
    ///     <para>
    ///         This mirrors the fields and closure vocabulary used by <c>EventRank</c>.
    ///         The boundary is deterministic because Sydney job 9a66a239 (2026-09-03) proved that
    ///         a re-issued construction record with only a "Closed" label and no measured impact is
    ///         correctly omitted by the desk.
    ///     </para>
    /// </summary>
    private static bool MustBrief(BulletinEvent evt)
    {
        if (evt.Facts.Any(fact =>
                fact.Field.Equals("delayMinutes", StringComparison.OrdinalIgnoreCase)
                && ParseDelayMinutes(fact.Value) > 0))
        {
            return true;
        }

        if (evt.Facts.Any(fact =>
                fact.Field.Equals("lanesClosed", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        bool works = evt.Facts.Any(fact =>
            fact.Field.Equals("incident", StringComparison.OrdinalIgnoreCase)
            && WorksWords.Any(word => fact.Value.Contains(word, StringComparison.OrdinalIgnoreCase)));

        return !works && evt.Facts.Any(fact =>
            (fact.Field.Equals("detail", StringComparison.OrdinalIgnoreCase)
                || fact.Field.Equals("incident", StringComparison.OrdinalIgnoreCase))
            && ClosureWords.Any(word => fact.Value.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    ///     Parses the same numeric and spoken-minute forms used to rank an event. Normalization turns
    ///     small numeric values into speech, while persisted or hand-built evidence may remain digits.
    /// </summary>
    private static int ParseDelayMinutes(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        int spoken = Array.IndexOf(SpokenNumbers, value.Trim().ToLowerInvariant());
        return spoken > 0 ? spoken : 0;
    }

    private static readonly string[] ClosureWords = ["clos", "shut", "blocked", "crash", "collision"];
    private static readonly string[] WorksWords =
        ["construction", "roadwork", "roadworks", "maintenance", "planned"];
    private static readonly string[] SpokenNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];
    /// <summary>
    ///     Synthesizes minimal fallback briefs directly from unbriefed non-droppable events
    ///     when an LLM desk returns empty results twice. Ensures mandatory events reach the composer.
    /// </summary>
    private static IReadOnlyList<StoryBrief> CreateFallbackBriefs(IReadOnlyList<BulletinEvent> events)
    {
        var stories = new List<StoryBrief>();
        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            string subject = evt.Fact("road")?.Value
                ?? evt.Fact("location")?.Value
                ?? evt.Label;
            string condition = evt.Fact("detail")?.Value
                ?? evt.Fact("incident")?.Value
                ?? "affected by an incident";

            stories.Add(new StoryBrief(
                Order: i + 1,
                StoryId: $"fallback-{evt.EventId}",
                Subject: subject,
                EventIds: [evt.EventId],
                Priority: evt.Rank > 0 ? evt.Rank : 80,
                WhyItMatters: "Mandatory incident synthesized via deterministic fallback brief",
                Condition: condition));
        }
        return stories;
    }


    private async Task<BulletinBriefs> WriteWholeAsync(
        WorkflowRequest request,
        NormalizedInput input,
        int targetWords,
        TraceRecorder trace,
        string role,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string userPrompt = string.Empty;
        string systemPrompt = string.Empty;
        LlmReply? reply = null;

        try
        {
            userPrompt = RenderUserPrompt(request, input, targetWords);
            systemPrompt = StationPlaybook.Resolve(request, role, trace, Stage);
            string schema = PromptLibrary.Schema(role);
            LlmReply result = await llm.CompleteAsync(role, systemPrompt, userPrompt, schema, cancellationToken)
                .ConfigureAwait(false);
            reply = result;
            stopwatch.Stop();

            trace.LlmCall(Stage, new LlmCallRecord(
                role,
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

            if (!result.IsSuccess)
            {
                trace.Failure(Stage, $"{role} model failed", result.Error ?? "The writer model returned a failure.");
                return BulletinBriefs.Empty;
            }

            try
            {
                return ParseBriefs(result.Text);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                trace.Failure(Stage, $"{role} response could not be parsed", exception.Message);
                return BulletinBriefs.Empty;
            }

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            trace.LlmCall(Stage, new LlmCallRecord(
                role,
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
            trace.Failure(Stage, $"{role} call failed", exception.Message);
            return BulletinBriefs.Empty;
        }
    }

    private static string RenderUserPrompt(WorkflowRequest request, NormalizedInput input, int targetWords)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("STATION BRIEF");
        prompt.AppendLine(SourceDataRenderer.RenderBrief(request, targetWords));

        string editorialPolicy = PolicyRenderer.ForPlanner(request.EffectivePolicy.Editorial);
        if (!string.IsNullOrWhiteSpace(editorialPolicy))
        {
            prompt.AppendLine();
            prompt.AppendLine("EDITORIAL POLICY");
            prompt.AppendLine(editorialPolicy);
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
            prompt.AppendLine("PREVIOUS SCRIPT (for continuity judgement only; do not treat it as source data)");
            prompt.AppendLine(request.PreviousScript);
        }

        int storyBudget = SourceDataRenderer.StoryBudget(input, targetWords);
        prompt.AppendLine();
        prompt.AppendLine("TASK");
        prompt.AppendLine($"Brief every story worth voicing. A {targetWords}-word read has air for about {storyBudget} of them told properly, but brief them all and let your colleague choose: priority is how you tell them what to drop.");
        prompt.AppendLine("Assign every event below to exactly one story. Group events that genuinely belong together into a single story rather than briefing them separately.");
        prompt.AppendLine();
        prompt.AppendLine("SOURCE DATA (the only factual source; read it once)");
        prompt.AppendLine(SourceDataRenderer.Render(input));
        return prompt.ToString().TrimEnd();
    }

    private static BulletinBriefs ParseBriefs(string rawJson)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson, JsonOptions);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("stories", out JsonElement storiesElement) ||
            storiesElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Response must be an object with a stories array.");
        }

        var stories = new List<StoryBrief>();
        foreach (JsonElement storyElement in storiesElement.EnumerateArray())
        {
            if (storyElement.ValueKind != JsonValueKind.Object)
                continue;

            // A story covering no event has nothing to voice: the facts now come from the events,
            // so the id list is what makes a story real. Previously an empty `facts` array was the
            // test, which a desk could fail merely by declining to transcribe.
            var eventIds = ReadStrings(storyElement, "eventIds");
            if (eventIds.Count == 0)
                continue;

            string subject = ReadString(storyElement, "subject").Trim();
            string storyId = ReadString(storyElement, "storyId").Trim();
            if (storyId.Length == 0)
                storyId = $"story-{stories.Count + 1}";

            stories.Add(new StoryBrief(
                stories.Count + 1,
                storyId,
                subject.Length > 0 ? subject : storyId,
                eventIds,
                Math.Clamp(ReadInt32(storyElement, "priority"), 0, 100),
                ReadString(storyElement, "whyItMatters").Trim(),
                Required: false,
                Condition: OnePhrase(ReadString(storyElement, "condition"))));
        }

        return new BulletinBriefs(stories);
    }

    /// <summary>
    ///     A desk condition is ONE phrase. The composer voices it inside the sentence that names
    ///     the road and supplies the comma in front of it, so a comma INSIDE the condition hangs a
    ///     second afterthought off that sentence. Dev goldcoast <c>f98d7521</c> (2026-09-03) aired
    ///     "…from Smith Street Motorway to Arundel, moving through, though not at its usual pace" —
    ///     two tails where the read had room for one, from the condition
    ///     "moving through, though not at its usual pace".
    ///     <para>
    ///         The prompts ask for one phrase; this keeps the promise when a desk hedges anyway, by
    ///         retaining the leading phrase — the state — and dropping the qualifier bolted to it.
    ///         A grouped story that spent its second clause on a second road loses that clause,
    ///         which is the accepted cost: the composer still has every fact of every event the
    ///         story cites, and a condition is colour, not evidence.
    ///     </para>
    /// </summary>
    private static string OnePhrase(string condition)
    {
        string trimmed = condition.Trim();
        int comma = trimmed.IndexOf(',');
        if (comma <= 0)
            return trimmed;

        return trimmed[..comma].TrimEnd(' ', ';', ':', '-', '—');
    }


    /// <summary>
    ///     Makes the stories a genuine partition: an event claimed by a higher-priority story is
    ///     removed from every lower-priority one, and a story left with nothing of its own is
    ///     dropped.
    ///     <para>
    ///         The prompt asks for this, and a prompt is not a guarantee. Overlap is the specific
    ///         defect that put one Adelaide closure on air three times, so it is settled in code
    ///         where the outcome is certain and visible in the trace.
    ///     </para>
    /// </summary>
    private static BulletinBriefs EnforcePartition(BulletinBriefs briefs, TraceRecorder trace)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<StoryBrief>();
        var overlaps = new List<string>();

        foreach (StoryBrief story in briefs.ByPriority)
        {
            var own = story.EventIds.Where(id => claimed.Add(id)).ToList();
            var taken = story.EventIds.Except(own, StringComparer.OrdinalIgnoreCase).ToList();
            if (taken.Count > 0)
                overlaps.Add($"{story.StoryId} also claimed {string.Join(", ", taken)}");

            // A story whose events were all claimed elsewhere is a duplicate telling of a story
            // already briefed. One that never named an event cannot be checked, so it survives.
            if (story.EventIds.Count > 0 && own.Count == 0)
                continue;

            kept.Add(story with { Order = kept.Count + 1, EventIds = own });
        }

        if (overlaps.Count > 0)
        {
            trace.Decision(Stage,
                $"Enforced one story per event: {briefs.Stories.Count - kept.Count} duplicate story/stories dropped",
                new Dictionary<string, string> { ["overlaps"] = string.Join(" | ", overlaps) });
        }

        return new BulletinBriefs(kept);
    }

    /// <summary>
    ///     Flags the stories the station contract demands, from
    ///     <see cref="NormalizedInput.RequiredEventIds" /> rather than from the model's priority.
    ///     <para>
    ///         Live on 2026-08-26 the writer scored Adelaide's weather 35 and every traffic story
    ///         above it, which is defensible editorially and wrong contractually: the station always
    ///         carries weather. That read aired with none. Priority is a judgement about what
    ///         matters most; a fixture is not up for judgement, so the two must not share a
    ///         mechanism.
    ///     </para>
    /// </summary>
    private static BulletinBriefs MarkFixtures(BulletinBriefs briefs, NormalizedInput input)
    {
        if (input.RequiredEventIds.Count == 0)
            return briefs;

        var required = new HashSet<string>(input.RequiredEventIds, StringComparer.Ordinal);
        return new BulletinBriefs(briefs.Stories
            .Select(story => story with { Required = story.EventIds.Any(required.Contains) })
            .ToList());
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
            return string.Empty;

        return value.GetString() ?? string.Empty;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToList();
    }

    private static int ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int priority)
            ? priority
            : 0;

    private static BulletinBriefs Finish(BulletinBriefs briefs, TraceRecorder trace, Stopwatch stopwatch)
    {
        var artifact = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (StoryBrief story in briefs.ByPriority)
        {
            artifact[$"[{story.Order}] p{story.Priority} {story.StoryId}"] =
                $"{story.Subject}  |  {story.EventIds.Count} event(s): {string.Join("; ", story.EventIds)}";
        }

        trace.Artifact(Stage, "Story briefs", artifact);
        trace.StageEnd(Stage, $"Briefed {briefs.Stories.Count} stories", stopwatch.Elapsed.TotalMilliseconds);
        return briefs;
    }
}

/// <summary>
///     Thrown when the incidents desk was handed at least one event that the desk playbook does not
///     permit it to drop, then returned no stories twice. A works event with no measured impact is
///     allowed to disappear because the prompt explicitly marks it as unremarkable.
///     <para>
///         This is deliberately an exception rather than a degraded return. Every other failure in
///         this class softens to <c>BulletinBriefs.Empty</c>, which is right when a desk had nothing
///         to say. Dev Adelaide job 369b0c37 aired "Clear runs are the story across Adelaide" over
///         three unreported non-works closures because that case degraded silently. The workflow
///         converts this into a failed outcome so the engine retries and, failing that, falls back.
///     </para>
/// </summary>
public sealed class BriefingDeskLostEventsException(string role, int eventCount)
    : Exception($"The {role} desk briefed none of its {eventCount} event(s) after a retry; "
        + "airing the read would drop a non-droppable event silently.")
{
    /// <summary>The desk role that lost its events, e.g. <c>writeIncidents</c>.</summary>
    public string Role { get; } = role;

    /// <summary>How many events the desk was handed and failed to brief.</summary>
    public int EventCount { get; } = eventCount;
}
