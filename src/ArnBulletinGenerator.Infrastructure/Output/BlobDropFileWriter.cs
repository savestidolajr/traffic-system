using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Writes delivery files to an SFTP-enabled Azure Blob storage drop account.
///     The app writes blobs natively via managed identity; the playout side pulls
///     the same files over SFTP (local user scoped to the drop container).
/// </summary>
public sealed class BlobDropFileWriter(
    IOptions<ZettaOptions> zettaOptions,
    TokenCredential credential,
    ILogger<BlobDropFileWriter> logger) : IDeliveryFileWriter
{
    private readonly ZettaOptions _options = zettaOptions.Value;
    private BlobContainerClient? _containerClient;

    public async Task<Result> WriteAsync(byte[] data, string fileName, CancellationToken cancellationToken = default)
    {
        if (data is null || data.Length == 0)
            return Result.Failure("Data must not be null or empty.");
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure("File name must not be null or empty.");
        if (string.IsNullOrWhiteSpace(_options.DropAccountName))
            return Result.Failure("Delivery drop account name not configured.");
        if (string.IsNullOrWhiteSpace(_options.DropContainerName))
            return Result.Failure("Delivery drop container name not configured.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var blobClient = GetContainerClient().GetBlobClient(fileName);
            await blobClient.UploadAsync(
                new BinaryData(data),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "audio/wav" }
                },
                linkedCts.Token);

            logger.LogInformation(
                "Wrote {Bytes} bytes to blob drop {Account}/{Container}/{FileName}",
                data.Length, _options.DropAccountName, _options.DropContainerName, fileName);
            return Result.Success();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                $"Blob drop write timed out after {_options.TimeoutSeconds}s for {fileName}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blob drop write failed for {FileName}", fileName);
            return Result.Failure($"Blob drop write failed: {ex.Message}");
        }
    }

    public async Task<Result<Option<DateTimeOffset>>> GetPendingWriteTimeAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Result<Option<DateTimeOffset>>.Failure("File name must not be null or empty.");
        if (string.IsNullOrWhiteSpace(_options.DropAccountName))
            return Result<Option<DateTimeOffset>>.Failure("Delivery drop account name not configured.");
        if (string.IsNullOrWhiteSpace(_options.DropContainerName))
            return Result<Option<DateTimeOffset>>.Failure("Delivery drop container name not configured.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var blobClient = GetContainerClient().GetBlobClient(fileName);
            if (!await blobClient.ExistsAsync(linkedCts.Token))
                return Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.None);

            var properties = (await blobClient.GetPropertiesAsync(cancellationToken: linkedCts.Token)).Value;
            logger.LogInformation(
                "Probed blob drop {Account}/{Container}/{FileName}: last written {LastModified}",
                _options.DropAccountName, _options.DropContainerName, fileName, properties.LastModified);
            return Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.Some(properties.LastModified));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Result<Option<DateTimeOffset>>.Failure(
                $"Blob drop probe timed out after {_options.TimeoutSeconds}s for {fileName}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blob drop probe failed for {FileName}", fileName);
            return Result<Option<DateTimeOffset>>.Failure($"Blob drop probe failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Lazily resolves the drop container, shared by write and probe so both always probe and
    ///     write the SAME container — a probe against a differently-constructed client would report
    ///     on a folder the writer never touches.
    /// </summary>
    private BlobContainerClient GetContainerClient()
    {
        return _containerClient ??= new BlobContainerClient(
            new Uri($"https://{_options.DropAccountName}.blob.core.windows.net/{_options.DropContainerName}"),
            credential);
    }
}
