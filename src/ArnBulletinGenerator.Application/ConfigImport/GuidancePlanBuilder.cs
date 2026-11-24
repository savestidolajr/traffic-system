using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the Guidance config section.</summary>
internal static class GuidancePlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigGuidanceDto> dtos, string actor, List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyList<ConfigImportGuidanceScope>? existingScopes = null)
    {
        // Entries are matched within their (scope, scopeId) coordinates; list each referenced
        // scope once. An id supplied but absent from its scope imports as a create with that id.
        var scopeLists = new Dictionary<(GuidanceScope, string), IReadOnlyList<GuidanceEntry>>();

        // Two entries sharing an explicit id in the same scope bucket would both plan against the
        // existing entries and the second apply would upsert over the first — reject at plan time.
        // (Storage keys guidance rows by (scope partition, id), so the id is only unique per scope.)
        var seenIds = new HashSet<(GuidanceScope Scope, string ScopeId, string Id)>();

        foreach (var dto in dtos)
        {
            var key = dto.Id ?? dto.Text ?? "(unnamed)";
            var name = Truncate(dto.Text, 60);
            if (string.IsNullOrWhiteSpace(dto.Text))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name, "Text is required."));
                continue;
            }

            if (dto.Text.Length > ConfigValidationLimits.MaxTextLength)
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name,
                    $"Text must be at most {ConfigValidationLimits.MaxTextLength} characters."));
                continue;
            }

            if (!Enum.TryParse<GuidanceScope>(dto.Scope, true, out var scope))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name, "Scope must be Global, Market, or Station."));
                continue;
            }

            if (!Enum.TryParse<GuidanceType>(dto.Type, true, out var type))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name,
                    "Type must be Style, Editorial, Lexicon, Prohibition, or HouseStyle."));
                continue;
            }

            string scopeId = GuidanceEntry.NormalizeScopeId(scope, dto.ScopeId);
            if (scope != GuidanceScope.Global && string.IsNullOrWhiteSpace(scopeId))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name,
                    "ScopeId is required for Market/Station scope."));
                continue;
            }

            if (scope != GuidanceScope.Global && !StorageKey.IsValid(scopeId))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name, "ScopeId is not a valid identifier."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add((scope, scopeId, dto.Id.Trim())))
            {
                items.Add(ErrorItem(ConfigSections.Guidance, key, name,
                    "Each guidance id may appear only once."));
                continue;
            }

            if (!scopeLists.TryGetValue((scope, scopeId), out var scoped))
            {
                var indexed = existingScopes?.FirstOrDefault(s =>
                    s.Scope == scope && string.Equals(s.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
                if (indexed is not null)
                {
                    scoped = indexed.Entries;
                }
                else
                {
                    var listed = await ctx.Guidance.ListAsync(scope, scopeId, ct);
                    if (listed.IsFailure)
                        return Result.Failure($"Failed to list {scope} guidance ('{scopeId}'): {listed.Error}");
                    scoped = listed.Value;
                }
                scopeLists[(scope, scopeId)] = scoped;
            }

            var byId = string.IsNullOrWhiteSpace(dto.Id)
                ? null
                : scoped.FirstOrDefault(e => string.Equals(e.Id, dto.Id, StringComparison.OrdinalIgnoreCase));
            var match = byId ?? scoped.FirstOrDefault(e =>
                e.Type == type && string.Equals(e.Text, dto.Text.Trim(), StringComparison.OrdinalIgnoreCase));

            var now = ctx.TimeProvider.GetUtcNow().UtcDateTime;
            var review = dto.ReviewAfterUtc.HasValue
                ? Option<DateTime>.Some(dto.ReviewAfterUtc.Value)
                : Option<DateTime>.None;

            if (match is null)
            {
                var entry = string.IsNullOrWhiteSpace(dto.Id)
                    ? GuidanceEntry.Create(scope, scopeId, type, dto.Text, dto.Enabled, actor, now,
                        reviewAfterUtc: review)
                    : new GuidanceEntry(dto.Id!, scope, scopeId, type, dto.Text.Trim(), dto.Enabled,
                        actor, now, now, Option<string>.Some(GuidanceEntry.ManualSource), review);
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.Guidance, entry.Id, name!, ConfigImportActions.Create),
                    ct => ctx.Guidance.UpsertAsync(entry, ct)));
                continue;
            }

            bool changed =
                match.Type != type
                || !string.Equals(match.Text, dto.Text.Trim(), StringComparison.Ordinal)
                || match.Enabled != dto.Enabled
                || match.ReviewAfterUtc.Match(d => (DateTime?)d, () => null) != dto.ReviewAfterUtc;

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.Guidance, match.Id, Truncate(match.Text, 60)!));
                continue;
            }

            var updatedEntry = new GuidanceEntry(match.Id, scope, scopeId, type, dto.Text.Trim(), dto.Enabled,
                actor, now, now, Option<string>.Some(GuidanceEntry.ManualSource), review);
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.Guidance, match.Id, name!, ConfigImportActions.Update),
                ct => ctx.Guidance.UpsertAsync(updatedEntry, ct)));
        }

        return Result.Success();
    }
}
