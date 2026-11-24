using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     The simplest arm: NO structured output, NO per-item evidence-id citation, a WORD target
///     instead of a syllable target, and a real revision turn.
///     <para>
///         Three changes from <see cref="SyllableLoopProbe" />, each testing a separate suspicion:
///         <list type="number">
///             <item><b>Plain text instead of JSON.</b> The matrix arm's reads came out listy, and
///                 one leaked a raw data label verbatim ("Porter Street at the Road construction,
///                 Contraflow"). Forcing one JSON object per fact may be fragmenting the prose that
///                 a single coherent read is supposed to deliver.</item>
///             <item><b>Words instead of syllables.</b> Syllables predict duration slightly better
///                 (6.38% vs 7.09% CV within a voice bucket), but a word is a unit the model can
///                 actually see, and per the length-control literature models track units they can
///                 count. A 0.7pp better predictor is worthless if the writer cannot aim at it.</item>
///             <item><b>Revision, not re-draft.</b> The matrix arm rebuilt the prompt from scratch
///                 each iteration and never showed the model its own previous text, so "add 11
///                 syllables" was unanchored: mildura went 101 -> 101 -> 67 against a target of 112.
///                 That is three blind draws, not a refinement loop. Here iteration 2+ receives the
///                 previous script verbatim and is asked to revise it by an exact delta.</item>
///         </list>
///     </para>
///     <para>
///         Dropping the evidence-id contract does NOT drop grounding: the checks move from the model
///         self-reporting ids to code verifying the finished text against the evidence values, which
///         is the stronger arrangement anyway ("code is the policy"). <see cref="GroundingTrips" />
///         flags a road named in the script that does not appear in the evidence (fabrication) and a
///         road voiced without the suburb the evidence supplies for it (the London Circuit defect).
///     </para>
///     <para>Opt-in via <c>ARN_PLAIN_PROBE=1</c>; makes real model calls.</para>
/// </summary>
public sealed class PlainReadProbe(ITestOutputHelper output)
{
    private const double BandFraction = 0.0638;
    private const int MaxIterations = 3;

    private static readonly string[] RoadTypes =
    [
        "Road", "Street", "Highway", "Motorway", "Freeway", "Avenue", "Drive", "Parade",
        "Terrace", "Way", "Place", "Boulevard", "Tunnel", "Bridge", "Expressway", "Circuit", "Lane"
    ];

    [Fact]
    public async Task PlainRead_WordTarget_RevisionLoop()
    {
        if (Environment.GetEnvironmentVariable("ARN_PLAIN_PROBE") != "1")
        {
            output.WriteLine("skipped: set ARN_PLAIN_PROBE=1 (makes real model calls)");
            return;
        }

        var fixtures = FallbackReplayFixture.LoadAll()
            .Where(f => !string.Equals(f.Market, "TEST STATION", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Snapshot.FillOrder is { Count: > 0 })
            .GroupBy(f => (f.Market, f.Snapshot.TargetDurationSeconds))
            .Select(g => g.First())
            .ToList();
        Assert.NotEmpty(fixtures);

        var configuration = TestConfigurationHelper.BuildConfiguration(
            TestConfigurationHelper.DeployedPipeline());
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddInfrastructure(configuration);
        services.AddSingleton<TokenCredential>(new AzureCliCredential());
        await using var provider = services.BuildServiceProvider();

        var transport = new ResponsesTransport(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<AzureAIOptions>>(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<LlmScriptGenerator>>(),
            provider.GetRequiredService<ILlmCallCapture>(),
            provider.GetRequiredService<TokenCredential>());

        var auth = await transport.GetAuthHeaderAsync(CancellationToken.None);
        Assert.True(auth.IsSuccess, $"auth failed: {auth.Error}");

        var rates = SpeechRates.FromSeed(FallbackReplayFixture.SynthesisHistorySeedPath());
        var rows = new List<PlainRow>();
        foreach (var fixture in fixtures)
        {
            try
            {
                rows.Add(await RunOneAsync(fixture, transport, auth.Value, rates));
            }
            catch (Exception ex)
            {
                output.WriteLine($"{fixture.Market}: EXCEPTION {ex.GetType().Name} {ex.Message}");
            }
        }

        Report(rows);
    }

    private async Task<PlainRow> RunOneAsync(
        FallbackReplayFixture fixture,
        ResponsesTransport transport,
        (string Name, string Value) auth,
        SpeechRates rates)
    {
        var context = fixture.Snapshot.ToContext(fixture.JobId);
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);
        string evidence = PromptLibrary.RenderEvidenceUnits(units);

        var roadValues = units
            .Where(u => u.Field is "road" or "clear road" or "roadName")
            .Select(u => u.Value)
            .Concat(units.Where(u => u.Field == "extent").Select(u => u.Value))
            .ToList();
        var suburbByRoad = BuildSuburbByRoad(units);

        double slot = fixture.Snapshot.TargetDurationSeconds;
        double speed = fixture.VoiceConfig.Settings?.Speed ?? 1.0;
        double wps = rates.WordsPerSecond(fixture.VoiceConfig.VoiceId, fixture.VoiceConfig.Model, speed);

        var bookend = PlainBookends.For(fixture.Market);
        int bookendWords = bookend is null ? 0 : WordCounter.Count($"{bookend.Intro} {bookend.Outro}");

        double slotWords = slot * wps;
        double targetWords = slotWords - bookendWords;
        int lo = (int)Math.Floor(targetWords * (1 - BandFraction));
        int hi = (int)Math.Ceiling(targetWords * (1 + BandFraction));

        var attempts = new List<int>();
        var trips = new List<string>();
        string script = string.Empty;

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            string prompt = iteration == 1
                ? BuildDraftPrompt(context.Location, evidence, (int)Math.Round(targetWords), lo, hi)
                : BuildRevisionPrompt(script, attempts[^1], (int)Math.Round(targetWords), lo, hi, trips);

            var result = await transport.DispatchStructuredLlmAsync(
                "Draft", prompt, 4096, CancellationToken.None, auth,
                jsonSchema: null,
                systemInstructionsOverride: PromptLibrary.SystemInstructions + PlainContract,
                jobId: $"plain-{fixture.JobId}",
                hasProducerFeedback: false);

            if (result.IsFailure)
                return new PlainRow(fixture.Market, slot, wps, (int)slotWords, bookendWords,
                    (int)targetWords, lo, hi, attempts, false, [$"dispatch: {result.Error}"],
                    iteration, string.Empty, bookend, 0);

            script = result.Value.Trim();
            int words = WordCounter.Count(script);
            attempts.Add(words);

            trips = GroundingTrips(script, roadValues, suburbByRoad);
            bool inBand = words >= lo && words <= hi;
            if (inBand && trips.Count == 0)
                return new PlainRow(fixture.Market, slot, wps, (int)slotWords, bookendWords,
                    (int)targetWords, lo, hi, attempts, true, [], iteration, script, bookend,
                    SyllableCounter.CountSyllables(script));
        }

        return new PlainRow(fixture.Market, slot, wps, (int)slotWords, bookendWords,
            (int)targetWords, lo, hi, attempts, false, trips, MaxIterations, script, bookend,
            SyllableCounter.CountSyllables(script));
    }

    private const string PlainContract = """


        ## OUTPUT CONTRACT (overrides any menu/variant/JSON instruction above)

        Return the finished bulletin as PLAIN SPOKEN TEXT and nothing else. No JSON, no headings,
        no labels, no bullet points, no commentary, no bracketed ids. Just the words the presenter
        reads, as one continuous read.

        Every road you name MUST carry the location the evidence supplies for it - the suburb or
        the cross-street. Shortening the read by dropping where something is makes the item
        unairable: a driver cannot act on "London Circuit is closed eastbound", and a generic name
        like Church Street is ambiguous across suburbs. Buy length by writing more plainly or by
        voicing fewer roads, NEVER by deleting a location.

        Name only roads that appear in the evidence. Never invent one to fill the read.
        """;

    private static string BuildDraftPrompt(string location, string evidence, int target, int lo, int hi)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Write one traffic bulletin for {location} radio.");
        sb.AppendLine();
        sb.AppendLine("EVIDENCE:");
        sb.AppendLine(evidence);
        sb.AppendLine();
        sb.AppendLine($"LENGTH: {target} words (acceptable {lo}-{hi}). Count your words before you answer.");
        sb.AppendLine("Voice the most important roads first, and only as many as that length allows.");
        return sb.ToString();
    }

    /// <summary>
    ///     The revision turn. The previous script is present verbatim, so "remove 9 words" is an
    ///     instruction the model can execute against real text rather than a hint it has to guess
    ///     at. The matrix arm's oscillation (101 -> 101 -> 67 against a target of 112) was caused by
    ///     omitting exactly this.
    /// </summary>
    private static string BuildRevisionPrompt(
        string previous, int actual, int target, int lo, int hi, IReadOnlyList<string> trips)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Revise this bulletin. It is your own previous draft:");
        sb.AppendLine();
        sb.AppendLine(previous);
        sb.AppendLine();
        sb.AppendLine($"It is {actual} words. The target is {target} words (acceptable {lo}-{hi}).");
        if (actual > hi)
            sb.AppendLine($"Remove exactly {actual - target} words. Drop the least important road "
                          + "entirely rather than trimming detail from several.");
        else if (actual < lo)
            sb.AppendLine($"Add exactly {target - actual} words of real detail already present in "
                          + "the evidence. Do not invent anything, and do not pad with filler phrases.");
        if (trips.Count > 0)
            sb.AppendLine($"Also fix these: {string.Join("; ", trips.Distinct())}.");
        sb.AppendLine("Keep every road's location. Return the revised bulletin as plain spoken text only.");
        return sb.ToString();
    }

    /// <summary>
    ///     Deterministic grounding on PLAIN text, replacing the model's self-reported evidence ids:
    ///     a named road absent from the evidence is a fabrication, and a road voiced without the
    ///     suburb the evidence gives it is the unairable-item defect.
    /// </summary>
    private static List<string> GroundingTrips(
        string script,
        IReadOnlyList<string> evidenceRoads,
        IReadOnlyDictionary<string, string> suburbByRoad)
    {
        var trips = new List<string>();
        foreach (var match in NamedRoads(script))
        {
            bool known = evidenceRoads.Any(r =>
                r.Contains(match, StringComparison.OrdinalIgnoreCase) ||
                match.Contains(r, StringComparison.OrdinalIgnoreCase));
            if (!known)
                trips.Add($"road-not-in-evidence:{match}");
        }
        foreach (var (road, suburb) in suburbByRoad)
        {
            if (string.IsNullOrWhiteSpace(suburb))
                continue;
            if (script.Contains(road, StringComparison.OrdinalIgnoreCase)
                && !script.Contains(suburb, StringComparison.OrdinalIgnoreCase))
                trips.Add($"location-dropped:{road} ({suburb})");
        }
        return trips;
    }

    /// <summary>Capitalised runs ending in a road type - "the Gateway Motorway", "Porter Street".</summary>
    private static IEnumerable<string> NamedRoads(string script)
    {
        string types = string.Join("|", RoadTypes);
        var pattern = new Regex($@"\b((?:[A-Z][\w'\-]*\s+){{0,3}}(?:{types}))\b", RegexOptions.None);
        return pattern.Matches(script).Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildSuburbByRoad(IReadOnlyList<EvidenceUnit> units)
    {
        // Evidence ids share a per-source prefix ("r5:suburb", "r5:road"), so the suburb for a road
        // is the suburb unit with the same prefix.
        static string Prefix(string id)
        {
            int colon = id.IndexOf(':');
            return colon > 0 ? id[..colon] : id;
        }

        var roads = units
            .Where(u => u.Field is "road" or "clear road" or "roadName")
            .GroupBy(u => Prefix(u.Id), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units.Where(u => u.Field.Equals("suburb", StringComparison.OrdinalIgnoreCase)))
        {
            if (roads.TryGetValue(Prefix(unit.Id), out string? road) && !string.IsNullOrWhiteSpace(road))
                result[road] = unit.Value;
        }
        return result;
    }

    private void Report(IReadOnlyList<PlainRow> rows)
    {
        output.WriteLine("");
        output.WriteLine("| market | slot | w/s | slot w | bookend | body target | band | attempts | iters | passed | grounding |");
        output.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            output.WriteLine($"| {r.Market} | {r.Slot:F0}s | {r.Wps:F2} | {r.SlotWords} | "
                             + $"{(r.BookendWords == 0 ? "-" : r.BookendWords.ToString())} | {r.TargetWords} | "
                             + $"{r.Lo}-{r.Hi} | {string.Join(" -> ", r.Attempts)} | {r.Iterations} | "
                             + $"{(r.Passed ? "yes" : "NO")} | "
                             + $"{(r.Trips.Count == 0 ? "clean" : string.Join(", ", r.Trips.Distinct().Take(2)))} |");

        int passed = rows.Count(r => r.Passed);
        int first = rows.Count(r => r.Passed && r.Iterations == 1);
        int fabricated = rows.Count(r => r.Trips.Any(t => t.StartsWith("road-not-in-evidence", StringComparison.Ordinal)));
        int dropped = rows.Count(r => r.Trips.Any(t => t.StartsWith("location-dropped", StringComparison.Ordinal)));
        double meanIters = rows.Count > 0 ? rows.Average(r => r.Iterations) : 0;

        output.WriteLine("");
        output.WriteLine($"PLAIN SUMMARY | fixtures={rows.Count} inBand={passed}/{rows.Count} "
                         + $"firstIteration={first}/{rows.Count} meanIterations={meanIters:F1} "
                         + $"fabricatedRoad={fabricated} locationDropped={dropped} "
                         + $"| llmCallsPerBulletin={meanIters:F1}");

        foreach (var r in rows)
        {
            output.WriteLine("");
            output.WriteLine($"### {r.Market} ({r.Slot:F0}s | bookend {r.BookendWords}w | target {r.TargetWords}w "
                             + $"| got {r.Attempts.LastOrDefault()}w / {r.Syllables} syl)");
            output.WriteLine(r.Bookend is null
                ? r.Script
                : $"[{r.Bookend.Intro}] {r.Script} [{r.Bookend.Outro}]");
        }
    }

    private sealed record PlainRow(
        string Market,
        double Slot,
        double Wps,
        int SlotWords,
        int BookendWords,
        int TargetWords,
        int Lo,
        int Hi,
        IReadOnlyList<int> Attempts,
        bool Passed,
        IReadOnlyList<string> Trips,
        int Iterations,
        string Script,
        PlainBookend? Bookend,
        int Syllables);
}

internal sealed record PlainBookend(string Intro, string Outro);

internal static class PlainBookends
{
    private static readonly (string Match, string Intro, string Outro)[] Pairs =
    [
        ("cairns", "Having a look at the roads across Cairns and the tropical far north......",
            "Thats traffic and weather together with Johnny and Inkie back for breakfast tomorrow "
            + "from 6am on Star one oh two point seven"),
        ("sydney", "Now looking over Sydney.", "That's KIIS traffic and weather together, powered by"),
        ("goldcoast", "Looking over the Gold Coast today, here's your traffic.",
            "That's it for Gold Coast traffic on Hot Tomato.")
    ];

    public static PlainBookend? For(string market)
    {
        string key = market.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        foreach (var (match, intro, outro) in Pairs)
            if (key.Contains(match, StringComparison.Ordinal))
                return new PlainBookend(intro, outro);
        return null;
    }
}

internal static class WordCounter
{
    public static int Count(string text) =>
        text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Measured words-per-second AND syllables-per-second per voice+model+speed bucket.</summary>
internal sealed class SpeechRates
{
    private readonly Dictionary<(string, string, double), (double Wps, double Sps)> _buckets;
    private readonly (double Wps, double Sps) _pooled;

    private SpeechRates(Dictionary<(string, string, double), (double, double)> buckets, (double, double) pooled)
    {
        _buckets = buckets;
        _pooled = pooled;
    }

    public static SpeechRates FromSeed(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var samples = new List<(string Voice, string Model, double Speed, double Wps, double Sps)>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("AchievedDuration", out var d)
                || !row.TryGetProperty("WordCount", out var w)
                || !row.TryGetProperty("SyllableCount", out var s))
                continue;
            double duration = d.GetDouble();
            double words = w.GetDouble();
            double syllables = s.GetDouble();
            if (duration <= 0 || words <= 0 || syllables <= 0)
                continue;
            samples.Add((
                row.TryGetProperty("VoiceId", out var v) ? v.GetString() ?? "" : "",
                row.TryGetProperty("ModelId", out var m) ? m.GetString() ?? "" : "",
                row.TryGetProperty("Speed", out var sp) && sp.ValueKind is JsonValueKind.Number
                    ? Math.Round(sp.GetDouble(), 2)
                    : 1.0,
                words / duration,
                syllables / duration));
        }
        var buckets = samples
            .GroupBy(x => (x.Voice, x.Model, x.Speed))
            .Where(g => g.Count() >= 8)
            .ToDictionary(g => g.Key, g => (g.Average(x => x.Wps), g.Average(x => x.Sps)));
        var pooled = samples.Count > 0
            ? (samples.Average(x => x.Wps), samples.Average(x => x.Sps))
            : (2.52, 4.06);
        return new SpeechRates(buckets, pooled);
    }

    public double WordsPerSecond(string? voice, string? model, double speed) =>
        _buckets.TryGetValue((voice ?? "", model ?? "", Math.Round(speed, 2)), out var r) ? r.Wps : _pooled.Wps;

    public double SyllablesPerSecond(string? voice, string? model, double speed) =>
        _buckets.TryGetValue((voice ?? "", model ?? "", Math.Round(speed, 2)), out var r) ? r.Sps : _pooled.Sps;
}
