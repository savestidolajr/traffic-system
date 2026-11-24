using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class HazardBlockageDetectorTests
{
    private static BroadcastWithTraffic Ctx(IReadOnlyList<TrafficCorridor> corridors) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors,
            GoodRunRoads = []
        };

    private static TrafficCorridor Corridor(string roadName, IReadOnlyList<TrafficIncident> incidents) =>
        new(roadName, 3, 1, 0, 0, "accident", string.Empty, incidents);

    private static TrafficIncident Incident(string id, string description) =>
        new(id, description, 3, "location", "accident");

    // ── POSITIVE: fabricated lane blockage detected ───────────────────────────────

    [Fact]
    public void Flags_fabricated_lane_blockage_on_hazard_road()
    {
        // Debris is a hazard keyword; the data does NOT report a lane block — yet the script
        // claims centre lanes are blocked.  The detector must flag this fabrication.
        var ctx = Ctx([
            Corridor("Tonkin Highway", [
                Incident("id1", "Debris on Tonkin Hwy, all lanes affected")
            ])
        ]);
        const string script = "Debris is blocking the centre lanes on the Tonkin Highway.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeTrue();
    }

    // ── NEGATIVE: data already reports a lane block → script is faithful ──────────

    [Fact]
    public void Does_not_flag_when_data_shows_lane_block()
    {
        // The incident description itself says "lanes blocked" — the script is faithfully
        // reflecting real lane-closure data, not inventing one.
        var ctx = Ctx([
            Corridor("Tonkin Highway", [
                Incident("id1", "Debris on Tonkin Hwy, lanes blocked")
            ])
        ]);
        const string script = "Debris is blocking the centre lanes on the Tonkin Highway.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_when_no_hazard_in_data()
    {
        // "accident" is not a hazard keyword (debris/object/lost load), so the detector skips
        // this corridor entirely — no hazard means no fabrication check needed.
        var ctx = Ctx([
            Corridor("Tonkin Highway", [
                Incident("id1", "Multi-vehicle accident on Tonkin Hwy")
            ])
        ]);
        const string script = "There is an accident blocking lanes on the Tonkin Highway.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeFalse();
    }

    [Fact]
    public void Passes_clean_script_with_hazard()
    {
        // Hazard keyword IS present in the data, but the script only says "take care through
        // the area" — it does NOT claim any lane is blocked.  No fabrication, no flag.
        var ctx = Ctx([
            Corridor("Tonkin Highway", [
                Incident("id1", "Debris on Tonkin Hwy, all lanes affected")
            ])
        ]);
        const string script = "There is debris on the Tonkin Highway, take care through the area.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeFalse();
    }

    [Fact]
    public void Flags_object_on_road_hazard()
    {
        // "object on the road" is a hazard keyword.  Data has no lane-block, but the script
        // says lanes are blocked → fabrication detected.
        var ctx = Ctx([
            Corridor("M4", [
                Incident("id1", "Object on the road on M4")
            ])
        ]);
        const string script = "Lanes are blocked on the M4.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeTrue();
    }

    [Fact]
    public void Flags_lost_load_hazard()
    {
        // "lost load" is a hazard keyword.  Data has no lane-block, but the script says a
        // lane is blocked → fabrication detected.
        var ctx = Ctx([
            Corridor("Pacific Highway", [
                Incident("id1", "Lost load on Pacific Highway")
            ])
        ]);
        const string script = "A lane is blocked on the Pacific Highway.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeTrue();
    }

    [Fact]
    public void Does_not_flag_road_not_in_context()
    {
        // The script mentions "Western Distributor" but no corridor exists for that road —
        // the detector has nothing to match against, so no flag.
        var ctx = Ctx([
            Corridor("Tonkin Highway", [
                Incident("id1", "Debris on Tonkin Hwy")
            ])
        ]);
        const string script = "Blocking lanes on the Western Distributor, take care.";

        HazardBlockageDetector.Detect(script, ctx).HasHazardBlockage.ShouldBeFalse();
    }
}
