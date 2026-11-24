namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Script Stage DTOs
// =============================================================================

/// <summary>
///     A single sentence of the bulletin script with its originating corridor keys, the monitored
///     drive-time routes it names, and the evidence-unit ids committed by the structured Draft.
///     All three empty means the sentence has no traffic source (branding, sign-off, general
///     advice).
/// </summary>
public sealed record ScriptSentenceDto(
    string Text,
    IReadOnlyList<string> CorridorKeys,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> RouteKeys);

/// <summary>A candidate phrasing for a drafted sentence, preserving its committed evidence.</summary>
public sealed record DraftedVariant(string Style, string Text);

/// <summary>
///     One sentence as the Draft or Refine committed it: the facts it cited, then the words.
///     <see cref="CorridorKeys"/> carries the owner road for those facts even when the spoken
///     sentence names only a cross street or other incident detail. <see cref="Variants"/> contains
///     alternate phrasings of the same cited facts.
/// </summary>
public sealed record DraftedSentence
{
    public DraftedSentence(
        int order,
        string text,
        IReadOnlyList<string> evidenceIds,
        IReadOnlyList<DraftedVariant>? variants = null,
        IReadOnlyList<string>? corridorKeys = null)
    {
        Order = order;
        Text = text;
        EvidenceIds = evidenceIds;
        Variants = variants ?? [];
        CorridorKeys = corridorKeys ?? [];
    }

    public int Order { get; init; }
    public string Text { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; }
    public IReadOnlyList<DraftedVariant> Variants { get; init; }
    public IReadOnlyList<string> CorridorKeys { get; init; }

    /// <summary>
    ///     The bulletin segment this sentence voices, decided by the workflow from the supplied
    ///     facts the sentence carries (the same material <c>SegmentRunningOrder</c> checks), never
    ///     from a word list. <c>null</c> means the sentence voices no segment (intro, outro,
    ///     filler, connective). This is the single source of truth downstream stages use to
    ///     protect required weather/fuel lines and to label sentence provenance.
    /// </summary>
    public Models.BulletinSegment? Section { get; init; }
}

/// <summary>A generated script plus its per-sentence citation and phrasing structure.</summary>
public sealed record DraftedScript(string Text, IReadOnlyList<DraftedSentence> Sentences)
{
    /// <summary>
    ///     Provider response/thread identifier for the composer that authored this read. When
    ///     present, a measured duration miss can be corrected in that same conversation rather
    ///     than paying for a fresh writer/composer run that has forgotten the chosen read.
    /// </summary>
    public string? ComposerThreadId { get; init; }
}

/// <summary>
///     A data-backed road phrase found in the rendered script. <see cref="Start" /> and
///     <see cref="End" /> are UTF-16 offsets with <c>End</c> exclusive. When a valid tagged read
///     exists, the offsets are in that tagged text; otherwise they are in <see cref="ScriptDataDto.Text" />.
///     <see cref="Text" /> is the exact phrase the operator will teach the pronunciation dictionary,
///     while <see cref="RoadName" /> is the canonical road identity from the traffic snapshot.
/// </summary>
public sealed record ScriptRoadPhraseDto(
    string RoadName,
    int Start,
    int End,
    string Text);

/// <summary>
///     Generated script with version tracking, duration estimate, optional per-sentence provenance,
///     and server-resolved road phrase targets for operator pronunciation editing.
/// </summary>
public sealed record ScriptDataDto(
    int Version,
    string Text,
    double EstimatedDurationSec,
    IReadOnlyList<ScriptSentenceDto>? Sentences = null,
    string? TaggedText = null,
    string? DeliveryStyleId = null,
    int? DeliveryStyleVersion = null,
    IReadOnlyList<ScriptRoadPhraseDto>? RoadPhraseSpans = null);
