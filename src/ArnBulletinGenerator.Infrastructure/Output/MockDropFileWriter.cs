using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     In-memory playout drop. Exercises the FULL delivery path — integrity seal, market-code
///     resolution, file naming, and the tray probe the on-air board reads — while writing
///     nothing to ARN's drop account.
///     <para>
///         This exists because the drop share is a LIVE broadcast input: an SMB client on the
///         playout side mounts it and collects whatever appears, so any environment pointed at a
///         real share can put audio on air as a side effect of a test. On 2026-08-11 a prod
///         deploy restart made the watchdog sweep a day's backlog and write 11 files to
///         <c>informationbulletins</c> in 6 seconds, hours after those slots; the playout side
///         held one open (<c>SharingViolation</c>), proving it was mounted and collecting.
///         Selected by <see cref="Configuration.DeliveryTransport.Mock" />.
///     </para>
///     <para>
///         Distinct from <c>Zetta:Enabled=false</c>, which SKIPS delivery entirely
///         (<see cref="Domain.Interfaces.FileDeliveryStatus.SkippedDisabled" />) and so proves
///         nothing about the delivery code. The mock reports a real success and a real tray
///         state, so "did the audio reach playout" stays a question the app can answer.
///     </para>
/// </summary>
public sealed class MockDropFileWriter(ILogger<MockDropFileWriter> logger) : IDeliveryFileWriter
{
    private readonly ConcurrentDictionary<string, (int Bytes, DateTimeOffset WrittenAt)> _tray = new();

    /// <summary>Files currently "awaiting pickup", newest write per name. Test/diagnostic surface.</summary>
    public IReadOnlyDictionary<string, (int Bytes, DateTimeOffset WrittenAt)> Tray => _tray;

    public Task<Result> WriteAsync(byte[] data, string fileName, CancellationToken cancellationToken = default)
    {
        // Same guard contract as the real writers, so a caller cannot pass validation here and
        // fail against a live share.
        if (data is null || data.Length == 0)
            return Task.FromResult(Result.Failure("Data must not be null or empty."));
        if (string.IsNullOrWhiteSpace(fileName))
            return Task.FromResult(Result.Failure("File name must not be null or empty."));

        _tray[fileName] = (data.Length, DateTimeOffset.UtcNow);
        logger.LogInformation(
            "MOCK delivery: {Bytes} bytes would have been written to the playout drop as {FileName}. "
            + "No file left this process.",
            data.Length, fileName);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<Option<DateTimeOffset>>> GetPendingWriteTimeAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing collects from a mock tray, so a written file stays "awaiting pickup" — the
        // same state a real share reports until playout picks it up.
        return Task.FromResult(Result<Option<DateTimeOffset>>.Success(
            _tray.TryGetValue(fileName, out var entry)
                ? Option<DateTimeOffset>.Some(entry.WrittenAt)
                : Option<DateTimeOffset>.None));
    }
}
