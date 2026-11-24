using ArnBulletinGenerator.Application.Validation;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Validation;

public sealed class TrafficScriptStyleValidatorTests
{
    [Fact]
    public void Validate_EmptyScript_ReturnsInvalidWithMessage()
    {
        var result = TrafficScriptStyleValidator.Validate("");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem().ShouldContain("empty");
    }

    [Fact]
    public void Validate_CleanScript_ReturnsValid()
    {
        string script = "Traffic is flowing well across the city this morning. " +
                        "No major delays to report on the Monash Freeway.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_SpeakerLabel_ReturnsInvalid()
    {
        string script = "Alex: Good morning, here's your traffic update.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("speaker label"));
    }

    [Fact]
    public void Validate_FourOrMoreSpokenFigures_ReturnsValid()
    {
        string script = "Delays of six minutes on the Eastern Freeway. " +
                        "A three kilometre queue on the Monash, a four minute run on the Tulla " +
                        "and another two minutes of queuing on the Western Ring Road.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_DriveTimeComparisonPlusOneNumber_ReturnsValid()
    {
        // ATN style: "a 36-minute run, normally 18" plus one more number is fine
        string script = "It's a 36 minute run on the M4, normally 18 minutes, " +
                        "and a 3 km queue has formed on the Monash.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_DanglingConjunctionEnding_ReturnsInvalid()
    {
        string script = "The Monash is patchy citybound near Warrigal Road, and";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("complete clause"));
    }

    [Fact]
    public void Validate_BannedCloser_ReturnsInvalid()
    {
        string script = "The Monash is slow near Warrigal Road. Motorists are warned to plan ahead.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("closers"));
    }

    [Fact]
    public void Validate_RepetitiveClosureQueuePattern_ReturnsInvalid()
    {
        string script = "The Burnley Tunnel is closed and queues stretch back to Toorak Road. " +
                        "Domain Tunnel is closed with queues banked back to Swan Street.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("closure"));
    }

    [Fact]
    public void Validate_LowercaseAfterSentenceEnd_ReturnsInvalid()
    {
        string script = "Traffic is heavy on the freeway. delays are expected until noon.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("trailing fragment"));
    }

    [Fact]
    public void Validate_CleanMultiSentenceScript_ReturnsValid()
    {
        string script = "Good morning! Traffic is moving well across the metro area. " +
                        "The Monash Freeway is clear in both directions. " +
                        "Enjoy the commute today.";

        var result = TrafficScriptStyleValidator.Validate(script);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
