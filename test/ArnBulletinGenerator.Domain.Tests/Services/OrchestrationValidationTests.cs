using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class OrchestrationValidationTests
{
    private static OrchestrationRequestDto ValidDto()
    {
        return new OrchestrationRequestDto(
            60,
            "Sydney",
            new CoverageDto(-33.8688, 151.2093, 30),
            VoiceId: "voice-123");
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsSuccess()
    {
        var result = OrchestrationValidation.Validate(ValidDto());
        result.IsSuccess.ShouldBeTrue();
        result.Value.Location.ShouldBe("Sydney");
        result.Value.TargetDurationSeconds.ShouldBe(60);
    }

    [Fact]
    public void Validate_MissingVoice_ReturnsExactFailure()
    {
        var dto = ValidDto() with { VoiceId = null, VoiceProfileId = null };

        var result = OrchestrationValidation.Validate(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(
            "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");
    }

    [Fact]
    public void Validate_VoiceIdOnly_ReturnsSuccess()
    {
        var dto = ValidDto() with { VoiceProfileId = null };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.Value.ShouldBe("voice-123");
        result.Value.VoiceProfileId.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Validate_VoiceProfileIdOnly_ReturnsSuccess()
    {
        var dto = ValidDto() with { VoiceId = null, VoiceProfileId = "profile-123" };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.HasValue.ShouldBeFalse();
        result.Value.VoiceProfileId.Value.ShouldBe("profile-123");
    }

    [Fact]
    public void Validate_WhitespaceVoiceValues_ReturnsExactFailure()
    {
        var dto = ValidDto() with { VoiceId = "   ", VoiceProfileId = "\t" };

        var result = OrchestrationValidation.Validate(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(
            "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");
    }

    [Fact]
    public void Validate_WhitespaceVoiceIdWithProfileId_ReturnsSuccess()
    {
        var dto = ValidDto() with { VoiceId = "  ", VoiceProfileId = "profile-123" };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.HasValue.ShouldBeFalse();
        result.Value.VoiceProfileId.Value.ShouldBe("profile-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingLocation_ReturnsFailure(string? location)
    {
        var dto = ValidDto() with { Location = location };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Location");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Validate_InvalidDuration_ReturnsFailure(double? duration)
    {
        var dto = ValidDto() with { TargetDurationSeconds = duration };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("TargetDurationSeconds");
    }

    [Fact]
    public void Validate_MissingCoverage_ReturnsFailure()
    {
        var dto = ValidDto() with { Coverage = null };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Coverage");
    }

    [Fact]
    public void Validate_InvalidLatitude_ReturnsFailure()
    {
        var dto = ValidDto() with { Coverage = new CoverageDto(91.0, 151.0, 30) };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InvalidLongitude_ReturnsFailure()
    {
        var dto = ValidDto() with { Coverage = new CoverageDto(-33.0, 181.0, 30) };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ZeroRadius_ReturnsFailure()
    {
        var dto = ValidDto() with { Coverage = new CoverageDto(-33.0, 151.0, 0) };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ValidVoiceSettings_ReturnsSuccess()
    {
        var dto = ValidDto() with
        {
            VoiceSettings = new VoiceSettingsDto(
                1.0, 0.5, 0.8, 0.2, true)
        };
        var result = OrchestrationValidation.Validate(dto);
        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceSettings.HasValue.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InvalidVoiceSettings_ReturnsFailure()
    {
        var dto = ValidDto() with
        {
            VoiceSettings = new VoiceSettingsDto(
                5.0, 0.5, 0.8, 0.2, true)
        };
        var result = OrchestrationValidation.Validate(dto);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validate_NoVoiceSettings_VoiceSettingsIsNone()
    {
        var result = OrchestrationValidation.Validate(ValidDto());
        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceSettings.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Validate_VoiceIdAndName_MappedAsOptions()
    {
        var dto = ValidDto() with { VoiceId = "voice-123", VoiceName = "Jane" };
        var result = OrchestrationValidation.Validate(dto);
        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.Value.ShouldBe("voice-123");
        result.Value.VoiceName.Value.ShouldBe("Jane");
    }

    [Fact]
    public void Validate_NullVoiceId_WithProfileId_MapsToNone()
    {
        var dto = ValidDto() with { VoiceId = null, VoiceProfileId = "profile-123" };
        var result = OrchestrationValidation.Validate(dto);
        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.HasValue.ShouldBeFalse();
        result.Value.VoiceProfileId.Value.ShouldBe("profile-123");
    }

    [Fact]
    public void Validate_ScriptOnly_PreservedInOutput()
    {
        var dto = ValidDto() with { ScriptOnly = true };
        var result = OrchestrationValidation.Validate(dto);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ScriptOnly.ShouldBeTrue();
    }

    // ── FillOrder (per-station fill priority) ───────────────────────────

    [Fact]
    public void Validate_FillOrder_ParsesAllSourcesInGivenOrder()
    {
        var dto = ValidDto() with { FillOrder = ["weather", "fuel", "filler"] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FillOrder.ShouldBe(new[] { FillSource.Weather, FillSource.Fuel, FillSource.Filler });
    }

    [Fact]
    public void Validate_FillOrder_TrimsWhitespaceAndIgnoresCase()
    {
        var dto = ValidDto() with { FillOrder = [" WEATHER "] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FillOrder.ShouldBe(new[] { FillSource.Weather });
    }

    [Fact]
    public void Validate_FillOrder_UnknownValue_FailsNamingTheValue()
    {
        var dto = ValidDto() with { FillOrder = ["petrol"] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("petrol");
    }

    [Fact]
    public void Validate_FillOrder_DuplicateSource_Fails()
    {
        var dto = ValidDto() with { FillOrder = ["weather", "weather"] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Duplicate");
    }

    [Fact]
    public void Validate_FillOrder_DuplicateAfterTrimAndCaseNormalization_Fails()
    {
        // " WEATHER " normalizes to the already-seen weather — duplicates are caught on the
        // PARSED source, not the raw wire string.
        var dto = ValidDto() with { FillOrder = ["weather", " WEATHER "] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Duplicate");
    }

    [Fact]
    public void Validate_NullFillOrder_MapsToNull()
    {
        var result = OrchestrationValidation.Validate(ValidDto());

        result.IsSuccess.ShouldBeTrue();
        result.Value.FillOrder.ShouldBeNull();
    }

    [Fact]
    public void Validate_EmptyFillOrder_MapsToNull()
    {
        var dto = ValidDto() with { FillOrder = [] };

        var result = OrchestrationValidation.Validate(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FillOrder.ShouldBeNull();
    }
}
