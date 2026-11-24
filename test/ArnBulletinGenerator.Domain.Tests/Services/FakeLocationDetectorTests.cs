using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class FakeLocationDetectorTests
{
    [Fact]
    public void Flags_the_exact_live_shipped_defect_variant_one()
    {
        // Observed live (Newcastle, 3/3 reproductions): the item had NO real Location line, and
        // the writer invented a pseudo-location out of the incident type itself.
        const string script = "Mitchell Street outbound is closed at a road closure.";

        var result = FakeLocationDetector.Detect(script);

        result.HasFakeLocation.ShouldBeTrue();
        result.Feedback.ShouldContain("FAKE LOCATION");
    }

    [Fact]
    public void Flags_the_exact_live_shipped_defect_variant_two()
    {
        const string script = "Mitchell Street outbound is closed in Newcastle at the road closure.";

        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeTrue();
    }

    [Fact]
    public void Flags_the_exact_live_shipped_defect_variant_three()
    {
        const string script = "Mitchell Street outbound is closed near the road closure point.";

        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeTrue();
    }

    [Theory]
    [InlineData("The M1 is slow near the accident.")]
    [InlineData("Traffic is banked up at the construction.")]
    [InlineData("Reid Highway is closed at the roadworks.")]
    public void Flags_other_incident_type_nouns_used_as_fake_locations(string script) =>
        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeTrue();

    [Theory]
    // Possessive pseudo-anchors: the writer, pressured to pin a no-anchor item, invents a
    // self-referential "place" that locates nothing (observed fresh-eval: "near its listed
    // location", "at a standstill near its main run").
    [InlineData("Telegraph Road is at a standstill near its main run.")]
    [InlineData("Barclay Road is congested near its listed location.")]
    [InlineData("The freeway is slow at its usual spot.")]
    public void Flags_possessive_pseudo_anchors(string script) =>
        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeTrue();

    [Fact]
    public void Passes_their_possessive_about_people()
    {
        // "their" refers to people, not the road — must never trip the possessive class.
        FakeLocationDetector.Detect("Drivers on their way into the city face delays at Church Street.")
            .HasFakeLocation.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_genuine_cross_street_anchor()
    {
        const string script = "Warringah Freeway is closed at Brook Street.";

        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_genuine_named_road_anchor()
    {
        const string script = "Reid Highway is closed at Altone Road.";

        FakeLocationDetector.Detect(script).HasFakeLocation.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        FakeLocationDetector.Detect(string.Empty).HasFakeLocation.ShouldBeFalse();
    }

    [Fact]
    public void Repair_strips_the_fake_location_leaving_a_clean_sentence()
    {
        const string script = "Mitchell Street outbound is closed at a road closure.";

        string repaired = FakeLocationDetector.Repair(script);

        FakeLocationDetector.Detect(repaired).HasFakeLocation.ShouldBeFalse();
        repaired.ShouldBe("Mitchell Street outbound is closed.");
    }

    [Fact]
    public void Repair_strips_the_trailing_generic_noun_variant()
    {
        const string script = "Mitchell Street outbound is closed near the road closure point.";

        string repaired = FakeLocationDetector.Repair(script);

        repaired.ShouldBe("Mitchell Street outbound is closed.");
    }

    [Fact]
    public void Repair_leaves_a_clean_script_unchanged()
    {
        const string script = "Warringah Freeway is closed at Brook Street.";

        FakeLocationDetector.Repair(script).ShouldBe(script);
    }

    [Fact]
    public void Repair_preserves_other_sentences_untouched()
    {
        const string script =
            "Warringah Freeway is shut at Brook Street. Mitchell Street outbound is closed at a road closure. " +
            "Marion Street has minor delays.";

        var result = FakeLocationDetector.Repair(script);

        result.ShouldContain("Warringah Freeway is shut at Brook Street.");
        result.ShouldContain("Marion Street has minor delays.");
        result.ShouldNotContain("at a road closure");
    }

    [Fact]
    public void Detect_computes_exact_sentence_order_and_sentence_scope()
    {
        const string script =
            "The M4 is moving well. " +
            "Mitchell Street outbound is closed at a road closure. " +
            "Pacific Highway is clear.";

        var result = FakeLocationDetector.Detect(script);

        result.HasFakeLocation.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        var issue = result.Issue!;
        issue.Order.ShouldBe(2);
        issue.Defect.ShouldBe("FAKE_LOCATION");
        issue.Scope.ShouldBe(ArnBulletinGenerator.Domain.Models.RefineScope.Sentence);
        issue.IsPatchable.ShouldBeTrue();
    }
}
