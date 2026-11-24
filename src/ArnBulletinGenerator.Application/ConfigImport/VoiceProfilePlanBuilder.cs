using ArnBulletinGenerator.Domain.Contracts.Mapping;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for the VoiceProfiles config section.</summary>
internal static class VoiceProfilePlanBuilder
{
    public static void Plan(
        ConfigImportContext ctx,
        List<ConfigVoiceProfileDto> dtos, IReadOnlyList<VoiceProfile> existing, string actor,
        List<PlannedConfigItem> items, HashSet<string> knownIds)
    {
        // Two entries with the same id in one bundle would both plan Create against the existing
        // library and the second apply would upsert over the first — a report claiming Creates=2
        // while only one record survives. Reject at plan time (same guard shape as the other
        // sections' duplicate-id checks).
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var key = dto.Id ?? dto.Name ?? "(unnamed)";
            if (!string.IsNullOrWhiteSpace(dto.Id) && !seenIds.Add(dto.Id.Trim()))
            {
                items.Add(ErrorItem(ConfigSections.VoiceProfiles, key, dto.Name,
                    "Each voice profile id may appear only once."));
                continue;
            }

            string? invalid = Validate(dto);
            if (invalid is not null)
            {
                items.Add(ErrorItem(ConfigSections.VoiceProfiles, key, dto.Name, invalid));
                continue;
            }

            var settings = dto.Settings!.ToDomain();
            if (settings.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.VoiceProfiles, key, dto.Name, settings.Error));
                continue;
            }

            var match = FindMatch(existing, dto.Id, dto.Name, p => p.Id, p => p.Name);
            if (match is null)
            {
                knownIds.Add(dto.Id ?? $"pending:{key}");
                items.Add(new PlannedConfigItem(
                    new ConfigImportItemDto(ConfigSections.VoiceProfiles, key, dto.Name!, ConfigImportActions.Create),
                    ct => AsResult(ctx.VoiceProfiles.CreateAsync(new CreateVoiceProfileInput(
                        dto.Name!, dto.Description, dto.BaseVoiceId!, dto.BaseVoiceName ?? dto.BaseVoiceId!,
                        dto.Model!, settings.Value, dto.ChangeNote ?? "Config import", dto.PaceBoostWpm, dto.Id),
                        actor, ct))));
                continue;
            }

            var current = match.Versions[^1];
            bool changed =
                !NamesEqual(match.Name, dto.Name)
                || !DescriptionsEqual(match.Description, dto.Description)
                || !string.Equals(match.BaseVoiceId, dto.BaseVoiceId, StringComparison.Ordinal)
                || !string.Equals(current.Model, dto.Model, StringComparison.Ordinal)
                || current.ConfigurationKey != settings.Value.GetConfigurationKey(dto.BaseVoiceId!)
                || Math.Abs(current.PaceBoostWpm - dto.PaceBoostWpm) > 0.01;

            if (!changed)
            {
                items.Add(UnchangedItem(ConfigSections.VoiceProfiles, match.Id, match.Name));
                continue;
            }

            bool baseVoiceChanged = !string.Equals(match.BaseVoiceId, dto.BaseVoiceId, StringComparison.Ordinal);
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.VoiceProfiles, match.Id, dto.Name!, ConfigImportActions.Update),
                ct => AsResult(ctx.VoiceProfiles.UpdateAsync(match.Id, new UpdateVoiceProfileInput(
                    dto.Name!, dto.Description, dto.Model!, settings.Value,
                    dto.ChangeNote ?? "Config import",
                    baseVoiceChanged ? dto.BaseVoiceId : null,
                    baseVoiceChanged ? dto.BaseVoiceName : null,
                    dto.PaceBoostWpm), actor, ct))));
        }

        static string? Validate(ConfigVoiceProfileDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            if (string.IsNullOrWhiteSpace(dto.BaseVoiceId)) return "BaseVoiceId is required.";
            if (string.IsNullOrWhiteSpace(dto.Model)) return "Model is required.";
            if (dto.Settings is null) return "Settings is required.";
            if (dto.Id is not null && !StorageKey.IsValid(dto.Id)) return "Id is not a valid identifier.";
            return null;
        }
    }
}
