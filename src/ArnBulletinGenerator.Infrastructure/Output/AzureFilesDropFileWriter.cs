using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Writes delivery files to an AD DS-authenticated Azure Files SMB share on the
///     drop account. The app writes files natively via managed identity over the
///     Files REST API; the playout side mounts the same share over SMB using its own
///     on-prem AD identity — no SFTP client or vaulted credential on either side.
///     Files data-plane OAuth requires <see cref="ShareTokenIntent.Backup" /> and the
///     "Storage File Data Privileged Contributor" role (the SMB Share RBAC roles used
///     for on-prem Kerberos mounts do NOT carry the backup-semantics data actions
///     OAuth/REST calls need — see docs/canonical/decisions.md, 2026-07-24).
/// </summary>
public sealed class AzureFilesDropFileWriter(
    IOptions<ZettaOptions> zettaOptions,
    TokenCredential credential,
    ILogger<AzureFilesDropFileWriter> logger) : IDeliveryFileWriter
{
    /// <summary>
    ///     Backoff delays between write attempts when the target file is held open by an SMB client
    ///     (the playout side). Short, because the playout side releases the handle within seconds
    ///     and the linked timeout token cuts a delay short on cancellation. The count of delays is
    ///     the number of RETRIES; total attempts = delays + 1.
    /// </summary>
    private static readonly TimeSpan[] SharingViolationBackoffs =
        [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = Task.Delay;
    private readonly ZettaOptions _options = zettaOptions.Value;
    private ShareDirectoryClient? _rootDirectoryClient;

    /// <summary>
    ///     Visible for unit testing: injects the root directory client so the Azure SDK calls can be
    ///     substituted, and a delay function so backoff does not wait on the wall clock.
    /// </summary>
    internal AzureFilesDropFileWriter(
        IOptions<ZettaOptions> zettaOptions,
        TokenCredential credential,
        ILogger<AzureFilesDropFileWriter> logger,
        ShareDirectoryClient? rootDirectoryClient = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(zettaOptions, credential, logger)
    {
        _rootDirectoryClient = rootDirectoryClient;
        if (delay is not null)
            _delay = delay;
    }

    public async Task<Result> WriteAsync(byte[] data, string fileName, CancellationToken cancellationToken = default)
    {
        if (data is null || data.Length == 0)
            return Result.Failure("Data must not be null or empty.");
        if (string.IsNullOrWhiteSpace(fileName))
            return Result.Failure("File name must not be null or empty.");
        if (string.IsNullOrWhiteSpace(_options.DropAccountName))
            return Result.Failure("Delivery drop account name not configured.");
        if (string.IsNullOrWhiteSpace(_options.DropFileShareName))
            return Result.Failure("Delivery drop file share name not configured.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var fileClient = GetRootDirectoryClient().GetFileClient(fileName);

            // The playout system mounts the same share over SMB and may hold the target WAV open
            // while we try to replace it. That surfaces as a 409 SharingViolation and clears within
            // seconds, so retry ONLY that case with short backoff; any other failure must fail fast.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await fileClient.CreateAsync(
                        data.Length,
                        new ShareFileCreateOptions { HttpHeaders = new ShareFileHttpHeaders { ContentType = "audio/wav" } },
                        cancellationToken: linkedCts.Token);

                    using var stream = new MemoryStream(data);
                    await fileClient.UploadAsync(stream, cancellationToken: linkedCts.Token);
                    break;
                }
                catch (RequestFailedException ex) when (
                    ex.ErrorCode == "SharingViolation" && attempt < SharingViolationBackoffs.Length)
                {
                    logger.LogWarning(
                        "Azure Files drop write for {FileName} hit SMB sharing violation (attempt {Attempt}/{MaxAttempts}) — retrying in {DelayMs}ms",
                        fileName, attempt + 1, SharingViolationBackoffs.Length + 1,
                        (int)SharingViolationBackoffs[attempt].TotalMilliseconds);
                    await _delay(SharingViolationBackoffs[attempt], linkedCts.Token);
                }
            }

            logger.LogInformation(
                "Wrote {Bytes} bytes to Azure Files drop {Account}/{Share}/{FileName}",
                data.Length, _options.DropAccountName, _options.DropFileShareName, fileName);
            return Result.Success();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                $"Azure Files drop write timed out after {_options.TimeoutSeconds}s for {fileName}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Azure Files drop write failed for {FileName}", fileName);
            return Result.Failure($"Azure Files drop write failed: {ex.Message}");
        }
    }

    public async Task<Result<Option<DateTimeOffset>>> GetPendingWriteTimeAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Result<Option<DateTimeOffset>>.Failure("File name must not be null or empty.");
        if (string.IsNullOrWhiteSpace(_options.DropAccountName))
            return Result<Option<DateTimeOffset>>.Failure("Delivery drop account name not configured.");
        if (string.IsNullOrWhiteSpace(_options.DropFileShareName))
            return Result<Option<DateTimeOffset>>.Failure("Delivery drop file share name not configured.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var fileClient = GetRootDirectoryClient().GetFileClient(fileName);
            if (!await fileClient.ExistsAsync(linkedCts.Token))
                return Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.None);

            var properties = (await fileClient.GetPropertiesAsync(cancellationToken: linkedCts.Token)).Value;
            logger.LogInformation(
                "Probed Azure Files drop {Account}/{Share}/{FileName}: last written {LastModified}",
                _options.DropAccountName, _options.DropFileShareName, fileName, properties.LastModified);
            return Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.Some(properties.LastModified));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Result<Option<DateTimeOffset>>.Failure(
                $"Azure Files drop probe timed out after {_options.TimeoutSeconds}s for {fileName}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Azure Files drop probe failed for {FileName}", fileName);
            return Result<Option<DateTimeOffset>>.Failure($"Azure Files drop probe failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Lazily resolves the share root, shared by write and probe so both always probe and
    ///     write the SAME share — a probe against a differently-constructed client would report
    ///     on a folder the writer never touches.
    /// </summary>
    private ShareDirectoryClient GetRootDirectoryClient()
    {
        return _rootDirectoryClient ??= new ShareClient(
            new Uri($"https://{_options.DropAccountName}.file.core.windows.net/{_options.DropFileShareName}"),
            credential,
            new ShareClientOptions { ShareTokenIntent = ShareTokenIntent.Backup }).GetRootDirectoryClient();
    }
}
