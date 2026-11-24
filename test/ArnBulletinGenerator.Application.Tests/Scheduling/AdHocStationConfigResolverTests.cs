using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Scheduling;

/// <summary>
///     Covers the resolver that makes a manual "Trigger now" inherit its station's speech
///     configuration (2026-08-10 UI audit: the manual path carried no delivery style, no
///     pronunciation dictionary, no bookends and the default fill order, so a bulletin triggered
///     for a configured station did not sound like that station).
///     <para>
///         The per-field resolution itself is <see cref="StationConfigResolver" />'s, already
///         covered by the poller's tests — this class covers what is NEW: reading the right
///         schedule, inheriting its fill order, and staying fail-soft so an unreadable or
///         unconfigured station degrades to platform defaults instead of blocking the broadcast.
///         That a populated config actually reaches the queue message is locked in
///         <c>QueueMessageMappingExtensionsTests</c>.
///     </para>
/// </summary>
public class AdHocStationConfigResolverTests
{
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly IPronunciationDictionaryService _dictionaries =
        Substitute.For<IPronunciationDictionaryService>();
    private readonly IBookendService _bookends = Substitute.For<IBookendService>();
    private readonly AdHocStationConfigResolver _sut;

    public AdHocStationConfigResolverTests()
    {
        // The pronunciation stack always does one list read to discover the platform-wide layer.
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));

        _sut = new AdHocStationConfigResolver(
            _schedules,
            Substitute.For<IModelSelector>(),
            Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBulletinPlaybookService>(),
            _bookends,
            _dictionaries,
            Options.Create(new BroadcastOptions()),
            TimeProvider.System,
            NullLogger<AdHocStationConfigResolver>.Instance);
    }

    private static MarketSchedule Schedule(IReadOnlyList<FillSource>? fillOrder = null)
    {
        var schedule = MarketSchedule.Create(
            "sydney", "Sydney KIIS 90s Traffic", "KIIS 90s", "Australia/Sydney",
            "0 0 * * * *", true, CoverageArea.Create(-33.87, 151.21, 39).Value,
            "vp-1", "out", 30, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        if (fillOrder is not null) schedule.FillOrder = fillOrder;
        return schedule;
    }

    private static Bookend LegacyBookend(string intro, string outro)
    {
        var now = new DateTime(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);
        return Bookend.Reconstitute(
            "bk-legacy", "Legacy Pair", null, "active", now, now, null, [],
            [new BookendVersion(1, new BookendTemplate(intro, outro), null, now, null)]);
    }

    [Fact]
    public async Task ResolveAsync_LegacyIncompleteOutro_OmitsOutroAndKeepsIntro()
    {
        var schedule = Schedule();
        schedule.BookendId = "bk-legacy";
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(schedule));
        _bookends.GetAsync("bk-legacy", Arg.Any<CancellationToken>())
            .Returns(Result<Bookend>.Success(LegacyBookend(
                "Looking over {city} today", "That's all from {city} traffic, powered by")));

        var config = await _sut.ResolveAsync("sydney", "eleven_v3");

        config.Bookends.ShouldNotBeNull();
        config.Bookends!.IntroText.ShouldBe("Looking over Sydney today");
        config.Bookends.OutroText.ShouldBe("");
    }

    [Fact]
    public async Task ResolveAsync_CompleteOutroWithoutFinalPunctuation_IsUnchanged()
    {
        var schedule = Schedule();
        schedule.BookendId = "bk-legacy";
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(schedule));
        _bookends.GetAsync("bk-legacy", Arg.Any<CancellationToken>())
            .Returns(Result<Bookend>.Success(LegacyBookend(
                "Looking over {city} today", "That's all from {city} traffic")));

        var config = await _sut.ResolveAsync("sydney", "eleven_v3");

        config.Bookends.ShouldNotBeNull();
        config.Bookends!.IntroText.ShouldBe("Looking over Sydney today");
        config.Bookends.OutroText.ShouldBe("That's all from Sydney traffic");
    }

    [Fact]
    public async Task ResolveAsync_InheritsTheStationsFillOrder()
    {
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule([FillSource.Fuel, FillSource.Weather])));

        var config = await _sut.ResolveAsync("sydney", "eleven_v3");

        config.FillOrder.ShouldBe(new[] { FillSource.Fuel, FillSource.Weather });
    }

    [Fact]
    public async Task ResolveAsync_StationWithNoSpeechConfig_InventsNothing()
    {
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule()));

        var config = await _sut.ResolveAsync("sydney", "eleven_v3");

        config.DeliveryStyle.ShouldBeNull();
        config.DeliveryStyleId.ShouldBeNull();
        config.PronunciationLocator.ShouldBeNull();
        config.GlobalPronunciationLocator.ShouldBeNull();
        config.Bookends.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnreadableSchedule_DegradesToDefaults_RatherThanFailingTheBroadcast()
    {
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("storage unavailable"));

        var config = await _sut.ResolveAsync("sydney", "eleven_v3");

        config.ShouldBe(AdHocStationConfig.None);
    }

    [Fact]
    public async Task ResolveAsync_BlankMarket_DoesNotTouchStorage()
    {
        var config = await _sut.ResolveAsync("  ", "eleven_v3");

        config.ShouldBe(AdHocStationConfig.None);
        await _schedules.DidNotReceive().GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MissingVoiceModel_StillResolves()
    {
        // The model only gates phoneme-rule safety. A request with no resolved model must not
        // throw its way out of a broadcast.
        _schedules.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule([FillSource.Filler])));

        var config = await _sut.ResolveAsync("sydney", null);

        config.FillOrder.ShouldBe(new[] { FillSource.Filler });
    }
}
