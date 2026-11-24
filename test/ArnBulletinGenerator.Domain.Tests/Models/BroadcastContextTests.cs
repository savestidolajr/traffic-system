using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class BroadcastContextTests
{
    private static BroadcastRequest CreateRequest()
    {
        return new BroadcastRequest
        {
            TargetDurationSeconds = 60,
            JobId = "job-1",
            Location = "Sydney",
            Coverage = CoverageArea.Create(-33.87, 151.21, 25).Value
        };
    }

    private static TrafficIncident CreateIncident(string id = "inc-1")
    {
        return new TrafficIncident(
            id,
            "Accident on M4",
            2,
            "M4 Westbound",
            "accident");
    }

    private static BroadcastWithTraffic CreateWithTraffic(
        BroadcastRequest request, string traffic, IReadOnlyList<TrafficIncident> incidents)
    {
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = request.TargetDurationSeconds,
            JobId = request.JobId,
            Location = request.Location,
            ScriptOnly = request.ScriptOnly,
            MarketId = request.MarketId,
            TimezoneId = request.TimezoneId,
            VoiceId = request.VoiceId,
            Model = request.Model,
            VoiceSettings = request.VoiceSettings,
            RejectionFeedback = request.RejectionFeedback,
            PreviousScript = request.PreviousScript,
            PreviousJobId = request.PreviousJobId,
            Traffic = traffic,
            Incidents = incidents
        };
    }

    [Fact]
    public void HasTraffic_OnBroadcastRequest_ReturnsFalse()
    {
        var request = CreateRequest();

        request.HasTraffic.ShouldBeFalse();
    }

    [Fact]
    public void HasTraffic_OnBroadcastWithTraffic_ReturnsTrue()
    {
        var withTraffic = CreateWithTraffic(CreateRequest(), "traffic data", [CreateIncident()]);

        withTraffic.HasTraffic.ShouldBeTrue();
    }

    [Fact]
    public void WithTraffic_CopiesAllFieldsAndAddsTraffic()
    {
        var request = CreateRequest();
        var incidents = new[] { CreateIncident() };

        var result = CreateWithTraffic(request, "heavy traffic", incidents);

        result.JobId.ShouldBe("job-1");
        result.Location.ShouldBe("Sydney");
        result.TargetDurationSeconds.ShouldBe(60);
        result.Traffic.ShouldBe("heavy traffic");
        result.Incidents.Count().ShouldBe(1);
        result.Corridors.ShouldBeEmpty();
    }

    [Fact]
    public void WithCorridors_OnRequest_CreatesWithTrafficFromCorridors()
    {
        var request = CreateRequest();
        var corridor = new TrafficCorridor(
            "M4",
            2,
            1,
            5000,
            80,
            "accident",
            "Accident on M4",
            [CreateIncident()]);

        var result = request.WithCorridors([corridor]);

        result.Incidents.Count().ShouldBe(1);
        result.Corridors.Count().ShouldBe(1);
        result.Traffic.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void CastToBroadcastWithTraffic_OnBroadcastWithTraffic_Succeeds()
    {
        var withTraffic = CreateWithTraffic(CreateRequest(), "data", []);

        var cast = withTraffic as BroadcastWithTraffic;

        cast.ShouldNotBeNull();
        cast.ShouldBeSameAs(withTraffic);
    }

    [Fact]
    public void CastToBroadcastWithTraffic_OnBroadcastRequest_ReturnsNull()
    {
        BroadcastContextBase request = CreateRequest();

        var cast = request as BroadcastWithTraffic;

        cast.ShouldBeNull();
    }

    [Fact]
    public void WithCorridors_OnBroadcastWithTraffic_RebuildsTraffic()
    {
        var initial = CreateWithTraffic(CreateRequest(), "old", []);
        var corridor = new TrafficCorridor(
            "Pacific Hwy",
            3,
            2,
            2000,
            50,
            "congestion",
            "Slow traffic",
            [CreateIncident("inc-2")]);

        var rebuilt = initial.WithCorridors([corridor]);

        rebuilt.Corridors.Count().ShouldBe(1);
        rebuilt.Incidents.Count().ShouldBe(1);
        rebuilt.Incidents[0].Id.ShouldBe("inc-2");
        rebuilt.Traffic.ShouldNotBe("old");
    }
}
