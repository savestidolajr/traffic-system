using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Composes EVERY factual data section a bulletin may draw from — traffic, weather,
///     fuel, good-run status — into one block. The reviewer's fabrication check must see
///     exactly the data the draft saw, or legitimate weather/fuel content gets flagged
///     as invented.
/// </summary>
internal static class BulletinDataComposer
{
    /// <param name="includeFillerStatements">
    ///     Renders the market's standing road facts into the block. Production review/refine
    ///     never sets this — filler is ARN's own editorial content, applied deterministically by
    ///     BroadcastOrchestrator AFTER review passes, so no LLM call ever sees it. Only the eval
    ///     pack sets it true, to expose the same "legitimate content" reference set its
    ///     fabrication scorer checks the (already filler-appended) final script against.
    /// </param>
    public static string ComposeAllData(BroadcastWithTraffic context, bool includeFillerStatements = false)
    {
        // ONE payload for every role. This used to compose a separate PROSE view
        // (TrafficNarrativeSummarizer.Summarize plus per-source formatters) while the draft was
        // grounded in BuildEvidenceUnits, so the reviewer judged "the bulletin may assert only
        // facts present here" against different facts than the writer was given. Observed live
        // 2026-08-20 on Heathcote Road: the draft held [location:0]=At New Illawarra Road,
        // [location:1]=At Macarthur Drive and detail="road closure and roadworks", while the prose
        // capped the anchors at one and omitted the closure — so a correctly grounded read drew
        // FALSE-CLOSURE and "Macarthur Drive fabricated". Both agents were behaving rationally
        // against inputs that disagreed. BuildEvidenceUnits already covers traffic, weather, fuel,
        // good runs and cleared items under the same Content gating the prose applied, so rendering
        // it here makes every role cite the same ids the draft chose from.
        var parts = new List<string>
        {
            PromptLibrary.RenderEvidenceUnits(TrafficNarrativeSummarizer.BuildEvidenceUnits(context)),
        };

        if (includeFillerStatements && context.FillerStatements.Count > 0)
            // Contract text mirrors the gap-covering selection (BFN-002) and the trailing-fill
            // ordering (ARN notes: filler comes AFTER the bulletin; no ATN read returns to traffic
            // after a reminder): the statements handed over are already sized to the unfilled slot
            // time and CLOSE the read, in order.
            parts.Add("Standing road facts for this market (real, admin-authored, already sized to the unfilled slot time — they CLOSE the read: voice them in order, trimmed tight, after all traffic, weather and fuel):\n"
                      + string.Join("\n", context.FillerStatements.Select(s => $"- {s.Text}")));

        return string.Join("\n\n", parts);
    }
}
