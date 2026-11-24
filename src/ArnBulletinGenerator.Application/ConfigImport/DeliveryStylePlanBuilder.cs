using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the DeliveryStyles config section.</summary>
internal static class DeliveryStylePlanBuilder
{
    public static void Plan(
        ConfigImportContext ctx,
        List<ConfigDeliveryStyleDto> dtos, IReadOnlyList<DeliveryStyle> existing, string actor,
        List<PlannedConfigItem> items, HashSet<string> knownIds)
    {
        // Two entries with the same id would both plan against the existing library and the
        // second apply would upsert over the first — reject at plan time.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var key = dto.Id ?? dto.Name ?? "(unnamed)";
            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add(dto.Id.Trim()))
            {
                items.Add(ErrorItem(ConfigSections.DeliveryStyles, key, dto.Name,
                    "Each delivery style id may appear only once."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                items.Add(ErrorItem(ConfigSections.DeliveryStyles, key, dto.Name, "Name is required."));
                continue;
            }

            if (dto.Policy is null)
            {
                items.Add(ErrorItem(ConfigSections.DeliveryStyles, key, dto.Name, "Policy is required."));
                continue;
            }

            if (dto.Id is not null && !StorageKey.IsValid(dto.Id))
            {
                items.Add(ErrorItem(ConfigSections.DeliveryStyles, key, dto.Name, "Id is not a valid identifier."));
                continue;
            }

            var policy = dto.Policy.ToDomain();
            var match = FindMatch(existing, dto.Id, dto.Name, s => s.Id, s => s.Name);
            if (match is null)
            {
                knownIds.Add(dto.Id ?? $"pending:{key}");
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.DeliveryStyles, key, dto.Name!, ConfigImportActions.Create),
                    ct => AsResult(ctx.DeliveryStyles.CreateAsync(new CreateDeliveryStyleInput(
                        dto.Name!, dto.Description, policy, dto.ChangeNote ?? "Config import", dto.Id), actor, ct))));
                continue;
            }

            bool changed =
                !NamesEqual(match.Name, dto.Name)
                || !DescriptionsEqual(match.Description, dto.Description)
                || match.CurrentPolicy.ConfigurationKey != policy.ConfigurationKey;

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.DeliveryStyles, match.Id, match.Name));
                continue;
            }

            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.DeliveryStyles, match.Id, dto.Name!, ConfigImportActions.Update),
                ct => AsResult(ctx.DeliveryStyles.UpdateAsync(match.Id, new UpdateDeliveryStyleInput(
                    dto.Name!, dto.Description, policy, dto.ChangeNote ?? "Config import"), actor, ct))));
        }
    }
}
