using System.Text.RegularExpressions;
using ArnBulletinGenerator.Workflow.Prompts;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Prompts;

public sealed class ExemplarSelectorTests
{
    private static readonly Regex SelectionMetadata = new(
        @"\[ATN exemplar \d+ \| (?<region>\w+) \| (?<daypart>\w+) \| (?<density>[\w-]+) \| (?<words>\d+) words\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Bank_has_all_twenty_one_tagged_reads()
    {
        ExemplarSelector.Bank.Count.ShouldBe(21);
        ExemplarSelector.Bank.Select(exemplar => exemplar.Id).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(21);
        ExemplarSelector.Bank.ShouldAllBe(exemplar =>
            exemplar.WordCount > 0
            && (exemplar.RegionClass == "metro" || exemplar.RegionClass == "regional")
            && (exemplar.Daypart == "morning" || exemplar.Daypart == "midday"
                || exemplar.Daypart == "afternoon" || exemplar.Daypart == "evening")
            && (exemplar.Density == "busy" || exemplar.Density == "light" || exemplar.Density == "all-clear"));
    }

    [Fact]
    public void Same_inputs_are_deterministic()
    {
        var date = new DateOnly(2026, 9, 2);

        string first = ExemplarSelector.Render(90, "metro", "morning", "kiis-101", date);
        string second = ExemplarSelector.Render(90, "metro", "morning", "kiis-101", date);

        second.ShouldBe(first);
    }

    [Fact]
    public void Daypart_changes_the_selected_style_rotation()
    {
        var date = new DateOnly(2026, 9, 2);

        string morning = ExemplarSelector.Render(90, "metro", "morning", "kiis-101", date);
        string midday = ExemplarSelector.Render(90, "metro", "midday", "kiis-101", date);

        midday.ShouldNotBe(morning);
        SelectionMetadata.Matches(midday).Cast<Match>().ShouldContain(match =>
            match.Groups["daypart"].Value == "midday");
    }

    [Fact]
    public void Region_class_is_respected()
    {
        string output = ExemplarSelector.Render(90, "regional", "morning", "cairns-4ca",
            new DateOnly(2026, 9, 2));

        var matches = SelectionMetadata.Matches(output).Cast<Match>().ToList();
        matches.Count.ShouldBe(3);
        matches.ShouldAllBe(match => match.Groups["region"].Value == "regional");
    }

    /// <summary>
    ///     The bank is body copy, 38 to 100 words, and every LONG metro morning read is busy — so a
    ///     90-word target has no light-board read anywhere near it. The density rule then reaches
    ///     down the length ranking on purpose to show a second board state, and the seeded rotation
    ///     moves the first pick within the nearest few. One absolute window over all three picks is
    ///     therefore not a property this selector has; what it guarantees is that the length-ranked
    ///     pick lands near the target and nothing wildly off-length is shown.
    /// </summary>
    [Fact]
    public void Selected_word_counts_stay_close_to_the_target()
    {
        const int targetWords = 90;
        string output = ExemplarSelector.Render(targetWords, "metro", "morning", "sydney",
            new DateOnly(2026, 9, 2));

        var counts = SelectionMetadata.Matches(output)
            .Cast<Match>()
            .Select(match => int.Parse(match.Groups["words"].Value))
            .ToList();

        counts.Count.ShouldBe(3);
        counts.Min(count => Math.Abs(count - targetWords)).ShouldBeLessThanOrEqualTo(15);
        counts.ShouldAllBe(count => count >= 30 && count <= 120);
    }

    [Fact]
    public void Selection_contains_at_least_two_board_densities()
    {
        string output = ExemplarSelector.Render(90, "regional", "morning", "townsville",
            new DateOnly(2026, 9, 2));

        SelectionMetadata.Matches(output)
            .Cast<Match>()
            .Select(match => match.Groups["density"].Value)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Every_bank_word_count_matches_its_text()
    {
        foreach (ExemplarSelector.AtnExemplar exemplar in ExemplarSelector.Bank)
        {
            int actual = Regex.Matches(exemplar.Text, @"\b[\p{L}\p{N}’'-]+\b",
                RegexOptions.CultureInvariant).Count;
            double difference = Math.Abs(actual - exemplar.WordCount) / (double)actual;

            difference.ShouldBeLessThanOrEqualTo(0.05, exemplar.Id);
        }
    }

    /// <summary>
    ///     The bank teaches BODY copy only. Dev goldcoast job <c>de2d1b60</c> (2026-09-03) opened
    ///     "It's Hot Tomato traffic:" — the exemplars' own station sign-off shape with the brief
    ///     header's station name substituted — and the configured intro bookend was then prepended
    ///     in front of it, so the read aired two openers. One prompt line forbids writing an
    ///     intro or sign-off; three exemplars per call demonstrated the opposite, and demonstration
    ///     won. Anything a downstream gate blocks must not be modelled here either: the driver
    ///     advice in the original CADA read ("Please stay alert and follow the speed limit") is
    ///     exactly what <c>ConversationalChecks.NoAdvice</c> rejects.
    /// </summary>
    [Theory]
    [InlineData(@"\*\[", "a sponsor, hotline or other production label")]
    [InlineData(@"(?i)\bthat's the latest traffic\b", "a presenter sign-off")]
    [InlineData(@"(?i)\btraffic and weather together\b", "a station sign-off")]
    [InlineData(@"(?i)\bTime Saver Traffic\b", "a station ident")]
    [InlineData(@"(?i)\bKIIS-cast\b", "a branded weather prefix")]
    // Case-SENSITIVE: a presenter self-identification is the contraction plus a proper noun
    // ("It's Brittany", "I'm Monique"), whereas "It's very busy" is ordinary traffic copy.
    [InlineData(@"\b(?:It's|It’s|I'm|I’m|This is)\s+[A-Z]", "a presenter self-identification")]
    [InlineData(@"(?i)\blooking over\b", "a presenter opener")]
    [InlineData(@"(?i)\banother check in\b", "a next-bulletin promise")]
    [InlineData(@"(?i)\bstay alert\b|\bmake sure you stick to\b|\btake [A-Z]\w+ (?:Road|Street|Avenue)\b",
        "driver advice a gate blocks")]
    public void No_exemplar_carries_a_production_wrapper(string pattern, string what)
    {
        var offenders = ExemplarSelector.Bank
            .Where(exemplar => Regex.IsMatch(exemplar.Text, pattern, RegexOptions.CultureInvariant))
            .Select(exemplar => exemplar.Id)
            .ToList();

        offenders.ShouldBeEmpty(
            $"these exemplars carry {what}, which the composer never writes: "
            + string.Join(", ", offenders));
    }
}
