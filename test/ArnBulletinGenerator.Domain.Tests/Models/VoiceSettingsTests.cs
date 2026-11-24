using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class VoiceSettingsTests
{
    [Fact]
    public void Create_WithDefaults_Succeeds()
    {
        var result = VoiceSettings.Create();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Stability.ShouldBe(0.50);
        result.Value.SimilarityBoost.ShouldBe(0.90);
        result.Value.Style.ShouldBe(0.0);
        result.Value.UseSpeakerBoost.ShouldBeTrue();
        result.Value.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void Create_WithValidBoundaryValues_Succeeds()
    {
        var result = VoiceSettings.Create(0.0, 1.0, 0.0, speed: 0.7);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_StabilityOutOfRange_Fails(double stability)
    {
        var result = VoiceSettings.Create(stability);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.VoiceSettings.StabilityOutOfRange);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_SimilarityBoostOutOfRange_Fails(double similarityBoost)
    {
        var result = VoiceSettings.Create(similarityBoost: similarityBoost);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.VoiceSettings.SimilarityBoostOutOfRange);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_StyleOutOfRange_Fails(double style)
    {
        var result = VoiceSettings.Create(style: style);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.VoiceSettings.StyleOutOfRange);
    }

    [Theory]
    [InlineData(0.69)]
    [InlineData(1.21)]
    public void Create_SpeedOutOfRange_Fails(double speed)
    {
        var result = VoiceSettings.Create(speed: speed);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.VoiceSettings.SpeedOutOfRange);
    }

    [Fact]
    public void GetConfigurationKey_FormatsCorrectly()
    {
        var settings = VoiceSettings.Create(0.5, 0.8, 0.2, speed: 1.0, useSpeakerBoost: true).Value;

        string key = settings.GetConfigurationKey("voice123");

        key.ShouldBe("voice123_1.00_0.50_0.80_0.20_True");
    }

    [Fact]
    public void Default_ReturnsValidInstance()
    {
        var settings = VoiceSettings.Default;

        settings.Stability.ShouldBe(0.50);
        settings.Speed.ShouldBe(1.0);
    }
}
