using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>
///     Plans Create/Update/Unchanged/Error actions for the Bookends config section. A bookend is
///     one intro/outro pair, so the planner runs once over the single list against the existing
///     library and seeds the known-id universe (see <see cref="ConfigImportService" />).
/// </summary>
internal static class BookendPlanBuilder
{
    public static void Plan(
        ConfigImportContext ctx,
        List<ConfigBookendDto> dtos,
        IReadOnlyList<Bookend> existing,
        string actor,
        List<PlannedConfigItem> items,
        HashSet<string> knownIds)
    {
        // Two entries with the same id would both plan against the existing library and the
        // second apply would upsert over the first — reject at plan time.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var key = dto.Id ?? dto.Name ?? "(unnamed)";
            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add(dto.Id.Trim()))
            {
                items.Add(ErrorItem(ConfigSections.Bookends, key, dto.Name,
                    "Each bookend id may appear only once."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                items.Add(ErrorItem(ConfigSections.Bookends, key, dto.Name, "Name is required."));
                continue;
            }

            var template = new BookendTemplate(dto.IntroText ?? "", dto.OutroText ?? "");
            var templateValidation = template.Validate();
            if (templateValidation.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.Bookends, key, dto.Name, templateValidation.Error));
                continue;
            }

            if (dto.Id is not null && !StorageKey.IsValid(dto.Id))
            {
                items.Add(ErrorItem(ConfigSections.Bookends, key, dto.Name, "Id is not a valid identifier."));
                continue;
            }

            var match = FindMatch(existing, dto.Id, dto.Name, b => b.Id, b => b.Name);
            if (match is null)
            {
                knownIds.Add(dto.Id ?? $"pending:{key}");
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.Bookends, key, dto.Name!, ConfigImportActions.Create),
                    ct => AsResult(ctx.Bookends.CreateAsync(new CreateBookendInput(
                        dto.Name!, dto.Description, dto.IntroText ?? "", dto.OutroText ?? "",
                        dto.ChangeNote ?? "Config import", actor, dto.Id), ct))));
                continue;
            }

            bool changed =
                !NamesEqual(match.Name, dto.Name)
                || !DescriptionsEqual(match.Description, dto.Description)
                || match.CurrentTemplate.ConfigurationKey != template.ConfigurationKey;

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.Bookends, match.Id, match.Name));
                continue;
            }

            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.Bookends, match.Id, dto.Name!, ConfigImportActions.Update),
                ct => AsResult(ctx.Bookends.UpdateAsync(new UpdateBookendInput(
                    match.Id, dto.Name!, dto.Description, dto.IntroText ?? "", dto.OutroText ?? "",
                    dto.ChangeNote ?? "Config import", actor), ct))));
        }
    }
}
