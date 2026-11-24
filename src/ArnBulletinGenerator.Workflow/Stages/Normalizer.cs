using System.Diagnostics;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Turns citation-level evidence into event-level meaning. Keeping the source key as the event
///     identity means a planner can only join facts that the fetcher already declared part of the
///     same source record; it cannot accidentally join similarly named corridors.
/// </summary>
public static class Normalizer
{
    private const string CorridorPrefix = "corridor:";

    /// <summary>
    ///     Groups evidence by its caller-owned source identity while preserving arrival order. Invalid
    ///     citation units are discarded rather than becoming un-citable events, because every retained
    ///     fact must have a usable citation handle for later workflow checks.
    /// </summary>
    public static NormalizedInput Normalize(WorkflowRequest request, TraceRecorder trace)
    {
        var stopwatch = Stopwatch.StartNew();
        trace.StageStart("Normalize", $"Normalizing {request.Evidence.Count} evidence units.");

        var groups = new Dictionary<string, List<EventFact>>(StringComparer.Ordinal);
        var sourceOrder = new List<string>();
        var skipped = 0;
        var unvoiceable = 0;

        foreach (var unit in request.Evidence)
        {
            if (unit is null || string.IsNullOrWhiteSpace(unit.Id) || string.IsNullOrWhiteSpace(unit.SourceKey))
            {
                skipped++;
                continue;
            }

            if (NonVoiceableFields.Contains(unit.Field))
            {
                unvoiceable++;
                continue;
            }

            if (!groups.TryGetValue(unit.SourceKey, out var facts))
            {
                facts = [];
                groups.Add(unit.SourceKey, facts);
                sourceOrder.Add(unit.SourceKey);
            }

            facts.Add(new EventFact(unit.Id, unit.Field, Speakable(unit.Field, unit.Value), unit.DeskOnly));
        }

        if (skipped > 0)
        {
            trace.Decision(
                "Normalize",
                $"Skipped {skipped} evidence unit(s) with a null or whitespace Id or SourceKey.");
        }

        if (unvoiceable > 0)
        {
            trace.Decision(
                "Normalize",
                $"Withheld {unvoiceable} provider-label unit(s) that cannot be spoken on air.");
        }

        var events = new List<BulletinEvent>(sourceOrder.Count);
        var duplicateFacts = 0;

        foreach (var sourceKey in sourceOrder)
        {
            var facts = groups[sourceKey];
            var kind = KindFor(sourceKey);
            var label = LabelFor(sourceKey, kind, facts);
            events.Add(new BulletinEvent(sourceKey, kind, label, facts) { Rank = EventRank.Of(kind, facts) });

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fact in facts)
            {
                if (!seenIds.Add(fact.EvidenceId))
                {
                    duplicateFacts++;
                }
            }
        }

        if (duplicateFacts > 0)
        {
            trace.Decision(
                "Normalize",
                $"Found {duplicateFacts} duplicate evidence id occurrence(s) within source group(s); retained all facts.");
        }

        // RequiredSections names source events, not supplement roles. Only a matching event becomes
        // mandatory; selected weather and fuel events remain eligible for the fitter to shed.
        var requiredEventIds = new List<string>();
        foreach (var requiredSection in request.RequiredSections)
        {
            var matchingEvent = events.Find(eventItem =>
                string.Equals(eventItem.EventId, requiredSection, StringComparison.OrdinalIgnoreCase));
            if (matchingEvent is not null)
            {
                requiredEventIds.Add(matchingEvent.EventId);
            }
        }

        var artifact = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var eventItem in events)
        {
            var renderedFacts = string.Join(
                "; ",
                eventItem.Facts.Select(fact => $"{OneLine(fact.Field)}={OneLine(fact.Value)}"));
            artifact[eventItem.EventId] =
                $"{eventItem.Kind} | {OneLine(eventItem.Label)} | {eventItem.Facts.Count} facts: {renderedFacts}";
        }

        trace.Artifact("Normalize", "Events", artifact);
        stopwatch.Stop();
        trace.StageEnd("Normalize", $"Normalized {events.Count} event(s).", stopwatch.Elapsed.TotalMilliseconds);

        return events.Count == 0 && request.Evidence.Count == 0
            ? new NormalizedInput([], [])
            : new NormalizedInput(events, requiredEventIds);
    }

    /// <summary>
    ///     Fields carrying a provider's internal label rather than anything a presenter could say.
    ///     <para>
    ///         Measured live 2026-08-26: HERE supplied <c>sourceDetail</c> values of "Asphalt Works",
    ///         "Weed Spraying", "Mowing", "Miscellaneous Works" and "Vms Trailer" for Adelaide. The
    ///         predecessor pipeline offered these to its writer, which quietly ignored them. This
    ///         workflow cannot: once the fitter spends budget on a fact, must-express obliges the
    ///         composer to voice it, and the read went to air saying "They are Asphalt Works."
    ///     </para>
    ///     <para>
    ///         Withholding them here rather than asking the composer to use judgement is deliberate.
    ///         An unspeakable label is not an editorial question, and every stage downstream is
    ///         entitled to assume the evidence it was given can be said out loud.
    ///     </para>
    /// </summary>
    private static readonly HashSet<string> NonVoiceableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "sourceDetail", "corroboration", "canonicalClass", "authorityPriority"
    };

    private static readonly string[] SmallNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];

    /// <summary>
    ///     Renders a value in the form it is meant to be SAID.
    ///     <para>
    ///         The compose prompt tells the model to voice supplied values exactly as given, which is
    ///         right, but <c>delayMinutes</c> arrives as the bare integer <c>2</c>. Taken literally
    ///         that produced "That delay is 2 minutes." on air. The instruction and the data have to
    ///         agree, and the data is the side that should change: a presenter says "two minutes".
    ///     </para>
    /// </summary>
    private static string Speakable(string field, string value)
    {
        if (!string.Equals(field, "delayMinutes", StringComparison.OrdinalIgnoreCase))
            return value;

        return int.TryParse(value.Trim(), out int minutes) && minutes >= 0 && minutes < SmallNumbers.Length
            ? SmallNumbers[minutes]
            : value;
    }

    private static BulletinEventKind KindFor(string sourceKey)
    {
        // Drive-time routes were normalising to Unknown, exactly as goodrun did before it. Brisbane
        // 2026-08-26 supplied "route:the Pacific Motorway into the city" carrying the single most
        // useful fact in the bulletin, and it reached the writer labelled Unknown.
        if (sourceKey.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
            return BulletinEventKind.DriveTime;

        if (sourceKey.StartsWith(CorridorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BulletinEventKind.Corridor;
        }

        if (string.Equals(sourceKey, "weather", StringComparison.Ordinal))
        {
            return BulletinEventKind.Weather;
        }

        if (string.Equals(sourceKey, "fuel", StringComparison.Ordinal))
        {
            return BulletinEventKind.Fuel;
        }

        // "goodrun" and per-road variants like "goodrun:South Eastern Freeway" are real, speakable
        // material. Leaving them Unknown (as before 2026-08-26) hid two valid clear-run facts from
        // planning entirely.
        if (sourceKey.StartsWith("goodrun", StringComparison.OrdinalIgnoreCase))
        {
            return BulletinEventKind.ClearRun;
        }

        return BulletinEventKind.Unknown;
    }

    private static string LabelFor(string sourceKey, BulletinEventKind kind, IReadOnlyList<EventFact> facts)
    {
        if (kind == BulletinEventKind.Weather)
        {
            return "Weather";
        }

        if (kind == BulletinEventKind.Fuel)
        {
            return "Fuel";
        }

        if (kind == BulletinEventKind.ClearRun)
        {
            return "Clear runs";
        }

        if (kind != BulletinEventKind.Corridor)
        {
            return sourceKey;
        }

        var roadName = sourceKey[CorridorPrefix.Length..];
        var suffixStart = roadName.LastIndexOf('#');
        if (suffixStart >= 0 && suffixStart < roadName.Length - 1 &&
            roadName[(suffixStart + 1)..].All(char.IsAsciiDigit))
        {
            roadName = roadName[..suffixStart];
        }

        var suburb = facts.FirstOrDefault(fact =>
            string.Equals(fact.Field, "suburb", StringComparison.OrdinalIgnoreCase));
        return suburb is null ? roadName : $"{roadName}, {suburb.Value}";
    }

    private static string OneLine(string value) => value.Replace("\r", " ").Replace("\n", " ");
}
