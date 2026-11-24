using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Quality eval and CANONICAL BENCHMARK for prompt/pipeline iteration. Default mode uses replayed
///     (deterministic, free) HERE traffic + live Azure OpenAI generation, scored by the deterministic
///     scorers + (optionally) the ATN-primed LLM judge, and rendered as a scorecard.
///     <list type="bullet">
///         <item>Replay mode (default) is the reproducible content/style benchmark — use it to A/B a
///         prompt change against a stable baseline (target-shape % + judge %).</item>
///         <item><c>ARN_EVAL_HERE_LIVE=1</c> hits live HERE for the flow/congestion/routing dimensions
///         the corpus cannot replay (non-reproducible — for feature exercise, not regression).</item>
///         <item><c>ARN_EVAL_JUDGE=1</c> adds the holistic ATN judge (extra model call per market;
///         set <c>ARN_EVAL_JUDGE_MODEL</c> to a stronger model than the author).</item>
///     </list>
///     The locked set below is three markets at fixed coverage + a 30s target. Grow condition diversity
///     (peak / all-clear / weather-alert / fuel-on) by RECORDING new HERE corpus snapshots at those times
///     via the recording handler — never by hand-authoring traffic, which would not reflect real data.
///     <para>
///         Excluded from CI (<c>Category=Integration</c>); needs Azure OpenAI credentials. Run with:
///         <c>ARN_EVAL_LIVE=1 AZURE_OPENAI_ENDPOINT=… AZURE_OPENAI_API_KEY=… AZURE_OPENAI_MODEL=… \
///         dotnet test --filter "FullyQualifiedName~BulletinEvalTests"</c>.
///     </para>
/// </summary>
public sealed class BulletinEvalTests(ITestOutputHelper output)
{
    private static readonly BulletinScenario[] Scenarios =
    [
        // Canonical baseline: 3 markets, traffic-only (matches BulletinContentSelection.Default).
        // To probe content/density permutations, temporarily add scenarios with explicit
        // Traffic/Weather/Fuel toggles + MaxCorridors, then revert.
        new("sydney", "Sydney", -33.8688, 151.2093, 25, 30),
        new("melbourne", "Melbourne", -37.8136, 144.9631, 25, 30),
        new("goldcoast", "Gold Coast", -28.0167, 153.4000, 20, 30)
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QualityEval_ReplayedTraffic_LiveLlm_ProducesScorecard()
    {
        if (Environment.GetEnvironmentVariable("ARN_EVAL_LIVE") != "1")
        {
            output.WriteLine(
                "Skipped: set ARN_EVAL_LIVE=1 and Azure OpenAI credentials to run the live quality eval.");
            return;
        }

        bool liveHere = Environment.GetEnvironmentVariable("ARN_EVAL_HERE_LIVE") == "1";

        // Script-only mode (ARN_EVAL_SCRIPT_ONLY=1): skip ElevenLabs synthesis and Whisper entirely —
        // no TTS quota, duration is the word-rate estimate, pronunciation is omitted.
        bool scriptOnly = Environment.GetEnvironmentVariable("ARN_EVAL_SCRIPT_ONLY") == "1";
        bool review = Environment.GetEnvironmentVariable("ARN_EVAL_REVIEW") == "1";
        bool fullPipeline = Environment.GetEnvironmentVariable("ARN_EVAL_FULL_PIPELINE") == "1";
        var transcriber = scriptOnly ? null : WhisperTranscriber.CreateIfAvailable();
        output.WriteLine(liveHere
            ? "HERE mode: live APIs (flow/congestion/routing dimensions enabled)."
            : "HERE mode: replay corpus (deterministic; flow/congestion/routing dimensions may be absent).");
        output.WriteLine(scriptOnly
            ? "Script-only — no ElevenLabs, no Whisper (duration is an estimate, pronunciation skipped)."
            : transcriber is null
                ? "Whisper not found — pronunciation column will be blank."
                : "Whisper found — pronunciation scoring enabled.");
        if (review)
            output.WriteLine("Review pack: enabled (agent-judged; written to output/eval/).");
        if (fullPipeline)
            output.WriteLine("Full pipeline: draft → review → refine (judging the shipped script).");

        var scores = new List<ScenarioScore>();
        foreach (var scenario in Scenarios)
        {
            using var provider = liveHere
                ? HereLive.BuildProvider()
                : HereReplay.BuildProvider(LoadReplay(scenario.Market));
            var score = await BulletinEval.EvaluateAsync(
                provider, scenario, CancellationToken.None, measureDuration: !scriptOnly,
                transcriber: transcriber, fullPipeline: fullPipeline);
            scores.Add(score);

            output.WriteLine($"--- {scenario.Market} [{(scenario.Traffic ? "T" : "-")}{(scenario.Weather ? "W" : "-")}{(scenario.Fuel ? "F" : "-")}] (corridors: {score.CorridorCount}) ---");
            output.WriteLine(score.Script);
        }

        output.WriteLine(BulletinScorecard.Render(scores));

        if (review)
        {
            string label = $"{(liveHere ? "live" : "replay")}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}";
            string path = Path.Combine("output", "eval", $"review-{label}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ReviewPack.Render(scores, label));
            output.WriteLine($"Review pack written: {Path.GetFullPath(path)}");
        }
        scores.ShouldAllBe(s => s.Script.Length > 0);
    }

    private static HereCorpusReplay LoadReplay(string market)
    {
        var replay = HereCorpusReplay.Load(market);
        replay.Count.ShouldBeGreaterThan(0, $"{market} corpus is empty");
        return replay;
    }
}
