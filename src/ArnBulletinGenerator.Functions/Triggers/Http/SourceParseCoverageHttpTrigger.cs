using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
/// Read-only operator surface for the deterministic parser coverage ledger. The persisted contract
/// supports structured, prose, and absent origins, but the live builder currently records only
/// structured-versus-absent values, so live prose share remains zero until a prose origin is
/// supplied.
/// </summary>
public sealed class SourceParseCoverageHttpTrigger(
    ISourceParseReportRepository repository,
    TimeProvider timeProvider,
    IMarketAccessService? marketAccess = null,
    IOptions<CoverageOptions>? coverageOptions = null,
    ILogger<SourceParseCoverageHttpTrigger>? logger = null)
{
    private ILogger<SourceParseCoverageHttpTrigger>? Logger { get; } = logger;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    [Function("GetSourceParseCoverage")]
    public async Task<HttpResponseData> Coverage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "coverage")]
        HttpRequestData req, FunctionContext ctx)
    {
        var range = ParseRange(req);
        if (range.Error is not null)
            return await HttpResponseHelper.BadRequestAsync(req, range.Error);

        var scope = await ResolveMarketsAsync(req, range.Market, ctx.CancellationToken);
        if (scope.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, scope.Error);

        var result = await repository.ListAsync(scope.Value, range.From, range.To, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var reports = result.Value
            .Where(report => report.AtUtc >= range.From && report.AtUtc <= range.To)
            .ToList();
        var items = CoverageCalculator.Calculate(reports, range.From, range.To);
        return await HttpResponseHelper.OkJsonAsync(req, new CoverageResponse(items, items.Count));
    }

    [Function("GetUnknownSourceVocabulary")]
    public async Task<HttpResponseData> UnknownVocabulary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "coverage/unknown")]
        HttpRequestData req, FunctionContext ctx)
    {
        var range = ParseRange(req);
        if (range.Error is not null)
            return await HttpResponseHelper.BadRequestAsync(req, range.Error);

        var scope = await ResolveMarketsAsync(req, range.Market, ctx.CancellationToken);
        if (scope.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, scope.Error);

        var result = await repository.ListAsync(scope.Value, range.From, range.To, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var reports = result.Value
            .Where(report => report.AtUtc >= range.From && report.AtUtc <= range.To)
            .ToList();
        var items = CoverageCalculator.UnknownVocabulary(reports);
        return await HttpResponseHelper.OkJsonAsync(req, new UnknownVocabularyResponse(items, items.Count));
    }

    private (string? Market, DateTimeOffset From, DateTimeOffset To, string? Error) ParseRange(HttpRequestData req)
    {
        DateTimeOffset to = MarketScopeRequest.ParseDate(MarketScopeRequest.Query(req, "to"))
            ?? timeProvider.GetUtcNow();
        DateTimeOffset from = MarketScopeRequest.ParseDate(MarketScopeRequest.Query(req, "from"))
            ?? MarketScopeRequest.ParseDate(MarketScopeRequest.Query(req, "since"))
            ?? to - DefaultWindow;

        if (to < from)
            return (null, from, to, "to must not be before from");
        if (to - from > MaxWindow)
            return (null, from, to, "date range too wide — max 366 days");

        return (MarketScopeRequest.Query(req, "market"), from, to, null);
    }

    private async Task<Result<IReadOnlyList<string>>> ResolveMarketsAsync(
        HttpRequestData req,
        string? singularMarket,
        CancellationToken cancellationToken)
    {
        CoverageOptions settings = coverageOptions?.Value ?? new CoverageOptions();
        var requested = new List<string>();
        if (!string.IsNullOrWhiteSpace(singularMarket))
            requested.AddRange(MarketScopeRequest.ParseMarkets(singularMarket));
        requested.AddRange(MarketScopeRequest.ParseMarkets(MarketScopeRequest.Query(req, "markets")));

        var distinctRequested = requested
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string market in distinctRequested)
        {
            if (!ArnBulletinGenerator.Domain.Models.StorageKey.IsValid(market))
                return Result<IReadOnlyList<string>>.Failure(
                    $"market '{market}' contains characters that are not allowed.");
        }

        // A scoped operator's assignment is the existing authorized bounded market set. An
        // unrestricted/admin caller must opt into a bounded list instead of causing a cross-table
        // partition scan. The optional configured set is the trusted operator-wide read option.
        var principal = ClientPrincipal.Parse(req);
        var allowed = Option<IReadOnlyList<string>>.None;
        if (marketAccess is not null && principal is not null)
            allowed = await marketAccess.GetAllowedMarketsAsync(
                principal.ObjectId, principal.IsAdmin, cancellationToken);

        if (distinctRequested.Count == 0)
        {
            if (allowed.IsSome && allowed.Value.Count > 0)
                distinctRequested.AddRange(allowed.Value);
            else
            {
                var configured = settings.BoundedReadsEnabled
                    ? settings.BoundedMarkets.AsEnumerable()
                    : Array.Empty<string>();
                distinctRequested.AddRange(configured
                    .Where(market => !string.IsNullOrWhiteSpace(market))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }

        if (allowed.IsSome)
        {
            var allowedSet = new HashSet<string>(
                allowed.Value.Where(market => !string.IsNullOrWhiteSpace(market)),
                StringComparer.OrdinalIgnoreCase);
            distinctRequested = distinctRequested
                .Where(allowedSet.Contains)
                .ToList();
        }

        if (distinctRequested.Count == 0)
            return Result<IReadOnlyList<string>>.Failure(
                "A market or an authorized bounded market set is required.");

        int maxMarkets = Math.Max(1, settings.MaxMarkets);
        if (distinctRequested.Count > maxMarkets)
            return Result<IReadOnlyList<string>>.Failure(
                $"At most {maxMarkets} markets may be requested at once.");

        return Result<IReadOnlyList<string>>.Success(distinctRequested);
    }

    internal sealed record CoverageResponse(IReadOnlyList<CoverageTrendItem> Items, int Total);

    internal sealed record UnknownVocabularyResponse(
        IReadOnlyList<UnknownVocabularyItem> Items, int Total);

    /// <summary>Structured/prose counts for one source and field, including the two-window trend.</summary>
    internal sealed record CoverageTrendItem(
        string SourceId,
        string SchemaVersion,
        string Field,
        int Structured,
        int Prose,
        int Absent,
        int Total,
        double ProseShare,
        double FirstWindowProseShare,
        double LastWindowProseShare,
        double ProseShareDelta,
        bool ProseFalling);

    /// <summary>A deduplicated vocabulary value ready to be added to the incident crosswalk.</summary>
    internal sealed record UnknownVocabularyItem(
        string SourceId,
        string Value,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        int Count,
        string ToDo);

    internal static class CoverageCalculator
    {
        internal static IReadOnlyList<CoverageTrendItem> Calculate(
            IReadOnlyList<StoredParseReport> reports,
            DateTimeOffset from,
            DateTimeOffset to)
        {
            DateTimeOffset midpoint = from + (to - from) / 2;
            var rows = reports
                .SelectMany(report => report.Report.Fields.Select(field => new FieldObservation(
                    report.Report.SourceId, report.Report.SchemaVersion, field, report.AtUtc < midpoint)))
                .GroupBy(x => (x.SourceId, x.Field.Field), StringTupleComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.Where(x => x.IsFirstWindow).Select(x => x.Field).ToList();
                    var last = group.Where(x => !x.IsFirstWindow).Select(x => x.Field).ToList();
                    int structured = group.Sum(x => x.Field.Structured);
                    int prose = group.Sum(x => x.Field.Prose);
                    int absent = group.Sum(x => x.Field.Absent);
                    double firstShare = ProseShare(first);
                    double lastShare = ProseShare(last);
                    return new CoverageTrendItem(
                        group.Key.SourceId,
                        group.Select(x => x.SchemaVersion).FirstOrDefault() ?? string.Empty,
                        group.Key.Field,
                        structured,
                        prose,
                        absent,
                        structured + prose + absent,
                        ProseShare(structured, prose),
                        firstShare,
                        lastShare,
                        lastShare - firstShare,
                        first.Count > 0 && last.Count > 0 && lastShare < firstShare);
                })
                .OrderBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Field, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return rows;
        }

        internal static IReadOnlyList<UnknownVocabularyItem> UnknownVocabulary(
            IReadOnlyList<StoredParseReport> reports)
        {
            var entries = reports
                .SelectMany(report => report.Report.UnknownVocabulary.Select(value =>
                    new UnknownObservation(report.Report.SourceId, value, report.AtUtc)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(x => (x.SourceId, Field: x.Value.Trim()), StringTupleComparer.OrdinalIgnoreCase)
                .Select(group => new UnknownVocabularyItem(
                    group.Key.SourceId,
                    group.Key.Field,
                    group.Min(x => x.AtUtc),
                    group.Max(x => x.AtUtc),
                    group.Count(),
                    $"Add '{group.Key.Field}' to IncidentClassCrosswalk for {group.Key.SourceId}"))
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return entries;
        }

        private static double ProseShare(IReadOnlyList<FieldCoverage> fields) =>
            fields.Count == 0 ? 0 : ProseShare(fields.Sum(f => f.Structured), fields.Sum(f => f.Prose));

        private static double ProseShare(int structured, int prose)
        {
            int known = structured + prose;
            return known == 0 ? 0 : (double)prose / known;
        }

        private sealed record FieldObservation(
            string SourceId, string SchemaVersion, FieldCoverage Field, bool IsFirstWindow);

        private sealed record UnknownObservation(string SourceId, string Value, DateTimeOffset AtUtc);

        private sealed class StringTupleComparer : IEqualityComparer<(string SourceId, string Field)>
        {
            internal static StringTupleComparer OrdinalIgnoreCase { get; } = new();

            public bool Equals((string SourceId, string Field) x, (string SourceId, string Field) y) =>
                string.Equals(x.SourceId, y.SourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Field, y.Field, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string SourceId, string Field) obj) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceId),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Field));
        }
    }
}
