using System.Text.Json;
using System.Text;
using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>
///     What the writer hands the composer: a partition of the source events into distinct stories,
///     each one a BRIEF rather than a finished sentence.
///     <para>
///         The predecessor handed over finished prose, and the composer's job collapsed into
///         paraphrase: measured live on 2026-08-26, reads tracked the writer's wording almost
///         clause for clause, so the second model call bought arrangement and nothing else. A brief
///         states what happened, which facts are available, how much it matters and why, and
///         deliberately stops short of saying it. The sentence is then genuinely the composer's.
///     </para>
///     <para>
///         The stories PARTITION the events: every event belongs to exactly one story. The old menu
///         offered overlapping choices - "South Road", "James Congdon Drive", and a combined
///         "South Road and James Congdon Drive" as three separate lines - and a composer picking
///         two of the three voiced the same closure twice. Grouping is the writer's decision to
///         make once, not a menu of alternatives for the composer to re-litigate.
///     </para>
/// </summary>
public sealed record BulletinBriefs(IReadOnlyList<StoryBrief> Stories)
{
    public static BulletinBriefs Empty { get; } = new([]);

    /// <summary>
    ///     Highest priority first, then the writer's own order. Claim precedence for
    ///     <c>EnforcePartition</c> - which story keeps a contested event - NOT reading order.
    /// </summary>
    public IReadOnlyList<StoryBrief> ByPriority =>
        Stories.OrderByDescending(story => story.Priority).ThenBy(story => story.Order).ToList();

    /// <summary>
    ///     Renders the briefs with the fact/reasoning boundary made structural rather than implied.
    ///     <para>
    ///         Facts and reasoning arrive in separately labelled blocks because they have different
    ///         permissions: a fact may be voiced, a reason may only be weighed. Rendered as one
    ///         paragraph the composer cannot tell which is which, and "a major northern route" -
    ///         a judgement the writer made, present in no evidence - reads exactly like a supplied
    ///         value and lands on air as one.
    ///     </para>
    ///     <para>
    ///         Facts are RESOLVED from the source events by id, never taken from the desk's reply.
    ///         Copying them back cost two things. Latency: measured 2026-08-27 on a 17-event sydney
    ///         board, 57% of the desk's output was the input transcribed verbatim - 77 fact strings
    ///         it had just been given - and output tokens decode one at a time, which is most of
    ///         why that call took 21 seconds. Correctness: a ten-road story arrived as ONE flat
    ///         list, "road: M5, suburb: Milperra, ..., delayMinutes: fourteen, road: M4, ...", with
    ///         nothing but list order binding a figure to a road. That is a fabrication path, and
    ///         it nearly fired once already (a 4-minute Richmond Road delay and a 2-minute M4 one
    ///         flattened into a single list). Grouping under the event id fixes both at once, and
    ///         a desk that cannot restate a value cannot mistype one.
    ///     </para>
    /// </summary>
    public string Render(NormalizedInput input)
    {
        var byId = input.Events.ToDictionary(evt => evt.EventId, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (StoryBrief story in Stories)
        {
            string fixture = story.Required ? " | REQUIRED: this station always carries it" : string.Empty;
            sb.AppendLine($"[{story.Order}] priority {story.Priority}{fixture} | {story.StoryId}");
            sb.AppendLine($"    STORY: {story.Subject}");
            sb.AppendLine("    AVAILABLE FACTS (you may voice any of these and nothing else; choose what matters):");
            foreach (string eventId in story.EventIds)
            {
                if (!byId.TryGetValue(eventId, out BulletinEvent? evt))
                    continue;
                // One heading per event, so every value stays attached to the thing it describes.
                // Desk-only material is withheld here and nowhere else: the desk read it in the
                // source block and has already turned it into `condition`. Left in, the composer
                // voices it — brisbane df18a7d8 aired "with no delay measured" verbatim.
                var airable = evt.Facts.Where(fact => !fact.DeskOnly).ToList();
                if (airable.Count == 0)
                    continue;
                sb.AppendLine($"      {evt.Label}");
                foreach (EventFact fact in airable)
                    sb.AppendLine($"        - {fact.Field}: {fact.Value}");
            }

            // Between the facts and the reasoning: airable, unlike WHY IT MATTERS, and the desk's
            // own words rather than a cited value.
            //
            // Presented as WORDING TO KEEP, which is the opposite of how every other value is
            // framed, and deliberately so. The other values are written by code, so their wording
            // is ours and the composer is told to say what they mean in its own words. This one
            // carries the STATION's configured voice — the desk applied ARN's per-condition rules
            // to produce it — so paraphrasing it discards the configuration. Measured on dev
            // 2026-08-31: melbourne's playbook set "SHUT TIGHT", the desk used it, and the read
            // aired "closed tight" because this block invited a rephrase.
            if (story.Condition.Length > 0)
            {
                sb.AppendLine("    HOW IT IS BEHAVING (your colleague's own words, carrying this station's");
                sb.AppendLine("      configured wording: keep the words themselves and build your sentence");
                sb.AppendLine("      around them, or drop the story — never restate it differently):");
                sb.AppendLine($"      {story.Condition}");
            }

            if (story.WhyItMatters.Length > 0)
            {
                sb.AppendLine("    WHY IT MATTERS (editorial reasoning, never a fact, never voice it):");
                sb.AppendLine($"      {story.WhyItMatters}");
            }

            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
///     One story the writer has identified, briefed for a colleague who will write the words.
/// </summary>
/// <param name="Order">The writer's own numbering, used by the composer and the editor to refer to it.</param>
/// <param name="StoryId">A stable slug for the story, e.g. <c>james-congdon-closure</c>.</param>
/// <param name="Subject">
///     What the story is about, as a label rather than a sentence, e.g. "James Congdon Drive
///     citybound closure". Naming the story is not writing it.
/// </param>
/// <param name="EventIds">
///     The source events this story covers. Every event belongs to exactly one story, which is what
///     makes the set of stories a partition rather than a menu of overlapping options.
/// </param>
/// <remarks>
///     There is deliberately no <c>Facts</c> member. The desk used to copy every supplied value
///     back as <c>field: value</c>, which made 57% of its reply a transcript of its own prompt and
///     pooled ten roads' figures into one unattributed list. <see cref="BulletinBriefs.Render" />
///     resolves them from the source events instead, so the composer's factual surface is the
///     evidence itself and a desk cannot mistype a value it never restates.
/// </remarks>
/// <param name="Priority">0-100, the writer's editorial judgement of how much this matters to a driver.</param>
/// <param name="WhyItMatters">
///     Why that priority, in the terms a listener would care about. Reasoning, not evidence: it
///     explains the ordering and never licenses a claim.
/// </param>
/// <param name="Required">
///     True when the station contract demands this story, e.g. weather on a station that always
///     carries it. Derived from <see cref="NormalizedInput.RequiredEventIds" /> rather than asked
///     of the writer: measured live on 2026-08-26 the writer priced Adelaide's weather at 35,
///     below every traffic story, so "drop the lowest-priority story" would have dropped a fixture
///     the station is contractually owed - and on that run the read shipped with no weather at all.
/// </param>
/// <param name="Condition">
///     How this story's roads are behaving, IN THE DESK'S OWN WORDS. Empty when the measurements
///     carry it already or when nothing is worth saying.
///     <para>
///         The one thing the desk is licensed to phrase rather than cite, and it exists because
///         qualitative state is the one dimension code cannot phrase well. The emitter sees a single
///         corridor, so it used to pick wording by hashing the road name into a two-option palette —
///         which cannot help repeating itself once three roads share a band, and did: brisbane and
///         melbourne each aired two roads described in identical words ("barely noticeable"). The
///         desk sees every event at once, so it is the only stage that can say "roadworks on three
///         roads, none of them holding anyone up" in one breath instead of three.
///     </para>
///     <para>
///         Not a fact and never a substitute for one. Figures still travel as evidence and are still
///         checked verbatim; a condition carrying a quantity is rejected outright by
///         <c>ConversationalChecks.DeskConditionCarriesNoFigure</c>, because a number that entered
///         here would have bypassed citation entirely.
///     </para>
/// </param>
public sealed record StoryBrief(
    int Order,
    string StoryId,
    string Subject,
    IReadOnlyList<string> EventIds,
    int Priority,
    string WhyItMatters,
    bool Required = false,
    string Condition = "")
{
    /// <summary>
    ///     The story's supplied values, resolved from the source events it covers. The single
    ///     definition of "what this story may voice", shared by the composer's rendering and the
    ///     post-read checks so the two can never disagree about the permitted set.
    /// </summary>
    public IEnumerable<EventFact> ResolveFacts(NormalizedInput input)
    {
        foreach (string eventId in EventIds)
        {
            BulletinEvent? evt = input.Events.FirstOrDefault(
                candidate => string.Equals(candidate.EventId, eventId, StringComparison.Ordinal));
            if (evt is null)
                continue;
            foreach (EventFact fact in evt.Facts)
                yield return fact;
        }
    }
}

/// <summary>
///     The verdict on a finished read. Feedback is written to be read by the writer. An unreviewed
///     verdict means the editor could not provide a valid response; it is intentionally distinct
///     from approval so the operator-visible trace can say that no editorial review was completed.
/// </summary>
/// <param name="Approved">True when the read can air as written.</param>
/// <param name="Feedback">
///     Reasons, phrased as a colleague would phrase them, e.g. "sentences 3-6 repeat the same
///     roadworks and location construction". Empty when approved. May carry the length note as
///     well as editorial concerns, so the correction message never claims the editor said
///     something the editor did not — an approved read that misses the slot is revised for
///     length, not "rejected" (dev job a6b75762, 2026-08-27). For an unreviewed verdict, contains
///     the reason the editor response could not be used.
/// </param>
public sealed record EditorVerdict(bool Approved, IReadOnlyList<string> Feedback)
{
    public static EditorVerdict Approve { get; } = new(true, []);

    /// <summary>
    ///     True when the editor did not produce a usable verdict. This is not an editorial
    ///     rejection: the workflow ships the already-generated read for operator approval without
    ///     spending a revision on feedback that does not exist.
    /// </summary>
    public bool IsUnreviewed { get; init; }

    /// <summary>
    ///     Creates a verdict for an editor call that could not be completed or parsed.
    /// </summary>
    /// <param name="reason">The transport or response-validation reason recorded in the trace.</param>
    /// <returns>An unreviewed verdict carrying <paramref name="reason"/> as diagnostic feedback.</returns>
    public static EditorVerdict Unreviewed(string reason) =>
        new(false, [string.IsNullOrWhiteSpace(reason) ? "The editor did not return a usable verdict." : reason])
        {
            IsUnreviewed = true
        };

    /// <summary>The revision request as the composer will receive it, in the second person.</summary>
    public string AsCorrectionMessage() =>
        "The previous bulletin cannot air as written, for the following reasons:"
        + Environment.NewLine
        + string.Join(Environment.NewLine, Feedback.Select(f => $"  - {f}"))
        + Environment.NewLine
        + "Rewrite the whole bulletin: fix every reason listed while preserving the facts.";
}

/// <summary>
///     Renders normalized events as the source-data surface both the writer and the editor read.
///     <para>
///         One compact JSON object per line. The predecessor packed each event onto a
///         pipe-and-semicolon line, which is dense but positional: a reader parses a private
///         format before it can read a fact, and the field/value boundary is a bare <c>=</c>
///         inside a <c>;</c>-separated run. JSON is a structure these models parse natively, and
///         it makes explicit the two things that matter here - which facts belong to which event,
///         and where a value starts and ends.
///     </para>
///     <para>
///         Repeated fields become arrays. A corridor legitimately carries two locations, and
///         duplicate object keys are not JSON: collapsing them silently drops evidence. Measured
///         live on 2026-08-26, the flat format lost one of Bermuda Street's two roadwork spans on
///         every goldcoast read; as an array both aired.
///     </para>
///     <para>
///         Line-delimited rather than one indented document, because indentation is the only part
///         of JSON a model does not need: measured on the same 13-event Adelaide set, indenting
///         cost 67% over the flat format against 21% for this, for identical structure. The block
///         goes to both the writer and the editor, so that difference is paid twice a bulletin.
///     </para>
///     <para>
///         The writer and the editor read the identical text, so the two are looking at the same
///         page when they disagree.
///     </para>
/// </summary>
public static class SourceDataRenderer
{
    public static string Render(NormalizedInput input)
    {
        var sb = new StringBuilder();
        foreach (BulletinEvent e in input.Events)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("id", e.EventId);
                json.WriteString("kind", e.Kind.ToString());
                json.WriteString("name", e.Label);

                json.WriteStartObject("facts");
                foreach (var field in e.Facts.GroupBy(f => f.Field, StringComparer.OrdinalIgnoreCase))
                {
                    var values = field.Select(f => f.Value).ToList();
                    if (values.Count == 1)
                    {
                        json.WriteString(field.Key, values[0]);
                        continue;
                    }

                    json.WriteStartArray(field.Key);
                    foreach (string value in values)
                        json.WriteStringValue(value);
                    json.WriteEndArray();
                }

                json.WriteEndObject();
                json.WriteEndObject();
            }

            sb.AppendLine(Encoding.UTF8.GetString(buffer.ToArray()));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     The station brief: what the read has to be, separate from what it may say.
    ///     <para>
    ///         <paramref name="targetWords" /> is nullable because not every reader is entitled to
    ///         it. The editor is explicitly barred from judging duration, so showing it a word
    ///         target is both misleading and an invitation to overreach into a decision that was
    ///         made before it. Pass null there; pass the real number to the composer, which needs it.
    ///     </para>
    /// </summary>
    public static string RenderBrief(WorkflowRequest request, int? targetWords)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Market: {request.Market}   Station: {request.Station}   Location: {request.Location}");
        if (targetWords is { } words)
            // Words only, never derived seconds: the word target is the engine's (bookends already
            // subtracted, voice-calibrated), while a seconds figure recomputed through the probe
            // oracle disagreed with it on air ("13.4 seconds, which is about 45 spoken words") and
            // gave the composer two contradictory sizes for one read.
            sb.AppendLine($"Slot: about {words} spoken words of air.");
        if (request.RequiredSections.Count > 0)
            sb.AppendLine($"This station always carries: {string.Join(", ", request.RequiredSections)}.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     Stories the slot holds TOLD PROPERLY — a lead with room plus quick calls — after the
    ///     fixtures (weather/fuel evidence actually on the board, not just the REQUIRED flags)
    ///     take their words. 20 words per story is the measured density of a read that still
    ///     sounds like radio; the old guidance divided by 16 with fixtures uncounted, told a
    ///     22-second slot it had "room for about 3", and produced three eleven-word telegrams in
    ///     the one wrapper a sentence that short admits (dev 2026-08-27: "has N-minute delays"
    ///     three times in one read).
    /// </summary>
    public static int StoryBudget(NormalizedInput input, int targetWords)
    {
        int fixtureWords = 5 * input.Events.Count(e =>
            e.Kind is BulletinEventKind.Weather or BulletinEventKind.Fuel);
        return Math.Clamp(
            (int)Math.Round((targetWords - fixtureWords) / 20d, MidpointRounding.AwayFromZero), 1, 5);
    }

    /// <summary>
    ///     Deterministic quiet-board verdict for the composer's BOARD STATE line. The prompt's
    ///     quiet-board rule asks the model to notice that nothing on the menu is worth acting on,
    ///     and measured on 2026-08-27 it never did the arithmetic: cairns led a two-minute
    ///     roadworks recital twice with the clear runs briefed right below it. The board is quiet
    ///     when no fact anywhere claims a closure and no delay reaches ten minutes; the caller
    ///     states that as data instead of hoping it is inferred.
    /// </summary>
    public static (bool IsQuiet, int MaxDelayMinutes) BoardState(NormalizedInput input)
    {
        int maxDelay = 0;
        bool closure = false;
        foreach (BulletinEvent e in input.Events)
        {
            foreach (EventFact fact in e.Facts)
            {
                if (fact.Value.Contains("closed", StringComparison.OrdinalIgnoreCase)
                    || fact.Value.Contains("closure", StringComparison.OrdinalIgnoreCase))
                    closure = true;
                if (fact.Field.Equals("delayMinutes", StringComparison.OrdinalIgnoreCase)
                    && SpokenNumber.TryParse(fact.Value) is { } minutes)
                    maxDelay = Math.Max(maxDelay, minutes);
            }
        }

        return (!closure && maxDelay < 10, maxDelay);
    }
}

/// <summary>
///     Parses a figure whether it arrives as digits or as the spoken form the normalizer renders
///     ("three", "twenty-five"). Returns null for anything that names no number.
/// </summary>
internal static class SpokenNumber
{
    private static readonly string[] Words =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    public static int? TryParse(string value)
    {
        string word = value.Trim();
        if (int.TryParse(word, out int digits))
            return digits;

        int index = Array.FindIndex(Words, w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return index;

        if (Tens.TryGetValue(word, out int tens))
            return tens;

        string[] parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && Tens.TryGetValue(parts[0], out int high))
        {
            int unit = Array.FindIndex(Words, w => w.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (unit is > 0 and < 10)
                return high + unit;
        }

        return null;
    }
}
