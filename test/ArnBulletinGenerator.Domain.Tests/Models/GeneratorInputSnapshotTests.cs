using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class GeneratorInputSnapshotTests
{
    private static TrafficCorridor FlowCorridor(string road, int delayMinutes) => new(
        RoadName: road,
        Severity: 2,
        FunctionalClass: 1,
        TotalLengthMeters: 1000,
        ImpactScore: 100,
        PrimaryType: "congestion",
        Description: "congestion",
        Incidents: [],
        FlowDelayMinutes: delayMinutes);

    private static TrafficCorridor IncidentCorridor(string road, string incidentId) => new(
        RoadName: road,
        Severity: 2,
        FunctionalClass: 1,
        TotalLengthMeters: 1000,
        ImpactScore: 100,
        PrimaryType: "construction",
        Description: "roadworks",
        Incidents:
        [
            new TrafficIncident(
                Id: incidentId,
                Description: "construction",
                Severity: 2,
                Location: road,
                Type: "construction",
                LengthMeters: 100,
                FunctionalClass: 1,
                Latitude: 0,
                Longitude: 0)
        ]);

    private static GeneratorInputSnapshot Snapshot(
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<EditedIncidentDto> edited) => new()
    {
        TargetDurationSeconds = 22,
        Location = "sydney",
        Corridors = corridors,
        EditedIncidents = edited
    };

    [Fact]
    public void ToContext_KeepsPureFlowCorridors()
    {
        // The 38-minute Hume congestion corridor has NO incident records; it must reach the
        // writer. The old filter deleted every such corridor on curation resume and replay.
        var snapshot = Snapshot(
            [FlowCorridor("Hume Highway", 38), IncidentCorridor("Botany Road", "i-1")],
            edited: []);

        var context = snapshot.ToContext("job-1");

        context.Corridors.Select(c => c.RoadName)
            .ShouldBe(["Botany Road", "Hume Highway"], ignoreOrder: true);
    }

    [Fact]
    public void ToContext_DropsCorridorWhoseIncidentsWereAllExcluded()
    {
        var snapshot = Snapshot(
            [IncidentCorridor("Botany Road", "i-1"), FlowCorridor("Hume Highway", 38)],
            edited: [new EditedIncidentDto("i-1", Included: false, Priority: 0)]);

        var context = snapshot.ToContext("job-1");

        context.Corridors.Select(c => c.RoadName).ShouldBe(["Hume Highway"]);
    }

    [Fact]
    public void ToContext_OrdersOperatorPrioritisedIncidentCorridorsFirst()
    {
        var snapshot = Snapshot(
            [FlowCorridor("Hume Highway", 38), IncidentCorridor("Botany Road", "i-1")],
            edited: [new EditedIncidentDto("i-1", Included: true, Priority: 1)]);

        var context = snapshot.ToContext("job-1");

        context.Corridors[0].RoadName.ShouldBe("Botany Road");
        context.Corridors[1].RoadName.ShouldBe("Hume Highway");
    }
}
