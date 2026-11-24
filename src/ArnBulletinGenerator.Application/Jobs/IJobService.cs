using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
///     Contract for the job lifecycle: create, approve, reject, reject-to-fallback, publish,
///     air-source, regenerate, mark-failed, and queue publication.
/// </summary>
public interface IJobService
{
    Task<Result> ApproveJobAsync(string jobId, string by = "unknown", string? expectedEtag = null,
        string? byObjectId = null, CancellationToken cancellationToken = default);
    Task<Result> EscalateJobAsync(string jobId, string reason, CancellationToken cancellationToken = default);
    Task<Result<Job>> CreateJobAsync(CreateJobCommand command, CancellationToken cancellationToken = default);
    Task<Result> RejectJobAsync(RejectJobCommand command, CancellationToken cancellationToken = default);
    Task<Result> RejectToFallbackAsync(RejectToFallbackCommand command, CancellationToken cancellationToken = default);
    Task<Result> PublishJobAsync(string jobId, string by = "unknown", string? expectedEtag = null,
        string? byObjectId = null, CancellationToken cancellationToken = default);
    Task<Result> ProceedPastCurationAsync(string jobId, string by = "unknown", string? expectedEtag = null,
        IReadOnlyList<EditedIncidentDto>? editedIncidents = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Operator-initiated air-source override: attach a chosen source (an earlier same-day
    ///     bulletin or the filler cart) to the slot's job and deliver it to playout in one call.
    /// </summary>
    Task<Result> SetAirSourceAsync(SetAirSourceCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the audio a covered slot now carries into the playout drop, attributed to
    ///     <paramref name="by" /> ("system (deadline)" when the sweep acted). Every path that
    ///     covers a slot MUST finish through here: applying a fallback only moves a status, and
    ///     each path that grew its own delivery step instead shipped silence.
    /// </summary>
    /// <param name="what">
    ///     What is covering the slot, in the operator's words ("the standby cart"), used verbatim
    ///     in the job's audit log.
    /// </param>
    Task<Result> DeliverCoveredSlotAsync(string jobId, string what, string by,
        CancellationToken cancellationToken = default);

    Task<Result<RegenerationQueueMessage>> RegenerateJobAsync(RegenerateJobCommand command,
        string by = "unknown", CancellationToken cancellationToken = default);

    Task<Result> PublishBroadcastJobAsync(string jobId, BroadcastQueueMessage message,
        CancellationToken cancellationToken = default);

    Task<Result> PublishRegenerationJobAsync(string jobId, RegenerationQueueMessage message,
        CancellationToken cancellationToken = default);

    Task<Result> MarkJobFailedAsync(string jobId, string error, CancellationToken cancellationToken = default);
}
