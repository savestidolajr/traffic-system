using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ArnBulletinGenerator.ExternalSmoke.Tests;

[Collection(ExternalSmokeCollection.Name)]
[Trait("Category", "ExternalSmoke")]
public sealed class ElevenLabsContractSmokeTests(ExternalSmokeFixture fixture)
{
    [Fact]
    public async Task VoiceProvider_ListsVoicesAndOptionalTtsParsesAudio()
    {
        if (!fixture.Enabled) return;
        var voices = fixture.Services.GetRequiredService<IVoiceProvider>();
        var result = await voices.GetVoicesAsync();
        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Count.ShouldBeGreaterThan(0);

        if (Environment.GetEnvironmentVariable("ARN_EXTERNAL_SMOKE_TTS") != "1")
            return;

        var voiceId = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID") ?? result.Value[0].VoiceId;
        var synth = fixture.Services.GetRequiredService<IAudioSynthesizer>();
        var audio = await synth.SynthesizeAsync(
            "Traffic update.",
            Option<VoiceSettings>.Some(VoiceSettings.Default),
            Option<string>.Some(voiceId),
            Option<string>.Some("eleven_v3"));
        audio.IsSuccess.ShouldBeTrue(audio.Error);
        audio.Value.Audio.Length.ShouldBeGreaterThan(44);
    }
}
