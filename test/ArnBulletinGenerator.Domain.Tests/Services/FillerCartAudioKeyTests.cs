using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FillerCartAudioKeyTests
{
    private const string Script =
        "You're listening to Your Traffic Network. We'll have your next full update on the roads very shortly.";

    private static VoiceSettings DefaultSettings() => VoiceSettings.Create().Value;

    // ── Determinism ──────────────────────────────────────────────────────

    [Fact]
    public void Build_IsDeterministic_SameInputsProduceSameKey()
    {
        var settings = DefaultSettings();
        string first = FillerCartAudioKey.Build(Script, "voice-1", settings, "eleven_v3");
        string second = FillerCartAudioKey.Build(Script, "voice-1", settings, "eleven_v3");

        first.ShouldBe(second);
    }

    // ── Script axis ─────────────────────────────────────────────────────

    [Fact]
    public void Build_DiffersWhenScriptTextDiffers()
    {
        var settings = DefaultSettings();
        string keyA = FillerCartAudioKey.Build("Keep it locked here.", "voice-1", settings, "eleven_v3");
        string keyB = FillerCartAudioKey.Build("We'll be back soon.", "voice-1", settings, "eleven_v3");

        keyA.ShouldNotBe(keyB);
    }

    [Fact]
    public void Build_DiffersWhenScriptWhitespaceChanges()
    {
        var settings = DefaultSettings();
        string keyA = FillerCartAudioKey.Build("Back soon with the latest.", "voice-1", settings, "eleven_v3");
        string keyB = FillerCartAudioKey.Build("Back  soon  with the latest.", "voice-1", settings, "eleven_v3");

        keyA.ShouldNotBe(keyB);
    }

    // ── Voice ID axis ────────────────────────────────────────────────────

    [Fact]
    public void Build_DiffersWhenVoiceIdDiffers()
    {
        var settings = DefaultSettings();
        string keyA = FillerCartAudioKey.Build(Script, "voice-1", settings, "eleven_v3");
        string keyB = FillerCartAudioKey.Build(Script, "voice-2", settings, "eleven_v3");

        keyA.ShouldNotBe(keyB);
    }

    // ── VoiceSettings axis (one field at a time) ──────────────────────────

    [Fact]
    public void Build_DiffersWhenStabilityDiffers()
    {
        var a = VoiceSettings.Create(stability: 0.30).Value;
        var b = VoiceSettings.Create(stability: 0.80).Value;

        FillerCartAudioKey.Build(Script, "v", a, "m").ShouldNotBe(FillerCartAudioKey.Build(Script, "v", b, "m"));
    }

    [Fact]
    public void Build_DiffersWhenSpeedDiffers()
    {
        var a = VoiceSettings.Create(speed: 0.80).Value;
        var b = VoiceSettings.Create(speed: 1.10).Value;

        FillerCartAudioKey.Build(Script, "v", a, "m").ShouldNotBe(FillerCartAudioKey.Build(Script, "v", b, "m"));
    }

    // ── Model axis ───────────────────────────────────────────────────────

    [Fact]
    public void Build_DiffersWhenModelDiffers()
    {
        var settings = DefaultSettings();
        string keyA = FillerCartAudioKey.Build(Script, "voice-1", settings, "eleven_v3");
        string keyB = FillerCartAudioKey.Build(Script, "voice-1", settings, "eleven_multilingual_v2");

        keyA.ShouldNotBe(keyB);
    }
}
