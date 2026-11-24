using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class IncidentConditionCatalogTests
{
    [Fact]
    public void PrimaryCodeDecidesTheCondition()
    {
        // codes[0] is the condition key; 701 is roadworks, a standing footprint rather than an event.
        var condition = IncidentConditionCatalog.FromCodes([701, 520]);

        condition.Kind.ShouldBe(IncidentConditionKind.RoadWorks);
        condition.Phrase.ShouldBe("roadworks");
        condition.IsStandingFootprint.ShouldBeTrue();
        condition.ExpectsClosure.ShouldBeFalse();
    }

    [Fact]
    public void CodeOrderIsTheFact_NotAnIncidentalDetail()
    {
        // AlertC orders by primary cause: (520, 701) is a lane blocked at a worksite, while
        // (701, 520) is a worksite that happens to block a lane. Folding the list together loses
        // which one HERE reported.
        IncidentConditionCatalog.FromCodes([520, 701]).Kind.ShouldBe(IncidentConditionKind.LaneBlocked);
        IncidentConditionCatalog.FromCodes([701, 520]).Kind.ShouldBe(IncidentConditionKind.RoadWorks);
    }

    [Fact]
    public void ClosureCodeExpectsAClosure()
    {
        var condition = IncidentConditionCatalog.FromCodes([401]);

        condition.Kind.ShouldBe(IncidentConditionKind.Closed);
        condition.ExpectsClosure.ShouldBeTrue();
        condition.IsStandingFootprint.ShouldBeFalse();
    }

    [Theory]
    [InlineData(735, IncidentConditionKind.ClosedForRoadWorks, true, true)]
    [InlineData(938, IncidentConditionKind.ClosedForFlooding, true, false)]
    [InlineData(136, IncidentConditionKind.TrafficCongestion, false, false)]
    [InlineData(916, IncidentConditionKind.PoorRoadSurface, false, true)]
    public void MeasuredAustralianCodesMapToTheirCondition(
        int code, IncidentConditionKind kind, bool expectsClosure, bool standingFootprint)
    {
        var condition = IncidentConditionCatalog.FromCodes([code]);

        condition.Kind.ShouldBe(kind);
        condition.ExpectsClosure.ShouldBe(expectsClosure);
        condition.IsStandingFootprint.ShouldBe(standingFootprint);
        condition.Phrase.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NoCodesOrAnUnmappedCodeIsUnknownAndNeverVoiced()
    {
        // An unmapped code tells us nothing we may put to air: ClaimGate withholds the record rather
        // than letting it borrow a plausible-sounding condition.
        foreach (var condition in new[]
                 {
                     IncidentConditionCatalog.FromCodes([]),
                     IncidentConditionCatalog.FromCodes(null),
                     IncidentConditionCatalog.FromCodes([99999])
                 })
        {
            condition.Kind.ShouldBe(IncidentConditionKind.Unknown);
            condition.Phrase.ShouldBeEmpty();
            condition.ExpectsClosure.ShouldBeFalse();
            condition.IsStandingFootprint.ShouldBeFalse();
        }
    }
}
