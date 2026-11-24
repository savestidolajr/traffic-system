using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Contracts.Messages;
namespace ArnBulletinGenerator.Domain.Models;
/// <summary>
///     Base for all broadcast context types. Common properties hoisted here to eliminate duplication.
/// </summary>
public abstract record BroadcastContextBase
{
    public required double TargetDurationSeconds { get; init; }
    public required string JobId { get; init; }
    public required string Location { get; init; }
    public bool ScriptOnly { get; init; }
    public Option<string> MarketId { get; init; }
    /// <summary>Queue processing phase; zero preserves legacy fetch-and-generate messages.</summary>
    public Phase Phase { get; init; } = Phase.FetchAndGenerate;
    public Option<string> TimezoneId { get; init; }
    public Option<string> VoiceId { get; init; }
    public Option<string> Model { get; init; }
    public Option<VoiceSettings> VoiceSettings { get; init; }

    /// <summary>
    ///     Additive WPM boost over the resolved voice's calibrated speaking rate (see
    ///     <see cref="VoiceProfileVersion.PaceBoostWpm" />), applied to pre-synthesis script
    ///     sizing only — never sent to the TTS provider. 0 = no-op (ad-hoc/manual broadcasts that
    ///     bypass the voice profile library default to 0).
    /// </summary>
    public double PaceBoostWpm { get; init; }
    public Option<string> RejectionFeedback { get; init; }
    public Option<string> PreviousScript { get; init; }
    public Option<string> PreviousJobId { get; init; }

    public Option<string> StationId { get; init; }

    /// <summary>
    ///     Resolved delivery policy (ElevenLabs v3 audio-tag steering) for this bulletin.
    ///     None = neutral (no tagging; reads as today). Set by the poller from the station's
    ///     delivery style, or by config default.
    /// </summary>
    public Option<DeliveryStylePolicy> DeliveryStyle { get; init; }

    /// <summary>Id of the applied delivery style, for job provenance. None = neutral/config-default.</summary>
    public Option<string> DeliveryStyleId { get; init; }
    /// <summary>
    ///     Resolved station bulletin playbook overlay, snapshotted before queue dispatch. Null means
    ///     no station-specific playbook is assigned.
    /// </summary>
    public PlaybookOverlay? Playbook { get; init; }

    /// <summary>Version of the applied delivery style, for job provenance.</summary>
    public Option<int> DeliveryStyleVersion { get; init; }

    /// <summary>
    ///     The station's resolved bookend pair — the fixed lines read immediately BEFORE and AFTER
    ///     the bulletin body ("Looking over Melbourne today..." / "That's it for Melbourne
    ///     traffic."), already token-substituted for this station. None = the station has no
    ///     bookend and the bulletin reads exactly as it does today; either line inside the pair may
    ///     also be empty. Set by the poller from the station's assigned pair; never set for
    ///     ad-hoc/manual broadcasts.
    ///     <para>
    ///         When the pair has an outro, filler is spoken INSIDE the same take before it rather
    ///         than stitched after it, so the closer always has the last word (see
    ///         <c>ScriptGenerationEngine</c>).
    ///     </para>
    /// </summary>
    public Option<ResolvedBookends> Bookends { get; init; }

    /// <summary>
    ///     Optional plain-language re-direction from an operator ("read it calmer") applied on
    ///     top of the policy on a delivery-only regeneration. None for normal generation.
    /// </summary>
    public Option<string> DeliveryIntent { get; init; }

    /// <summary>
    ///     The approved words carrying the operator's own v3 audio tags. When present and
    ///     word-identical to the script, it is synthesised verbatim and the delivery director
    ///     is skipped — one author per read (decision 2026-08-02).
    /// </summary>
    public Option<string> TonedScript { get; init; }

    /// <summary>
    ///     Resolved pronunciation dictionaries (ElevenLabs) for this bulletin, station layer
    ///     first so it overrides the platform-wide layer per word. Empty = no dictionary applies.
    ///     Set by the poller from the station's assigned dictionary plus the active global one;
    ///     attached to the synthesis request so suburb/road rules apply.
    /// </summary>
    public PronunciationStack PronunciationStack { get; init; } = PronunciationStack.Empty;

    /// <summary>
    ///     Content sources for this bulletin (WS1: traffic + weather + fuel, per-market
    ///     selection, independently toggleable). Defaults to traffic-only.
    /// </summary>
    public BulletinContentSelection Content { get; init; } = BulletinContentSelection.Default;

    /// <summary>
    ///     Per-station order remaining-slot fill sources are tried in (BFN-003) once traffic
    ///     incidents alone can't reach the target duration. Set by the poller from the schedule's
    ///     configured order; defaults to weather, fuel, minor traffic, filler if unset.
    /// </summary>
    public IReadOnlyList<FillSource> FillOrder { get; init; } =
        [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler];

    /// <summary>
    ///     In-process observer for structured generation progress. It is deliberately ignored by
    ///     queue serialization because the reporter owns storage resources and is only valid during
    ///     the current generation invocation.
    /// </summary>
    [JsonIgnore]
    public IGenerationProgressReporter? ProgressReporter { get; init; }
    public bool HasTraffic => this is BroadcastWithTraffic;
}

/// <summary>
///     Initial request — traffic will be fetched from the location.
/// </summary>
public sealed record BroadcastRequest : BroadcastContextBase
{
    public required CoverageArea Coverage { get; init; }

    public BroadcastWithTraffic WithCorridors(IReadOnlyList<TrafficCorridor> corridors)
    {
        return CreateWithTraffic(
            CorridorFormatter.Format(corridors),
            corridors.SelectMany(c => c.Incidents).ToList(),
            corridors);
    }

    private BroadcastWithTraffic CreateWithTraffic(
        string traffic, IReadOnlyList<TrafficIncident> incidents, IReadOnlyList<TrafficCorridor> corridors)
    {
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = TargetDurationSeconds,
            JobId = JobId,
            Location = Location,
            ScriptOnly = ScriptOnly,
            Phase = Phase,
            MarketId = MarketId,
            TimezoneId = TimezoneId,
            VoiceId = VoiceId,
            Model = Model,
            VoiceSettings = VoiceSettings,
            PaceBoostWpm = PaceBoostWpm,
            RejectionFeedback = RejectionFeedback,
            PreviousScript = PreviousScript,
            Content = Content,
            FillOrder = FillOrder,
            PreviousJobId = PreviousJobId,
            StationId = StationId,
            DeliveryStyle = DeliveryStyle,
            DeliveryStyleId = DeliveryStyleId,
            DeliveryStyleVersion = DeliveryStyleVersion,
            Playbook = Playbook,
            Bookends = Bookends,
            DeliveryIntent = DeliveryIntent,
            TonedScript = TonedScript,
            PronunciationStack = PronunciationStack,
            Traffic = traffic,
            Incidents = incidents,
            Corridors = corridors
        };
    }
}

/// <summary>
///     Context after traffic has been fetched — ready for script generation.
/// </summary>
public sealed record BroadcastWithTraffic : BroadcastContextBase
{
    public required string Traffic { get; init; }
    public required IReadOnlyList<TrafficIncident> Incidents { get; init; }
    public IReadOnlyList<TrafficCorridor> Corridors { get; init; } = [];

    /// <summary>
    ///     Every corridor the fetch returned, before the slot-capacity cut that produced
    ///     <see cref="Corridors" />. Empty when nothing was cut.
    ///     <para>
    ///         Slot capacity is a limit on what a read can VOICE, and applying it to what the
    ///         writers may SEE means the choosing happened in a scoring formula before any editor
    ///         looked. Cairns job 8383b04c: 17 rows fetched, <c>Take(6)</c> kept six, and because
    ///         several were repeat sites on one road only FOUR reached the desks. Nine distinct
    ///         roads - including three separate Manoora works that together were the actual story -
    ///         were never shown to anyone.
    ///     </para>
    ///     <para>
    ///         The matrix still reads <see cref="Corridors" />: its prose voices what it is given,
    ///         so widening that would overstuff the read. The conversational writers brief from
    ///         here and let the composer drop what will not fit, which is the order the prompts
    ///         already assume - "brief them all and let your colleague choose".
    ///     </para>
    /// </summary>
    public IReadOnlyList<TrafficCorridor> AllCorridors { get; init; } = [];
    public Option<IncidentDeltaContext> IncidentChanges { get; init; }
    public Option<string> City { get; init; }
    public Option<int> TargetWordCountOverride { get; init; }
    public Option<string> RetryContext { get; init; }

    /// <summary>
    ///     Operator-curated corridor road names (ordered) from a regeneration request.
    ///     When set, freshly fetched corridors are filtered/reordered to this selection
    ///     and the narrative preserves the operator's priority order.
    /// </summary>
    public Option<IReadOnlyList<string>> CuratedCorridorRoads { get; init; }

    /// <summary>
    ///     Operator-curated good-run road names from a regeneration request (Flow tab pick). When
    ///     set, only these flowing roads air — overriding the automatic good-run policy. Data-backed:
    ///     the orchestrator restricts the pick to roads present in the live flow pool.
    /// </summary>
    public Option<IReadOnlyList<string>> CuratedGoodRunRoads { get; init; }

    /// <summary>
    ///     HERE incident ids the operator excluded on the job-review rundown. Dropped from the
    ///     fresh fetch during regeneration (key match; cleared ones already absent). None = keep all.
    /// </summary>
    public Option<IReadOnlyList<string>> ExcludedIncidentKeys { get; init; }

    /// <summary>
    ///     Re-voice only: synthesise the existing/edited script verbatim, skipping traffic re-fetch
    ///     and LLM generation/review. The operator's exact words go to air (still safety-checked).
    /// </summary>
    public bool RevoiceOnly { get; init; }

    /// <summary>
    ///     True when this generation was requested by an operator working the job right now (the
    ///     regeneration queue is only ever fed by the console/API regenerate action). An attended
    ///     generation that comes back unairable must NOT cover-and-publish the slot early — the
    ///     operator is mid-edit and the deadline sweep remains the slot's net (prod 2026-08-18
    ///     05:44: a curated regeneration hit content-limited and the cart was published 10 minutes
    ///     before deadline while the operator was still working). False for poller first-pass and
    ///     ad-hoc trigger generations, preserving cover-now for unattended slots.
    /// </summary>
    public bool OperatorAttended { get; init; }

    /// <summary>Fetched weather for this bulletin (None when not selected or unavailable).</summary>
    public Option<WeatherReport> Weather { get; init; }

    /// <summary>Fetched fuel prices for this bulletin (None when not selected or unavailable).</summary>
    public Option<FuelPriceReport> Fuel { get; init; }

    /// <summary>Priority roads with no active incidents — reported as good runs (WS2 / SDD §4.2).</summary>
    public IReadOnlyList<string> GoodRunRoads { get; init; } = [];

    /// <summary>
    ///     The full ranked, data-backed pool of roads HERE flow reported as flowing freely (with
    ///     the station/priority weighting applied), persisted for the review Flow tab. A superset of
    ///     <see cref="GoodRunRoads"/> (the subset chosen to air).
    /// </summary>
    public IReadOnlyList<FlowRoad> FlowPool { get; init; } = [];

    /// <summary>Configured commuter routes with live-vs-typical drive times for headline selection.</summary>
    public IReadOnlyList<MonitoredRouteStatus> MonitoredRoutes { get; init; } = [];

    /// <summary>Active explicit editorial lessons for this station, replayed into the writer prompt.</summary>
    public IReadOnlyList<FutureLessonEntry> FutureLessons { get; init; } = [];

    /// <summary>Composed standing guidance for this bulletin (Layer 2 self-service guidance).</summary>
    public ComposedGuidance Guidance { get; init; } = ComposedGuidance.Empty;


    /// <summary>
    ///     ARN's resolved numeric editorial settings for this bulletin — the caps and thresholds that
    ///     were compile-time constants. Never null: an unconfigured platform resolves to
    ///     <see cref="EditorialSettings.Empty" />, whose every field is unset, so each consumer falls
    ///     back to its own shipped default and behaves exactly as the constants did.
    /// </summary>
    public EditorialSettings EditorialSettings { get; init; } = EditorialSettings.Empty;

    /// <summary>
    ///     Per-market evergreen filler statements available to top up a slot that real traffic,
    ///     weather, and fuel content cannot fill — the ONLY legitimate fill between real data and a
    ///     shorter read. Populated by the orchestrator from <c>IFillerStatementsRepository</c>,
    ///     already filtered to enabled statements whose gap range fits the estimated shortfall and
    ///     excluding any voiced in this station's previous same-day bulletin.
    /// </summary>
    public IReadOnlyList<FillerStatement> FillerStatements { get; init; } = [];

    /// <summary>
    ///     Rebuilds the context with fresh corridor data, reformatting the traffic narrative.
    /// </summary>
    public BroadcastWithTraffic WithCorridors(IReadOnlyList<TrafficCorridor> corridors)
    {
        return this with
        {
            Traffic = CorridorFormatter.Format(corridors),
            Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = corridors
        };
    }
}
