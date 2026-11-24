using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArnBulletinGenerator.Infrastructure.Persistence;


/// <summary>Maintains the denormalized watchdog work rows after a canonical job write.</summary>
internal sealed class JobIndexMaintainer(
    IJobWorkIndexRepository repository,
    IOptions<JobWorkIndexOptions> options,
    ILogger logger)
{
    private readonly JobWorkIndexOptions _options = options.Value;

    internal async Task<Result> AfterSaveAsync(Job job, CancellationToken cancellationToken)
    {
        if (!_options.WriteEnabled || _options.KillSwitch)
            return Result.Success();

        var existing = await repository.FindByJobIdAsync(job.JobId, cancellationToken);
        if (existing.IsFailure)
        {
            logger.LogWarning("Failed to find old work-index rows for {JobId}: {Error}", job.JobId, existing.Error);
            return Result.Failure(existing.Error ?? "Failed to read old work-index rows");
        }

        foreach (var item in existing.Value)
            await DeleteBestEffortAsync(item, cancellationToken);

        foreach (var item in JobWorkIndexProjection.For(job))
        {
            var result = await repository.UpsertAsync(item, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning("Failed to upsert work-index row for {JobId}: {Error}", job.JobId, result.Error);
                return result;
            }
        }

        return Result.Success();
    }

    internal async Task<Result> AfterDeleteAsync(
        string jobId, string market, CancellationToken cancellationToken)
    {
        _ = market;
        if (!_options.WriteEnabled || _options.KillSwitch)
            return Result.Success();

        var existing = await repository.FindByJobIdAsync(jobId, cancellationToken);
        if (existing.IsFailure)
            return Result.Failure(existing.Error ?? "Failed to read old work-index rows");

        foreach (var item in existing.Value)
            await DeleteBestEffortAsync(item, cancellationToken);
        return Result.Success();
    }

    private async Task DeleteBestEffortAsync(JobWorkItem item, CancellationToken cancellationToken)
    {
        var result = await repository.DeleteAsync(item, cancellationToken);
        if (result.IsFailure)
            logger.LogWarning("Failed to delete stale work-index row for {JobId}: {Error}", item.JobId, result.Error);
    }
}
