using Azure;
using Azure.Data.Tables;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Row shape for <c>radioJobClaims</c>: the single <c>Claim</c> partition and job id row key
///     make each claim an atomic point read/update/delete.
/// </summary>
internal sealed class JobClaimEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Claim";
    public string RowKey { get; set; } = default!;
    public string Owner { get; set; } = default!;
    public DateTimeOffset ExpiresUtc { get; set; }
    public int Version { get; set; }
    public DateTimeOffset ClaimedUtc { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
