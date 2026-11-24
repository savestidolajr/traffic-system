using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Collects the STATION-AUTHORED lines a job's aired script may contain — the market's filler
///     statements and the bookend pair the job recorded — so
///     <see cref="StationLineSplitter" /> can peel them off before an operator-driven rewrite.
///
///     <para>
///         Both sources are ARN's own editorial content, appended verbatim after every LLM stage
///         (see <see cref="BulletinBookendComposer" />). Without this an operator rewrite hands them
///         to the grounding reviewer, which correctly reports "Download the iHeartRadio app" as
///         unsourced and refuses the whole rewrite — a refusal the operator can do nothing about.
///     </para>
///     <para>
///         Deliberately fail-soft: a lookup failure yields fewer station lines, which degrades to
///         today's behaviour rather than blocking the rewrite. Filler statements are collected
///         regardless of their enabled flag or availability window, because the question is what
///         this bulletin ALREADY SAID, not what a fresh one would say. Bookend text is returned as
///         its stored TEMPLATE, tokens and all — the splitter matches those on literal fragments,
///         so the station's token overrides (which live on the schedule, not the job) are not needed.
///     </para>
/// </summary>
internal sealed class JobStationLineResolver(
    IFillerStatementsRepository fillerStatements,
    IBookendRepository bookends,
    ILogger logger)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(Job job, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        // These stores are independent: start both before awaiting either one so a slow filler
        // lookup cannot hold the bookend point read behind it (and vice versa).
        Task<Result<IReadOnlyList<FillerStatement>>> fillerTask =
            !string.IsNullOrWhiteSpace(job.Market)
                ? fillerStatements.GetStatementsAsync(job.Market, cancellationToken)
                : Task.FromResult(Result<IReadOnlyList<FillerStatement>>.Success([]));
        Task<Result<Option<Bookend>>> bookendTask =
            job.BookendId.HasValue && !string.IsNullOrWhiteSpace(job.BookendId.Value)
                ? bookends.GetAsync(job.BookendId.Value, cancellationToken)
                : Task.FromResult(Result<Option<Bookend>>.Success(Option<Bookend>.None));

        var fillerResult = await fillerTask;
        if (fillerResult.IsSuccess)
            lines.AddRange(fillerResult.Value.Select(s => s.Text));
        else
            logger.LogWarning(
                "[StationLines] Job {JobId} filler-statement lookup failed (ignored): {Error}",
                job.JobId, fillerResult.Error);

        var bookendResult = await bookendTask;
        if (bookendResult.IsFailure)
            logger.LogWarning(
                "[StationLines] Job {JobId} bookend '{BookendId}' lookup failed (ignored): {Error}",
                job.JobId, job.BookendId.GetValueOr(string.Empty), bookendResult.Error);
        else if (bookendResult.Value.HasValue
                 && TemplateFor(bookendResult.Value.Value, job.BookendVersion) is { } template)
        {
            lines.Add(template.IntroText);
            lines.Add(template.OutroText);
        }

        return lines;
    }

    /// <summary>
    ///     The exact version the job aired, falling back to the current one when that version has
    ///     since been pruned — a slightly-reworded opener still peels, which is the point.
    /// </summary>
    private static BookendTemplate? TemplateFor(Bookend bookend, Option<int> version)
    {
        if (version.HasValue)
        {
            var exact = bookend.Versions.FirstOrDefault(v => v.Version == version.Value);
            if (exact is not null)
                return exact.Template;
        }

        return bookend.Versions.Count > 0 ? bookend.CurrentTemplate : null;
    }
}
