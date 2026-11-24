using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     In-memory, append-only progress state for one generation job. A single reporter owns the
///     document during an orchestration and persists a complete replacement after each event.
/// </summary>
public sealed class JobProgressDocument
{
    /// <summary>
    ///     Azure Table Storage leaves ample room for this document, while the cap keeps a long-lived
    ///     retry path bounded and makes every progress response cheap to transfer.
    /// </summary>
    public const int MaxEventCount = 200;

    private readonly List<JobProgressEventDto> _events;

    public JobProgressDocument(
        string jobId,
        string market,
        JobStatus status,
        DateTimeOffset createdAtUtc,
        ProgressStage initialStage = ProgressStage.Evidence)
        : this(jobId, market, status.ToString(), createdAtUtc, initialStage)
    {
    }

    public JobProgressDocument(
        string jobId,
        string market,
        string status,
        DateTimeOffset createdAtUtc,
        ProgressStage initialStage = ProgressStage.Evidence)
    {
        JobId = string.IsNullOrWhiteSpace(jobId) ? throw new ArgumentException("Job id is required.", nameof(jobId)) : jobId;
        Market = market ?? string.Empty;
        Status = string.IsNullOrWhiteSpace(status) ? JobStatus.Requested.ToString() : status;
        Stage = initialStage.ToString();
        StageStartedAt = FormatAt(createdAtUtc);
        UpdatedAt = StageStartedAt;
        _events = [];
    }

    private JobProgressDocument(
        string jobId,
        string market,
        string status,
        string stage,
        string stageStartedAt,
        int attempt,
        int maxAttempts,
        string updatedAt,
        int version,
        IEnumerable<JobProgressEventDto> events,
        IReadOnlyDictionary<string, double>? typicalSeconds)
    {
        JobId = jobId;
        Market = market;
        Status = status;
        Stage = stage;
        StageStartedAt = stageStartedAt;
        Attempt = Math.Max(0, attempt);
        MaxAttempts = Math.Max(0, maxAttempts);
        UpdatedAt = updatedAt;
        Version = Math.Max(0, version);
        _events = events.TakeLast(MaxEventCount).ToList();
        TypicalSeconds = typicalSeconds is null
            ? null
            : new Dictionary<string, double>(typicalSeconds, StringComparer.Ordinal);
    }

    public string JobId { get; }
    public string Market { get; }
    public string Status { get; private set; }
    public string Stage { get; private set; }
    public string StageStartedAt { get; private set; }
    public int Attempt { get; private set; }
    public int MaxAttempts { get; private set; }
    public string UpdatedAt { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<JobProgressEventDto> Events => _events;
    public IReadOnlyDictionary<string, double>? TypicalSeconds { get; private set; }

    /// <summary>Appends one event, updates the current stage, and drops the oldest event at the cap.</summary>
    public void Append(
        DateTimeOffset atUtc,
        int attempt,
        ProgressStage stage,
        ProgressEventKind kind,
        string detail,
        IReadOnlyDictionary<string, double>? counts = null,
        int? durationMs = null,
        string? model = null,
        string? route = null)
    {
        DateTimeOffset at = atUtc.ToUniversalTime();
        string atText = FormatAt(at);
        int effectiveAttempt = Math.Max(0, attempt);
        Stage = stage.ToString();
        if (kind is ProgressEventKind.StageStarted or ProgressEventKind.AttemptStarted
            || stage is ProgressStage.Ready or ProgressStage.Failed)
            StageStartedAt = atText;
        Attempt = effectiveAttempt;
        if (durationMs is < 0)
            durationMs = 0;

        var eventDto = new JobProgressEventDto(
            atText,
            effectiveAttempt,
            stage.ToString(),
            kind.ToString(),
            PlainSentence(detail),
            CopyCounts(counts),
            durationMs,
            string.IsNullOrWhiteSpace(model) ? null : model,
            string.IsNullOrWhiteSpace(route) ? null : route);
        _events.Add(eventDto);
        if (_events.Count > MaxEventCount)
            _events.RemoveRange(0, _events.Count - MaxEventCount);
        Version++;
        UpdatedAt = atText;
    }

    /// <summary>Updates the current attempt envelope without adding a second progress event.</summary>
    public void SetAttemptBounds(int attempt, int maxAttempts)
    {
        Attempt = Math.Max(0, attempt);
        MaxAttempts = Math.Max(0, maxAttempts);
    }

    /// <summary>
    ///     Records a status transition as a compact note so status changes also invalidate ETags.
    ///     Pipeline statuses are stamped at phase START (HereFetched is set as the traffic fetch
    ///     begins), so the note names the activity that is starting rather than announcing a name
    ///     that reads like a completion four seconds before the fetch returns.
    /// </summary>
    public void SetStatus(DateTimeOffset atUtc, JobStatus status)
    {
        string next = status.ToString();
        if (string.Equals(Status, next, StringComparison.Ordinal))
            return;

        Status = next;
        string note = status switch
        {
            JobStatus.HereFetched => "Fetching traffic.",
            JobStatus.IncidentsSelected => "Generating the script.",
            JobStatus.ScriptReady or JobStatus.SynthesizingAudio => "Synthesising audio.",
            JobStatus.AudioReady => "Finalising audio.",
            JobStatus.WaitingApproval => "Awaiting review.",
            _ => $"Job status changed to {next}."
        };
        Append(atUtc, Attempt, ParseStage(Stage), ProgressEventKind.Note, note);
    }

    /// <summary>Copies the current typical duration values into the document returned to the console.</summary>
    public void SetTypicalSeconds(IReadOnlyDictionary<string, double>? typicalSeconds)
    {
        TypicalSeconds = typicalSeconds is null
            ? null
            : new Dictionary<string, double>(typicalSeconds, StringComparer.Ordinal);
    }

    public JobProgressDto ToDto() => new(
        JobId,
        Status,
        Stage,
        StageStartedAt,
        Attempt,
        MaxAttempts,
        UpdatedAt,
        Version,
        Events,
        TypicalSeconds);

    /// <summary>Creates a detached copy before a replacement upsert so later events cannot mutate its payload.</summary>
    public JobProgressDocument Clone() => new(
        JobId,
        Market,
        Status,
        Stage,
        StageStartedAt,
        Attempt,
        MaxAttempts,
        UpdatedAt,
        Version,
        _events,
        TypicalSeconds);

    /// <summary>Rehydrates a stored wire document while preserving its bounded event history.</summary>
    public static JobProgressDocument FromDto(JobProgressDto dto, string market)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var document = new JobProgressDocument(
            dto.JobId,
            market,
            dto.Status,
            ParseAt(dto.UpdatedAt),
            ParseStage(dto.Stage));
        document.StageStartedAt = string.IsNullOrWhiteSpace(dto.StageStartedAt)
            ? document.UpdatedAt
            : dto.StageStartedAt;
        document.Attempt = Math.Max(0, dto.Attempt);
        document.MaxAttempts = Math.Max(0, dto.MaxAttempts);
        document.UpdatedAt = string.IsNullOrWhiteSpace(dto.UpdatedAt)
            ? document.StageStartedAt
            : dto.UpdatedAt;
        document.Version = Math.Max(0, dto.Version);
        document._events.AddRange(dto.Events.TakeLast(MaxEventCount));
        document.SetTypicalSeconds(dto.TypicalSeconds);
        return document;
    }

    private static IReadOnlyDictionary<string, double>? CopyCounts(
        IReadOnlyDictionary<string, double>? counts) => counts is null
        ? null
        : new Dictionary<string, double>(counts, StringComparer.Ordinal);

    private static string PlainSentence(string? detail)
    {
        string value = string.IsNullOrWhiteSpace(detail) ? "Progress updated." : detail.Trim();
        return value.Replace('\u2014', '-').Replace('\u2013', '-');
    }

    private static string FormatAt(DateTimeOffset atUtc) =>
        atUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseAt(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static ProgressStage ParseStage(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ProgressStage stage) ? stage : ProgressStage.Evidence;
}

/// <summary>One stored progress document together with its Table Storage ETag.</summary>
public sealed record JobProgressSnapshot(JobProgressDocument Document, string Etag);
