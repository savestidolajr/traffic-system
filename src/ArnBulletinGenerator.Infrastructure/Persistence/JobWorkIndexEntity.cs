using Azure;
using Azure.Data.Tables;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Scalar Azure Table row used by the watchdog job work index.</summary>
internal sealed class JobWorkIndexEntity : ITableEntity
{
    public string JobId { get; set; } = default!;
    public string Market { get; set; } = default!;
    public string Station { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTimeOffset DueUtc { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public DateTimeOffset? FallbackDeadlineUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public DateTimeOffset? CurationDeadlineUtc { get; set; }
    public bool HasAudio { get; set; }
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
