using System.Text;
using System.IO;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Exports the raw <c>radioJobs</c> table — scripts, approval/rejection history,
///     operator identities, and publish evidence — as a dated NDJSON snapshot in the
///     audit-exports blob container. Raw entities are exported (not domain mappings)
///     so the snapshot is a faithful copy of the stored evidence, including columns
///     a future schema change might drop from the domain model.
/// </summary>
public sealed class TableStorageAuditExporter(
    IOptions<StorageOptions> storageOptions,
    IOptions<AuditExportOptions> exportOptions,
    TokenCredential credential,
    BlobServiceClient blobServiceClient,
    TimeProvider timeProvider,
    ILogger<TableStorageAuditExporter> logger)
    : TableRepositoryBase(TableName, storageOptions, credential, logger), IAuditExporter
{
    private const string TableName = "radioJobs";

    private readonly AuditExportOptions _options = exportOptions.Value;
    private readonly ILogger<TableStorageAuditExporter> _logger = logger;

    public async Task<Result<int>> ExportJobsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var blobName = $"{TableName}/{timeProvider.GetUtcNow().UtcDateTime:yyyy-MM-dd}.ndjson";
            var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(blobName);
            await using var stream = await blobClient.OpenWriteAsync(true, new BlobOpenWriteOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/x-ndjson" }
            }, cancellationToken);
            await using var writer = new StreamWriter(stream);

            var count = 0;
            var batchCounter = 0;
            var query = TableClient.QueryAsync<TableEntity>((string?)null, null, null, cancellationToken);
            await foreach (var entity in query)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize<IDictionary<string, object>>(entity));
                count++;
                batchCounter++;

                if (batchCounter >= 100)
                {
                    await writer.FlushAsync();
                    batchCounter = 0;
                }
            }

            await writer.FlushAsync();

            _logger.LogInformation("[AuditExport] Exported {Count} {Table} records to {Container}/{BlobName}",
                count, TableName, _options.ContainerName, blobName);

            return count;
        });
    }
}
