using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using ArnBulletinGenerator.Infrastructure.Tests.Replay;
using ArnBulletinGenerator.Infrastructure.Tests.Eval;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.WorkflowExperiment;

/// <summary>
///     Exports the fully-rendered template variables (the data side of the prompt) for each
///     fallback-replay fixture, so the Python workflow harness can run arbitrary agentic
///     workflows against the EXACT same data the C# pipeline sees — without needing to
///     reimplement TrafficNarrativeSummarizer, ContentFormatters, DraftWordBudget, etc.
///
///     Trigger: ARN_EXPORT_INPUTS=1  (plus optional ARN_REPLAY_JOB=prefix1,prefix2 or "panel")
///     Output: output/workflow-inputs/&lt;jobId&gt;.json — one file per fixture, each containing
///             every template variable the draft/review/refine prompts consume.
/// </summary>
public sealed class ExportRenderedInputs(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Export_AllRenderedInputs()
    {
        if (Environment.GetEnvironmentVariable("ARN_EXPORT_INPUTS") != "1")
        {
            output.WriteLine("Skipped: set ARN_EXPORT_INPUTS=1 to export rendered prompt inputs.");
            return;
        }

        var replayFixtures = FallbackReplayFixture.LoadAll();
        var regressionFixtures = Regression.RegressionFixture.LoadAll();
        // Unified export view: fallback-replay fixtures keyed by job id, regression fixtures
        // keyed by their human-readable label (better scenario names in the workflow lab).
        var fixtures = replayFixtures
            .Select(f => (f.JobId, f.Market, f.Station, f.CapturedAtUtc, DevOutcome: (object?)f.DevOutcome, f.Snapshot))
            .Concat(regressionFixtures.Select(f => (
                JobId: f.Label, Market: f.Snapshot.Location, Station: (string?)f.SourceJobId,
                CapturedAtUtc: (DateTimeOffset?)f.CapturedAtUtc,
                DevOutcome: (object?)new { f.Reason, f.SourceJobId },
                f.Snapshot)))
            .ToList();
        fixtures.ShouldNotBeEmpty("No fixtures under test/corpus/fallback-replay/ or test/corpus/regressions/.");

        string? jobFilter = Environment.GetEnvironmentVariable("ARN_REPLAY_JOB");
        if (string.Equals(jobFilter, "panel", StringComparison.OrdinalIgnoreCase))
            jobFilter = "9ef6501ce9,2ab24e833b,1d438b96ab,65ec837833,d3ff1769b2,202d1435c0,0a87a1b418,646e14e705,190e5f5967,0871f23415";
        if (!string.IsNullOrWhiteSpace(jobFilter))
        {
            string[] prefixes = jobFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            fixtures = fixtures
                .Where(f => prefixes.Any(p => f.JobId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_LIMIT"), out int limit) && limit > 0)
            fixtures = fixtures.Take(limit).ToList();
        output.WriteLine($"Exporting rendered inputs for {fixtures.Count} fixture(s)...");

        string outDir = Path.Combine(CorpusPaths.RepoRoot(), "output", "workflow-inputs");
        Directory.CreateDirectory(outDir);

        // WordsPerMinute default (BroadcastOptions default; appsettings.json does not override it).
        // The exact per-attempt target the production duration predictor computes is a pipeline
        // concern; the experiment holds DATA constant across workflow arms, and each workflow may
        // recompute its own target from targetSeconds. 150 wpm matches the static-rate baseline.
        const int wordsPerMinute = 150;

        int exported = 0;
        foreach (var fixture in fixtures)
        {
            var context = fixture.Snapshot.ToContext(fixture.JobId);
            string city = context.City.HasValue ? context.City.Value : context.Location;
            int corridorCount = context.Corridors.Count;

            // Target word count follows the structured generator's duration-budget logic.
            int targetWordCount = context.TargetWordCountOverride.GetValueOr(
                (int)Math.Round(context.TargetDurationSeconds * wordsPerMinute / 60.0));
            var (wordsMin, isLowCorridorCount) = DraftWordBudget.Compute(targetWordCount, corridorCount);

            // ── All template variables the draft prompt consumes ──
            var arguments = new Dictionary<string, object?>
            {
                ["targetSeconds"] = context.TargetDurationSeconds,
                ["targetWords"] = targetWordCount,
                ["targetWordsMin"] = wordsMin,
                ["targetWordsMax"] = targetWordCount + 3,
                ["wordsPerMinute"] = wordsPerMinute,
                ["city"] = city,
                ["corridorCount"] = corridorCount,
                ["isLowCorridorCount"] = isLowCorridorCount
            };

            // Time of day — fixtures don't carry timezone, so this will be absent
            // (the draft prompt bans time-of-day words when timeOfDay is not set).

            if (context.Content.Traffic)
                arguments["traffic"] = PromptSafe.WrapData(TrafficNarrativeSummarizer.Summarize(context));
            else
                arguments["noTraffic"] = true;

            // Weather / fuel — a fetched source reaches the writer when FillOrder lists it, OR
            // the station requires it (fixture, must air whenever its data was fetched), OR
            // (weather only) it is a major weather alert.
            bool weatherEligible = context.FillOrder.Contains(FillSource.Weather);
            bool fuelEligible = context.FillOrder.Contains(FillSource.Fuel);
            if (context.Weather.HasValue && (weatherEligible || context.Content.RequiresWeather || context.Weather.Value.HasMajorAlert))
                arguments["weather"] = ContentFormatters.FormatWeather(context.Weather.Value);
            if (context.Fuel.HasValue && (fuelEligible || context.Content.RequiresFuel))
                arguments["fuel"] = ContentFormatters.FormatFuel(context.Fuel.Value);

            // Section order / required sections (draft prompt args)
            var sectionOrder = ContentSectionOrder.Resolve(context.FillOrder)
                .Where(s => s switch
                {
                    FillSource.Weather => arguments.ContainsKey("weather"),
                    FillSource.Fuel => arguments.ContainsKey("fuel"),
                    _ => false
                })
                .ToList();
            if (sectionOrder.Count >= 2)
                arguments["sectionOrder"] = ContentSectionOrder.Describe(sectionOrder);

            var requiredSections = new List<string>();
            if (context.Content.RequiresWeather && context.Weather.HasValue)
                requiredSections.Add("weather");
            if (context.Content.RequiresFuel && context.Fuel.HasValue)
                requiredSections.Add("fuel");
            if (requiredSections.Count > 0)
                arguments["requiredSections"] = string.Join(" and ", requiredSections);

            // Good runs
            if (context.GoodRunRoads.Count > 0)
            {
                arguments["goodRuns"] = ContentFormatters.FormatGoodRuns(
                    context.GoodRunRoads, CorridorTiering.CountVoiceable(context.Corridors));
                arguments["goodRunsAreFill"] = corridorCount == 0 || isLowCorridorCount;
            }

            // Fill order
            var availableFillSources = context.FillOrder
                .Where(f => f switch
                {
                    FillSource.Weather => context.Weather.HasValue,
                    FillSource.Fuel => context.Fuel.HasValue,
                    FillSource.MinorTraffic => CorridorTiering.ClassifyByTier(context.Corridors).Minor.Count > 0,
                    FillSource.Filler => context.FillerStatements.Count > 0,
                    _ => false
                })
                .ToList();
            if (availableFillSources.Count > 0)
                arguments["fillOrder"] = string.Join(", then ",
                    availableFillSources.Select(f => f switch
                    {
                        FillSource.Filler =>
                            "the standing filler lines (appended automatically after your text — leave the remaining gap to them)",
                        FillSource.MinorTraffic =>
                            "the Minor: traffic items below (lower-severity roadworks/delays — only once Major, Moderate, weather and fuel still leave the read short)",
                        _ => f.ToString().ToLowerInvariant()
                    }));

            // Explicit, active station lessons
            if (context.FutureLessons.Count > 0)
                arguments["futureLessons"] = ContentFormatters.FormatFutureLessons(context.FutureLessons);

            // Guidance
            if (context.Guidance.HasEntries)
            {
                string guidanceText = ContentFormatters.FormatGuidance(context.Guidance);
                if (!string.IsNullOrWhiteSpace(guidanceText))
                {
                    arguments["guidance"] = guidanceText;
                    arguments["guidanceScope"] = GuidanceComposer.BroadestScopeLabel(context.Guidance);
                }
                var houseStyle = context.Guidance.Entries.FirstOrDefault(g => g.Type == GuidanceType.HouseStyle);
                if (houseStyle is not null && !string.IsNullOrWhiteSpace(houseStyle.Text))
                    arguments["houseStyle"] = houseStyle.Text;
            }

            // Curated corridors
            if (context.CuratedCorridorRoads.HasValue)
                arguments["curatedTraffic"] = true;

            // Rejection feedback / retry context / previous script / incident changes
            if (context.RejectionFeedback.HasValue && !string.IsNullOrWhiteSpace(context.RejectionFeedback.Value))
                arguments["rejectionFeedback"] = PromptSafe.StripDelimiters(context.RejectionFeedback.Value);
            if (context.RetryContext.HasValue && !string.IsNullOrWhiteSpace(context.RetryContext.Value))
                arguments["retryContext"] = context.RetryContext.Value;
            if (context.PreviousScript.HasValue && !string.IsNullOrWhiteSpace(context.PreviousScript.Value))
                arguments["previousScript"] = context.PreviousScript.Value;
            if (context.IncidentChanges.HasValue)
            {
                string changesSummary = IncidentChangeFormatter.BuildSummary(context.IncidentChanges);
                if (!string.IsNullOrWhiteSpace(changesSummary))
                    arguments["incidentChanges"] = changesSummary;
            }

            // Evidence units
            var evidenceUnits = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);
            arguments["evidenceUnits"] = PromptLibrary.RenderEvidenceUnits(evidenceUnits);

            // Max incidents
            int maxIncidents = context.Content.Traffic && !context.CuratedCorridorRoads.HasValue
                ? DraftWordBudget.MaxVoicedIncidents(targetWordCount, evidenceUnits)
                : 0;
            if (maxIncidents > 0)
                arguments["maxIncidents"] = maxIncidents;

            // Also include the review/refine-specific variables (the review prompt needs
            // includesWeather/includesFuel/hasPreviousBulletin, plus the traffic data)
            if (context.Weather.HasValue)
                arguments["includesWeather"] = true;
            if (context.Fuel.HasValue)
                arguments["includesFuel"] = true;
            if (context.PreviousScript.HasValue)
                arguments["hasPreviousBulletin"] = true;
            if (context.IncidentChanges.HasValue)
                arguments["incidentChanges"] = IncidentChangeFormatter.BuildSummary(context.IncidentChanges);

            // Metadata for the harness. `snapshot` is the RAW writer input (Option-aware
            // serialization) so Python workflows can build their own data representation
            // instead of being locked to the C# renderer's prose.
            var export = new
            {
                jobId = fixture.JobId,
                market = fixture.Market,
                station = fixture.Station,
                capturedAtUtc = fixture.CapturedAtUtc,
                devOutcome = fixture.DevOutcome,
                variables = arguments,
                snapshot = JsonSerializer.SerializeToElement(fixture.Snapshot,
                    ArnBulletinGenerator.Infrastructure.Serialization.JsonDefaults.Storage)
            };

            string json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            string outPath = Path.Combine(outDir, $"{fixture.JobId}.json");
            await File.WriteAllTextAsync(outPath, json);
            exported++;
            output.WriteLine($"  {fixture.JobId} ({fixture.Market}) — {arguments.Count} variables, {json.Length} bytes");
        }

        output.WriteLine($"Exported {exported} fixture(s) to {outDir}/");
    }
}
