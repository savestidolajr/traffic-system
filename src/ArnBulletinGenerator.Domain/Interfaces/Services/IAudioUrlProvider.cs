using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Resolves playable URLs and byte-range streams for broadcast audio stored in our own blob container, distinguishing it from external references like filler carts.</summary>
public interface IAudioUrlProvider
{
    Task<Result<string>> GetAudioUrlAsync(string audioFileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reduces a stored audio reference to a blob name inside our own audio container.
    ///     Bare blob names pass through unchanged; an absolute URL pointing into our container is
    ///     reduced to the blob name; a genuinely external absolute URL (e.g. a filler cart hosted
    ///     elsewhere) returns None.
    /// </summary>
    Option<string> ResolveOwnBlobName(string audioUrl);

    /// <summary>
    ///     Opens a read stream over an audio blob in our own container, honouring an optional
    ///     byte range (clamped to the blob length). Fails for external URLs and missing blobs.
    /// </summary>
    Task<Result<AudioStreamHandle>> OpenAudioStreamAsync(
        string audioUrl, AudioByteRange? range, CancellationToken cancellationToken = default);
}
