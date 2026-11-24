using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class PerVoiceFillerCartResolverTests
{
    private const string CartScript =
        "You're listening to Your Traffic Network. We'll have your next full update on the roads very shortly.";

    private readonly IFillerAudioCacheService _fillerAudioCache = Substitute.For<IFillerAudioCacheService>();
    private readonly BroadcastOptions _options = new() { FillerCartScript = CartScript };
    private readonly PerVoiceFillerCartResolver _sut;

    public PerVoiceFillerCartResolverTests()
    {
        _sut = new PerVoiceFillerCartResolver(
            _fillerAudioCache,
            Options.Create(_options),
            Substitute.For<ILogger<PerVoiceFillerCartResolver>>());
    }

    private static Job JobWithVoiceConfig(Option<VoiceSettings> settings = default)
    {
        var job = Job.Create("job-1", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "elevenlabs", "voice-1", "Voice One", "eleven_v3", settings));
        return job;
    }

    [Fact]
    public async Task ResolveAsync_ScriptEmpty_ReturnsNoneWithoutCacheCall()
    {
        _options.FillerCartScript = "";
        var job = JobWithVoiceConfig(Option<VoiceSettings>.Some(VoiceSettings.Default));

        var result = await _sut.ResolveAsync(job, CancellationToken.None);

        result.HasValue.ShouldBeFalse();
        await _fillerAudioCache.DidNotReceiveWithAnyArgs().ResolveCartAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ResolveAsync_NoVoiceConfig_ReturnsNone()
    {
        var job = Job.Create("job-1", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;

        var result = await _sut.ResolveAsync(job, CancellationToken.None);

        result.HasValue.ShouldBeFalse();
        await _fillerAudioCache.DidNotReceiveWithAnyArgs().ResolveCartAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ResolveAsync_VoiceConfigWithoutSettings_ReturnsNone()
    {
        // Jobs using the broadcast default voice carry no concrete settings — the cache key
        // requires them, so the per-voice path deliberately skips to the evergreen cart.
        var job = JobWithVoiceConfig();

        var result = await _sut.ResolveAsync(job, CancellationToken.None);

        result.HasValue.ShouldBeFalse();
        await _fillerAudioCache.DidNotReceiveWithAnyArgs().ResolveCartAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ResolveAsync_CacheSuccess_ReturnsCartUrl()
    {
        var job = JobWithVoiceConfig(Option<VoiceSettings>.Some(VoiceSettings.Default));
        _fillerAudioCache.ResolveCartAsync(
                CartScript, "voice-1", VoiceSettings.Default, "eleven_v3", Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedCartClip>.Success(new ResolvedCartClip(
                "https://blob/filler-cache/__cart__/key.wav", [], 15.98, [])));

        var result = await _sut.ResolveAsync(job, CancellationToken.None);

        result.HasValue.ShouldBeTrue();
        result.Value.Url.ShouldBe("https://blob/filler-cache/__cart__/key.wav");
        result.Value.DurationSeconds.ShouldBe(15.98);
        await _fillerAudioCache.Received(1).ResolveCartAsync(
            CartScript, "voice-1", VoiceSettings.Default, "eleven_v3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_CacheFailure_ReturnsNone()
    {
        var job = JobWithVoiceConfig(Option<VoiceSettings>.Some(VoiceSettings.Default));
        _fillerAudioCache.ResolveCartAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedCartClip>.Failure("tts down"));

        var result = await _sut.ResolveAsync(job, CancellationToken.None);

        result.HasValue.ShouldBeFalse();
    }
}
