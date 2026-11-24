using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Builds the minimal weather/fuel fixture line from the fetched data, deterministically.
///     A station that marks a source "always include" has promised it airs whenever the data
///     arrived — and a missing fixture is the one defect class whose honest repair needs no LLM:
///     the facts (condition, temperature, prices, suburb) are already structured. The line this
///     produces is deliberately plain, and prices are normalised here with <c>SpokenPriceNormalizer</c>
///     because the review-loop splice runs after the writer's downstream generation stages.
///     Introduced 2026-08-21 when the whole-script refine was removed: previously a missing fixture
///     escalated to a full LLM rewrite to put back one sentence of data the pipeline already held.
/// </summary>
public static class FixtureLineComposer
{
    /// <summary>The minimal weather read: "Partly sunny, 17 degrees." plus an alert clause when one is active.</summary>
    public static string WeatherLine(WeatherReport report)
    {
        string condition = report.Conditions
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
        int degrees = (int)Math.Round(report.TemperatureC);
        // A malformed upstream payload (empty/whitespace condition) must never throw here — this
        // line runs inside the review loop's repair path, where an exception fails the job.
        string line = condition.Length > 0 ? $"{condition}, {degrees} degrees." : $"{degrees} degrees.";
        if (report.Alerts.Count > 0)
            line += $" There's a {report.Alerts[0].TrimEnd('.')} in place.";
        return line;
    }

    /// <summary>
    ///     The minimal fuel read, one sentence per cheapest-station suburb group:
    ///     "Unleaded 91 is a dollar ninety-eight and diesel is two dollars forty in Manoora."
    ///     A missing suburb is kept in its own empty-key group, so it can never inherit a named
    ///     neighbour's location (2026-08-24).
    /// </summary>
    /// <remarks>
    ///     Normalised HERE, not downstream (2026-08-23). This type's original contract said the plain
    ///     numbers "normalise downstream exactly as they do coming from the writer" — but they do not:
    ///     <see cref="SpokenPriceNormalizer" /> runs inside the Infrastructure generator, which has
    ///     already finished by the time the review loop splices a missing fixture line in the
    ///     Application layer. So every spliced fuel line has aired as raw numerals since this type was
    ///     introduced on 2026-08-21 — observed live on dev-PUBLISHED jobs for adelaide
    ///     ("Unleaded 91 is $1.93 in Kilburn") and portlincoln ("unleaded 91 is $2.23"), while
    ///     model-written lines in the same panel read "a dollar ninety-three".
    ///     Normalising at composition makes the guarantee local to the thing that needs it, and it is
    ///     idempotent: text with no numerals left to convert passes through unchanged.
    /// </remarks>
    public static IReadOnlyList<string> FuelLines(FuelPriceReport report)
    {
        return report.Summaries
            .GroupBy(s => s.CheapestStationSuburb ?? string.Empty, StringComparer.Ordinal)
            .Select(group =>
            {
                var parts = group
                    // SpokenFuelGrade, not s.FuelType: the raw HERE label carries the octane, and
                    // this path aired "Unleaded 91 is a dollar ninety-five" on dev brisbane
                    // e91805be (2026-08-25) after the writer-facing paths had already been fixed.
                    .Select(s => $"{SpokenFuelGrade.Of(s.FuelType)} is ${s.MinPrice:0.00}")
                    .ToList();
                string joined = string.Join(", and ", parts);
                string suburb = string.IsNullOrWhiteSpace(group.Key) ? string.Empty : $" in {group.Key}";
                return SpokenPriceNormalizer.Normalize($"{joined}{suburb}.");
            })
            .ToList();
    }
}
