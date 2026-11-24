namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A lease on a queued job. The ETag is the storage concurrency token and is refreshed by
///     <c>RenewAsync</c> so a long-running invocation can renew the same claim safely.
/// </summary>
public sealed record JobClaim
{
    public JobClaim(
        string jobId,
        string owner,
        DateTime expiresUtc,
        int version,
        DateTime claimedUtc,
        string etag = "")
    {
        JobId = jobId;
        Owner = owner;
        ExpiresUtc = expiresUtc;
        Version = version;
        ClaimedUtc = claimedUtc;
        ETag = etag;
    }

    public string JobId { get; init; }
    public string Owner { get; init; }
    public DateTime ExpiresUtc { get; set; }
    public int Version { get; set; }
    public DateTime ClaimedUtc { get; init; }

    /// <summary>Current Azure Table ETag; updated in place after a successful renewal.</summary>
    public string ETag { get; set; }
}
