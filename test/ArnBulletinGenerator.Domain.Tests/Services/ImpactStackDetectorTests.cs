using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class ImpactStackDetectorTests
{
    [Fact]
    public void Flags_the_exact_live_shipped_defect_perth()
    {
        // Observed live (pass 15, Perth): "congested" in the main clause, then a trailing clause
        // restating the SAME fact twice more via different severity synonyms.
        const string script =
            "Tonkin Highway northbound is congested at Great Eastern Highway and Dunreath Drive after an accident, " +
            "with heavy traffic and moderate delays.";

        var result = ImpactStackDetector.Detect(script);

        result.HasImpactStack.ShouldBeTrue();
        result.Feedback.ShouldContain("IMPACT-STACK");
    }

    [Fact]
    public void Flags_trailing_clause_with_including()
    {
        const string script = "The M1 outbound is slow near Stud Road, including heavy traffic and severe delays.";

        ImpactStackDetector.Detect(script).HasImpactStack.ShouldBeTrue();
    }

    [Fact]
    public void Passes_single_impact_word_in_trailing_clause()
    {
        // Only ONE severity word in the tail — a genuinely new fact (a cause), not a stack.
        const string script = "The M1 outbound is slow near Stud Road, with moderate delays after an accident.";

        ImpactStackDetector.Detect(script).HasImpactStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_clean_sentence_with_no_trailing_clause()
    {
        const string script = "Tonkin Highway northbound is congested at Great Eastern Highway after an accident.";

        ImpactStackDetector.Detect(script).HasImpactStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_two_items_each_with_their_own_single_impact_word()
    {
        // Two DIFFERENT roads, each with its own single impact fact — not a stack on one item.
        const string script = "King Street is slow with minor delays, and Point Road is congested near the mall.";

        ImpactStackDetector.Detect(script).HasImpactStack.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        ImpactStackDetector.Detect(string.Empty).HasImpactStack.ShouldBeFalse();
    }

    [Fact]
    public void Repair_strips_the_exact_live_shipped_defect()
    {
        const string script =
            "Tonkin Highway northbound is congested at Great Eastern Highway and Dunreath Drive after an accident, " +
            "with heavy traffic and moderate delays.";

        string repaired = ImpactStackDetector.Repair(script);

        ImpactStackDetector.Detect(repaired).HasImpactStack.ShouldBeFalse();
        repaired.ShouldBe(
            "Tonkin Highway northbound is congested at Great Eastern Highway and Dunreath Drive after an accident.");
    }

    [Fact]
    public void Repair_leaves_a_clean_script_unchanged()
    {
        const string script = "The M1 outbound is slow near Stud Road, with moderate delays after an accident.";

        ImpactStackDetector.Repair(script).ShouldBe(script);
    }

    [Fact]
    public void Repair_preserves_all_other_sentences_untouched()
    {
        const string script =
            "Warringah Freeway is shut at Brook Street. Tonkin Highway northbound is congested, " +
            "with heavy traffic and moderate delays. Marion Street has minor delays.";

        string repaired = ImpactStackDetector.Repair(script);

        repaired.ShouldContain("Warringah Freeway is shut at Brook Street.");
        repaired.ShouldContain("Marion Street has minor delays.");
    }
}
