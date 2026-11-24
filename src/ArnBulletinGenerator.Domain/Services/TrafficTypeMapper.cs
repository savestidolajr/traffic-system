namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Single source of truth for mapping raw traffic incident type strings (e.g. "accident",
///     "roadClosure") to the words a presenter says.
///     <para>
///         These labels are not display-only. They reach the writer twice: through
///         <c>CorridorAggregator.BuildCorridorDescription</c>, which becomes the corridor's
///         <c>Description</c> and then an evidence unit, and through <c>CorridorFormatter</c>. So
///         "human-readable" was never a high enough bar — the value has to be sayable, and two of
///         these were not. "Planned event" aired verbatim twice on the 2026-08-25 end-to-end run
///         ("Argyle Street is a touch slower for a planned event"), reaching air through the
///         Description path even after the incident-type map in the evidence builder was fixed,
///         because that builder had quietly grown a SECOND copy of this table with different
///         values. "Heavy congestion" is a banded severity label that
///         <see cref="EvidenceLabelVocabulary" /> bans outright.
///     </para>
///     <para>
///         The duplicate in <c>TrafficNarrativeSummarizer.MapIncidentType</c> is gone; it now calls
///         this. Callers that want lower case apply it themselves, as they already did.
///     </para>
/// </summary>
public static class TrafficTypeMapper
{
    // The single label map for raw HERE incident types. (A parallel, config-bound copy —
    // TrafficDisplayOptions.TypeLabels — was never registered or read and was deleted 2026-07-02.
    // A second parallel copy in the Infrastructure evidence builder was deleted 2026-08-25.)
    public static string MapType(string type)
    {
        return string.IsNullOrEmpty(type)
            ? "Incident"
            : type.ToLowerInvariant() switch
            {
                "accident" => "Crash",
                "roadclosure" => "Road closure",
                "disabledvehicle" => "Breakdown",
                "roadhazard" => "Hazard",
                "lanerestriction" => "Lane closure",
                "construction" => "Roadworks",
                "plannedevent" => "An event",
                // Bare "congestion" is ordinary radio language; the BANDED forms are the labels,
                // and EvidenceLabelVocabulary owns those.
                "congestion" => "Congestion",
                _ => "Incident"
            };
    }
}
