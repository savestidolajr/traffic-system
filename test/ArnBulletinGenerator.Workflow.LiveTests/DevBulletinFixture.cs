using System.Globalization;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.LiveTests;

/// <summary>
///     One captured dev bulletin plus the clean-room request rebuilt from the exact writer input
///     persisted with that job. The aired script remains alongside the request so a live run can be
///     reviewed against the read produced by the deployed pipeline.
/// </summary>
public sealed record DevBulletin(
    string JobId,
    string Market,
    string Station,
    double TargetSeconds,
    string AiredScript,
    double? AiredMeasuredSeconds,
    WorkflowRequest Request,
    int CorridorCount);

/// <summary>
///     Loads real dev job exports without allowing one stale or malformed row to stop a corpus scan.
///     The fixture deliberately uses the Infrastructure evidence projection rather than copying its
///     selection rules: the live comparison must receive the same atomic facts as the writer.
/// </summary>
public static class DevBulletinFixture
{
    private const string SolutionFileName = "ArnBulletinGenerator.slnx";
    private const double FallbackLatitude = -25.0;
    private const double FallbackLongitude = 133.0;
    private const double FallbackRadiusKm = 555.0;

    /// <summary>Directory containing the captured job rows.</summary>
    public static string CorpusRoot => Path.Combine(FindRepositoryRoot(), "test", "corpus", "dev-bulletins", "jobs");

    /// <summary>Returns every captured job path in deterministic ordinal filename order.</summary>
    public static IReadOnlyList<string> AllJobFiles()
    {
        string root = CorpusRoot;
        return !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();
    }

    /// <summary>
    ///     Attempts to rebuild one fixture. A bad export is deliberately represented by null so a
    ///     live comparison can continue with the other markets.
    /// </summary>
    /// <summary>
    ///     A stand-in ARN house style, so live runs exercise the voice channel instead of leaving
    ///     it empty.
    ///     <para>
    ///         Measured 2026-08-26: with no policy set, <c>PolicyRenderer.ForComposer</c> renders
    ///         nothing and the composer is asked to sound like ARN while being told nothing about
    ///         how ARN sounds. Against corridor data where every event carries the same fields, the
    ///         house style it invents is a template - five sentences of
    ///         "&lt;road&gt; is &lt;state&gt; at &lt;place&gt;, with &lt;delay&gt;".
    ///     </para>
    ///     <para>
    ///         Placeholder wording pending ARN's own. It belongs in a fixture rather than a prompt
    ///         because it is station configuration: real stations set this per market.
    ///     </para>
    /// </summary>
    private static readonly StationPolicy HouseStyle = new(
        EditorialPolicy.Empty,
        new VoicePolicy
        {
            Tone = "Relaxed but switched on. You're a mate in the next car, not a newsreader.",
            RoadFeelPalette = string.Join(Environment.NewLine,
                "Heavy: chockers, bumper to bumper, crawling, banked up, at a standstill",
                "Moderate: slow going, a slow grind, stacking up, under speed, dragging",
                "Light: a touch slower, an extra minute or two, just off the pace, a bit sticky",
                "Clear: running clean, a good run, flying, no dramas"),
            // Expressed as the knob rather than as prose in Notes. The same intent written as
            // "you don't need a delay figure on everything" was ignored on every sydney sentence.
            MaxDelayFigures = 1,
            Notes = string.Join(" ",
                "Talk like a person, not a spreadsheet.",
                "Don't start every line with the road name - lead with whatever actually matters in that one,",
                "whether that's the closure, the hold-up, the spot, or the fact it's finally cleared.",
                "Mix your sentence lengths. A short one lands harder after a long one.",
                "If two roads are part of the same mess, say them in the one breath.",
                "Never say the same thing twice in one read."),
        });

    public static DevBulletin? TryLoad(string jobFilePath)
    {
        try
        {
            using JsonDocument job = JsonDocument.Parse(File.ReadAllText(jobFilePath));
            JsonElement root = job.RootElement;

            if (!TryGetString(root, "ScriptText", out string airedScript))
                return null;
            if (!TryGetString(root, "GeneratorInputJson", out string generatorInputJson))
                return null;

            var snapshot = JsonSerializer.Deserialize<GeneratorInputSnapshot>(generatorInputJson, JsonDefaults.Storage);
            if (snapshot is null || snapshot.Corridors.Count == 0)
                return null;

            string jobId = GetString(root, "RowKey")
                           ?? GetString(root, "JobId")
                           ?? Path.GetFileNameWithoutExtension(jobFilePath);
            string market = GetString(root, "Market")
                            ?? (snapshot.MarketId.HasValue ? snapshot.MarketId.Value : string.Empty);
            string station = GetString(root, "Station") ?? string.Empty;
            double? measuredSeconds = GetNullableDouble(root, "MeasuredDurationSec");

            // BroadcastWithTraffic has no Coverage property because coverage is only consumed while
            // fetching. Seed the context through BroadcastRequest anyway: this keeps the replay's
            // reconstructed coverage valid when a future evidence source needs it, while preserving
            // every writer field from the persisted snapshot below.
            CoverageArea coverage = ResolveCoverage(root, snapshot);
            var context = snapshot.ToContext(jobId);
            // Coverage belongs to the fetch-stage request, not BroadcastWithTraffic. Re-seed the
            // replayed corridors through that request so the reconstructed geometry remains valid
            // without dropping the snapshot-only fields that ToContext restores (including edits).
            var seededContext = new BroadcastRequest
            {
                TargetDurationSeconds = context.TargetDurationSeconds,
                JobId = context.JobId,
                Location = context.Location,
                Coverage = coverage,
                MarketId = context.MarketId,
                Content = context.Content,
                Bookends = context.Bookends,
                FillOrder = context.FillOrder
            }.WithCorridors(context.Corridors);
            context = context with
            {
                Corridors = seededContext.Corridors,
                Traffic = string.Empty,
                Incidents = seededContext.Corridors
                    .SelectMany(corridor => corridor.Incidents)
                    .DistinctBy(incident => incident.Id, StringComparer.Ordinal)
                    .ToList()
            };

            var requiredSections = new List<string>(2);
            if (snapshot.Content.RequiresWeather)
                requiredSections.Add("weather");
            if (snapshot.Content.RequiresFuel)
                requiredSections.Add("fuel");

            // The deployed engine subtracts the station's bookend cost from the word target BEFORE
            // drafting (ScriptGenerationEngine.cs:102-112), because a bookend is unconditional. The
            // workflow has no bookend concept and its NoStationBookend check actively rejects one,
            // so the harness models the same reservation: hand the writer the BODY seconds it
            // actually owns, not the slot. Without this a 22s Cairns read is budgeted 8 seconds
            // longer than the air it will get.
            double bodySeconds = snapshot.TargetDurationSeconds - BookendReservationSeconds;
            if (bodySeconds <= 0)
                return null;

            var request = new WorkflowRequest(
                jobId,
                market,
                station,
                snapshot.Location,
                bodySeconds,
                TrafficNarrativeSummarizer.BuildEvidenceUnits(context),
                requiredSections)
            {
                Policy = HouseStyle
            };

            return new DevBulletin(
                jobId,
                market,
                station,
                snapshot.TargetDurationSeconds,
                airedScript,
                measuredSeconds,
                request,
                snapshot.Corridors.Count);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Selects the richest captured rows per market, making the live run useful even when the
    ///     corpus contains sparse fallback jobs. Both grouping and tie-breaking are deterministic.
    /// </summary>
    public static IReadOnlyList<DevBulletin> LoadRichest(int perMarket = 1, int minCorridors = 4)
    {
        if (perMarket <= 0)
            return [];

        return AllJobFiles()
            .Select(TryLoad)
            .Where(item => item is not null && item.CorridorCount >= minCorridors)
            .Select(item => item!)
            .Where(item => SlotFilterSeconds is not double slot
                           || Math.Abs(item.TargetSeconds - slot) < 0.01)
            .GroupBy(item => item.Market, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(item => item.CorridorCount)
                .ThenBy(item => item.JobId, StringComparer.Ordinal)
                .Take(perMarket))
            .ToList();
    }

    /// <summary>
    ///     Restricts the corpus to captures cut for one slot length, so a 22-second run is not
    ///     silently answered with a 30-second capture. Unset means every slot.
    /// </summary>
    private static double? SlotFilterSeconds =>
        double.TryParse(Environment.GetEnvironmentVariable("ARN_WORKFLOW_SLOT"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double slot)
            ? slot
            : null;

    /// <summary>
    ///     Spoken cost of the station's bookend pair, reserved out of the slot before drafting.
    ///     Expressed in WORDS because that is how the pair is authored and how the deployed
    ///     composer measures it; converted here at the same rate the workflow predicts with, so the
    ///     budget and the prediction cannot disagree.
    /// </summary>
    private static double BookendReservationSeconds
    {
        get
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("ARN_WORKFLOW_BOOKEND_WORDS"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int words) || words <= 0)
                return 0;
            return new WordRateDurationOracle().SecondsForWords(words);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }

    private static CoverageArea ResolveCoverage(JsonElement root, GeneratorInputSnapshot snapshot)
    {
        if (TryGetProperty(root, "CoverageJson", out JsonElement coverageJson)
            && coverageJson.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(coverageJson.GetString()))
        {
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(coverageJson.GetString()!);
                if (TryCreateCoverage(parsed.RootElement, out CoverageArea? persisted)
                    && persisted is not null)
                {
                    return persisted;
                }
            }
            catch (JsonException)
            {
                // Fall through to the row's scalar coordinates or the permissive default.
            }
        }

        if (TryCreateCoverage(root, out CoverageArea? scalar)
            && scalar is not null)
        {
            return scalar;
        }
        foreach (TrafficCorridor corridor in snapshot.Corridors)
        {
            if (corridor.Latitude.HasValue && corridor.Longitude.HasValue
                && TryCreateCoverage(corridor.Latitude.Value, corridor.Longitude.Value, 35.0,
                    out CoverageArea? corridorCoverage)
                && corridorCoverage is not null)
            {
                return corridorCoverage;
            }
        }

        return CoverageArea.Create(FallbackLatitude, FallbackLongitude, FallbackRadiusKm).Value;
    }

    private static bool TryCreateCoverage(JsonElement element, out CoverageArea? coverage)
    {
        coverage = null;
        double? latitude = GetNullableDouble(element, "Latitude");
        double? longitude = GetNullableDouble(element, "Longitude");
        double? radiusKm = GetNullableDouble(element, "RadiusKm");
        return latitude.HasValue && longitude.HasValue && radiusKm.HasValue
               && TryCreateCoverage(latitude.Value, longitude.Value, radiusKm.Value, out coverage);
    }

    private static bool TryCreateCoverage(double latitude, double longitude, double radiusKm,
        out CoverageArea? coverage)
    {
        coverage = null;
        var result = CoverageArea.Create(latitude, longitude, Math.Max(0.001, radiusKm));
        if (!result.IsSuccess)
            return false;
        coverage = result.Value;
        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = GetString(element, propertyName) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static double? GetNullableDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out number))
            return number;
        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
