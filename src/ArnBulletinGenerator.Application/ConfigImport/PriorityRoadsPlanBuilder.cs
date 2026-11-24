using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Validation;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans the per-market replace-list PriorityRoads config section.</summary>
internal static class PriorityRoadsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<PriorityRoadsDto> dtos, string actor, List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>? existingByMarket = null)
    {
        // The section is keyed by market, so a duplicated market would plan twice and the second
        // apply would replace the first — reject at plan time.
        var seenMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var market = dto.Market?.Trim() ?? string.Empty;
            if (!StorageKey.IsValid(market))
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market, "Market is not a valid identifier."));
                continue;
            }

            if (!seenMarkets.Add(market))
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    "Each market may appear only once."));
                continue;
            }

            // Same validate-then-build order as the interactive PUT: a bundle that silently lost a
            // blank-named or duplicated road would report a clean import while discarding config.
            var incoming = dto.Roads ?? [];

            if (incoming.Count > ConfigValidationLimits.MaxRoads)
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    $"At most {ConfigValidationLimits.MaxRoads} priority roads per market (found {incoming.Count})."));
                continue;
            }

            if (incoming.Any(r => r is null || string.IsNullOrWhiteSpace(r.Name)))
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    "Every priority road needs a name."));
                continue;
            }

            if (incoming.Any(r => r!.Name.Trim().Length > ConfigValidationLimits.MaxRoadLength))
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    $"Road names must be at most {ConfigValidationLimits.MaxRoadLength} characters."));
                continue;
            }

            if (incoming.Any(r => OperatorTextGuard.HasControlOrFormatCharacters(r!.Name)))
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    $"Every priority road name {OperatorTextGuard.RejectionMessage}."));
                continue;
            }

            var invalidFlowTier = incoming.FirstOrDefault(r =>
                !PriorityRoadMapping.TryWireToTier(r!.FlowTier, out _));
            if (invalidFlowTier is not null)
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    PriorityRoadMapping.InvalidFlowTierMessage(invalidFlowTier.FlowTier)));
                continue;
            }

            var duplicated = incoming
                .GroupBy(r => r!.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicated.Count > 0)
            {
                items.Add(ErrorItem(ConfigSections.PriorityRoads, market, market,
                    $"Each road may appear once. Duplicated: {string.Join(", ", duplicated)}."));
                continue;
            }

            var roads = incoming
                .Select(r => new PriorityRoad(r!.Name.Trim(), r.BoostIncidents,
                    PriorityRoadMapping.WireToTier(r.FlowTier), r.Enabled))
                .ToList();

            var existing = existingByMarket is not null && existingByMarket.TryGetValue(market, out var snapshotRoads)
                ? Result<IReadOnlyList<PriorityRoad>>.Success(snapshotRoads)
                : await ctx.PriorityRoads.GetRoadsAsync(market, ct);
            if (existing.IsFailure)
                return Result.Failure($"Failed to read priority roads for '{market}': {existing.Error}");

            // The repository's replace-list order is not configuration: exports and table reads
            // may enumerate the same roads differently. Compare the complete records as a set so
            // a reordered list is unchanged while any field edit still plans a replacement.
            if (existing.Value.Count > 0
                && existing.Value.Count == roads.Count
                && existing.Value.ToHashSet().SetEquals(roads))
            {
                items.Add(UnchangedItem(ConfigSections.PriorityRoads, market,
                    $"{market} ({roads.Count} roads)"));
                continue;
            }

            // Keep the existing create/update distinction for an empty market. An empty bundle
            // row is still a deliberate create request, while a populated row replaced by an
            // empty list remains an update.
            var action = existing.Value.Count == 0 ? ConfigImportActions.Create : ConfigImportActions.Update;
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.PriorityRoads, market, $"{market} ({roads.Count} roads)", action),
                ct => ctx.PriorityRoads.SetRoadsAsync(market, roads, actor, ct)));
        }

        return Result.Success();
    }
}
