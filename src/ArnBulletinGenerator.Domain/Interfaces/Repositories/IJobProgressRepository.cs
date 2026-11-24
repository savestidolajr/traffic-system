using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists and reads the bounded structured progress document for one job.</summary>
public interface IJobProgressRepository
{
    Task<Result> UpsertAsync(JobProgressDocument document, CancellationToken cancellationToken = default);

    Task<Result<Option<JobProgressSnapshot>>> GetAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>Reads the per-market completed-stage duration EMA, or no value when no history exists.</summary>
    Task<Result<Option<IReadOnlyDictionary<string, double>>>> GetTypicalAsync(
        string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates each supplied stage duration with alpha 0.3 and returns the resulting market EMA.
    ///     Stages absent from the supplied sample retain their prior value.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, double>>> UpdateTypicalAsync(
        string market,
        IReadOnlyDictionary<string, double> stageSeconds,
        CancellationToken cancellationToken = default);
}
