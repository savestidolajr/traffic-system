using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class IncidentDiversityFilterTests
{
    private static TrafficIncident MakeIncident(string id, int severity, int fc, double length = 100)
    {
        return new TrafficIncident(id, $"Incident {id}", severity, "Location", "accident", length, fc);
    }

    [Fact]
    public void EnsureDiversity_FewerThanMax_ReturnsAll()
    {
        var incidents = new[]
        {
            MakeIncident("1", 1, 1),
            MakeIncident("2", 2, 3),
            MakeIncident("3", 3, 5)
        };

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);
        result.Count().ShouldBe(3);
    }

    [Fact]
    public void EnsureDiversity_GuaranteesMinPerTier()
    {
        // 6 motorway (FC 1-2), 3 arterial (FC 3), 3 local (FC 4+)
        var incidents = new List<TrafficIncident>();
        for (int i = 0; i < 6; i++) incidents.Add(MakeIncident($"m{i}", 1, 1, 1000 - i));
        for (int i = 0; i < 3; i++) incidents.Add(MakeIncident($"a{i}", 2, 3, 500 - i));
        for (int i = 0; i < 3; i++) incidents.Add(MakeIncident($"l{i}", 3, 4, 200 - i));

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);

        result.Count().ShouldBe(9);

        var motorway = result.Where(i => i.FunctionalClass <= 2).ToList();
        var arterial = result.Where(i => i.FunctionalClass == 3).ToList();
        var local = result.Where(i => i.FunctionalClass >= 4).ToList();

        motorway.Count().ShouldBeGreaterThanOrEqualTo(3);
        arterial.Count().ShouldBeGreaterThanOrEqualTo(3);
        local.Count().ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void EnsureDiversity_FillsRemainingBySeverity()
    {
        // 5 motorway, 1 arterial, 1 local — not enough in each tier
        var incidents = new List<TrafficIncident>
        {
            MakeIncident("m1", 1, 1, 1000),
            MakeIncident("m2", 1, 2, 900),
            MakeIncident("m3", 2, 1, 800),
            MakeIncident("m4", 2, 2, 700),
            MakeIncident("m5", 3, 1, 600),
            MakeIncident("a1", 1, 3, 500),
            MakeIncident("l1", 2, 4, 400)
        };

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 5);

        result.Count().ShouldBe(5);
        result.Select(i => i.Id).ShouldBeUnique();
    }

    [Fact]
    public void EnsureDiversity_NoDuplicates()
    {
        var incidents = new List<TrafficIncident>();
        for (int i = 0; i < 4; i++) incidents.Add(MakeIncident($"m{i}", 1, 1));
        for (int i = 0; i < 4; i++) incidents.Add(MakeIncident($"a{i}", 2, 3));
        for (int i = 0; i < 4; i++) incidents.Add(MakeIncident($"l{i}", 3, 5));

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);

        result.Select(i => i.Id).ShouldBeUnique();
        result.Count().ShouldBe(9);
    }

    [Fact]
    public void EnsureDiversity_AllSameTier_StillReturnsMaxCount()
    {
        // All motorway — other tiers empty
        var incidents = Enumerable.Range(0, 12)
            .Select(i => MakeIncident($"m{i}", i % 3 + 1, 1))
            .ToList();

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);

        result.Count().ShouldBe(9);
        result.Select(i => i.Id).ShouldBeUnique();
    }

    [Fact]
    public void EnsureDiversity_EmptyInput_ReturnsEmpty()
    {
        var result = IncidentDiversityFilter.EnsureDiversity([], 9);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void EnsureDiversity_ExactlyMaxCount_ReturnsAll()
    {
        var incidents = Enumerable.Range(0, 9)
            .Select(i => MakeIncident($"x{i}", 2, i % 3 + 1))
            .ToList();

        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);
        result.Count().ShouldBe(9);
    }

    [Fact]
    public void EnsureDiversity_SortedBySeverityThenFcThenLength()
    {
        var incidents = new[]
        {
            MakeIncident("low", 3, 5),
            MakeIncident("high", 1, 1, 1000),
            MakeIncident("mid", 2, 3, 500)
        };

        // Fewer than max → returns all, but sorted
        var result = IncidentDiversityFilter.EnsureDiversity(incidents, 9);

        result[0].Id.ShouldBe("high");
        result[1].Id.ShouldBe("mid");
        result[2].Id.ShouldBe("low");
    }
}
