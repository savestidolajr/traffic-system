using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persistent store for historical synthesis outcomes.
///     Used by <c>HistoryBasedDurationPredictor</c> to interpolate expected duration
///     from past (voice, model, speed, syllableCount) → measured results.
/// </summary>
public interface ISynthesisHistoryRepository
{
    /// <summary>Record a single synthesis outcome. Called after every successful TTS call.</summary>
    void Record(SynthesisRecord record);

    /// <summary>
    ///     Find the N closest historical records that match the given voice+model+speed
    ///     and are nearest to the given syllable count.
    ///     Used to predict: "For voice V, model M, speed S, N syllables → what duration?"
    /// </summary>
    IReadOnlyList<SynthesisRecord> FindClosestBySyllables(
        string voiceId,
        string modelId,
        double speed,
        int syllableCount,
        int maxResults = 5);

    /// <summary>
    ///     Find the N closest historical records that match the given voice+model+speed
    ///     and are nearest to the given target duration.
    ///     Used to predict: "For voice V, model M, speed S, what syllable count hits duration D?"
    /// </summary>
    IReadOnlyList<SynthesisRecord> FindClosestByDuration(
        string voiceId,
        string modelId,
        double speed,
        double targetDuration,
        int maxResults = 5);

    /// <summary>All records for a given voice+model, ordered by timestamp descending.</summary>
    IReadOnlyList<SynthesisRecord> GetHistory(
        string voiceId,
        string modelId);

    /// <summary>Total number of records stored.</summary>
    int Count { get; }
}
