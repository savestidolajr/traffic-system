using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Azure Table caps one property at 64KB / 32K UTF-16 chars, and every versioned library
///     serialises its whole append-only history into that one property. Overflowing it is a
///     DEADLOCK, not a degradation: shrinking the history is itself a write, so the record becomes
///     permanently uneditable through the API and even <c>restore</c> fails.
///     <para>
///     Every versioned library shares this single serialised-history property and the same
///     trimmer, so the pronunciation-dictionary coverage here exercises the shared behaviour.
///     Three of the four libraries had no trim at all until 2026-07-28 — the global style-rule
///     set (since retired) was found at 30,866 of 32,768 chars, one ordinary edit from bricking,
///     and a throwaway delivery style was driven into the wall deliberately to confirm the
///     failure mode.
///     </para>
/// </summary>
public class VersionHistoryTrimTests
{
    private const int TableCeiling = 32_768;

    private static PronunciationDictionaryVersion PronunciationVersion(int number, int ruleCount)
    {
        var rules = Enumerable.Range(0, ruleCount)
            .Select(i => PronunciationRule.CreateAlias($"Suburbname{i:D3}", $"Sub-urb-name-{i:D3}").Value)
            .ToList();
        return new PronunciationDictionaryVersion(
            number, $"elevenlabs-version-{number:D4}", rules,
            $"Changed a word in version {number}", DateTime.UtcNow, "operator@arn.com.au");
    }

    // ─── Pronunciation dictionaries ──────────────────────────────────

    [Fact]
    public void ShortHistory_IsKeptWhole()
    {
        var versions = Enumerable.Range(1, 4).Select(n => PronunciationVersion(n, 19)).ToList();

        var retained = VersionHistoryTrimmer.TrimToFit(versions, v => v.Version, out string json);

        retained.Count.ShouldBe(4);
        json.Length.ShouldBeLessThan(TableCeiling);
    }

    [Fact]
    public void LongHistory_IsTrimmedToFitTheTableProperty()
    {
        // Well past the ~9-version ceiling that bricked dev.
        var versions = Enumerable.Range(1, 60).Select(n => PronunciationVersion(n, 19)).ToList();

        var retained = VersionHistoryTrimmer.TrimToFit(versions, v => v.Version, out string json);

        json.Length.ShouldBeLessThan(TableCeiling);
        retained.Count.ShouldBeLessThan(60);
        retained.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Trimming_DropsTheOldestAndKeepsTheCurrentVersion()
    {
        var versions = Enumerable.Range(1, 60).Select(n => PronunciationVersion(n, 19)).ToList();

        var retained = VersionHistoryTrimmer.TrimToFit(versions, v => v.Version, out _);

        // The version a NEW bulletin will air with must never be the one discarded.
        retained[^1].Version.ShouldBe(60);
        retained[0].Version.ShouldBeGreaterThan(1);
        retained.Select(v => v.Version).ShouldBeInOrder();
    }

    [Fact]
    public void OneEnormousVersion_IsStillKept()
    {
        // Trimming can never empty the history: without the current version the aggregate
        // fails to reconstitute and the dictionary disappears from the library entirely.
        var versions = new List<PronunciationDictionaryVersion> { PronunciationVersion(1, 400) };

        var retained = VersionHistoryTrimmer.TrimToFit(versions, v => v.Version, out _);

        retained.Count.ShouldBe(1);
        retained[0].Version.ShouldBe(1);
    }

    [Fact]
    public void UnorderedHistory_IsSortedBeforeTrimming()
    {
        // Storage order is not guaranteed; trimming the "oldest" must mean the lowest version
        // number, not whatever happens to sit first in the list.
        var versions = Enumerable.Range(1, 60)
            .Select(n => PronunciationVersion(n, 19))
            .OrderByDescending(v => v.Version)
            .ToList();

        var retained = VersionHistoryTrimmer.TrimToFit(versions, v => v.Version, out _);

        retained[^1].Version.ShouldBe(60);
        retained.Select(v => v.Version).ShouldBeInOrder();
    }
}
