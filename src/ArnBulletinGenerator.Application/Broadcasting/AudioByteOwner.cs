namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
/// Owns an in-memory audio byte array while a generation candidate is alive. Disposing drops the
/// owner reference immediately so a superseded or failed candidate does not keep a large WAV buffer
/// reachable until a later GC pass. The owner is deliberately not used for audio that is still
/// being returned to the caller.
/// </summary>
internal sealed class AudioByteOwner : IDisposable
{
    private byte[]? _buffer;

    public AudioByteOwner(byte[] buffer) => _buffer = buffer ?? [];

    public byte[] Buffer => Volatile.Read(ref _buffer) ?? [];

    public bool IsReleased => Volatile.Read(ref _buffer) is null;

    public void Dispose() => Interlocked.Exchange(ref _buffer, null);

    public static void Release(byte[]? buffer)
    {
        if (buffer is not null && buffer.Length > 0)
            Array.Clear(buffer, 0, buffer.Length);
    }
}
