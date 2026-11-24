using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Throwaway probe: dumps the EXACT real draft input (BulletinDataComposer.ComposeAllData —
///     anchored traffic + weather + fuel + good runs + fillers) for the 3 canonical markets, using
///     LIVE HERE, so the lean-prompt A/B runs against the identical input production assembles.
///     Gated on ARN_EVAL_LIVE=1. Writes output/real-input/&lt;market&gt;.txt.
/// </summary>
public sealed class DumpRealInputTests(ITestOutputHelper output)
{
    private static readonly BulletinScenario[] Scenarios =
    [
        new("sydney", "Sydney", -33.8688, 151.2093, 25, 30, Weather: true, Fuel: true),
        new("melbourne", "Melbourne", -37.8136, 144.9631, 25, 30, Weather: true, Fuel: true),
        new("goldcoast", "Gold Coast", -28.0167, 153.4000, 20, 30, Weather: true, Fuel: true),
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DumpRealInput_LiveHere()
    {
        if (Environment.GetEnvironmentVariable("ARN_EVAL_LIVE") != "1")
        {
            output.WriteLine("ARN_EVAL_LIVE != 1 — skipping.");
            return;
        }

        using var provider = HereLive.BuildProvider();
        Directory.CreateDirectory("output/real-input");
        foreach (var sc in Scenarios)
        {
            using var scope = provider.CreateScope();
            var ctx = await BulletinEval.BuildContextAsync(scope.ServiceProvider, sc, CancellationToken.None);
            string data = BulletinDataComposer.ComposeAllData(ctx, includeFillerStatements: true);
            string path = Path.Combine("output", "real-input", $"{sc.Market}.txt");
            await File.WriteAllTextAsync(path, data);
            output.WriteLine(
                $"{sc.Market}: {data.Length} chars | corridors={ctx.Corridors.Count} " +
                $"weather={ctx.Weather.HasValue} fuel={ctx.Fuel.HasValue} " +
                $"goodruns={ctx.GoodRunRoads.Count} fillers={ctx.FillerStatements.Count} -> {Path.GetFullPath(path)}");
        }
    }
}
