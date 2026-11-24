using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class VoiceModelResolverTests
{
    [Fact]
    public void NoBaseModels_UsesPreferred()
    {
        // Voice Design / generated voice (v3-native) reports no base models → preferred (v3).
        VoiceModelResolver.Resolve([], "eleven_v3").ShouldBe("eleven_v3");
    }

    [Fact]
    public void NullBaseModels_UsesPreferred()
    {
        VoiceModelResolver.Resolve(null, "eleven_v3").ShouldBe("eleven_v3");
    }

    [Fact]
    public void TrainedVoice_WithoutPreferred_UsesBestVerifiedBaseModel()
    {
        // A clone verified for the v2 family must NOT render on v3 (degrades it).
        VoiceModelResolver.Resolve(["eleven_multilingual_v2", "eleven_turbo_v2"], "eleven_v3")
            .ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public void TrainedVoice_PrefersMultilingualOverTurbo()
    {
        // Preference order independent of the order the API lists them.
        VoiceModelResolver.Resolve(["eleven_turbo_v2_5", "eleven_multilingual_v2"], "eleven_v3")
            .ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public void TurboOnlyVoice_UsesTurbo()
    {
        VoiceModelResolver.Resolve(["eleven_turbo_v2_5"], "eleven_v3").ShouldBe("eleven_turbo_v2_5");
    }

    [Fact]
    public void VoiceThatSupportsPreferred_HonoursIt()
    {
        // If the verified list includes the preferred model, use it.
        VoiceModelResolver.Resolve(["eleven_multilingual_v2", "eleven_v3"], "eleven_v3").ShouldBe("eleven_v3");
    }

    [Fact]
    public void PreferredIsV2Family_AndSupported_UsesIt()
    {
        VoiceModelResolver.Resolve(["eleven_multilingual_v2", "eleven_turbo_v2_5"], "eleven_multilingual_v2")
            .ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public void UnknownBaseModelsOnly_FallsBackToFirstListed()
    {
        VoiceModelResolver.Resolve(["some_future_model"], "eleven_v3").ShouldBe("some_future_model");
    }
}
