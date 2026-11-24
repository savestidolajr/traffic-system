namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The filler-cart clip resolved for one voice: the blob URL a fallback applies to the job,
///     the clip's measured spoken duration, and its word timings. The duration and audio are
///     surfaced alongside the URL so a fallback caller can judge whether the clip covers its slot
///     (bug-bash 2026-08-05 finding 32: a 15.98s cart aired in a 30s slot) and can pad it with
///     per-voice filler statements when it does not — a URL alone is not enough to do either.
/// </summary>
public sealed record ResolvedCartClip(
    string Url,
    byte[] Audio,
    double DurationSeconds,
    IReadOnlyList<WordTiming> WordTimings);
