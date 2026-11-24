using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RepeatedPrepositionDetectorTests
{
    [Fact]
    public void Flags_the_exact_live_shipped_defect_sydney_cbd()
    {
        // Observed live (pass 15, Sydney CBD): the two-incident split closure voiced as two bare
        // "at" clauses back to back.
        const string script =
            "The Sydney Harbour Tunnel is closed. The Warringah Freeway is shut outbound at Brook Street, " +
            "at the ramp to Alfred Street North.";

        var result = RepeatedPrepositionDetector.Detect(script);

        result.HasRepeatedPreposition.ShouldBeTrue();
        result.Feedback.ShouldContain("REPEATED PREPOSITION");
    }

    [Fact]
    public void Flags_repeated_at_within_one_sentence()
    {
        const string script =
            "Warringah Freeway citybound is closed at the ramp to Alfred Street North, at Brook Street, for construction.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeTrue();
    }

    [Fact]
    public void Flags_repeated_near()
    {
        const string script = "Traffic is slow near Church Street, near the shopping centre this morning.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeTrue();
    }

    [Fact]
    public void Passes_conjoined_locations_with_and()
    {
        // Two anchors joined naturally with "and" — not the repeated-preposition shape.
        const string script = "Footscray Road is shut at CityLink Tollway and Sims Street for construction.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeFalse();
    }

    [Fact]
    public void Passes_genuine_between_a_and_b_span()
    {
        const string script = "Coast Highway is shut between Pacific Motorway and Marble Arch Place for roadworks.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeFalse();
    }

    [Fact]
    public void Passes_naturally_varied_second_clause()
    {
        const string script =
            "Warringah Freeway is shut at Brook Street, with the ramp to Alfred Street North also closed.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeFalse();
    }

    [Fact]
    public void Passes_different_prepositions_on_the_same_road()
    {
        const string script =
            "Mountain Road has a road hazard at Tamborine Mountain Road, between Tamborine Mountain Road and Henri Robert Drive.";

        RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        RepeatedPrepositionDetector.Detect(string.Empty).HasRepeatedPreposition.ShouldBeFalse();
    }

    [Fact]
    public void Repair_swaps_second_preposition_for_and()
    {
        const string script =
            "The Warringah Freeway is shut outbound at Brook Street, at the ramp to Alfred Street North.";

        string repaired = RepeatedPrepositionDetector.Repair(script);

        RepeatedPrepositionDetector.Detect(repaired).HasRepeatedPreposition.ShouldBeFalse();
        repaired.ShouldContain("at Brook Street, and the ramp to Alfred Street North");
    }

    [Fact]
    public void Repair_leaves_a_clean_script_unchanged()
    {
        const string script = "Footscray Road is shut at CityLink Tollway and Sims Street for construction.";

        RepeatedPrepositionDetector.Repair(script).ShouldBe(script);
    }

    [Fact]
    public void Repair_preserves_all_other_sentences_untouched()
    {
        const string script =
            "The Sydney Harbour Tunnel is closed. The Warringah Freeway is shut outbound at Brook Street, " +
            "at the ramp to Alfred Street North. Marion Street has minor delays.";

        string repaired = RepeatedPrepositionDetector.Repair(script);

        repaired.ShouldContain("The Sydney Harbour Tunnel is closed.");
        repaired.ShouldContain("Marion Street has minor delays.");
    }
}
