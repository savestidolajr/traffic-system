using System.Text.Json;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     Builds a <see cref="RegressionFixture" /> from a job's raw persisted Table Storage columns
///     (supplied as a JSON file — see <c>scripts/export-prompts.py --build-fixture</c>) and replays
///     it through the LIVE current-pipeline generator, writing a clean JSON result for tooling to
///     consume — the "diagnose a deployed job, edit a prompt, rerun THIS job's exact original input,
///     compare, repeat" loop. Unlike <see cref="RegressionCorpusTests" /> (which replays the WHOLE
///     committed corpus and writes only a markdown pack), this targets exactly one job per run and
///     emits machine-readable JSON.
///     <para>
///         Prefers the job's own <c>GeneratorInputJson</c> snapshot when present (most faithful — the
///         app's own capture, byte-identical). Falls back to reconstructing from the independently
///         and reliably persisted <c>CorridorsJson</c>/<c>AllIncidentsJson</c>/<c>FlowRoadsJson</c>/
///         <c>GoodRunRoadsJson</c>/<c>MonitoredRoutesJson</c>/<c>WeatherJson</c>/<c>FuelJson</c>/
///         <c>ContentSelectionJson</c> columns — ALL of which exist regardless of whether the
///         best-effort <c>GeneratorInputJson</c> capture succeeded (its write is wrapped in a silent
///         try/catch that has been observed failing on real jobs). See
///         <see cref="IncidentMappingExtensions.ToTrafficCorridor" />/
///         <see cref="IncidentMappingExtensions.ToTrafficIncident" /> for the reverse mapping.
///         Guidance and MarketFeedback have no dedicated persisted column and are NOT reconstructed
///         in the fallback path (left at type defaults) — a disclosed, known gap, not a silent
///         inaccuracy: most flagged-bulletin defects live in corridor/weather/fuel wording, not
///         guidance content.
///     </para>
///     <para>
///         Gated on the <c>ARN_FIXTURE_INPUT</c> env var (path to the input JSON) so this is a no-op
///         in CI and a normal `dotnet test` run. Fixture-write-only vs full replay is gated
///         separately on <c>ARN_EVAL_LIVE=1</c> (+ Azure OpenAI credentials), matching
///         <see cref="RegressionCorpusTests" />'s convention.
///     </para>
/// </summary>
public sealed class JobFixtureReplayTests(ITestOutputHelper output)
{
    private sealed record FixtureInput(
        string SourceJobId, string Label, string Reason, string OriginalScript, DateTimeOffset CapturedAtUtc,
        double TargetDurationSeconds, string Station, string Market,
        string? GeneratorInputJson, string? CorridorsJson, string? AllIncidentsJson, string? FlowRoadsJson,
        string? GoodRunRoadsJson, string? MonitoredRoutesJson, string? WeatherJson, string? FuelJson,
        string? ContentSelectionJson);

    private sealed record FixtureReplayResult(
        string Label, string SourceJobId, string OriginalScript, string ReplayScript, int CorridorCount,
        bool FabricationPassed, IReadOnlyList<string> FabricationIssues,
        bool StylePassed, IReadOnlyList<string> StyleIssues,
        bool SafetyPassed, IReadOnlyList<string> SafetyIssues,
        string SourceData, string FixturePath);

    [Fact]
    public async Task JobFixture_BuildAndReplay()
    {
        string? inputPath = Environment.GetEnvironmentVariable("ARN_FIXTURE_INPUT");
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            output.WriteLine("Skipped: set ARN_FIXTURE_INPUT to a job-data JSON path " +
                              "(see scripts/export-prompts.py --build-fixture).");
            return;
        }

        var input = JsonSerializer.Deserialize<FixtureInput>(File.ReadAllText(inputPath), JsonDefaults.Storage)
                    ?? throw new InvalidOperationException($"Empty/invalid fixture input: {inputPath}");

        var snapshot = BuildSnapshot(input);
        var fixture = new RegressionFixture(
            input.Label, input.Reason, input.SourceJobId, input.OriginalScript, input.CapturedAtUtc, snapshot);

        string fixturePath = Path.Combine(CorpusPaths.RegressionCorpusRoot(), $"{input.Label}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        File.WriteAllText(fixturePath, fixture.ToJson());
        output.WriteLine($"Wrote fixture: {fixturePath}");

        if (Environment.GetEnvironmentVariable("ARN_EVAL_LIVE") != "1")
        {
            output.WriteLine("Fixture written; skipped replay (set ARN_EVAL_LIVE=1 + Azure OpenAI creds to replay).");
            return;
        }

        using var provider = HereLive.BuildProvider();
        var result = await RegressionEval.ReplayAsync(provider, fixture, CancellationToken.None);

        var replayResult = new FixtureReplayResult(
            fixture.Label, fixture.SourceJobId, fixture.OriginalScript, result.Score.Script,
            result.Score.CorridorCount,
            result.Score.Fabrication.Passed, result.Score.Fabrication.UnsupportedMentions,
            result.Score.Style.Passed, result.Score.Style.Issues,
            result.Score.Safety.Passed, result.Score.Safety.Violations,
            result.Score.SourceData, fixturePath);

        string outputPath = Environment.GetEnvironmentVariable("ARN_FIXTURE_OUTPUT")
                             ?? Path.Combine("output", "fixture-replay-result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(replayResult,
            new JsonSerializerOptions(JsonDefaults.Storage) { WriteIndented = true }));

        output.WriteLine($"Replay result: {Path.GetFullPath(outputPath)}");
        output.WriteLine($"ORIGINAL: {fixture.OriginalScript}");
        output.WriteLine($"REPLAY:   {result.Score.Script}");
    }

    private static GeneratorInputSnapshot BuildSnapshot(FixtureInput input)
    {
        // Most faithful path: the app's own captured snapshot, byte-identical to what it saw — but
        // only when it actually carries evidence. A snapshot can be PRESENT and BLANK: until the
        // 2026-08-03 AudioFinalizer fix, every operator re-voice rewrote GeneratorInputJson from the
        // contextless re-voice context, so jobs captured before then parse cleanly into an
        // all-empty snapshot while GoodRunRoadsJson/CorridorsJson still hold the real data. Taking
        // it made the fixture replay an empty read (job fa754637: six good-run roads on the record,
        // zero in the snapshot). Emptiness alone is not proof of corruption, so fall through only
        // when the sibling columns genuinely offer more.
        var direct = string.IsNullOrWhiteSpace(input.GeneratorInputJson)
            ? null
            : SafeParse<GeneratorInputSnapshot>(input.GeneratorInputJson);
        if (direct is not null && HasEvidence(direct)) return direct;

        // Fallback: reconstruct from the independently-persisted job columns.
        var jobCorridors = SafeParse<List<JobCorridor>>(input.CorridorsJson) ?? [];
        var jobIncidents = SafeParse<List<JobIncident>>(input.AllIncidentsJson) ?? [];
        var incidentsByKey = jobIncidents.ToDictionary(i => i.Key);
        var corridors = jobCorridors.Select(jc => jc.ToTrafficCorridor(incidentsByKey)).ToList();

        var reconstructed = new GeneratorInputSnapshot
        {
            TargetDurationSeconds = input.TargetDurationSeconds,
            Location = input.Station,
            MarketId = Option<string>.Some(input.Market),
            Content = SafeParse<BulletinContentSelection>(input.ContentSelectionJson) ?? BulletinContentSelection.Default,
            Corridors = corridors,
            MonitoredRoutes = SafeParse<List<MonitoredRouteStatus>>(input.MonitoredRoutesJson) ?? [],
            GoodRunRoads = SafeParse<List<string>>(input.GoodRunRoadsJson) ?? [],
            FlowPool = SafeParse<List<FlowRoad>>(input.FlowRoadsJson) ?? [],
            Weather = Option.OfNullable(SafeParse<WeatherReport>(input.WeatherJson)),
            Fuel = Option.OfNullable(SafeParse<FuelPriceReport>(input.FuelJson))
        };

        // Genuinely nothing to fetch either way: keep the app's own snapshot, which still carries the
        // guidance/feedback fields the reconstruction cannot rebuild.
        return HasEvidence(reconstructed) || direct is null ? reconstructed : direct;
    }

    /// <summary>Does this snapshot carry anything the writer could actually have built a read from?</summary>
    private static bool HasEvidence(GeneratorInputSnapshot s) =>
        s.Corridors.Count > 0 || s.GoodRunRoads.Count > 0 || s.MonitoredRoutes.Count > 0
        || s.FlowPool.Count > 0 || s.Weather.HasValue || s.Fuel.HasValue;

    private static T? SafeParse<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, JsonDefaults.Storage);
}
