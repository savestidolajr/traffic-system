namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Inclusive byte range requested for a partial audio read (HTTP <c>Range</c> semantics).
///     <see cref="To" /> is null for an open-ended request ("bytes=200-").
/// </summary>
public readonly record struct AudioByteRange(long From, long? To);

/// <summary>
///     An open read stream over a stored audio blob plus the metadata an HTTP handler needs to
///     shape a 200 (full) or 206 (partial) response. Dispose to release the underlying stream.
/// </summary>
public sealed class AudioStreamHandle(
    Stream content,
    long rangeStart,
    long contentLength,
    long totalLength,
    string contentType) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    /// <summary>First byte offset of <see cref="Content" /> within the blob.</summary>
    public long RangeStart { get; } = rangeStart;

    /// <summary>Number of bytes carried by <see cref="Content" />.</summary>
    public long ContentLength { get; } = contentLength;

    /// <summary>Total size of the blob in bytes (the denominator of Content-Range).</summary>
    public long TotalLength { get; } = totalLength;

    public string ContentType { get; } = contentType;

    /// <summary>True when the stream carries a strict subset of the blob (HTTP 206).</summary>
    public bool IsPartial => RangeStart != 0 || ContentLength != TotalLength;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
