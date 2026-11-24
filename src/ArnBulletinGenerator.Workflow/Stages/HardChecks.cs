using System.Diagnostics;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Cheap, deterministic gates over the assembled read. These checks deliberately run after
///     composition: the fitter bought facts and the composer is only allowed to express those facts,
///     so an HTML trace can distinguish a missing citation from a model's unsupported wording without
///     spending another model call.
/// </summary>
public static class HardChecks
{
    private const int MaximumSentenceWords = 24;

    private static readonly string[] AdvicePhrases =
    [
        "allow extra time",
        "take care",
        "drive safely",
        "plan ahead",
        "stay safe",
        "slow down",
        "be careful",
        "motorists are warned"
    ];

    private static readonly HashSet<string> DirectionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "northbound", "southbound", "eastbound", "westbound", "inbound", "outbound", "citybound"
    };

    private static readonly Regex ExplicitGlobalScopePattern = new(
        @"\b(?:elsewhere|everywhere|across\s+(?:the\s+)?(?:city|network|region|area|town)|throughout\s+(?:the\s+)?(?:city|network|region|area)|(?:the\s+)?(?:whole|entire)\s+(?:city|network|region|area)|(?:city|network)[-\s]?wide|(?:all|every)\s+(?:of\s+the\s+|the\s+)?roads?|(?:the\s+)?rest\s+of\s+(?:the\s+)?(?:roads?|network)|everything\s+else)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WholeNetworkAllClearPattern = new(
        @"\b(?:(?:the\s+)?(?:whole|entire)\s+(?:city|network|area|region)|(?:the\s+)?(?:network|city|roads?|board|everything)|(?:all|most)\s+(?:the\s+)?(?:roads?|traffic)|(?:the\s+)?rest\s+of\s+(?:the\s+)?(?:roads?|network)|every\s+road)(?:\s+(?:is|are|has|have|has\s+been|have\s+been)|['’]s)\s+(?:all\s+)?(?:clear|quiet|light|good|no\s+(?:delays?|major\s+(?:delays?|issues?|problems?))|only\s+(?:minor|light)\s+(?:movement|traffic|delays?)|minimal\s+(?:movement|traffic|delays?)|little\s+(?:movement|traffic)|running\s+(?:clean|cleanly|well|smoothly)|moving\s+(?:well|nicely|smoothly|freely)|flowing\s+(?:well|nicely|freely)|looking\s+good|free[-\s]?flowing|no\s+dramas?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrafficAllClearPattern = new(
        @"\b(?:the\s+)?traffic(?:\s+(?:is|are|has|have|has\s+been|have\s+been)|['’]s)\s+(?:all\s+)?(?:clear|quiet|light|good|no\s+(?:delays?|major\s+(?:delays?|issues?|problems?))|only\s+(?:minor|light)\s+(?:movement|traffic|delays?)|minimal\s+(?:movement|traffic|delays?)|little\s+(?:movement|traffic)|running\s+(?:clean|cleanly|well|smoothly)|moving\s+(?:well|nicely|smoothly|freely)|flowing\s+(?:well|nicely|freely)|looking\s+good|free[-\s]?flowing|no\s+dramas?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnqualifiedAllClearPattern = new(
        @"\b(?:all\s+clear|no\s+delays?|only\s+(?:minor|light)\s+movement|minimal\s+movement)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GenericRoadNamePattern = new(
        @"\b(?:[A-Z]{1,3}\d{1,3}|[A-Za-z][A-Za-z'-]*(?:\s+[A-Za-z][A-Za-z'-]*){0,4}\s+(?:Road|Rd|Street|St|Avenue|Ave|Highway|Hwy|Freeway|Motorway|Drive|Dr|Way|Bridge|Tunnel|Parkway|Pkwy|Boulevard|Blvd|Lane|Ln|Circuit|Crescent|Cres|Terrace|Tce|Close|Court|Ct|Place|Pl))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     Runs every objective gate in a stable order. A non-empty read receives one result per
    ///     gate, including passing results, because the timeline is a verification surface rather
    ///     than an exception list. An empty composition is handled separately: twelve failures would
    ///     obscure the single useful diagnosis that composition produced no read at all.
    /// </summary>
    public static IReadOnlyList<CheckResult> Run(
        WorkflowRequest request,
        NormalizedInput input,
        FittedPlan plan,
        ComposedRead read,
        TraceRecorder trace)
    {
        var stopwatch = Stopwatch.StartNew();
        trace.StageStart("HardChecks", "Running deterministic checks over the composed read.");

        if (read.Sentences.Count == 0)
        {
            var empty = new CheckResult(
                "ComposedRead",
                false,
                "The composer produced no sentences, so the read cannot be verified.");
            trace.Check("HardChecks", $"{empty.Name}: {empty.Detail}", empty.Passed);
            trace.StageEnd("HardChecks", "Composition was empty; verification stopped.", stopwatch.Elapsed.TotalMilliseconds);
            return [empty];
        }

        var results = new List<CheckResult>();
        results.Add(CitationExists(input, read));
        results.Add(CitationAuthorised(input, plan, read));
        results.Add(MandatoryContentPresent(input, plan, read));
        results.Add(NoStoryDropped(plan, read));
        results.Add(RunningOrderFollowsPlan(plan, read));
        results.Add(NoDuplicateClaim(input, read));
        results.Add(NoUnsupportedScopeClaim(request, input, read));
        results.Add(NoRepeatedDirection(read));
        results.Add(SelectedFactCoverage(input, plan, read));
        results.Add(SentenceLength(read));
        results.Add(UnspokenPunctuation(read));
        results.Add(NoAdvice(read));
        results.Add(NoStationBookend(read));
        results.Add(DelayFigureOnce(read));
        if (plan.TargetWords != 0)
            results.Add(WordTargetBand(plan, read));

        foreach (var result in results)
            trace.Check("HardChecks", $"{result.Name}: {result.Detail}", result.Passed);

        int failed = results.Count(result => !result.Passed);
        trace.StageEnd(
            "HardChecks",
            $"Completed {results.Count} deterministic checks; {failed} failed.",
            stopwatch.Elapsed.TotalMilliseconds);
        return results;
    }

    private static CheckResult CitationExists(NormalizedInput input, ComposedRead read)
    {
        var factIds = input.Events
            .SelectMany(@event => @event.Facts)
            .Select(fact => fact.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var failures = read.Sentences
            .Select(sentence =>
            {
                var unknown = sentence.EvidenceIds
                    .Where(id => !factIds.Contains(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return (sentence, unknown);
            })
            .Where(item => item.unknown.Count > 0)
            .ToList();

        if (failures.Count == 0)
            return Passed("CitationExists", $"All cited evidence IDs resolve to known facts across {read.Sentences.Count} sentences.");

        string detail = string.Join(
            "; ",
            failures.Select(item =>
                $"sentence {item.sentence.Order} cites unknown evidence ID(s): {string.Join(", ", item.unknown)}"));
        return Failed("CitationExists", detail, failures[0].sentence.Order);
    }

    private static CheckResult CitationAuthorised(
        NormalizedInput input,
        FittedPlan plan,
        ComposedRead read)
    {
        var eventsByEvidence = input.Events
            .SelectMany(@event => @event.EvidenceIds.Select(evidenceId => (EvidenceId: evidenceId, Owner: @event)))
            .GroupBy(item => item.EvidenceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Owner).ToList(), StringComparer.Ordinal);
        var stories = plan.Stories.ToDictionary(story => story.StoryId, StringComparer.Ordinal);
        var failures = new List<(int Order, string Detail)>();

        foreach (var sentence in read.Sentences)
        {
            if (sentence.StoryId is null)
                continue;

            if (!stories.TryGetValue(sentence.StoryId, out var story))
            {
                failures.Add((sentence.Order, $"sentence {sentence.Order} names story '{sentence.StoryId}', which the fitted plan did not grant"));
                continue;
            }

            var authorisedIds = new List<string>();
            if (sentence.EvidenceIds.Count == 0)
            {
                failures.Add((sentence.Order, $"sentence {sentence.Order} is assigned to story '{story.StoryId}' but cites no evidence IDs"));
                continue;
            }

            foreach (var evidenceId in sentence.EvidenceIds.Distinct(StringComparer.Ordinal))
            {
                bool granted = story.AuthorizedEvidenceIds.Contains(evidenceId, StringComparer.Ordinal)
                    && eventsByEvidence.TryGetValue(evidenceId, out var owners)
                    && owners.Any(@event => story.EventIds.Contains(@event.EventId, StringComparer.Ordinal));
                if (!granted)
                    authorisedIds.Add(evidenceId);
            }

            if (authorisedIds.Count > 0)
            {
                failures.Add((
                    sentence.Order,
                    $"sentence {sentence.Order} cites evidence ID(s) not granted to story '{story.StoryId}': {string.Join(", ", authorisedIds)}"));
            }
        }

        if (failures.Count == 0)
            return Passed("CitationAuthorised", "Every attributed sentence cites only evidence granted to its fitted story.");

        return Failed(
            "CitationAuthorised",
            string.Join("; ", failures.Select(failure => failure.Detail)),
            failures[0].Order);
    }

    /// <summary>
    ///     Every fixture the STATION requires is actually voiced.
    ///     <para>
    ///         Deliberately answers to <see cref="NormalizedInput.RequiredEventIds" />, the contract,
    ///         rather than to the stories the planner happened to produce. Checking the plan against
    ///         itself makes this vacuous exactly when it matters: live on 2026-08-26 the planner
    ///         emitted four traffic stories and no weather or fuel story for melbourne, and the
    ///         earlier plan-scoped version reported PASS while the read went out with neither
    ///         fixture. A missing fixture is a breach whether or not anything planned it.
    ///     </para>
    /// </summary>
    private static CheckResult MandatoryContentPresent(
        NormalizedInput input,
        FittedPlan plan,
        ComposedRead read)
    {
        var voiced = read.Sentences.SelectMany(sentence => sentence.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        // The contract: every required event must have at least one of its facts cited on air.
        foreach (string requiredEventId in input.RequiredEventIds)
        {
            var required = input.Event(requiredEventId);
            if (required is null)
                continue;
            if (!required.EvidenceIds.Any(voiced.Contains))
                missing.Add(requiredEventId);
        }

        // Plus any fixture story the fitter did buy, which must also survive composition.
        foreach (var story in plan.Stories.Where(story => story.Role is StoryRole.Weather or StoryRole.Fuel))
        {
            if (!story.MustExpressEvidenceIds.Any(voiced.Contains) && !missing.Contains(story.StoryId))
                missing.Add(story.StoryId);
        }

        return missing.Count == 0
            ? Passed("MandatoryContentPresent",
                $"Every station-required fixture is voiced ({input.RequiredEventIds.Count} required).")
            : Failed(
                "MandatoryContentPresent",
                $"The station requires content that the read never voices: {string.Join(", ", missing)}.");
    }

    private static CheckResult NoStoryDropped(FittedPlan plan, ComposedRead read)
    {
        var realised = read.Sentences
            .Where(sentence => sentence.StoryId is not null)
            .Select(sentence => sentence.StoryId!)
            .ToHashSet(StringComparer.Ordinal);
        var missing = plan.Stories
            .Where(story => !realised.Contains(story.StoryId))
            .Select(story => story.StoryId)
            .ToList();

        return missing.Count == 0
            ? Passed("NoStoryDropped", $"All {plan.Stories.Count} fitted stories are assigned to at least one sentence.")
            : Failed(
                "NoStoryDropped",
                $"The composer omitted fitted story/stories: {string.Join(", ", missing)}.");
    }
    /// <summary>
    ///     Ensures the first mention of each realised story follows the fitted plan. Stories may be
    ///     omitted by composition, but the composer cannot transpose stories the station ordered.
    /// </summary>
    public static CheckResult RunningOrderFollowsPlan(FittedPlan plan, ComposedRead read)
    {
        var order = plan.Stories
            .Select((story, index) => (story.StoryId, Index: index))
            .ToDictionary(item => item.StoryId, item => item.Index, StringComparer.Ordinal);
        int previous = -1;
        string? previousStory = null;

        foreach (var sentence in read.Sentences)
        {
            if (sentence.StoryId is null)
                continue;

            if (!order.TryGetValue(sentence.StoryId, out int current))
                return Failed(
                    "RunningOrderFollowsPlan",
                    $"Sentence {sentence.Order} names story '{sentence.StoryId}', which is not in the fitted plan.",
                    sentence.Order);

            if (current < previous)
            {
                string required = string.Join(" then ", plan.Stories.Select(story => story.StoryId));
                return Failed(
                    "RunningOrderFollowsPlan",
                    $"Story '{sentence.StoryId}' is transposed after '{previousStory}'; required order is {required}.",
                    sentence.Order);
            }

            previous = current;
            previousStory = sentence.StoryId;
        }

        return Passed(
            "RunningOrderFollowsPlan",
            "The first mention of every realised story follows the fitted plan order.");
    }

    /// <summary>
    ///     Fields whose fact is a substantive condition rather than a way of locating or naming
    ///     something. Only these can constitute a duplicated claim.
    /// </summary>
    private static readonly HashSet<string> SubstantiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "detail", "impact", "delayMinutes", "incident", "condition", "price", "extentLength"
    };

    /// <summary>
    ///     The same SUBSTANTIVE fact must not be asserted in two sentences.
    ///     <para>
    ///         Replaces the old NoDuplicateRoad, which matched road NAMES in sentence text. Live on
    ///         2026-08-26 it fired on a correct pair — S1's subject James Congdon Drive naming South
    ///         Road as a cross-street, S2 inverting them — and chasing that false positive drove the
    ///         composer to delete the subject road's name entirely. A road name is not a fact: its
    ///         role in the sentence matters, and the deployed baseline writes exactly that pair.
    ///         So this checks cited evidence IDs instead: only an id whose field is a condition
    ///         (detail, impact, delay, price...) can be said twice; locating ids (location, suburb,
    ///         direction, road) never trigger, because naming the same cross-street from two
    ///         directions is legitimate radio.
    ///     </para>
    /// </summary>
    private static CheckResult NoDuplicateClaim(NormalizedInput input, ComposedRead read)
    {
        var factsById = input.Events
            .SelectMany(@event => @event.Facts)
            .GroupBy(fact => fact.EvidenceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // Keyed by (evidence id, story) rather than by evidence id alone. One story is allowed to
        // span several sentences - the composer is told outright that it may, and the read is better
        // for it: "Citybound road closures affect James Congdon Drive between South Road and Sir
        // Donald Bradman Drive. THAT CLOSURE covers about half a kay." Both sentences cite the same
        // detail because the second elaborates the first, which is continuation, not repetition.
        //
        // Measured live 2026-08-26: keying on the id alone flagged exactly that pair and cost two
        // extra whole-read compose calls per bulletin to "fix" correct copy. Repetition across
        // DIFFERENT stories is still a defect, because there the second mention has no antecedent.
        var citations = new Dictionary<(string EvidenceId, string Story), List<int>>();
        foreach (var sentence in read.Sentences)
        {
            foreach (string evidenceId in sentence.EvidenceIds.Distinct(StringComparer.Ordinal))
            {
                if (!factsById.TryGetValue(evidenceId, out var fact) || !SubstantiveFields.Contains(fact.Field))
                    continue;

                var key = (evidenceId, sentence.StoryId ?? string.Empty);
                if (!citations.TryGetValue(key, out var orders))
                {
                    orders = [];
                    citations.Add(key, orders);
                }

                orders.Add(sentence.Order);
            }
        }

        // A story restating its OWN fact in consecutive sentences is elaboration. Restating it after
        // the read has moved on is not, so only adjacent repetition inside one story is forgiven.
        var duplicates = citations
            .Where(pair => pair.Value.Count > 1)
            .Where(pair => !IsAdjacentWithinOneStory(pair.Key.Story, pair.Value))
            .Select(pair => (Fact: factsById[pair.Key.EvidenceId], Orders: pair.Value))
            .OrderBy(item => item.Orders[0])
            .ToList();
        if (duplicates.Count == 0)
            return Passed("NoDuplicateClaim", "No substantive fact is asserted in more than one sentence.");

        string detail = string.Join(
            "; ",
            duplicates.Select(duplicate =>
                $"the same {duplicate.Fact.Field} ('{duplicate.Fact.Value}') is stated twice: sentence order(s) {string.Join(", ", duplicate.Orders)}"));
        return Failed("NoDuplicateClaim", detail, duplicates[0].Orders[0]);
    }

    /// <summary>
    ///     Quiet-board copy may describe only the named roads in the evidence. A sentence that
    ///     broadens that evidence to "elsewhere", a city or the whole network is unsupported even
    ///     when it also names one or two roads, so it must be recomposed rather than rewritten after
    ///     the fact.
    /// </summary>
    private static CheckResult NoUnsupportedScopeClaim(
        WorkflowRequest request,
        NormalizedInput input,
        ComposedRead read)
    {
        var failures = read.Sentences
            .Select(sentence => (sentence, Terms: ScopeTerms(request, input, sentence.Text)))
            .Where(item => item.Terms.Count > 0)
            .ToList();

        if (failures.Count == 0)
            return Passed("NoUnsupportedScopeClaim", "Quiet-board and all-clear wording stays limited to named roads.");

        return Failed(
            "NoUnsupportedScopeClaim",
            string.Join(
                "; ",
                failures.Select(item =>
                    $"sentence {item.sentence.Order} makes an unsupported broad-scope claim ({string.Join(", ", item.Terms)})")),
            failures[0].sentence.Order);
    }

    private static IReadOnlyList<string> ScopeTerms(
        WorkflowRequest request,
        NormalizedInput input,
        string text)
    {
        var terms = ExplicitGlobalScopePattern
            .Matches(text)
            .Select(match => match.Value.Trim())
            .ToList();

        foreach (string location in LocationCandidates(request))
        {
            if (text.Contains($"across {location}", StringComparison.OrdinalIgnoreCase)
                || text.Contains($"across the {location}", StringComparison.OrdinalIgnoreCase))
            {
                terms.Add($"across {location}");
            }
        }

        bool namedRoad = ContainsNamedRoad(input, text);
        if (WholeNetworkAllClearPattern.IsMatch(text)
            || ((TrafficAllClearPattern.IsMatch(text) || UnqualifiedAllClearPattern.IsMatch(text))
                && !namedRoad))
        {
            terms.Add("whole-network all-clear wording");
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> LocationCandidates(WorkflowRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Market))
            yield return request.Market.Trim();

        string[] words = request.Location
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int maximum = Math.Min(words.Length, 3);
        for (int count = 1; count <= maximum; count++)
        {
            string candidate = string.Join(" ", words.Take(count)).Trim(',', '.', ':', ';');
            if (candidate.Length > 0)
                yield return candidate;
        }
    }

    private static bool ContainsNamedRoad(NormalizedInput input, string text) =>
        GenericRoadNamePattern.IsMatch(text)
        || input.Events
            .SelectMany(eventItem => eventItem.Facts)
            .Where(fact =>
                fact.Field.Contains("road", StringComparison.OrdinalIgnoreCase)
                || fact.Field.Contains("street", StringComparison.OrdinalIgnoreCase)
                || fact.Field.Equals("name", StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value.Trim())
            .Where(value => value.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     A direction supplied by HOW IT IS BEHAVING is already doing its job. Repeating the same
    ///     direction token in that sentence is redundant wording and a reliable recompose signal.
    /// </summary>
    private static CheckResult NoRepeatedDirection(ComposedRead read)
    {
        var failures = new List<(ComposedSentence Sentence, string Token, int Count)>();
        foreach (var sentence in read.Sentences)
        {
            var repeats = Tokenize(sentence.Text)
                .Where(DirectionTokens.Contains)
                .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => (Sentence: sentence, Token: group.Key, Count: group.Count()));
            failures.AddRange(repeats);
        }

        if (failures.Count == 0)
            return Passed("NoRepeatedDirection", "No sentence repeats a direction token.");

        return Failed(
            "NoRepeatedDirection",
            string.Join(
                "; ",
                failures.Select(item =>
                    $"sentence {item.Sentence.Order} repeats direction token '{item.Token}' {item.Count} times")),
            failures[0].Sentence.Order);
    }

    /// <summary>
    ///     True when one story elaborates its own fact across consecutive sentences. Requires a real
    ///     story id: an unattributed sentence has no antecedent to elaborate.
    /// </summary>
    private static bool IsAdjacentWithinOneStory(string storyId, List<int> orders)
    {
        if (storyId.Length == 0)
            return false;

        var sorted = orders.Order().ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] - sorted[i - 1] != 1)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Every fact the fitter SPENT BUDGET on is actually voiced. This closes the shrinking
    ///     treadmill measured live on 2026-08-26: the fitter budgeted 82 words, the composer wrote
    ///     69, the duration controller read the short output as a budget signal, and each pass shrank
    ///     the read further. Without this gate nothing told the composer that dropping budgeted
    ///     material was a failure rather than economy.
    /// </summary>
    private static CheckResult SelectedFactCoverage(NormalizedInput input, FittedPlan plan, ComposedRead read)
    {
        var voiced = read.Sentences.SelectMany(sentence => sentence.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        var factsById = input.Events
            .SelectMany(@event => @event.Facts)
            .GroupBy(fact => fact.EvidenceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var failures = new List<string>();
        foreach (var story in plan.Stories)
        {
            var unvoiced = story.MustExpressEvidenceIds
                .Where(id => !voiced.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unvoiced.Count == 0)
                continue;

            string rendered = string.Join(
                ", ",
                unvoiced.Select(id => factsById.TryGetValue(id, out var fact)
                    ? $"'{fact.Field}: {fact.Value}' ({id})"
                    : id));
            failures.Add($"story '{story.StoryId}' was fitted with facts it never voiced: {rendered}");
        }

        return failures.Count == 0
            ? Passed("SelectedFactCoverage", $"Every budgeted fact across {plan.Stories.Count} fitted story/stories is voiced.")
            : Failed("SelectedFactCoverage", string.Join("; ", failures) + ".");
    }

    private static CheckResult SentenceLength(ComposedRead read)
    {
        var counts = read.Sentences
            .Select(sentence => (sentence, CountWords(sentence.Text)))
            .ToList();
        var failures = counts.Where(item => item.Item2 > MaximumSentenceWords).ToList();
        int maximum = counts.Max(item => item.Item2);
        if (failures.Count == 0)
            return Passed("SentenceLength", $"All sentences are at most {MaximumSentenceWords} words; the longest contains {maximum} words.");

        string detail = string.Join(
            "; ",
            failures.Select(item => $"sentence {item.sentence.Order} contains {item.Item2} words (limit {MaximumSentenceWords})"));
        return Failed("SentenceLength", detail, failures[0].sentence.Order);
    }

    private static CheckResult UnspokenPunctuation(ComposedRead read)
    {
        var failures = read.Sentences
            .Where(sentence => sentence.Text.Contains(';') || sentence.Text.Contains(':'))
            .Select(sentence =>
            {
                var punctuation = new string(sentence.Text.Where(character => character is ';' or ':').Distinct().ToArray());
                return (sentence, punctuation);
            })
            .ToList();

        if (failures.Count == 0)
            return Passed("UnspokenPunctuation", "No sentence contains a semicolon or colon that lacks a spoken equivalent.");

        return Failed(
            "UnspokenPunctuation",
            string.Join("; ", failures.Select(item => $"sentence {item.sentence.Order} contains '{item.punctuation}'")),
            failures[0].sentence.Order);
    }

    private static CheckResult NoAdvice(ComposedRead read)
    {
        var failures = read.Sentences
            .SelectMany(sentence => AdvicePhrases
                .Where(phrase => sentence.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .Select(phrase => (sentence, phrase)))
            .ToList();

        if (failures.Count == 0)
            return Passed("NoAdvice", "The read states conditions without telling motorists what to do.");

        return Failed(
            "NoAdvice",
            $"The read contains advice phrase(s): {string.Join(", ", failures.Select(item => $"'{item.phrase}' in sentence {item.sentence.Order}"))}.",
            failures[0].sentence.Order);
    }

    private static CheckResult NoStationBookend(ComposedRead read)
    {
        var issues = new List<string>();
        if (read.Script.Contains("powered by", StringComparison.OrdinalIgnoreCase))
            issues.Add("contains 'powered by'");

        string opening = read.Sentences[0].Text.Trim();
        if (opening.StartsWith("Now looking over", StringComparison.OrdinalIgnoreCase)
            || opening.StartsWith("Looking over", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("opens with station furniture");
        }

        return issues.Count == 0
            ? Passed("NoStationBookend", "The read contains no deterministic station bookend wording.")
            : Failed("NoStationBookend", $"The read {string.Join(" and ", issues)}.");
    }

    private static CheckResult DelayFigureOnce(ComposedRead read)
    {
        var words = Tokenize(read.Script);
        var figures = new List<string>();
        for (int minuteIndex = 0; minuteIndex < words.Count; minuteIndex++)
        {
            if (!IsMinute(words[minuteIndex]))
                continue;

            for (int offset = 1; offset <= 4 && minuteIndex - offset >= 0; offset++)
            {
                string candidate = words[minuteIndex - offset];
                if (!IsNumber(candidate))
                    continue;

                figures.Add($"{candidate} {words[minuteIndex]}");
                break;
            }
        }

        if (figures.Count <= 1)
            return Passed("DelayFigureOnce", $"The read contains {figures.Count} numeric delay figure; no delay is repeated.");

        return Failed(
            "DelayFigureOnce",
            $"The read contains {figures.Count} numeric delay figures: {string.Join(", ", figures)}.");
    }

    private static CheckResult WordTargetBand(FittedPlan plan, ComposedRead read)
    {
        double margin = Math.Abs(plan.TargetWords) * 0.15;
        double lower = plan.TargetWords - margin;
        double upper = plan.TargetWords + margin;
        bool passed = read.WordCount >= lower && read.WordCount <= upper;
        string detail = $"Measured {read.WordCount} words against a target of {plan.TargetWords} words (allowed band {Math.Ceiling(lower)}–{Math.Floor(upper)}).";
        return new CheckResult("WordTargetBand", passed, detail);
    }

    private static CheckResult Passed(string name, string detail) => new(name, true, detail);

    private static CheckResult Failed(string name, string detail, int? sentenceOrder = null) =>
        new(name, false, detail, sentenceOrder);

    private static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    private static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        int start = -1;
        for (int index = 0; index <= text.Length; index++)
        {
            bool isWord = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (isWord && start < 0)
                start = index;
            else if (!isWord && start >= 0)
            {
                words.Add(text[start..index]);
                start = -1;
            }
        }

        return words;
    }

    private static bool IsMinute(string word) =>
        word.Equals("minute", StringComparison.OrdinalIgnoreCase)
        || word.Equals("minutes", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumber(string word)
    {
        if (word.Length > 0 && word.All(char.IsDigit))
            return true;

        return word.ToLowerInvariant() switch
        {
            "zero" or "one" or "two" or "three" or "four" or "five" or "six" or "seven" or "eight" or "nine"
                or "ten" or "eleven" or "twelve" or "thirteen" or "fourteen" or "fifteen" or "sixteen"
                or "seventeen" or "eighteen" or "nineteen" or "twenty" or "thirty" or "forty" or "fifty"
                or "sixty" or "seventy" or "eighty" or "ninety" or "hundred" or "thousand" or "million" => true,
            _ => false
        };
    }
}
