using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FillerAudioCacheKeyTests
{
    private static VoiceSettings DefaultSettings() => VoiceSettings.Create().Value;

    // ── Determinism ──────────────────────────────────────────────────────

    [Fact]
    public void Build_IsDeterministic_SameInputsProduceSameKey()
    {
        var settings = DefaultSettings();
        string first = FillerAudioCacheKey.Build("voice-1", settings, "eleven_v3");
        string second = FillerAudioCacheKey.Build("voice-1", settings, "eleven_v3");

        first.ShouldBe(second);
    }

    // ── Voice ID axis ────────────────────────────────────────────────────

    [Fact]
    public void Build_DiffersWhenVoiceIdDiffers()
    {
        var settings = DefaultSettings();
        string keyA = FillerAudioCacheKey.Build("voice-1", settings, "eleven_v3");
        string keyB = FillerAudioCacheKey.Build("voice-2", settings, "eleven_v3");

        keyA.ShouldNotBe(keyB);
    }

    // ── VoiceSettings axis (one field at a time) ──────────────────────────

    [Fact]
    public void Build_DiffersWhenStabilityDiffers()
    {
        var a = VoiceSettings.Create(stability: 0.30).Value;
        var b = VoiceSettings.Create(stability: 0.80).Value;

        FillerAudioCacheKey.Build("v", a, "m").ShouldNotBe(FillerAudioCacheKey.Build("v", b, "m"));
    }

    [Fact]
    public void Build_DiffersWhenSimilarityBoostDiffers()
    {
        var a = VoiceSettings.Create(similarityBoost: 0.40).Value;
        var b = VoiceSettings.Create(similarityBoost: 0.95).Value;

        FillerAudioCacheKey.Build("v", a, "m").ShouldNotBe(FillerAudioCacheKey.Build("v", b, "m"));
    }

    [Fact]
    public void Build_DiffersWhenStyleDiffers()
    {
        var a = VoiceSettings.Create(style: 0.0).Value;
        var b = VoiceSettings.Create(style: 0.75).Value;

        FillerAudioCacheKey.Build("v", a, "m").ShouldNotBe(FillerAudioCacheKey.Build("v", b, "m"));
    }

    [Fact]
    public void Build_DiffersWhenUseSpeakerBoostDiffers()
    {
        var a = VoiceSettings.Create(useSpeakerBoost: true).Value;
        var b = VoiceSettings.Create(useSpeakerBoost: false).Value;

        FillerAudioCacheKey.Build("v", a, "m").ShouldNotBe(FillerAudioCacheKey.Build("v", b, "m"));
    }

    [Fact]
    public void Build_DiffersWhenSpeedDiffers()
    {
        var a = VoiceSettings.Create(speed: 0.80).Value;
        var b = VoiceSettings.Create(speed: 1.10).Value;

        FillerAudioCacheKey.Build("v", a, "m").ShouldNotBe(FillerAudioCacheKey.Build("v", b, "m"));
    }

    // ── Model axis ───────────────────────────────────────────────────────

    [Fact]
    public void Build_DiffersWhenModelDiffers()
    {
        var settings = DefaultSettings();
        string keyA = FillerAudioCacheKey.Build("voice-1", settings, "eleven_v3");
        string keyB = FillerAudioCacheKey.Build("voice-1", settings, "eleven_multilingual_v2");

        keyA.ShouldNotBe(keyB);
    }
}
