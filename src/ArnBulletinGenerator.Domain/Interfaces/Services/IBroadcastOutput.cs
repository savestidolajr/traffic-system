using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Stores finished broadcast audio to its delivery destination and marks it published once it has aired.</summary>
public interface IBroadcastOutput
{
    Task<Result> SaveAsync(byte[] audioData, string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks stored broadcast audio as published (aired). <paramref name="audioFileName" /> is the
    ///     full stored file name (e.g. <c>broadcast_20260612_080000.wav</c>) as held in <c>Job.AudioUrl</c>.
    ///     Implementations succeed quietly when the file is not managed by this output
    ///     (e.g. an external filler-cart reference).
    /// </summary>
    Task<Result> MarkPublishedAsync(string audioFileName, CancellationToken cancellationToken = default);
}
