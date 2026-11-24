namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Cached-clip metadata for one (filler statement × voice × voice settings × TTS model)
///     combination. The audio itself lives in blob storage keyed by
///     <see cref="FillerStatementId" />/<see cref="CacheKey" />; this record is the Table
///     Storage row that lets the pipeline skip re-synthesis for a combination already produced.
/// </summary>
public sealed record FillerAudioClip(
    string FillerStatementId,
    string CacheKey,
    double DurationSeconds,
    IReadOnlyList<WordTiming> WordTimings,
    DateTime CreatedAtUtc);
