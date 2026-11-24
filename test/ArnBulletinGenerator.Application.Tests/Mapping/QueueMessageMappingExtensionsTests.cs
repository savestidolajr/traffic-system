using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class QueueMessageMappingExtensionsTests
{
    // =====================================================================
    // ToBroadcastRequest
    // =====================================================================

    [Fact]
    public void ToBroadcastRequest_MapsAllFields()
    {
        var msg = new BroadcastQueueMessage(
            "job-1",
            "mkt-1",
            "Sydney",
            30,
            -33.8,
            151.2,
            25,
            "v-1",
            "eleven_v3",
            TimezoneId: "Australia/Sydney",
            ScriptOnly: true);

        var req = msg.ToBroadcastRequest().Value;

        req.JobId.ShouldBe("job-1");
        req.MarketId.HasValue.ShouldBeTrue(); // MarketId mapped from Option.OfWhitespace
        req.Location.ShouldBe("Sydney");
        req.TargetDurationSeconds.ShouldBe(30);
        req.Coverage.Latitude.ShouldBe(-33.8);
        req.Coverage.Longitude.ShouldBe(151.2);
        req.Coverage.RadiusKm.ShouldBe(25);
        req.VoiceId.Value.ShouldBe("v-1");
        req.TimezoneId.Value.ShouldBe("Australia/Sydney");
        req.Model.Value.ShouldBe("eleven_v3");
    }

    [Fact]
    public void ToBroadcastRequest_PreservesSourceSpecificCoverageOverrides()
    {
        var coverage = CoverageArea.Create(-16.92, 145.77, 80).Value with
        {
            WeatherCoverage = CoverageArea.Create(-16.91, 145.75, 1).Value,
            FuelCoverage = CoverageArea.Create(-16.92, 145.77, 12).Value
        };
        var msg = new BroadcastQueueMessage(
            "job-coverage",
            "cairns-4ca",
            "Cairns",
            30,
            coverage.Latitude,
            coverage.Longitude,
            coverage.RadiusKm,
            Model: "eleven_v3",
            Coverage: coverage);

        var request = msg.ToBroadcastRequest().Value;

        request.Coverage.WeatherCoverage!.Longitude.ShouldBe(145.75);
        request.Coverage.FuelCoverage!.RadiusKm.ShouldBe(12);
    }

    [Fact]
    public void ToBroadcastRequest_NullVoiceId_MapsToNone()
    {
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1, Model: "eleven_v3");

        var req = msg.ToBroadcastRequest().Value;

        req.VoiceId.HasValue.ShouldBeFalse();
        req.Model.Value.ShouldBe("eleven_v3");
        req.VoiceSettings.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastRequest_MissingResolvedModel_FailsClearly()
    {
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1);

        var result = msg.ToBroadcastRequest();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("resolved TTS model");
    }

    [Fact]
    public void ToBroadcastRequest_WithVoiceSettings_MapsSome()
    {
        var vs = VoiceSettings.Default;
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1,
            Model: "eleven_v3", VoiceSettings: vs);

        var req = msg.ToBroadcastRequest().Value;

        req.VoiceSettings.HasValue.ShouldBeTrue();
        req.VoiceSettings.Value.Speed.ShouldBe(vs.Speed);
    }

    // =====================================================================
    // ToBroadcastQueueMessage
    // =====================================================================

    [Fact]
    public void ToBroadcastQueueMessage_UsesResolvedJobVoiceConfiguration()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "Melbourne",
            TargetDurationSeconds = 25,
            Coverage = CoverageArea.Create(-37.8, 144.9, 20).Value,
            ScriptOnly = false
        };
        var job = Job.Create("job-99", "3AW", "Melbourne", 25, DateTime.UtcNow).Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "elevenlabs",
            "v-1",
            "Dolly Traffic",
            "eleven_v3",
            Option<VoiceSettings>.Some(VoiceSettings.Default),
            7));

        var msg = validated.ToBroadcastQueueMessage(job, "melbourne", AdHocStationConfig.None);

        msg.JobId.ShouldBe("job-99");
        msg.MarketId.ShouldBe("melbourne");
        msg.Location.ShouldBe("Melbourne");
        msg.TargetDurationSeconds.ShouldBe(25);
        msg.Latitude.ShouldBe(-37.8);
        msg.Longitude.ShouldBe(144.9);
        msg.RadiusKm.ShouldBe(20);
        msg.VoiceId.ShouldBe("v-1");
        msg.VoiceSettings.ShouldNotBeNull();
        msg.Model.ShouldBe("eleven_v3");
        msg.PaceBoostWpm.ShouldBe(7);
        msg.ScriptOnly.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastQueueMessage_NoneVoiceId_MapsToNull()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "L",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(0, 0, 1).Value,
            VoiceId = Option<string>.None
        };
        var job = Job.Create("j", "s", "m", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "m", AdHocStationConfig.None);

        msg.VoiceId.ShouldBeNull();
        msg.VoiceSettings.ShouldBeNull();
    }

    // =====================================================================
    // MapVoiceSettings
    // =====================================================================

    [Fact]
    public void MapVoiceSettings_Null_ReturnsNone()
    {
        var result = QueueMessageMappingExtensions.MapVoiceSettings(null);

        result.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void MapVoiceSettings_NotNull_ReturnsSome()
    {
        var result = QueueMessageMappingExtensions.MapVoiceSettings(VoiceSettings.Default);

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe(VoiceSettings.Default);
    }

    // =====================================================================
    // FillOrder (per-station fill priority)
    // =====================================================================

    [Fact]
    public void ToBroadcastRequest_NullFillOrder_DefaultsToWeatherFuelMinorTrafficFiller()
    {
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1, Model: "eleven_v3");

        var req = msg.ToBroadcastRequest().Value;

        req.FillOrder.ShouldBe(new[] { FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler });
    }

    [Fact]
    public void ToBroadcastRequest_EmptyFillOrder_DefaultsToWeatherFuelMinorTrafficFiller()
    {
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1,
            Model: "eleven_v3", FillOrder: []);

        var req = msg.ToBroadcastRequest().Value;

        req.FillOrder.ShouldBe(new[] { FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler });
    }

    [Fact]
    public void ToBroadcastRequest_ExplicitFillOrder_MappedExactly()
    {
        // An explicit single-source order means exactly that source — the default is NOT merged in.
        var msg = new BroadcastQueueMessage("j", "m", "l", 30, 0, 0, 1,
            Model: "eleven_v3", FillOrder: [FillSource.Fuel]);

        var req = msg.ToBroadcastRequest().Value;

        req.FillOrder.ShouldBe(new[] { FillSource.Fuel });
    }

    [Fact]
    public void ToBroadcastQueueMessage_FillOrder_PassedThrough()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "L",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(0, 0, 1).Value,
            FillOrder = [FillSource.Filler, FillSource.Weather]
        };
        var job = Job.Create("j", "s", "m", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "m", AdHocStationConfig.None);

        msg.FillOrder.ShouldBe(new[] { FillSource.Filler, FillSource.Weather });
    }

    [Fact]
    public void ToBroadcastQueueMessage_NullFillOrder_StaysNull()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "L",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(0, 0, 1).Value
        };
        var job = Job.Create("j", "s", "m", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "m", AdHocStationConfig.None);

        msg.FillOrder.ShouldBeNull();
    }

    [Fact]
    public void FillOrder_RoundTrip_ValidatedThroughQueueToBroadcastRequest_PreservesExplicitOrder()
    {
        // The full wire path a station's configured priority travels: validated request →
        // queue message → broadcast request. [Fuel] survives verbatim, never widened to the default.
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "L",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(0, 0, 1).Value,
            FillOrder = [FillSource.Fuel],
            VoiceModel = Option<string>.Some("eleven_v3"),
        };
        var job = Job.Create("j", "s", "m", 30, DateTime.UtcNow).Value;

        var req = validated.ToBroadcastQueueMessage(job, "m", AdHocStationConfig.None).ToBroadcastRequest().Value;

        req.FillOrder.ShouldBe(new[] { FillSource.Fuel });
    }
    [Fact]
    public void ToBroadcastQueueMessage_MarketId_IndependentOfOtherFields()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "Brisbane",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(0, 0, 1).Value,
            FillOrder = [FillSource.Weather, FillSource.Filler]
        };
        var job = Job.Create("j", "s", "m", 30, DateTime.UtcNow).Value;

        var msgA = validated.ToBroadcastQueueMessage(job, "brisbane", AdHocStationConfig.None);
        var msgB = validated.ToBroadcastQueueMessage(job, "perth", AdHocStationConfig.None);

        // marketId changed …
        msgA.MarketId.ShouldBe("brisbane");
        msgB.MarketId.ShouldBe("perth");

        // … nothing else should differ
        msgA.Location.ShouldBe(msgB.Location);
        msgA.JobId.ShouldBe(msgB.JobId);
        msgA.TargetDurationSeconds.ShouldBe(msgB.TargetDurationSeconds);
        msgA.Latitude.ShouldBe(msgB.Latitude);
        msgA.Longitude.ShouldBe(msgB.Longitude);
        msgA.RadiusKm.ShouldBe(msgB.RadiusKm);
        msgA.FillOrder.ShouldBe(msgB.FillOrder);
        msgA.ScriptOnly.ShouldBe(msgB.ScriptOnly);
    }

    // =====================================================================
    // Ad-hoc station configuration (2026-08-10 UI audit)
    //
    // A manual "Trigger now" for a configured station used to queue with NO
    // delivery style, NO pronunciation dictionary, NO bookends and the
    // platform-default fill order, so it did not sound like the station the
    // operator had just selected. These lock the inheritance.
    // =====================================================================

    private static AdHocStationConfig ConfiguredStation() => new()
    {
        DeliveryStyle = new DeliveryStylePolicy("brisk", ["[brightly]"], 2),
        DeliveryStyleId = "ds-623f49e7",
        DeliveryStyleVersion = 2,
        PronunciationLocator = new PronunciationLocator("pd-sydney", "v11"),
        GlobalPronunciationLocator = new PronunciationLocator("pd-global", "v3"),
        Bookends = new ResolvedBookends(
            "Looking over Sydney today, here's your traffic.",
            "That's KIIS traffic and weather together.", "bk-1", 3),
        FillOrder = [FillSource.Fuel, FillSource.Weather]
    };

    [Fact]
    public void ToBroadcastQueueMessage_CarriesTheStationsSpeechConfiguration()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "KIIS 90s",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 39).Value
        };
        var job = Job.Create("j", "KIIS 90s", "sydney", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "sydney", ConfiguredStation());

        msg.DeliveryStyleId.ShouldBe("ds-623f49e7");
        msg.DeliveryStyleVersion.ShouldBe(2);
        msg.DeliveryStyle.ShouldNotBeNull();
        msg.PronunciationLocator!.DictionaryId.ShouldBe("pd-sydney");
        msg.GlobalPronunciationLocator!.DictionaryId.ShouldBe("pd-global");
        msg.Bookends!.Id.ShouldBe("bk-1");
        msg.FillOrder.ShouldBe(new[] { FillSource.Fuel, FillSource.Weather });
    }

    [Fact]
    public void ToBroadcastQueueMessage_RequestFillOrderWins_OverTheStations()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "KIIS 90s",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 39).Value,
            FillOrder = [FillSource.Filler]
        };
        var job = Job.Create("j", "KIIS 90s", "sydney", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "sydney", ConfiguredStation());

        msg.FillOrder.ShouldBe(new[] { FillSource.Filler });
    }

    [Fact]
    public void ToBroadcastQueueMessage_UnconfiguredLocation_QueuesPlatformDefaults()
    {
        var validated = new ValidatedOrchestrationRequestDto
        {
            Location = "Sydnye",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 39).Value
        };
        var job = Job.Create("j", "s", "sydnye", 30, DateTime.UtcNow).Value;

        var msg = validated.ToBroadcastQueueMessage(job, "sydnye", AdHocStationConfig.None);

        msg.DeliveryStyle.ShouldBeNull();
        msg.DeliveryStyleId.ShouldBeNull();
        msg.PronunciationLocator.ShouldBeNull();
        msg.GlobalPronunciationLocator.ShouldBeNull();
        msg.Bookends.ShouldBeNull();
        msg.FillOrder.ShouldBeNull();
    }
}
