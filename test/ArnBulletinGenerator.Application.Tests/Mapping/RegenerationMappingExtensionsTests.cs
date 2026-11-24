using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class RegenerationMappingExtensionsTests
{
    [Fact]
    public void ToBroadcastWithTraffic_MapsAllFields()
    {
        var msg = new RegenerationQueueMessage(
            "job-regen",
            "Sydney",
            30,
            "Traffic narrative",
            new List<RegenerationIncident>
            {
                new("i-1", "Crash on M4", 2, "M4", "accident", 500, 1, -33.8, 151.2),
                new("i-2", "Roadwork", 4, "Parramatta Rd", "construction", 200, 3)
            },
            "v-1",
            "eleven_turbo",
            VoiceSettings.Default,
            PaceBoostWpm: 25,
            RejectionFeedback: "make it shorter",
            PreviousScript: "old script text");

        var result = msg.ToBroadcastWithTraffic();

        result.JobId.ShouldBe("job-regen");
        result.Location.ShouldBe("Sydney");
        result.TargetDurationSeconds.ShouldBe(30);
        result.Traffic.ShouldBe("Traffic narrative");
        result.VoiceId.Value.ShouldBe("v-1");
        result.Model.Value.ShouldBe("eleven_turbo");
        result.VoiceSettings.HasValue.ShouldBeTrue();
        result.PaceBoostWpm.ShouldBe(25);
        result.RejectionFeedback.Value.ShouldBe("make it shorter");
        result.PreviousScript.Value.ShouldBe("old script text");
    }

    [Fact]
    public void ToBroadcastWithTraffic_MapsIncidents_WithOptionCoordinates()
    {
        var msg = new RegenerationQueueMessage(
            "j", "L", 30, "T",
            new List<RegenerationIncident>
            {
                new("i-1", "Desc", 1, "Loc", "type", 100, 2, -33.0, 151.0),
                new("i-2", "Desc2", 2, "Loc2", "type2") // no lat/lon
            });

        var result = msg.ToBroadcastWithTraffic();

        result.Incidents.Count().ShouldBe(2);

        result.Incidents[0].Id.ShouldBe("i-1");
        result.Incidents[0].Latitude.HasValue.ShouldBeTrue();
        result.Incidents[0].Latitude.Value.ShouldBe(-33.0);
        result.Incidents[0].Longitude.Value.ShouldBe(151.0);

        result.Incidents[1].Latitude.HasValue.ShouldBeFalse();
        result.Incidents[1].Longitude.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastWithTraffic_NullOptionals_MapToNone()
    {
        var msg = new RegenerationQueueMessage("j", "L", 30, "T", new List<RegenerationIncident>());

        var result = msg.ToBroadcastWithTraffic();

        result.VoiceId.HasValue.ShouldBeFalse();
        result.Model.HasValue.ShouldBeFalse();
        result.VoiceSettings.HasValue.ShouldBeFalse();
        result.RejectionFeedback.HasValue.ShouldBeFalse();
        result.PreviousScript.HasValue.ShouldBeFalse();
        result.PronunciationStack.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ToBroadcastWithTraffic_MapsExactPronunciationLocator()
    {
        var locator = new PronunciationLocator("dictionary-original", "version-original");
        var msg = new RegenerationQueueMessage(
            "j-pron", "L", 30, "T", new List<RegenerationIncident>(),
            PronunciationLocator: locator);

        var result = msg.ToBroadcastWithTraffic();

        result.PronunciationStack.Station.Value.ShouldBe(locator);
        result.PronunciationStack.Global.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastWithTraffic_StacksStationBeforeGlobal()
    {
        // Order is precedence: the first locator wins a word both layers define, so the station
        // layer must reach the provider first (verified live 2026-07-31).
        var station = new PronunciationLocator("dict-station", "ver-station");
        var global = new PronunciationLocator("dict-global", "ver-global");
        var msg = new RegenerationQueueMessage(
            "j-stack", "L", 30, "T", new List<RegenerationIncident>(),
            PronunciationLocator: station, GlobalPronunciationLocator: global);

        var result = msg.ToBroadcastWithTraffic();

        result.PronunciationStack.Locators.ShouldBe([station, global]);
    }

    [Fact]
    public void ToBroadcastWithTraffic_WhitespaceVoiceId_MapsToNone()
    {
        var msg = new RegenerationQueueMessage(
            "j", "L", 30, "T", new List<RegenerationIncident>(),
            "   ");

        var result = msg.ToBroadcastWithTraffic();

        result.VoiceId.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastWithTraffic_MapsExcludedIncidentsAndRevoice()
    {
        var msg = new RegenerationQueueMessage(
            "j", "L", 30, "T", new List<RegenerationIncident>(),
            ExcludedIncidentKeys: ["inc-3", "inc-7"],
            RevoiceOnly: true);

        var result = msg.ToBroadcastWithTraffic();

        result.ExcludedIncidentKeys.HasValue.ShouldBeTrue();
        result.ExcludedIncidentKeys.Value.ShouldBe(["inc-3", "inc-7"]);
        result.RevoiceOnly.ShouldBeTrue();
    }

    [Fact]
    public void ToBroadcastWithTraffic_EmptyExcluded_MapsToNoneAndNoRevoice()
    {
        var msg = new RegenerationQueueMessage("j", "L", 30, "T", new List<RegenerationIncident>());

        var result = msg.ToBroadcastWithTraffic();

        result.ExcludedIncidentKeys.HasValue.ShouldBeFalse();
        result.RevoiceOnly.ShouldBeFalse();
    }

    [Fact]
    public void ToBroadcastWithTraffic_IsAlwaysOperatorAttended()
    {
        // The regeneration queue has exactly one producer: an operator's regenerate action. The
        // flag gates HandleUnairableAsync's early cover — an attended unairable outcome must mark
        // Failed instead of publishing the cart out from under the operator (prod 2026-08-18).
        var msg = new RegenerationQueueMessage("j", "L", 30, "T", new List<RegenerationIncident>());

        msg.ToBroadcastWithTraffic().OperatorAttended.ShouldBeTrue();
    }
}
