using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the PronunciationDictionaries config section.</summary>
internal static class PronunciationDictionaryPlanBuilder
{
    public static void Plan(
        ConfigImportContext ctx,
        List<ConfigPronunciationDictionaryDto> dtos, IReadOnlyList<PronunciationDictionary> existing, string actor,
        List<PlannedConfigItem> items, HashSet<string> knownIds, HashSet<string> globalDictionaryIds)
    {
        // Exactly one platform-wide dictionary may exist, so a bundle declaring two is rejected at
        // PLAN time rather than discovered halfway through applying it.
        var globalDtos = dtos
            .Where(d => ParseScope(d.Scope) == PronunciationScope.Global)
            .ToList();
        var incumbentGlobal = existing.FirstOrDefault(d => d.IsGlobal && d.IsActive);

        // Two entries with the same id would both plan against the existing library and the
        // second apply would upsert over the first — reject at plan time.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var key = dto.Id ?? dto.Name ?? "(unnamed)";
            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add(dto.Id.Trim()))
            {
                items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name,
                    "Each pronunciation dictionary id may appear only once."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name, "Name is required."));
                continue;
            }

            if (dto.Id is not null && !StorageKey.IsValid(dto.Id))
            {
                items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name,
                    "Id is not a valid identifier."));
                continue;
            }

            if (!PronunciationRuleMapping.TryParseScope(dto.Scope, out var scope))
            {
                items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name,
                    PronunciationRuleMapping.UnrecognisedScope(dto.Scope)));
                continue;
            }

            var rules = PronunciationRuleMapping.ParseRules(dto.Rules);
            if (rules.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name, rules.Error));
                continue;
            }

            var match = FindMatch(existing, dto.Id, dto.Name, d => d.Id, d => d.Name);

            // Keep the platform-wide id set in step with what this bundle will leave behind, so a
            // schedule assigned a promoted dictionary is rejected in the same plan.
            if (match is not null)
            {
                if (scope == PronunciationScope.Global)
                    globalDictionaryIds.Add(match.Id);
                else
                    globalDictionaryIds.Remove(match.Id);
            }
            else if (scope == PronunciationScope.Global && dto.Id is not null)
            {
                globalDictionaryIds.Add(dto.Id);
            }

            if (scope == PronunciationScope.Global)
            {
                if (globalDtos.Count > 1)
                {
                    items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name,
                        $"This bundle marks {globalDtos.Count} dictionaries as platform-wide; only one may be."));
                    continue;
                }

                // A collision with the incumbent is fine when the incumbent IS this dictionary.
                if (incumbentGlobal is not null
                    && (match is null || !string.Equals(incumbentGlobal.Id, match.Id, StringComparison.Ordinal)))
                {
                    items.Add(ErrorItem(ConfigSections.PronunciationDictionaries, key, dto.Name,
                        $"'{incumbentGlobal.Name}' is already the platform-wide pronunciation dictionary here. " +
                        "Demote it before importing another platform-wide dictionary."));
                    continue;
                }
            }

            if (match is null)
            {
                knownIds.Add(dto.Id ?? $"pending:{key}");
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.PronunciationDictionaries, key, dto.Name!,
                        ConfigImportActions.Create),
                    ct => AsResult(ctx.PronunciationDictionaries.CreateAsync(new CreatePronunciationDictionaryInput(
                        dto.Name!, dto.Description, rules.Value, dto.ChangeNote ?? "Config import", dto.Id, scope),
                        actor, ct))));
                continue;
            }

            bool changed =
                !NamesEqual(match.Name, dto.Name)
                || !DescriptionsEqual(match.Description, dto.Description)
                || match.Scope != scope
                || RuleSetKey(match.CurrentRules) != RuleSetKey(rules.Value);

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.PronunciationDictionaries, match.Id, match.Name));
                continue;
            }

            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.PronunciationDictionaries, match.Id, dto.Name!,
                    ConfigImportActions.Update),
                ct => AsResult(ctx.PronunciationDictionaries.UpdateAsync(match.Id,
                    new UpdatePronunciationDictionaryInput(
                            dto.Name!, dto.Description, rules.Value, dto.ChangeNote ?? "Config import",
                            Scope: scope),
                        actor, ct))));
        }

        static string RuleSetKey(IReadOnlyList<PronunciationRule> rules) =>
            string.Join(";", rules.Select(r => r.Signature).OrderBy(s => s, StringComparer.Ordinal));
    }

    private static PronunciationScope ParseScope(string? value) =>
        PronunciationRuleMapping.TryParseScope(value, out var scope) ? scope : PronunciationScope.Station;
}
