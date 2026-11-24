using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Models;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for global operator assignments.</summary>
internal static class OperatorAssignmentsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigOperatorAssignmentDto> dtos, string actor,
        List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyList<OperatorAssignment>? existingAssignments = null)
    {
        var existingResult = existingAssignments is not null
            ? Result<IReadOnlyList<OperatorAssignment>>.Success(existingAssignments)
            : await ctx.OperatorAssignments.ListAsync(ct);
        if (existingResult.IsFailure)
            return Result.Failure($"Failed to list operator assignments: {existingResult.Error}");
        var existing = existingResult.Value;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            var key = dto.ObjectId?.Trim() ?? string.Empty;
            if (!StorageKey.IsValid(key))
            {
                items.Add(ErrorItem(ConfigSections.OperatorAssignments, key, dto.DisplayName,
                    "ObjectId is not a valid identifier."));
                continue;
            }

            if (!seen.Add(key))
            {
                items.Add(ErrorItem(ConfigSections.OperatorAssignments, key, dto.DisplayName,
                    "Each operator object id may appear only once."));
                continue;
            }

            if (dto.Markets is null)
            {
                items.Add(ErrorItem(ConfigSections.OperatorAssignments, key, dto.DisplayName,
                    "Markets must be supplied as an array (empty array = unrestricted)."));
                continue;
            }

            if (dto.Markets.Any(m => string.IsNullOrWhiteSpace(m) || !StorageKey.IsValid(m.Trim())))
            {
                items.Add(ErrorItem(ConfigSections.OperatorAssignments, key, dto.DisplayName,
                    "Every market must be a valid identifier."));
                continue;
            }

            var markets = dto.Markets.Select(m => m.Trim()).ToList();
            if (markets.GroupBy(m => m, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                items.Add(ErrorItem(ConfigSections.OperatorAssignments, key, dto.DisplayName,
                    "Each market may appear only once per operator."));
                continue;
            }

            var prior = existing.FirstOrDefault(a =>
                string.Equals(a.ObjectId, key, StringComparison.OrdinalIgnoreCase));
            var updatedAt = dto.UpdatedAtUtc?.ToUniversalTime() ??
                            (prior?.UpdatedAtUtc ?? ctx.TimeProvider.GetUtcNow().UtcDateTime);
            var updatedBy = string.IsNullOrWhiteSpace(dto.UpdatedBy)
                ? (prior?.UpdatedBy ?? actor)
                : dto.UpdatedBy.Trim();
            var assignment = new OperatorAssignment(
                key,
                dto.DisplayName?.Trim() ?? string.Empty,
                markets,
                updatedAt,
                updatedBy);

            if (prior is not null && Equivalent(prior, assignment))
            {
                items.Add(UnchangedItem(ConfigSections.OperatorAssignments, prior.ObjectId,
                    prior.DisplayName));
                continue;
            }

            var action = prior is null ? ConfigImportActions.Create : ConfigImportActions.Update;
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.OperatorAssignments, key,
                    assignment.DisplayName, action),
                async cancellationToken =>
                {
                    var result = await ctx.OperatorAssignments.UpsertAsync(
                        assignment, cancellationToken);
                    if (result.IsSuccess)
                        ctx.MarketAccessCacheInvalidator?.InvalidateScope(assignment.ObjectId);
                    return result;
                }));
        }

        return Result.Success();

        static bool Equivalent(OperatorAssignment current, OperatorAssignment incoming) =>
            string.Equals(current.ObjectId, incoming.ObjectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.DisplayName, incoming.DisplayName, StringComparison.Ordinal)
            && SetEquals(
                current.Markets.Select(m => m.Trim().ToLowerInvariant()),
                incoming.Markets.Select(m => m.Trim().ToLowerInvariant()))
            && current.UpdatedAtUtc.ToUniversalTime() == incoming.UpdatedAtUtc.ToUniversalTime()
            && string.Equals(current.UpdatedBy, incoming.UpdatedBy, StringComparison.Ordinal);
    }
}
