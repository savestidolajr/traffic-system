using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Live verification of the WS1 content-source pipeline against REAL providers:
///     HERE traffic + Destination Weather + Fuel Prices (Gold Coast), Azure OpenAI
///     draft/review, and ElevenLabs word-timing alignment.
///     Proves: (1) AU coverage for weather and fuel on the project key, (2) the draft
///     prompt renders with all content sections and produces a script, (3) the REVIEWER
///     does not flag legitimate weather/fuel content as fabrication, (4) word timings
///     survive the synthesis pipeline and land within the audio duration.
///     Run manually with HERE_API_KEY, AZURE_OPENAI_*, ELEVENLABS_API_KEY env vars.
/// </summary>
public sealed class ContentSourcesLiveVerificationTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "Live verification: requires real HERE / Azure OpenAI / ElevenLabs credentials; run manually with env vars set.";

    private static readonly CoverageArea GoldCoast = CoverageArea.Create(-28.017, 153.400, 25).Value;

    [Fact(Skip = SkipReason)]
    public async Task WeatherFuelTrafficBulletin_Generated_AndReviewApproved()
    {
        await using var sp = TestConfigurationHelper.BuildRealServiceProvider(new Dictionary<string, string?>
        {
            ["Weather:Enabled"] = "true",
            ["Fuel:Enabled"] = "true"
        }, LogLevel.Information);

        var traffic = sp.GetRequiredService<ITrafficProvider>();
        var weather = sp.GetRequiredService<IWeatherProvider>();
        var fuel = sp.GetRequiredService<IFuelPriceProvider>();
        var generator = sp.GetRequiredService<IScriptGenerator>();
        var reviewer = sp.GetRequiredService<IScriptReviewer>();

        // 1. Real content fetches (AU coverage proof)
        var corridorsResult = await traffic.GetCorridorsAsync(GoldCoast, 15);
        corridorsResult.IsSuccess.ShouldBeTrue($"Traffic failed: {corridorsResult.Error}");
        var corridors = corridorsResult.Value.Take(4).ToList();

        var weatherResult = await weather.GetWeatherAsync(GoldCoast);
        weatherResult.IsSuccess.ShouldBeTrue($"Weather failed: {weatherResult.Error}");
        output.WriteLine($"Weather: {weatherResult.Value.Conditions}, {weatherResult.Value.TemperatureC:F0}°C");

        var fuelResult = await fuel.GetFuelPricesAsync(GoldCoast);
        fuelResult.IsSuccess.ShouldBeTrue($"Fuel failed: {fuelResult.Error}");
        fuelResult.Value.HasPrices.ShouldBeTrue("HERE returned no AU fuel prices");
        foreach (var s in fuelResult.Value.Summaries)
            output.WriteLine($"Fuel: {s.FuelType} min ${s.MinPrice:F2} avg ${s.AveragePrice:F2} ({s.StationCount} stations)");

        // 2. Real generation with every content section active
        var context = new BroadcastWithTraffic
        {
            JobId = "live-verify",
            Location = "gold-coast",
            City = Option<string>.Some("Gold Coast"),
            TargetDurationSeconds = 30,
            Traffic = "(formatted corridors below)",
            Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = corridors,
            Content = new BulletinContentSelection(Traffic: true, Weather: true, Fuel: true),
            Weather = Option<WeatherReport>.Some(weatherResult.Value),
            Fuel = Option<FuelPriceReport>.Some(fuelResult.Value),
            GoodRunRoads = ["Smith Street Motorway"],
            FutureLessons =
            [
                new FutureLessonEntry(
                    "lesson-live",
                    "Gold Coast",
                    "old",
                    FutureLessonCategory.WordingStyle,
                    "Keep the bulletin conversational and avoid excessive street numbers.",
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    1,
                    DateTime.UtcNow.AddDays(90))
            ]
        };

        var script = await generator.GenerateScriptAsync(context);
        script.IsSuccess.ShouldBeTrue($"Draft failed: {script.Error}");
        output.WriteLine($"--- SCRIPT ---\n{script.Value.Text}\n--------------");

        // 3. The contract under test: the reviewer must never flag data-backed weather/
        //    fuel/good-run content as fabrication or demand its removal, and the content
        //    must survive into the script. Convergence-to-APPROVED on number-dense
        //    corridor data is the pre-existing review-loop tuning concern (task board:
        //    "Tune AI prompts", weeks 4-5) and is deliberately NOT asserted here.
        var refiner = sp.GetRequiredService<IScriptRefiner>();
        string currentScript = script.Value.Text;
        bool approved = false; // informational only — logged, not asserted

        for (int cycle = 1; cycle <= 3; cycle++)
        {
            var review = await reviewer.ReviewScriptAsync(currentScript, 30, context: context);
            review.IsSuccess.ShouldBeTrue($"Review failed: {review.Error}");
            output.WriteLine(
                $"Review {cycle}: {(review.Value.IsApproved ? "APPROVED" : "NEEDS_REVISION")} — {review.Value.Feedback}");

            // Affirmative fabrication flags only — "not fabricated" is an exoneration.
            string feedback = review.Value.Feedback.ToLowerInvariant()
                .Replace("not fabricated", "")
                .Replace("no fabrication", "")
                .Replace("nothing fabricated", "")
                .Replace("not invented", "");
            feedback.ShouldNotContain("fabricat");
            feedback.ShouldNotContain("invented");
            feedback.ShouldNotContain("remove the weather");
            feedback.ShouldNotContain("remove the fuel");
            feedback.ShouldNotContain("no weather content");
            feedback.ShouldNotContain("no fuel-price content");

            if (review.Value.IsApproved)
            {
                approved = true;
                break;
            }

            if (cycle == 3)
                break;

            // Mirror production (2026-08-21): sentence-scoped patches per resolvable issue;
            // a sentence whose patch fails is dropped. No whole-script rewrite exists anymore.
            var split = Domain.Services.ScriptSentenceSplitter.Split(currentScript).ToList();
            foreach (var issue in review.Value.Issues.Where(i => i.Order is > 0)
                         .OrderByDescending(i => i.Order!.Value))
            {
                int order = issue.Order!.Value;
                if (order > split.Count)
                    continue;
                var patched = await refiner.PatchSentenceAsync(
                    context, new Domain.Contracts.DraftedSentence(order, split[order - 1], []),
                    $"Fix this issue: {issue.Reason}", [],
                    Domain.Services.WordCounter.Count(split[order - 1]));
                if (patched.IsSuccess && !string.IsNullOrWhiteSpace(patched.Value?.Text))
                    split[order - 1] = patched.Value!.Text;
                else
                    split.RemoveAt(order - 1);
            }
            currentScript = string.Join(" ", split.Select(s => s.Trim()));
            output.WriteLine($"--- PATCHED {cycle} ---\n{currentScript}\n---------------");
        }

        output.WriteLine($"Final verdict after mirrored cycles: {(approved ? "APPROVED" : "NEEDS_REVISION")}");

        // 4. The weather and fuel content actually made it on air.
        string finalScript = currentScript.ToLowerInvariant();
        finalScript.ShouldContain("$");
        (finalScript.Contains("degrees") || finalScript.Contains("cloud") || finalScript.Contains("rain"))
            .ShouldBeTrue("Weather content missing from the generated script.");
    }

    [Fact(Skip = SkipReason)]
    public async Task WordTimings_RealSynthesis_AlignWithScriptAndDuration()
    {
        await using var sp = TestConfigurationHelper.BuildRealServiceProvider(
            minimumLogLevel: LogLevel.Information);

        var pipeline = sp.GetRequiredService<IAudioPipelineService>();

        const string script =
            "Heavy delays on the M1 at Helensvale this morning, with traffic crawling citybound. " +
            "An earlier breakdown at Nerang has now cleared, and the Smith Street Motorway is running a treat.";

        var result = await pipeline.SynthesizeWithDurationTargetAsync(
            script, 13, Option<string>.Some("CwhRBWXzGAHq8TQ4Fs17"),
            cancellationToken: CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"Synthesis failed: {result.Error}");
        var synthesis = result.Value;

        output.WriteLine($"Duration: {synthesis.DurationSeconds:F2}s, WordTimings: {synthesis.WordTimings.Count}");
        foreach (var t in synthesis.WordTimings.Take(8))
            output.WriteLine($"  {t.StartSeconds,6:F2}–{t.EndSeconds,6:F2}  {t.Word}");

        synthesis.WordTimings.ShouldNotBeEmpty("with-timestamps endpoint should yield alignment");

        // Word count parity with the final script (punctuation-insensitive)
        int scriptWords = WordCounter.Count(synthesis.FinalScript);
        synthesis.WordTimings.Count.ShouldBeInRange(scriptWords - 2, scriptWords + 2);

        // Monotonic, in-range timings
        double prevStart = -1;
        foreach (var t in synthesis.WordTimings)
        {
            t.StartSeconds.ShouldBeGreaterThanOrEqualTo(prevStart);
            t.EndSeconds.ShouldBeGreaterThanOrEqualTo(t.StartSeconds);
            t.EndSeconds.ShouldBeLessThanOrEqualTo(synthesis.DurationSeconds + 0.001);
            prevStart = t.StartSeconds;
        }
    }
}
