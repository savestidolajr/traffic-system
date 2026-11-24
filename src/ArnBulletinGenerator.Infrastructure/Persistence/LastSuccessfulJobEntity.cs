using Azure;
using Azure.Data.Tables;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Scalar CAS pointer to the newest qualifying continuity/fallback job.</summary>
internal sealed class LastSuccessfulJobEntity : ITableEntity
{
    public string JobId { get; set; } = null!;
    public string Market { get; set; } = null!;
    public string Station { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string AudioUrl { get; set; } = null!;
    public string JobPartitionKey { get; set; } = null!;
    public string PartitionKey { get; set; } = "LastSuccessful";
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
