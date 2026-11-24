namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>
///     Schedule summary for list and detail views.
/// </summary>
public sealed record ScheduleSummaryDto(
    string MarketId,
    string? Name,
    string? Station,
    string? Timezone,
    string? CronExpression,
    bool Enabled,
    double TargetDurationSeconds,
    int NotifyBeforePlayoutMinutes,
    int EscalationBeforePlayoutMinutes,
    string? VoiceProfileId,
    DateTime? NextRunAt,
    string? CurrentJobId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    CoverageDto? Coverage,
    ContentSelectionDto? Content = null,
    string? DeliveryStyleId = null,
    string? PlaybookId = null,
    string? PronunciationDictionaryId = null,
    VoiceRotationDto? VoiceRotation = null,
    IReadOnlyList<string>? FillOrder = null,
    string? BookendId = null,
    BookendRotationDto? BookendRotation = null,
    IReadOnlyDictionary<string, string>? BookendTokens = null,
    /// <summary>
    ///     Advisory rendered ONLY on the create/update schedule PUT response, null on every other
    ///     path (GET, list): when the assigned bookend pair cannot share the slot with a minimum
    ///     viable traffic read, generation drops the bookend and airs the traffic (finding 4
    ///     policy) — the admin learns at save time that this station will not read the pair.
    ///     A warning, never an error: the save succeeds either way. The null-vs-string value is
    ///     the notice/error discriminator the console styles on.
    /// </summary>
    string? BookendFitWarning = null,
    ScheduleCalendarDto? Calendar = null,
    bool CurationEnabled = false,
    /// <summary>
    ///     Whether this station is armed for playout delivery in THIS environment. True (armed) is
    ///     the default so a station written before the flag existed keeps delivering.
    /// </summary>
    bool PlayoutEnabled = true);

/// <summary>Named day-type pattern used by an optional station calendar.</summary>
public sealed record SchedulePatternDto(string Id, string Name, string CronExpression);

/// <summary>Inclusive station-local date assignment to a pattern or a suppressed day.</summary>
public sealed record ScheduleAssignmentDto(
    string StartDate,
    string EndDate,
    string? PatternId = null,
    bool Suppress = false,
    string? Note = null);

/// <summary>Optional station calendar containing reusable patterns and date assignments.</summary>
public sealed record ScheduleCalendarDto(
    IReadOnlyList<SchedulePatternDto> Patterns,
    IReadOnlyList<ScheduleAssignmentDto> Assignments);

/// <summary>
///     Per-station voice selection policy. Mode is one of "single" | "roundRobin" | "timeOfDay".
///     VoiceProfileIds carries the round-robin set; Windows carries the time-of-day windows.
///     Absent (null) means single-voice using the schedule's VoiceProfileId.
/// </summary>
public sealed record VoiceRotationDto(
    string Mode,
    IReadOnlyList<string>? VoiceProfileIds = null,
    IReadOnlyList<VoiceWindowDto>? Windows = null);

/// <summary>A local-time window (times as "HH:mm") mapping to a fixed voice or a rotation set.</summary>
public sealed record VoiceWindowDto(
    string Start,
    string End,
    IReadOnlyList<string> VoiceProfileIds);

/// <summary>
///     Per-station bookend (intro/outro pair) selection policy. Mode is one of
///     "single" | "roundRobin" | "timeOfDay". BookendIds carries the round-robin set;
///     Windows carries the time-of-day windows. Absent (null) means the schedule's single
///     primary BookendId (or no bookend at all).
/// </summary>
public sealed record BookendRotationDto(
    string Mode,
    IReadOnlyList<string>? BookendIds = null,
    IReadOnlyList<BookendWindowDto>? Windows = null);

/// <summary>A local-time window (times as "HH:mm") mapping to a fixed bookend or a rotation set.</summary>
public sealed record BookendWindowDto(
    string Start,
    string End,
    IReadOnlyList<string>? BookendIds = null);

/// <summary>
///     Per-bulletin content sources (WS1: traffic + weather + fuel). Nulls fall back to
///     the defaults (traffic on, weather/fuel off). WeatherRequired/FuelRequired make the
///     matching source a FIXTURE: when its data was fetched it must air. A required source
///     must also be selected (the write path rejects the opposite), and a required source
///     whose fetch failed still leaves the bulletin on air (fetch stays fail-soft).
/// </summary>
public sealed record ContentSelectionDto(
    bool? Traffic = null,
    bool? Weather = null,
    bool? Fuel = null,
    bool? WeatherRequired = null,
    bool? FuelRequired = null,
    IReadOnlyList<string>? SegmentOrder = null,
    string? TrafficOrder = null);

/// <summary>
///     Coverage area data. Shape fields are optional: omitted (or type
///     "circle") means the classic centre + radius. For "bbox", "corridor"
///     and "polygon", latitude/longitude/radiusKm still carry the derived
///     enclosing circle so older clients keep rendering something sane.
/// </summary>
public sealed record CoverageDto(
    double Latitude,
    double Longitude,
    double RadiusKm,
    string? ShapeType = null,
    double? West = null,
    double? South = null,
    double? East = null,
    double? North = null,
    IReadOnlyList<CoveragePointDto>? CorridorPoints = null,
    double? CorridorRadiusMeters = null,
    IReadOnlyList<CoveragePointDto>? PolygonPoints = null,
    CoverageDto? WeatherCoverage = null,
    CoverageDto? FuelCoverage = null);

/// <summary>A WGS84 point on a coverage corridor or polygon ring.</summary>
public sealed record CoveragePointDto(double Lat, double Lng);

/// <summary>
///     Response for list schedules endpoint.
/// </summary>
public sealed record ScheduleListResponseDto(
    IReadOnlyList<ScheduleSummaryDto> Items,
    int Total);

/// <summary>
///     Request body for create/update schedule.
/// </summary>
public sealed record UpsertScheduleRequest(
    string? Name = null,
    string? Station = null,
    string? Timezone = null,
    string? CronExpression = null,
    bool? Enabled = null,
    double? TargetDurationSeconds = null,
    int? NotifyBeforePlayoutMinutes = null,
    int? EscalationBeforePlayoutMinutes = null,
    string? VoiceProfileId = null,
    string? OutputPath = null,
    CoverageDto? Coverage = null,
    ContentSelectionDto? Content = null,
    string? DeliveryStyleId = null,
    string? PlaybookId = null,
    string? PronunciationDictionaryId = null,
    VoiceRotationDto? VoiceRotation = null,
    IReadOnlyList<string>? FillOrder = null,
    string? BookendId = null,
    BookendRotationDto? BookendRotation = null,
    IReadOnlyDictionary<string, string>? BookendTokens = null,
    /// <summary>
    ///     Optional echo of the market id in the URL. A body value that CONTRADICTS the URL used
    ///     to be silently discarded under a 200 (dev 2026-07-28); it is now rejected so the caller
    ///     learns which id actually won. Last in the list so existing positional callers are
    ///     unaffected.
    /// </summary>
    string? MarketId = null,
    ScheduleCalendarDto? Calendar = null,
    bool? CurationEnabled = null,
    bool? PlayoutEnabled = null);
