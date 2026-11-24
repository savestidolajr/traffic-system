namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A resolved filler clip ready to stitch onto a bulletin's traffic audio — either freshly
///     synthesized or served from the filler audio cache. See <see cref="FillerAudioClip" /> for
///     the persisted cache-metadata shape.
/// </summary>
public sealed record ResolvedFillerAudio(byte[] Audio, double DurationSeconds, IReadOnlyList<WordTiming> WordTimings);
