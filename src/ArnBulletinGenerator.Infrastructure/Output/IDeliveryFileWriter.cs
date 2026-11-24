using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Writes a delivery file to the configured playout drop transport — an
///     AD DS-authenticated Azure Files share, or the SFTP-enabled blob drop on the
///     same account. Abstracted for testability.
/// </summary>
public interface IDeliveryFileWriter
{
    Task<Result> WriteAsync(byte[] data, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports the last-write time of <paramref name="fileName" /> while the file is STILL in
    ///     the drop target, or None once playout has collected it (or it was never written).
    ///     Playout COLLECTS the file from the folder, so "absent" is the normal collected state,
    ///     not an error — only a real transport failure (auth, network) is a Failure. The on-air
    ///     board needs this to decide whether a same-name overwrite for the next slot is safe:
    ///     present = not yet picked up = the swap replaces it; absent = collected = writing again
    ///     queues a NEW file that could air in the wrong break.
    /// </summary>
    Task<Result<Option<DateTimeOffset>>> GetPendingWriteTimeAsync(
        string fileName, CancellationToken cancellationToken = default);
}
