using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

/// <summary>
///     Renders the evidence payload the draft agent actually receives, so a reviewer can read it
///     rather than infer it from field lists. Built from the live Sydney job of 2026-08-20, whose
///     Mitchell Road item reached the writer as a bare location with nothing to say: minor tier,
///     so the emitter condensed away its incident, extent, lanes and advice.
/// </summary>
public class DraftPayloadShapeTests(ITestOutputHelper output)
{
    private static BroadcastWithTraffic SydneyContext()
    {
        var pittwater = new TrafficIncident(
            "here-1", "Closed", 4, "Pittwater Road", "construction",
            Direction: Option<string>.Some("westbound"));
        var mitchell = new TrafficIncident(
            "here-2", "Left lane blocked", 1, "Mitchell Road", "construction");

        var corridors = new[]
        {
            new TrafficCorridor(
                "Pittwater Road", 4, 3, 1240, 40, "construction",
                "Road closure westbound", [pittwater],
                Suburb: Option<string>.Some("North Manly"),
                Direction: Option<string>.Some("westbound"),
                Extent: Option<string>.Some("Kentwell Road to Oliver Street"),
                FlowDelayMinutes: Option<int>.Some(22),
                OfficialEvidences:
                [
                    new OfficialEvidence(
                        "livetraffic-nsw", "SCHEDULED ROADWORK", IsIncidentClass: false,
                        AdviceTags: ["Allow extra travel time", "Use an alternative route"],
                        SpeedLimitKph: 40, LanesClosed: 2, LanesTotal: 3)
                ]),

            // Minor tier — the item that used to arrive empty.
            new TrafficCorridor(
                "Mitchell Road", 1, 5, 320, 5, "construction",
                "roadworks between Sydney Park Road and Maddox Street", [mitchell],
                Suburb: Option<string>.Some("Alexandria"),
                Direction: Option<string>.Some("northbound"),
                OfficialEvidences:
                [
                    new OfficialEvidence(
                        "livetraffic-nsw", "SCHEDULED ROADWORK", IsIncidentClass: false,
                        AdviceTags: ["Use an alternative route"], LanesClosed: 1, LanesTotal: 2)
                ])
        };

        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = 22,
            JobId = "payload-shape",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = corridors,
            Incidents = [pittwater, mitchell],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };
    }

    [Fact]
    public void DraftPayload_CarriesVoiceableFactsAndNoClassifierLabels()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(SydneyContext());

        string rendered = PromptLibrary.RenderEvidenceUnits(units);
        output.WriteLine("=== payload as the draft agent receives it ===");
        output.WriteLine(rendered);
        output.WriteLine($"=== {rendered.Length} chars, {units.Count} facts, "
                         + $"'corridor' appears {CountOccurrences(rendered, "corridor")}x ===");

        var fields = units.Select(u => u.Field).ToHashSet(StringComparer.Ordinal);

        // Never handed to a writer: our own classifier outputs.
        fields.ShouldNotContain("magnitudeTier");
        fields.ShouldNotContain("voiceability");

        // The minor-tier item now carries a story instead of a bare location.
        var mitchell = units.Where(u => u.SourceKey == "corridor:Mitchell Road").ToList();
        mitchell.ShouldContain(u => u.Field == "detail");
        mitchell.ShouldContain(u => u.Field == "direction");
        mitchell.ShouldContain(u => u.Field == "lanesClosed");
        mitchell.ShouldContain(u => u.Field == "advice");

        // The rendering must not reprint the boilerplate the flat form repeated on every line, and
        // must not name our own bucket word: the live cairns draft carried "corridor" 88 times in a
        // 5,915-char prompt while MetaLeakDetector was paid to catch the writer echoing it.
        rendered.ShouldNotContain("source=");
        rendered.ShouldNotContain("; field=");
        rendered.ShouldNotContain("corridor ");

        // Headings name the road, so a road with several spots reads as ONE item.
        rendered.ShouldContain("Pittwater Road, North Manly");
        rendered.ShouldContain("Mitchell Road, Alexandria");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
