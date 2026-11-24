using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.Tests.Fixtures;

/// <summary>
///     Evidence sets taken from real dev bulletins, so the workflow is exercised against the shapes
///     that actually broke the previous pipeline rather than against tidy invented data.
/// </summary>
public static class EvidenceFixtures
{
    /// <summary>
    ///     Adelaide, 2026-08-26. Carries the corridor that produced the WRONG_LOCATION false positive:
    ///     one event holding BOTH a closure detail and two location points, which the old reviewer
    ///     could not tell were compositionally joinable.
    /// </summary>
    public static WorkflowRequest Adelaide30S() => new(
        JobId: "fixture-adelaide-30s",
        Market: "adelaide",
        Station: "KIIS 102.3",
        Location: "Adelaide",
        TargetSeconds: 30.0,
        Evidence:
        [
            new EvidenceUnit("r9:suburb", "corridor:South Road#1", "suburb", "Torrensville"),
            new EvidenceUnit("r9:direction", "corridor:South Road#1", "direction", "southbound"),
            new EvidenceUnit("r9:delayMinutes", "corridor:South Road#1", "delayMinutes", "11"),
            new EvidenceUnit("r9:location:0", "corridor:South Road#1", "location", "At Sir Donald Bradman Drive"),
            new EvidenceUnit("r9:location:1", "corridor:South Road#1", "location", "At Henley Beach Road"),
            new EvidenceUnit("r9:detail", "corridor:South Road#1", "detail", "road closure southbound"),
            new EvidenceUnit("r9:detail:1", "corridor:South Road#1", "detail", "closure spans the South Road section"),

            new EvidenceUnit("r4:suburb", "corridor:Port Wakefield Road#1", "suburb", "Virginia"),
            new EvidenceUnit("r4:delayMinutes", "corridor:Port Wakefield Road#1", "delayMinutes", "6"),
            new EvidenceUnit("r4:incident:0", "corridor:Port Wakefield Road#1", "incident", "roadworks"),

            new EvidenceUnit("r7:suburb", "corridor:Northern Expressway#1", "suburb", "Buchfelde"),
            new EvidenceUnit("r7:impact", "corridor:Northern Expressway#1", "impact", "minor impact"),

            new EvidenceUnit("weather:condition", "weather", "condition", "Passing clouds"),
            new EvidenceUnit("weather:temperature", "weather", "temperature", "7 degrees")
        ],
        RequiredSections: ["weather"],
        HouseStyle: "",
        RoadFeelPalette: "");

    /// <summary>
    ///     Sydney, 22s. A tight slot with more content than fits, which is the case that exposes
    ///     whether the fitter drops semantic content cleanly or strands a connective.
    /// </summary>
    public static WorkflowRequest Sydney22S() => new(
        JobId: "fixture-sydney-22s",
        Market: "sydney",
        Station: "KIIS 90s",
        Location: "Sydney",
        TargetSeconds: 22.0,
        Evidence:
        [
            new EvidenceUnit("r5:suburb", "corridor:Old Windsor Road#1", "suburb", "Old Toongabbie"),
            new EvidenceUnit("r5:delayMinutes", "corridor:Old Windsor Road#1", "delayMinutes", "4"),

            new EvidenceUnit("r1:suburb", "corridor:Centenary Drive#1", "suburb", "Strathfield"),
            new EvidenceUnit("r1:delayMinutes", "corridor:Centenary Drive#1", "delayMinutes", "4"),

            new EvidenceUnit("r2:suburb", "corridor:Copeland Street#1", "suburb", "Liverpool"),
            new EvidenceUnit("r2:impact", "corridor:Copeland Street#1", "impact", "moderate delays"),

            new EvidenceUnit("r3:suburb", "corridor:M5 East Tunnel#1", "suburb", "Arncliffe"),
            new EvidenceUnit("r3:delayMinutes", "corridor:M5 East Tunnel#1", "delayMinutes", "7"),

            new EvidenceUnit("weather:condition", "weather", "condition", "Partly cloudy"),
            new EvidenceUnit("weather:temperature", "weather", "temperature", "15 degrees")
        ],
        RequiredSections: ["weather"]);
}
