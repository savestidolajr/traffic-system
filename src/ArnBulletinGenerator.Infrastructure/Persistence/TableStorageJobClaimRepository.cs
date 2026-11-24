using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Data.Tables;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Stores queue-processing leases in <c>radioJobClaims</c>. Claims use an insert-or-ETag-conditional
///     replace loop so two workers cannot both become the owner of the same job.
/// </summary>
public sealed class TableStorageJobClaimRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobClaimRepository> logger)
    : TableRepositoryBase("radioJobClaims", options, credential, logger), IJobClaimRepository
{
    private const string PartitionKey = "Claim";
    private const int MaxClaimAttempts = 4;
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(8);

    /// <inheritdoc />
    public async Task<Result<JobClaim?>> TryClaimAsync(
        string jobId,
        string owner,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Result<JobClaim?>.Failure("Job id is required.");
        if (string.IsNullOrWhiteSpace(owner))
            return Result<JobClaim?>.Failure("Claim owner is required.");
        if (lease <= TimeSpan.Zero)
            return Result<JobClaim?>.Failure("Claim lease must be positive.");

        return await ExecuteAsync(async () =>
        {
            for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
            {
                var currentResult = await GetEntityOrDefaultAsync<JobClaimEntity>(
                    PartitionKey, jobId, cancellationToken);
                if (currentResult.IsFailure)
                    throw new InvalidOperationException(currentResult.Error);

                var now = DateTimeOffset.UtcNow;
                if (currentResult.Value.IsSome)
                {
                    var current = currentResult.Value.Value;
                    if (current.ExpiresUtc > now)
                    {
                        // A same-owner duplicate is safe to acknowledge and lets the original
                        // invocation continue renewal. A different owner is the normal duplicate path.
                        return current.Owner == owner ? Map(current) : null;
                    }

                    var replacement = CreateEntity(jobId, owner, now, lease, current.Version + 1);
                    replacement.ETag = current.ETag;
                    try
                    {
                        var response = await TableClient.UpdateEntityAsync(
                            replacement, current.ETag, TableUpdateMode.Replace, cancellationToken);
                        replacement.ETag = new ETag(ReadEtag(response, replacement.ETag));
                        return Map(replacement);
                    }
                    catch (RequestFailedException ex) when (ex.Status == 412)
                    {
                        // Another worker won the expiry race; re-read and decide again.
                        continue;
                    }
                }
                else
                {
                    var candidate = CreateEntity(jobId, owner, now, lease, version: 1);
                    try
                    {
                        var response = await TableClient.AddEntityAsync(candidate, cancellationToken);
                        candidate.ETag = new ETag(ReadEtag(response, candidate.ETag));
                        return Map(candidate);
                    }
                    catch (RequestFailedException ex) when (ex.Status == 409)
                    {
                        // A concurrent insert won. The next iteration observes its owner/expiry.
                        continue;
                    }
                }
            }

            throw new RequestFailedException(412,
                $"Could not claim job '{jobId}' after concurrent claim updates.");
        });
    }

    /// <inheritdoc />
    public async Task<Result> RenewAsync(JobClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (string.IsNullOrWhiteSpace(claim.ETag))
            return Result.Failure("Claim ETag is required for renewal.");

        return await ExecuteAsync(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var renewed = CreateEntity(
                claim.JobId,
                claim.Owner,
                now,
                DefaultLease,
                claim.Version + 1);
            var response = await TableClient.UpdateEntityAsync(
                renewed, new ETag(claim.ETag), TableUpdateMode.Replace, cancellationToken);
            claim.ExpiresUtc = renewed.ExpiresUtc.UtcDateTime;
            claim.Version = renewed.Version;
            claim.ETag = ReadEtag(response, renewed.ETag);
        });
    }

    /// <inheritdoc />
    public async Task<Result> ReleaseAsync(JobClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (string.IsNullOrWhiteSpace(claim.ETag))
            return Result.Failure("Claim ETag is required for release.");

        // A read both lazily creates the table through TableRepositoryBase and makes release
        // idempotent when a previous cleanup already removed the row.
        var currentResult = await GetEntityOrDefaultAsync<JobClaimEntity>(
            PartitionKey, claim.JobId, cancellationToken);
        if (currentResult.IsFailure)
            return Result.Failure(currentResult.Error);
        if (currentResult.Value.IsNone)
            return Result.Success();

        return await ExecuteAsync(async () =>
        {
            try
            {
                await TableClient.DeleteEntityAsync(
                    PartitionKey, claim.JobId, new ETag(claim.ETag), cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Idempotent release: the claim has already been removed.
            }
        });
    }

    private static JobClaimEntity CreateEntity(
        string jobId,
        string owner,
        DateTimeOffset claimedUtc,
        TimeSpan lease,
        int version) =>
        new()
        {
            PartitionKey = PartitionKey,
            RowKey = jobId,
            Owner = owner,
            ClaimedUtc = claimedUtc,
            ExpiresUtc = claimedUtc.Add(lease),
            Version = version,
        };

    private static JobClaim Map(JobClaimEntity entity) =>
        new(
            entity.RowKey,
            entity.Owner,
            entity.ExpiresUtc.UtcDateTime,
            entity.Version,
            entity.ClaimedUtc.UtcDateTime,
            entity.ETag.ToString());

    private static string ReadEtag(Response response, ETag fallback) =>
        response.Headers.TryGetValue("ETag", out var etag) && !string.IsNullOrWhiteSpace(etag)
            ? etag
            : fallback.ToString();
}
