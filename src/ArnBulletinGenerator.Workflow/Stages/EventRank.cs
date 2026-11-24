using System.Globalization;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow.Model;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     How much a driver needs an event, on one scale shared by every kind of event.
///     <para>
///         This exists because the writing is split across desks. Two independent writers cannot
///         produce comparable priorities — one desk's 95 means nothing to the other — so the running
///         order cannot be theirs to invent. Computing it once, here, from the facts both desks
///         already share is what makes their briefs mergeable.
///     </para>
///     <para>
///         It is deliberately NOT sourced from the feeds' <c>authorityPriority</c>. That field looked
///         like the answer and is not: it carries categorical labels ("critical"), it is present only
///         on official-feed items and never on HERE corridors, and it is stripped at the evidence
///         boundary before the workflow sees anything. Measured on job b0867247, every event reached
///         the stitcher with rank 0 and the order collapsed to each desk's own numbering.
///     </para>
///     <para>
///         The band values are owned by <see cref="CorridorEditorialRank" /> in Domain. This type
///         is the evidence-fact projection of that scale, while <c>CorridorEditorialRank</c> is its
///         <c>TrafficCorridor</c> projection. They must stay in agreement: that is why the constants
///         are shared rather than copied.
///     </para>
///     <para>
///         Derived rather than plumbed on purpose. <c>CorridorEditorialRank</c> computes the same
///         judgement from a <c>TrafficCorridor</c>, but the workflow only ever sees evidence units,
///         and threading a new field through <c>BuildEvidenceUnits</c> would put decision metadata
///         in front of the matrix pipeline's writer. Rank must never become a voiceable evidence
///         fact — the exact leak that the deny-list exists to prevent.
///     </para>
/// </summary>
internal static class EventRank
{
    // Bands, not a continuum. What separates these is what a driver DOES about them: route around
    // it, allow more time, or nothing at all. Gaps are wide enough that no delay figure can lift a
    // queue above a closure. The shared values live in CorridorEditorialRank so corridor and
    // evidence-fact projections cannot drift.
    // One or two minutes are real, measured, and almost never worth the airtime they cost. This
    // band was given its own floor below everything else on 2026-08-27: it used to share the
    // BuildingDelay floor, so a two-minute suburban queue scored 200 and a four-minute run 204 —
    // indistinguishable. Job ffcad4af spent a 22 second Sydney slot on a two-minute delay at
    // Greenacre, a one-minute delay at Seven Hills and a one-minute delay at Mascot, while the M4
    // from Parramatta ran four minutes slow.

    /// <summary>
    ///     What a whole monitored journey is worth over a single corridor carrying the same delay.
    ///     <para>
    ///         A drive time measures an end-to-end run a listener is actually making; a corridor
    ///         measures a few hundred metres of one road. Four minutes across the M4 from
    ///         Parramatta and four minutes on a suburban street are not the same news, and nothing
    ///         else in the facts distinguishes them.
    ///     </para>
    /// </summary>
    private const int JourneyWeight = 40;

    /// <summary>Rank for one event. Higher leads.</summary>
    internal static int Of(BulletinEventKind kind, IReadOnlyList<EventFact> facts)
    {
        if (kind is BulletinEventKind.Weather or BulletinEventKind.Fuel)
        {
            // Fixtures are carried by Required, never by rank: a fixture is not up for judgement,
            // so it must not compete on the scale that expresses judgement.
            return 0;
        }

        if (kind is BulletinEventKind.ClearRun)
        {
            return CorridorEditorialRank.ClearRunBase;
        }

        int delay = DelayMinutes(facts);
        int delayPoints = Math.Min(delay * 8, 120);
        int journey = kind is BulletinEventKind.DriveTime ? JourneyWeight : 0;

        if (IsBlockage(facts))
        {
            return 120 + CorridorEditorialRank.ClosureBonus + delayPoints + journey;
        }

        return 60 + delayPoints + journey;
    }

    /// <summary>
    ///     Whether the road is blocked rather than merely slow. Read from the same fields that route
    ///     the event to the incidents desk, so a story's rank and its desk can never disagree.
    /// </summary>
    private static bool IsBlockage(IReadOnlyList<EventFact> facts) =>
        facts.Any(fact =>
            fact.Field.Equals("lanesClosed", StringComparison.OrdinalIgnoreCase)
            || ((fact.Field.Equals("detail", StringComparison.OrdinalIgnoreCase)
                    || fact.Field.Equals("incident", StringComparison.OrdinalIgnoreCase))
                && ClosureWords.Any(word => fact.Value.Contains(word, StringComparison.OrdinalIgnoreCase))));

    private static readonly string[] ClosureWords = ["clos", "shut", "blocked", "crash", "collision"];

    /// <summary>
    ///     The delay in minutes. Evidence arrives already worded for speech ("four"), so the spoken
    ///     form is read as well as the numeric one — a rank that silently scored every delay zero
    ///     would order the bulletin by nothing at all.
    /// </summary>
    private static int DelayMinutes(IReadOnlyList<EventFact> facts)
    {
        EventFact? fact = facts.FirstOrDefault(f =>
            f.Field.Equals("delayMinutes", StringComparison.OrdinalIgnoreCase));
        if (fact is null)
        {
            return 0;
        }

        if (int.TryParse(fact.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        int spoken = Array.IndexOf(SpokenNumbers, fact.Value.Trim().ToLowerInvariant());
        return spoken > 0 ? spoken : 0;
    }

    private static readonly string[] SpokenNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];
}
