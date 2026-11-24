using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class IncidentDescriptionGrammarTests
{
    [Fact]
    public void SplitsOnTheLastSeparator_SoAHyphenatedRoadNameSurvives()
    {
        // Four measured Australian records carry a road name containing " - " ("At Beaudesert -
        // Nerang Road - Road surface in poor condition", and three on "Railway Alice Springs -
        // Darwin"). Splitting on the FIRST separator truncates the road silently.
        var parsed = IncidentDescriptionGrammar.Parse(
            "At Beaudesert - Nerang Road - Road surface in poor condition");

        parsed.Shape.ShouldBe(DescriptionShape.At);
        parsed.PrimaryAnchor.Value.ShouldBe("Beaudesert - Nerang Road");
        parsed.ConditionText.ShouldBe("Road surface in poor condition");
    }

    [Fact]
    public void RampRecordNamesTheRampAndLiftsTheDirectionOutOfTheAnchor()
    {
        var parsed = IncidentDescriptionGrammar.Parse("Ramp to Gold Coast Highway (Eastbound) - Lane blocked");

        parsed.Shape.ShouldBe(DescriptionShape.RampTo);
        parsed.RampTarget.Value.ShouldBe("Gold Coast Highway");
        parsed.StatedDirection.Value.ShouldBe("eastbound");
        parsed.ConditionText.ShouldBe("Lane blocked");
        parsed.PrimaryAnchor.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void BetweenRecordKeepsBothEndpoints()
    {
        var parsed = IncidentDescriptionGrammar.Parse(
            "Between Lower Plenty Road and Watsonia Road - Lane blocked");

        parsed.Shape.ShouldBe(DescriptionShape.Between);
        parsed.PrimaryAnchor.Value.ShouldBe("Lower Plenty Road");
        parsed.SecondaryAnchor.Value.ShouldBe("Watsonia Road");
        parsed.ConditionText.ShouldBe("Lane blocked");
    }

    [Fact]
    public void BetweenIsOnlyMatchedAtTheStartOfThePrefix()
    {
        // The old anchor regex allowed a ".*?" prefix and so matched "between" mid-string, turning a
        // single place into a two-endpoint span it never was.
        var parsed = IncidentDescriptionGrammar.Parse("At the gap between the bridges - Road works");

        parsed.Shape.ShouldBe(DescriptionShape.At);
        parsed.PrimaryAnchor.Value.ShouldBe("the gap between the bridges");
        parsed.SecondaryAnchor.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void ABareStatusHasNoAnchorAtAll()
    {
        var parsed = IncidentDescriptionGrammar.Parse("Closed");

        parsed.Shape.ShouldBe(DescriptionShape.None);
        parsed.ConditionText.ShouldBe("Closed");
        parsed.PrimaryAnchor.IsNone.ShouldBeTrue();
        parsed.RampTarget.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void ANonEnglishPrefixCostsTheAnchorOnly_AndNeverThrows()
    {
        // Measured: a non-English `lang` destroys the grammar (0 of 69 recognised prefixes on de-DE /
        // fr-FR). The condition itself comes from codes[0], so an unrecognised prefix costs the anchor
        // and nothing else.
        var parsed = IncidentDescriptionGrammar.Parse("Zwischen Ausfahrt A und Ausfahrt B - Baustelle");

        parsed.Shape.ShouldBe(DescriptionShape.Unknown);
        parsed.PrimaryAnchor.IsNone.ShouldBeTrue();
        parsed.SecondaryAnchor.IsNone.ShouldBeTrue();
        parsed.ConditionText.ShouldBe("Baustelle");
    }

    [Fact]
    public void TotalOverNullBlankAndSeparatorlessInput()
    {
        IncidentDescriptionGrammar.Parse(null).Shape.ShouldBe(DescriptionShape.None);
        IncidentDescriptionGrammar.Parse("   ").ConditionText.ShouldBeEmpty();

        // No separator at all: HERE's whole string is its statement, and no anchor is invented from it.
        var german = IncidentDescriptionGrammar.Parse("Wegen Baustelle gesperrt");
        german.PrimaryAnchor.IsNone.ShouldBeTrue();
        german.ConditionText.ShouldBe("Wegen Baustelle gesperrt");
    }
}
