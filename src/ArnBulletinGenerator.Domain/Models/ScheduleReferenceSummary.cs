namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The schedule fields needed for referential-integrity checks. Unlike
///     <see cref="MarketSchedule"/>, this read model intentionally excludes coverage, calendar,
///     content, and other potentially large persisted JSON values.
/// </summary>
public sealed record ScheduleReferenceSummary(
    string MarketId,
    string Station,
    string VoiceProfileId,
    string DeliveryStyleId,
    string PlaybookId,
    string PronunciationDictionaryId,
    string BookendId,
    IReadOnlyList<string> VoiceProfileIds,
    IReadOnlyList<string> BookendIds)
{
    /// <summary>
    ///     Builds a complete assignment view from an already loaded schedule. The primary ids are
    ///     included even when a rotation policy is present because they remain the fallback for
    ///     uncovered time windows.
    /// </summary>
    public static ScheduleReferenceSummary FromSchedule(MarketSchedule schedule)
    {
        var voices = new List<string>();
        Add(voices, schedule.VoiceProfileId);
        if (schedule.VoiceRotation.HasValue)
            Add(voices, schedule.VoiceRotation.Value.AllReferencedVoiceIds());

        var bookends = new List<string>();
        Add(bookends, schedule.BookendId);
        if (schedule.BookendRotation.HasValue)
            Add(bookends, schedule.BookendRotation.Value.AllReferencedBookendIds());

        return new ScheduleReferenceSummary(
            schedule.MarketId,
            schedule.Station,
            schedule.VoiceProfileId,
            schedule.DeliveryStyleId,
            schedule.PlaybookId,
            schedule.PronunciationDictionaryId,
            schedule.BookendId,
            voices,
            bookends);
    }

    private static void Add(List<string> target, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        var trimmed = id.Trim();
        if (!target.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            target.Add(trimmed);
    }

    private static void Add(List<string> target, IEnumerable<string> ids)
    {
        foreach (var id in ids)
            Add(target, id);
    }
}
