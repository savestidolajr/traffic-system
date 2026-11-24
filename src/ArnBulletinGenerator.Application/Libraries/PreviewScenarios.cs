using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>
///     Two embedded, representative canned data sets ("busy" and "quiet") the Style Rules preview
///     runs a draft-pass-only side-by-side generation against. Canned (not live) input makes the
///     comparison controlled and works without HERE credentials.
/// </summary>
internal static class PreviewScenarios
{
    public static BroadcastWithTraffic? Build(string scenario)
    {
        return scenario switch
        {
            "busy" => Busy(),
            "quiet" => Quiet(),
            _ => null
        };
    }

    private static BroadcastWithTraffic Busy()
    {
        var coverage = CoverageArea.Create(-33.8688, 151.2093, 25).Value;
        var corridors = new List<TrafficCorridor>
        {
            new("Warringah Freeway", 1, 1, 2000, 200, "roadClosure", "road closure citybound", [],
                Direction: Option<string>.Some("citybound"),
                Extent: Option<string>.Some("at the ramp to Alfred Street North")),
            new("Victoria Road", 1, 2, 1500, 120, "accident", "crash blocking one lane", [],
                Suburb: Option<string>.Some("Rozelle"), Direction: Option<string>.Some("citybound")),
            new("Barclay Road", 1, 3, 1200, 90, "congestion", "at a standstill", []),
            new("M2 Motorway", 1, 1, 1200, 85, "congestion", "at a standstill", [])
        };

        var request = new BroadcastRequest
        {
            TargetDurationSeconds = 30,
            JobId = "style-preview",
            Location = "Sydney",
            Coverage = coverage,
            ScriptOnly = true
        };

        return request.WithCorridors(corridors) with
        {
            City = Option<string>.Some("Sydney"),
            GoodRunRoads = ["Sydney Gateway", "M8"],
            FillerStatements =
            [
                new FillerStatement("f1",
                    "School zones are back to 40 on school days — watch for the flashing signs morning and afternoon."),
                new FillerStatement("f2", "Keep left unless overtaking on roads signed 80 or above.")
            ]
        };
    }

    private static BroadcastWithTraffic Quiet()
    {
        var coverage = CoverageArea.Create(-32.9283, 151.7817, 25).Value;
        var corridors = new List<TrafficCorridor>
        {
            new("Mitchell Street", 2, 4, 400, 40, "roadClosure", "road closure outbound", [],
                Direction: Option<string>.Some("outbound"))
        };

        var request = new BroadcastRequest
        {
            TargetDurationSeconds = 30,
            JobId = "style-preview",
            Location = "Newcastle",
            Coverage = coverage,
            ScriptOnly = true
        };

        return request.WithCorridors(corridors) with
        {
            City = Option<string>.Some("Newcastle"),
            GoodRunRoads = ["City Road", "Egret Street", "Glebe Road", "George Street"],
            FillerStatements =
            [
                new FillerStatement("f1",
                    "School zones are back to 40 on school days — watch for the flashing signs morning and afternoon."),
                new FillerStatement("f2", "Keep left unless overtaking on roads signed 80 or above."),
                new FillerStatement("f3",
                    "Learner and P1 drivers can't use a phone at all behind the wheel, even hands-free.")
            ]
        };
    }
}
