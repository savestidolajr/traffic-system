namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One atomic, citable fact a Draft sentence can reference by <see cref="Id"/>. Built
///     deterministically from the same domain data the traffic narrative summarizer renders as
///     prose (see <c>TrafficNarrativeSummarizer.BuildEvidenceUnits</c> in Infrastructure), so the
///     evidence set shown to the model never diverges from the prose it is also shown.
/// </summary>
/// <param name="Id">
///     Stable per-request identifier the Draft response cites (e.g.
///     <c>corridor:Punt Road#1:location:0</c>). The corridor ordinal is deterministic within the
///     bulletin and disambiguates multiple corridors carrying the same road name. Spaces and
///     punctuation are fine — this is a JSON string value inside a schema enum, not a code identifier.
/// </param>
/// <param name="SourceKey">Groups units from the same source (e.g. <c>corridor:Punt Road</c>, <c>weather</c>).</param>
/// <param name="Field">The kind of fact (<c>location</c>, <c>incident</c>, <c>impact</c>, <c>condition</c>, ...).</param>
/// <param name="Value">The fact text, exactly as it appears in the prose block.</param>
/// <param name="DeskOnly">
///     True when the value is context for a writer to reason from but must never be surfaced to the
///     composer as airable evidence. This keeps provider diagnostics such as "no delay measured"
///     useful for editorial judgement without letting them leak into the spoken read.
/// </param>
public sealed record EvidenceUnit(
    string Id,
    string SourceKey,
    string Field,
    string Value,
    bool DeskOnly = false);
