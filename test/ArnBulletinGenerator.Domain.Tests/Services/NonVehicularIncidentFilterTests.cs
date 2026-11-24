using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class NonVehicularIncidentFilterTests
{
    private static TrafficIncident MakeIncident(
        string id = "test-1",
        string description = "Crash on road",
        string location = "Mitchell Fwy",
        Option<string> summary = default,
        Option<string> comment = default) =>
        new(id, description, Severity: 2, location, "accident", LengthMeters: 500, Summary: summary, Comment: comment);

    // ─── PSP dropped ───────────────────────────────────────────────────────

    [Fact]
    public void Filter_DropsPspIncidentInDescription()
    {
        var psp = MakeIncident(id: "psp-1", description: "Mitchell Fwy PSP between Loftus St and Aberdeen St - Upgrades");
        var result = NonVehicularIncidentFilter.Filter([psp]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("psp-1");
        result.Removed[0].Token.ShouldBe("PSP");
    }

    [Fact]
    public void Filter_DropsPspIncidentInComment()
    {
        var psp = MakeIncident(id: "psp-2", description: "Mitchell Fwy between Loftus St and Aberdeen St - Works",
            comment: Option<string>.Some("PSP closed for upgrades"));
        var result = NonVehicularIncidentFilter.Filter([psp]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("psp-2");
    }

    [Fact]
    public void Filter_DropsSharedPathIncidentInSummary()
    {
        var sharedPath = MakeIncident(id: "sp-1", description: "Reid Hwy works",
            summary: Option<string>.Some("Principal Shared Path closed"));
        var result = NonVehicularIncidentFilter.Filter([sharedPath]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("sp-1");
    }

    [Fact]
    public void Filter_DropsFootpathIncident()
    {
        var footpath = MakeIncident(id: "fp-1", description: "Footpath closed near station");
        var result = NonVehicularIncidentFilter.Filter([footpath]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("fp-1");
        result.Removed[0].Token.ShouldBe("footpath");
    }

    [Fact]
    public void Filter_DropsCyclewayIncident()
    {
        var cycleway = MakeIncident(id: "cw-1", description: "Cycleway upgrade between two intersections");
        var result = NonVehicularIncidentFilter.Filter([cycleway]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("cw-1");
        result.Removed[0].Token.ShouldBe("cycleway");
    }

    [Fact]
    public void Filter_DropsBikePathIncident()
    {
        var bikePath = MakeIncident(id: "bp-1", description: "Bike path closed for maintenance");
        var result = NonVehicularIncidentFilter.Filter([bikePath]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("bp-1");
        result.Removed[0].Token.ShouldBe("bike path");
    }

    [Fact]
    public void Filter_DropsPedestrianPathIncident()
    {
        var pedPath = MakeIncident(id: "pp-1", description: "Pedestrian path works near bridge");
        var result = NonVehicularIncidentFilter.Filter([pedPath]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("pp-1");
        result.Removed[0].Token.ShouldBe("pedestrian path");
    }

    [Fact]
    public void Filter_DropsSharedDashPathIncident()
    {
        var sharedDash = MakeIncident(id: "sd-1", description: "Shared-path between stations closed");
        var result = NonVehicularIncidentFilter.Filter([sharedDash]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("sd-1");
        result.Removed[0].Token.ShouldBe("shared-path");
    }

    [Fact]
    public void Filter_DropsFootbridgeClosedIncident()
    {
        var footbridge = MakeIncident(id: "fb-1", description: "Footbridge closed for structural repairs");
        var result = NonVehicularIncidentFilter.Filter([footbridge]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("fb-1");
        result.Removed[0].Token.ShouldBe("footbridge closed");
    }

    [Fact]
    public void Filter_DropsFootSpacePathIncident()
    {
        var footPath = MakeIncident(id: "fpath-1", description: "Foot path upgrade near river");
        var result = NonVehicularIncidentFilter.Filter([footPath]);
        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("fpath-1");
        result.Removed[0].Token.ShouldBe("foot path");
    }

    // ─── Normal crash survives ──────────────────────────────────────────────

    [Fact]
    public void Filter_KeepsNormalCrash()
    {
        var crash = MakeIncident(id: "crash-1", description: "Multi-vehicle crash on Mitchell Fwy");
        var result = NonVehicularIncidentFilter.Filter([crash]);
        result.Kept.Count.ShouldBe(1);
        result.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_KeepsNormalCrashOnSameRoad()
    {
        var crash = MakeIncident(
            id: "crash-1",
            description: "Multi-vehicle crash on Mitchell Fwy between Loftus St and Aberdeen St",
            location: "Mitchell Fwy");

        var result = NonVehicularIncidentFilter.Filter([crash]);

        result.Kept.Count.ShouldBe(1);
        result.Kept[0].Id.ShouldBe("crash-1");
        result.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_KeepsRoadworksIncident()
    {
        var works = MakeIncident(
            id: "rw-1",
            description: "Lane blocked on Reid Hwy due to roadworks",
            location: "Reid Hwy");

        var result = NonVehicularIncidentFilter.Filter([works]);

        result.Kept.Count.ShouldBe(1);
        result.Kept[0].Id.ShouldBe("rw-1");
        result.Removed.ShouldBeEmpty();
    }

    // ─── PSP word-boundary: no false positives ──────────────────────────────

    [Fact]
    public void Filter_DoesNotMatchPspInsideGypsumRd()
    {
        // "Gypsum" contains the substring "PSP" — word boundary must prevent match
        var incident = MakeIncident(
            id: "gyp-1",
            description: "Lane blocked on Gypsum Rd near intersection");

        var result = NonVehicularIncidentFilter.Filter([incident]);

        result.Kept.Count.ShouldBe(1);
        result.Kept[0].Id.ShouldBe("gyp-1");
        result.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_DoesNotMatchPspInsideSuspension()
    {
        // "suspspension" contains "PSP" as substring — word boundary must prevent match
        var incident = MakeIncident(
            id: "susp-1",
            description: "Vehicle with suspspension issues on M1");

        var result = NonVehicularIncidentFilter.Filter([incident]);

        result.Kept.Count.ShouldBe(1);
        result.Kept[0].Id.ShouldBe("susp-1");
        result.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_DoesNotMatchPspInsideDisperse()
    {
        // "disperse" contains "PSP" as substring — word boundary must prevent match
        var incident = MakeIncident(
            id: "disp-1",
            description: "Traffic dispersing on Pacific Hwy");

        var result = NonVehicularIncidentFilter.Filter([incident]);

        result.Kept.Count.ShouldBe(1);
        result.Kept[0].Id.ShouldBe("disp-1");
        result.Removed.ShouldBeEmpty();
    }

    // ─── Case insensitivity ─────────────────────────────────────────────────

    [Fact]
    public void Filter_MatchesPspCaseInsensitively()
    {
        var psp = MakeIncident(
            id: "psp-lower",
            description: "psp closed between two roads");

        var result = NonVehicularIncidentFilter.Filter([psp]);

        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("psp-lower");
        result.Removed[0].Token.ShouldBe("PSP");
    }

    [Fact]
    public void Filter_MatchesSharedPathCaseInsensitively()
    {
        var sharedPath = MakeIncident(
            id: "sp-mixed",
            description: "SHARED-PATH closed for works");

        var result = NonVehicularIncidentFilter.Filter([sharedPath]);

        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(1);
        result.Removed[0].Incident.Id.ShouldBe("sp-mixed");
        result.Removed[0].Token.ShouldBe("shared-path");
    }

    // ─── Mixed set ──────────────────────────────────────────────────────────

    [Fact]
    public void Filter_MixedSet_KeepsVehicularDropsNonVehicular()
    {
        var crash = MakeIncident(id: "crash-1", description: "Multi-car crash on Mitchell Fwy");
        var psp = MakeIncident(id: "psp-1", description: "Mitchell Fwy PSP between Loftus St - Upgrades");
        var works = MakeIncident(id: "rw-1", description: "Lane blocked on Reid Hwy");
        var sharedPath = MakeIncident(id: "sp-1", description: "Shared path closed near river");

        var result = NonVehicularIncidentFilter.Filter([crash, psp, works, sharedPath]);

        result.Kept.Count.ShouldBe(2);
        result.Kept.ShouldAllBe(i => i.Id == "crash-1" || i.Id == "rw-1");
        result.Removed.Count.ShouldBe(2);
        result.Removed.ShouldAllBe(r => r.Incident.Id == "psp-1" || r.Incident.Id == "sp-1");
    }

    // ─── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Filter_EmptyInput_ReturnsEmpty()
    {
        var result = NonVehicularIncidentFilter.Filter([]);
        result.Kept.ShouldBeEmpty();
        result.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_AllNonVehicular_ReturnsEmpty()
    {
        var psp1 = MakeIncident(id: "p1", description: "PSP works on road");
        var psp2 = MakeIncident(id: "p2", description: "Cycleway upgrade");

        var result = NonVehicularIncidentFilter.Filter([psp1, psp2]);

        result.Kept.ShouldBeEmpty();
        result.Removed.Count.ShouldBe(2);
    }

    [Fact]
    public void FindNonVehicularToken_ReturnsNullForNormalIncident()
    {
        var crash = MakeIncident(description: "Multi-vehicle crash on M1 southbound");
        NonVehicularIncidentFilter.FindNonVehicularToken(crash).ShouldBeNull();
    }

    [Fact]
    public void FindNonVehicularToken_ReturnsPspForPspIncident()
    {
        var psp = MakeIncident(description: "PSP closed between Loftus St and Aberdeen St");
        NonVehicularIncidentFilter.FindNonVehicularToken(psp).ShouldBe("PSP");
    }

    [Fact]
    public void FindNonVehicularToken_ReturnsTokenForMatchInComment()
    {
        var incident = MakeIncident(
            description: "Works near station",
            comment: Option<string>.Some("Pedestrian path closed"));

        NonVehicularIncidentFilter.FindNonVehicularToken(incident).ShouldBe("pedestrian path");
    }
}
