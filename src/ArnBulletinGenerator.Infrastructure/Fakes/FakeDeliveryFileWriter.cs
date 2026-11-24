using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Output;
using Microsoft.Extensions.Configuration;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for playout delivery: writes the audio to a local temp
///     folder (<c>E2E:ZettaOutputPath</c> or the OS temp directory) instead of the real drop
///     share, and logs the write.
/// </summary>
public sealed class FakeDeliveryFileWriter(IConfiguration configuration, FakeExternalCallLog log) : IDeliveryFileWriter
{
    public async Task<Result> WriteAsync(byte[] data, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = ResolvePath(fileName);
            await File.WriteAllBytesAsync(path, data, cancellationToken);
            log.Add($"delivery write path={path}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Reports the real last-write time of the local drop file, or None when it has not been
    ///     written (or has been deleted, standing in for playout collecting it). The fake writes
    ///     actual files to <c>E2E:ZettaOutputPath</c> (or the OS temp directory), so unlike a
    ///     call-log-only stand-in it can honestly report the file's state back to the tray probe.
    /// </summary>
    public Task<Result<Option<DateTimeOffset>>> GetPendingWriteTimeAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolvePath(fileName);
            if (!File.Exists(path))
                return Task.FromResult(Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.None));

            log.Add($"delivery probe path={path}");
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(path);
            return Task.FromResult(Result<Option<DateTimeOffset>>.Success(
                Option<DateTimeOffset>.Some(new DateTimeOffset(lastWriteUtc))));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<Option<DateTimeOffset>>.Failure(ex.Message));
        }
    }

    /// <summary>
    ///     Resolves the on-disk path for a drop file, shared by write and probe so the probe
    ///     always looks at the folder the fake actually writes into.
    /// </summary>
    private string ResolvePath(string fileName)
    {
        var root = configuration["E2E:ZettaOutputPath"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Path.GetTempPath(), "arn-e2e-zetta");

        Directory.CreateDirectory(root);
        var safeFileName = fileName.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        return Path.Combine(root, safeFileName);
    }
}
