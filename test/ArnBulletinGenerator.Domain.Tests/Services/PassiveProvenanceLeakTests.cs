using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// Pins the passive "is reported" leak, measured on 4 of 13 reads in the 2026-08-25 end-to-end run.
/// Every catching case below is a verbatim excerpt from a generated bulletin.
/// </summary>
public sealed class PassiveProvenanceLeakTests
{
    [Theory]
    [InlineData("On Cleveland Street, traffic is reported clear.")]
    [InlineData("Elsewhere, clear running is reported on Bruce Highway, Captain Cook Highway.")]
    [InlineData("A clear run is being reported on Hallet Place.")]
    [InlineData("The previously reported road closure on Mitchell Road has now cleared.")]
    [InlineData("Minor impacts are reported on Stuart Road at roadworks.")]
    // Bare participle, no auxiliary to anchor on - the form an earlier cut of this gate missed.
    [InlineData("South Road is a touch slower at Sir Donald Bradman Drive, with roadworks reported.")]
    [InlineData("Hallet Place is running clear, with no further traffic issues reported.")]
    // Active, and with the ROAD doing the reporting.
    [InlineData("The M8, Sydney Gateway and Cleveland Street are all reporting clear roads.")]
    public void Detect_CatchesThePresenterNarratingThatAFactWasSupplied(string aired)
    {
        MetaLeakDetector.Detect(aired).HasMetaLeak.ShouldBeTrue();
    }

    [Theory]
    // The ACTIVE noun form is real radio language for something genuinely unconfirmed, and must
    // survive: banning it would cost ARN the only honest way to attribute an unverified incident.
    [InlineData("There are reports of a crash on the M1 at Springwood.")]
    [InlineData("We're getting reports of a breakdown in the tunnel.")]
    // Plain statements of the same facts - what the writer should say instead.
    [InlineData("Cleveland Street is clear.")]
    [InlineData("The road closure on Mitchell Road has cleared.")]
    [InlineData("Bruce Highway and Captain Cook Highway are running clear.")]
    public void Detect_LeavesPlainCopyAndActiveAttributionAlone(string clean)
    {
        MetaLeakDetector.Detect(clean).HasMetaLeak.ShouldBeFalse();
    }

    [Fact]
    public void Detect_StillCatchesTheOriginalDatabaseLanguage()
    {
        // The patterns this gate already owned must not regress.
        MetaLeakDetector.Detect("No other problems are logged on the listed roads.")
            .HasMetaLeak.ShouldBeTrue();
        MetaLeakDetector.Detect("The rest of the monitored corridors are clear.")
            .HasMetaLeak.ShouldBeTrue();
    }

    [Theory]
    // Verbatim from the 2026-08-25 panel. Each repair drops only the provenance, never a fact.
    [InlineData(
        "Traffic is flowing freely on Sydney Gateway, with no delays reported.",
        "Traffic is flowing freely on Sydney Gateway.")]
    [InlineData(
        "Cleveland Street is moving freely at present, with no further issues reported.",
        "Cleveland Street is moving freely at present.")]
    [InlineData(
        "Good news on Mitchell Road, where the reported road closure has cleared.",
        "Good news on Mitchell Road, where the road closure has cleared.")]
    [InlineData(
        "On Cleveland Street, traffic is reported clear.",
        "On Cleveland Street, traffic is clear.")]
    public void Repair_StripsProvenanceAndKeepsTheFact(string aired, string expected)
    {
        MetaLeakDetector.Repair(aired).ShouldBe(expected);
        MetaLeakDetector.Detect(MetaLeakDetector.Repair(aired)).HasMetaLeak.ShouldBeFalse();
    }

    [Fact]
    public void Repair_LeavesAShapeThatNeedsARedraftForTheRedraft()
    {
        // Fixing this needs the sentence rebuilt, not a clause removed, so it must stay blocking.
        const string aired = "A clear run is being reported on Hallet Place.";
        MetaLeakDetector.Repair(aired).ShouldBe(aired);
        MetaLeakDetector.Detect(aired).HasMetaLeak.ShouldBeTrue();
    }

    [Fact]
    public void Repair_NeverTouchesTheHonestNounForm()
    {
        const string honest = "There are reports of a crash on the M1 at Springwood.";
        MetaLeakDetector.Repair(honest).ShouldBe(honest);
    }
}
