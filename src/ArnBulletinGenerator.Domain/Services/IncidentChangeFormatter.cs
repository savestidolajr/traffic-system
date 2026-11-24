using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Renders the same-day incident delta (New / Still ongoing / Cleared / Updated) into the
///     compact "Changes since last bulletin" block the writer receives for continuity.
/// </summary>
public static partial class IncidentChangeFormatter
{
    // Per-category caps bound the continuity block handed to the writer so it stays
    // proportionate to a ~30s read — the delta is context, not content. Weighted toward
    // New (the news), with the Still-ongoing feed deliberately narrow (cap 2 + the
    // IsOngoingWorthMentioning gate; see docs/canonical/decisions.md 2026-06-28 "Same-day
    // continuity must voice an explicit callback"). The specific values 3/2/2/1 were set
    // when the delta feature was introduced and carry no recorded tuning rationale —
    // re-tune against live continuity output, not by inspection.
    private const int NewCap = 3;
    private const int OngoingCap = 2;
    /// <summary>
    ///     Exposed because the Infrastructure evidence path must apply the SAME cap; it silently
    ///     did not, which is how a 22s Sydney read spent ~40% of its slot on cleared items.
    /// </summary>
    public const int DefaultClearedCap = 2;

    private const int UpdatedCap = 1;

    public static string BuildSummary(
        Option<IncidentDeltaContext> changes, EditorialSettings? settings = null)
    {
        return changes.Match(
            delta => FormatDelta(delta, settings),
            () => string.Empty);
    }

    private static string FormatDelta(IncidentDeltaContext delta, EditorialSettings? settings)
    {
        int newCap = settings?.ContinuityNewCap ?? NewCap;
        int ongoingCap = settings?.ContinuityOngoingCap ?? OngoingCap;
        int clearedCap = settings?.ContinuityClearedCap ?? DefaultClearedCap;
        int updatedCap = settings?.ContinuityUpdatedCap ?? UpdatedCap;

        var lines = FormatCategory("New", delta.NewIncidents.Take(newCap).ToList())
            .Concat(FormatCategory("Still ongoing", delta.UnchangedIncidents.Where(IsOngoingWorthMentioning).Take(ongoingCap).ToList()))
            .Concat(FormatCategory("Cleared", delta.ClearedIncidents.Where(IsClearedWorthVoicing).Take(clearedCap).ToList()))
            .Concat(FormatCategory("Updated", delta.UpdatedIncidents.Take(updatedCap).ToList()))
            .ToList();

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }

    private static bool IsOngoingWorthMentioning(IncidentDeltaItem item) =>
        item.Criticality <= 2 && !IncidentTypeClassifier.IsStandingFootprint(item.Type);

    /// <summary>
    ///     Whether a CLEARED incident has earned airtime, judged on severity rather than on recency.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Decided 2026-08-23, ARN's call. A major closure or crash reopening is useful to ANY
    ///         listener deciding a route right now — "the Harbour Tunnel's reopened" stands on its own.
    ///         Congestion easing is useful to nobody: it is not news to someone who heard the earlier
    ///         bulletin and it is meaningless to someone who did not. On a music station a listener
    ///         hears one bulletin, not a sequence, so continuity between bulletins is a fiction from
    ///         the audience's side almost all of the time.
    ///     </para>
    ///     <para>
    ///         This deliberately replaces the alternative design of gating on broadcast history ("only
    ///         say it cleared if a bulletin actually aired saying it was there"). That needed the
    ///         pipeline to persist what it BROADCAST rather than what it observed — new state, a new
    ///         way to be out of sync — to answer a question severity answers better. It also made a
    ///         first-of-day bulletin, a filler-cart-covered slot and a schedule gap into three
    ///         separate edge cases; here they all behave identically, because the test is "is this
    ///         worth telling a stranger". Explicit continuity ("as we mentioned earlier...") is
    ///         deliberately NOT a feature.
    ///     </para>
    ///     <para>
    ///         Measured cause: a live Sydney 22s read spent ~40% of the slot on seven cleared roads
    ///         while the only live story, a Harbour Tunnel closure, got one sentence.
    ///     </para>
    /// </remarks>
    public static bool IsClearedWorthVoicing(IncidentDeltaItem item) =>
        item.Criticality <= 2
        && (IncidentTypeClassifier.IsClosure(item.Type) || IncidentTypeClassifier.IsCrash(item.Type))
        && !IncidentTypeClassifier.IsStandingFootprint(item.Type);

    private static IEnumerable<string> FormatCategory(string label, IReadOnlyList<IncidentDeltaItem> items)
    {
        return items.Select(item =>
        {
            string location = BuildLocationLabel(item);
            string typeLabel = string.IsNullOrWhiteSpace(item.Type) ? "incident" : item.Type;
            return $"- {label}: {typeLabel} on {location}";
        });
    }

    private static string BuildLocationLabel(IncidentDeltaItem item)
    {
        var parts = new[] { item.RoadName, item.Suburb, item.Direction }
            .Where(o => o.HasValue)
            .Select(o => o.Value)
            .Where(v => !RoadNameFormatter.LooksLikeRawAddress(v))
            .ToList();

        return parts.Count > 0
            ? string.Join(", ", parts)
            : item.Description.GetValueOr("unknown location");
    }
}
