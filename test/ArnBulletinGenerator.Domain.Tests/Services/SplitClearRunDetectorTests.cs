using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class SplitClearRunDetectorTests
{
    // The exact live failure (Sydney, 2026-07-13).
    private const string SplitScript =
        "The Warringah Freeway is closed outbound. The Sydney Harbour Bridge, M4 Western Motorway and " +
        "M5 South West Motorway are moving nicely. Anzac Bridge is moving nicely. It's clear, 13 degrees.";

    [Fact]
    public void Flags_adjacent_clear_sentences_repeating_the_same_idiom()
    {
        SplitClearRunDetector.Detect(SplitScript).HasSplitClearRun.ShouldBeTrue();
    }

    [Fact]
    public void Repair_merges_the_split_into_one_grouped_clear_run()
    {
        string repaired = SplitClearRunDetector.Repair(SplitScript);

        // All four roads grouped into a single clause, idiom voiced once.
        repaired.ShouldContain(
            "The Sydney Harbour Bridge, M4 Western Motorway, M5 South West Motorway and Anzac Bridge are all moving nicely.");
        // "moving nicely" now appears exactly once.
        CountOccurrences(repaired, "moving nicely").ShouldBe(1);
        // Surrounding sentences preserved.
        repaired.ShouldContain("The Warringah Freeway is closed outbound.");
        repaired.ShouldContain("It's clear, 13 degrees.");
        // And the repaired script no longer trips the gate.
        SplitClearRunDetector.Detect(repaired).HasSplitClearRun.ShouldBeFalse();
    }

    [Fact]
    public void Merges_a_run_of_three_same_idiom_clear_sentences()
    {
        const string s = "The M1 is looking good. The M2 is looking good. The M3 is looking good.";
        string repaired = SplitClearRunDetector.Repair(s);
        repaired.ShouldBe("The M1, The M2 and The M3 are all looking good.");
    }

    [Fact]
    public void Does_not_flag_different_clear_idioms()
    {
        // Varying the phrasing across clear roads is legitimate — not repetition.
        const string s = "The Harbour Bridge is moving nicely. The Anzac Bridge is looking good.";
        SplitClearRunDetector.Detect(s).HasSplitClearRun.ShouldBeFalse();
        SplitClearRunDetector.Repair(s).ShouldBe(s);
    }

    [Fact]
    public void Does_not_flag_a_single_grouped_clear_clause()
    {
        const string s = "The Harbour Bridge, Anzac Bridge and M4 are all moving nicely.";
        SplitClearRunDetector.Detect(s).HasSplitClearRun.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_touch_incident_or_weather_sentences()
    {
        const string s = "The M1 is at a standstill. Pitt Street has minor delays. It's clear, 13 degrees.";
        SplitClearRunDetector.Detect(s).HasSplitClearRun.ShouldBeFalse();
        SplitClearRunDetector.Repair(s).ShouldBe(s);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }
}
