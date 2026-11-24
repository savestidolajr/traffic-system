using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.ConfigTransfer;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans the per-market replace-list FillerStatements config section.</summary>
internal static class FillerStatementsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<FillerStatementsDto> dtos, string actor, List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>? existingByMarket = null)
    {
        // The section is keyed by market, so a duplicated market would plan twice and the second
        // apply would replace the first — reject at plan time.
        var seenMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var market = dto.Market?.Trim() ?? string.Empty;
            if (!StorageKey.IsValid(market))
            {
                items.Add(ErrorItem(ConfigSections.FillerStatements, market, market,
                    "Market is not a valid identifier."));
                continue;
            }

            if (!seenMarkets.Add(market))
            {
                items.Add(ErrorItem(ConfigSections.FillerStatements, market, market,
                    "Each market may appear only once."));
                continue;
            }

            var incoming = (dto.Statements ?? [])
                .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Text))
                .ToList();

            // Two statements sharing an explicit id would previously collapse to the first
            // (silent dedupe) — reject instead, same shape as the road/word within-list guards.
            var duplicatedStatementIds = incoming
                .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                .GroupBy(s => s.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicatedStatementIds.Count > 0)
            {
                items.Add(ErrorItem(ConfigSections.FillerStatements, market, market,
                    $"Each filler statement id may appear only once. Duplicated: {string.Join(", ", duplicatedStatementIds)}."));
                continue;
            }

            var statements = new List<FillerStatement>(incoming.Count);
            bool failed = false;
            foreach (var s in incoming)
            {
                // Same conversion the PUT route uses, so a malformed window fails the plan with
                // the same message instead of importing a rule that never matches. One bad
                // statement fails the whole market (replace-list semantics: partial import
                // would silently drop the other statements).
                var availability = FillerAvailabilityMapping.ToDomain(s.Availability);
                if (availability.IsFailure)
                {
                    string who = string.IsNullOrWhiteSpace(s.Id) ? s.Text.Trim() : s.Id.Trim();
                    items.Add(ErrorItem(ConfigSections.FillerStatements, market, market,
                        $"Statement '{Truncate(who, 80)}' has an invalid availability window: {availability.Error}"));
                    failed = true;
                    break;
                }
                statements.Add(new FillerStatement(
                    string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString("N") : s.Id.Trim(),
                    s.Text.Trim(), s.Enabled, availability.Value));
            }
            if (failed)
                continue;

            string? invalid = Validate(statements);
            if (invalid is not null)
            {
                items.Add(ErrorItem(ConfigSections.FillerStatements, market, market, invalid));
                continue;
            }

            var existing = existingByMarket is not null && existingByMarket.TryGetValue(market, out var snapshotStatements)
                ? Result<IReadOnlyList<FillerStatement>>.Success(snapshotStatements)
                : await ctx.FillerStatements.GetStatementsAsync(market, ct);
            if (existing.IsFailure)
                return Result.Failure($"Failed to read filler statements for '{market}': {existing.Error}");
            if (SetEquals(existing.Value.Select(StatementKey), statements.Select(StatementKey)))
            {
                items.Add(UnchangedItem(ConfigSections.FillerStatements, market,
                    $"{market} ({statements.Count} statements)"));
                continue;
            }

            var action = existing.Value.Count == 0 ? ConfigImportActions.Create : ConfigImportActions.Update;
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.FillerStatements, market,
                    $"{market} ({statements.Count} statements)", action),
                ct => ApplyFillerStatementsWithInvalidationAsync(ctx, market, existing.Value, statements, actor, ct)));
        }

        return Result.Success();

        static string? Validate(IReadOnlyList<FillerStatement> statements)
        {
            if (statements.Count > ConfigValidationLimits.MaxStatements)
                return $"At most {ConfigValidationLimits.MaxStatements} filler statements per market.";
            var tooLong = statements.FirstOrDefault(s => s.Text.Length > ConfigValidationLimits.MaxStatementLength);
            if (tooLong is not null)
                return $"Statement text must be at most {ConfigValidationLimits.MaxStatementLength} characters.";
            return null;
        }

        static string StatementKey(FillerStatement s) =>
            $"{s.Id.ToLowerInvariant()}|{s.Text}|{s.Enabled}|{AvailabilityKey(s.Availability)}";

        static string AvailabilityKey(FillerAvailability? availability)
        {
            if (availability is null)
                return "-";
            return $"{availability.StartDate}|{availability.EndDate}|"
                + string.Join(",", availability.Days.Select(d => d.ToString()))
                + $"|{availability.StartLocal}|{availability.EndLocal}";
        }
    }

    private static async Task<Result> ApplyFillerStatementsWithInvalidationAsync(
        ConfigImportContext ctx,
        string market, IReadOnlyList<FillerStatement> oldStatements, IReadOnlyList<FillerStatement> newStatements,
        string actor, CancellationToken ct)
    {
        var failures = await FillerAudioCacheInvalidator.InvalidateChangedAsync(
            ctx.FillerAudioCache, oldStatements, newStatements, ct);
        foreach (var (id, error) in failures)
            ctx.Logger.LogWarning(
                "[FillerAudioCache] Failed to invalidate cached audio for statement {StatementId}: {Error}",
                id, error);
        return await ctx.FillerStatements.SetStatementsAsync(market, newStatements, actor, ct);
    }
}
