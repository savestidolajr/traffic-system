using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     The ratchet. Every read we judged unfit to air is committed to
///     <c>test/corpus/subpar-reads.json</c> with the deterministic gate that must catch its defect,
///     when such a gate exists. An explicit <c>none-yet</c> sentinel records a known defect that
///     still lacks deterministic protection.
///     <para>
///         The point is that quality judgements stop being verbal. A read ARN objects to becomes a
///         permanent, millisecond-fast assertion, so the same shape can never quietly return after
///         a prompt edit, a model swap, or a change to the slot-fit objective — all three of which have
///         reintroduced a fixed defect on this project before.
///     </para>
///     <para>
///         Rules for maintaining the corpus: append an entry the moment a subpar read is identified;
///         never delete one; never loosen a gate to make one pass. If an entry cannot be caught
///         deterministically, use <c>none-yet</c> rather than lying about an unrelated gate. The test
///         reports those entries as visible gate debt while preserving the hard assertion for every
///         entry that names a real gate.
///     </para>
/// </summary>
public sealed class SubparReadCorpusTests(ITestOutputHelper output)
{
    private const string NoDeterministicGate = "none-yet";

    private sealed record SubparEntry(
        string Id,
        string Market,
        string SourceJobId,
        string Defect,
        string Rule,
        string ExpectedGate,
        string Text);

    [Fact]
    public void EveryKnownSubparRead_IsStillCaughtByItsGate_WhenOneExists()
    {
        var entries = LoadCorpus();
        entries.Count.ShouldBeGreaterThan(0, "the subpar-read corpus must not be empty");

        var uncaught = new List<string>();
        int ungated = 0;
        foreach (var entry in entries)
        {
            if (entry.ExpectedGate == NoDeterministicGate)
            {
                ungated++;
                output.WriteLine($"RECORDED UNGATED {entry.Id}  gate={entry.ExpectedGate}");
                continue;
            }

            bool caught = IsCaught(entry);
            output.WriteLine($"{(caught ? "CAUGHT " : "MISSED ")} {entry.Id}  gate={entry.ExpectedGate}");
            if (!caught)
                uncaught.Add($"{entry.Id} (expected {entry.ExpectedGate}): {entry.Defect}");
        }

        output.WriteLine($"UNGATED KNOWN SUBPAR READS: {ungated}");
        uncaught.ShouldBeEmpty(
            "a read we already judged unfit to air is no longer being caught, so it can reach air again:"
            + Environment.NewLine + string.Join(Environment.NewLine, uncaught));
    }

    [Fact]
    public void CorpusEntries_AreWellFormed_SoTheRatchetCannotBeQuietlyDisarmed()
    {
        var entries = LoadCorpus();
        foreach (var entry in entries)
        {
            entry.Id.ShouldNotBeNullOrWhiteSpace();
            entry.Text.ShouldNotBeNullOrWhiteSpace($"{entry.Id} has no text to assert against");
            entry.ExpectedGate.ShouldNotBeNullOrWhiteSpace($"{entry.Id} names no gate");
            // A defect and the rule it breaks are what make an entry reviewable by a human later.
            entry.Defect.ShouldNotBeNullOrWhiteSpace($"{entry.Id} does not say what is wrong with it");
            entry.Rule.ShouldNotBeNullOrWhiteSpace($"{entry.Id} does not cite the rule it breaks");
        }
        entries.Select(entry => entry.Id).ShouldBeUnique();
    }

    /// <summary>
    ///     A clean, well-shaped read must NOT trip any gate. Without this the corpus could be
    ///     satisfied by a detector that simply flags everything.
    /// </summary>
    [Fact]
    public void AGoodRead_TripsNoGate()
    {
        const string good =
            "Checking out the roads... Roadworks northbound on the Bruce Highway at Foster Road in "
            + "Edmonton are adding about two minutes. There's another slow patch on the Captain Cook "
            + "Highway near Aeroglen. In Manoora, unleaded 91 is a dollar ninety-eight, with diesel at "
            + "two dollars forty. Sunny skies across Cairns, with 26 degrees.";

        var shape = SentenceShapeDetector.Detect(good);
        shape.HasIssue.ShouldBeFalse($"a clean read was flagged: {shape.Feedback}");

        var context = MinimalContext();
        SecondaryItemCapDetector.Detect(good, context).HasExcessItems.ShouldBeFalse();
    }

    private static bool IsCaught(SubparEntry entry)
    {
        var context = MinimalContext();
        return entry.ExpectedGate switch
        {
            nameof(SentenceShapeDetector) => SentenceShapeDetector.Detect(entry.Text).HasIssue,
            nameof(SecondaryItemCapDetector) => SecondaryItemCapDetector.Detect(entry.Text, context).HasExcessItems,
            nameof(SentenceCloneDetector) => SentenceCloneDetector.Detect(entry.Text, context).HasClone,
            nameof(RollCallDetector) => RollCallDetector.Detect(entry.Text, context).HasRollCall,
            nameof(RepeatedCauseDetector) => RepeatedCauseDetector.Detect(entry.Text, context).HasRepeatedCause,
            nameof(RepeatedFuelSuburbDetector) => RepeatedFuelSuburbDetector.Detect(entry.Text, context).HasRepeatedFuelSuburb,
            _ => throw new InvalidOperationException(
                $"corpus entry {entry.Id} names an unknown gate '{entry.ExpectedGate}'"),
        };
    }

    /// <summary>
    ///     Context-free reads: the corpus stores text only, so gates that take a traffic context are
    ///     given an empty one. Anything that genuinely needs the fetched data cannot be asserted here
    ///     and belongs in the replay corpus under <c>test/corpus/regressions/</c> instead.
    /// </summary>
    private static BroadcastWithTraffic MinimalContext() => new()
    {
        TargetDurationSeconds = 22,
        JobId = "subpar-corpus",
        Location = "corpus",
        Traffic = string.Empty,
        Incidents = [],
        Corridors = [],
    };

    private static List<SubparEntry> LoadCorpus()
    {
        string path = Path.Combine(CorpusPaths.RepoRoot(), "test", "corpus", "subpar-reads.json");
        File.Exists(path).ShouldBeTrue($"subpar-read corpus not found at {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entries = new List<SubparEntry>();
        foreach (var element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new SubparEntry(
                Read(element, "id"),
                Read(element, "market"),
                Read(element, "sourceJobId"),
                Read(element, "defect"),
                Read(element, "rule"),
                Read(element, "expectedGate"),
                Read(element, "text")));
        }
        return entries;
    }

    private static string Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
