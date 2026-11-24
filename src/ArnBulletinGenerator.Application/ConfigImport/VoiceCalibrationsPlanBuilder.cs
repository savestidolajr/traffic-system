using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Models;
using static ArnBulletinGenerator.Application.ConfigImport.ConfigImportHelpers;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Plans Create/Update/Unchanged/Error actions for persisted voice calibrations.</summary>
internal static class VoiceCalibrationsPlanBuilder
{
    public static async Task<Result> PlanAsync(
        ConfigImportContext ctx,
        List<ConfigVoiceCalibrationDto> dtos, string actor,
        List<PlannedConfigItem> items, CancellationToken ct,
        IReadOnlyList<VoiceCalibration>? existingCalibrations = null)
    {
        var existingResult = existingCalibrations is not null
            ? Result<IReadOnlyList<VoiceCalibration>>.Success(existingCalibrations)
            : await ctx.VoiceCalibrations.ListAsync(ct);
        if (existingResult.IsFailure)
            return Result.Failure($"Failed to list voice calibrations: {existingResult.Error}");
        var existing = existingResult.Value;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            var key = dto.ConfigurationKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key) || !StorageKey.IsValid(key))
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, dto.VoiceId,
                    "ConfigurationKey is required and must be a valid identifier."));
                continue;
            }

            if (!seen.Add(key))
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, dto.VoiceId,
                    "Each configuration key may appear only once."));
                continue;
            }

            var voiceId = dto.VoiceId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(voiceId) || !StorageKey.IsValid(voiceId))
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, voiceId,
                    "VoiceId is required and must be a valid identifier."));
                continue;
            }

            if (dto.Settings is null)
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, voiceId,
                    "Settings are required."));
                continue;
            }

            var settingsResult = VoiceSettings.Create(
                dto.Settings.Stability, dto.Settings.SimilarityBoost, dto.Settings.Style,
                dto.Settings.UseSpeakerBoost, dto.Settings.Speed);
            if (settingsResult.IsFailure)
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, voiceId,
                    $"Settings are invalid: {settingsResult.Error}"));
                continue;
            }

            var settings = settingsResult.Value;
            if (!string.Equals(settings.GetConfigurationKey(voiceId), key, StringComparison.Ordinal))
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, voiceId,
                    "ConfigurationKey does not match VoiceId and Settings."));
                continue;
            }

            if (!ValidMetric(dto.SyllablesPerSecond) || !ValidMetric(dto.SyllablesPerWord)
                || !ValidMetric(dto.WordsPerMinute) || !ValidMetric(dto.SyllablesPerMinute)
                || !ValidMetric(dto.ReferenceDurationSeconds) || dto.SyllablesPerSecond < 0
                || dto.SyllablesPerWord < 0 || dto.WordsPerMinute < 0 || dto.SyllablesPerMinute < 0
                || dto.ReferenceDurationSeconds < 0 || dto.RunCount < 0 || dto.ReferenceSyllableCount < 0
                || !ValidMetric(dto.ValidForDays) || dto.ValidForDays <= 0)
            {
                items.Add(ErrorItem(ConfigSections.VoiceCalibrations, key, voiceId,
                    "Calibration metrics must be finite, non-negative values and ValidForDays must be positive."));
                continue;
            }

            var calibration = new VoiceCalibration(key, voiceId, settings)
            {
                Provider = dto.Provider?.Trim() ?? string.Empty,
                SyllablesPerSecond = dto.SyllablesPerSecond,
                SyllablesPerWord = dto.SyllablesPerWord,
                WordsPerMinute = dto.WordsPerMinute,
                SyllablesPerMinute = dto.SyllablesPerMinute,
                RunCount = dto.RunCount,
                ReferenceSyllableCount = dto.ReferenceSyllableCount,
                ReferenceDurationSeconds = dto.ReferenceDurationSeconds,
                CalibratedAt = dto.CalibratedAt?.ToUniversalTime() ?? default,
                ValidFor = TimeSpan.FromDays(dto.ValidForDays)
            };

            var prior = existing.FirstOrDefault(c =>
                string.Equals(c.ConfigurationKey, key, StringComparison.OrdinalIgnoreCase));
            if (prior is not null && Equivalent(prior, calibration))
            {
                items.Add(UnchangedItem(ConfigSections.VoiceCalibrations, key, voiceId));
                continue;
            }

            var action = prior is null ? ConfigImportActions.Create : ConfigImportActions.Update;
            items.Add(new PlannedConfigItem(
                new ConfigImportItemDto(ConfigSections.VoiceCalibrations, key, voiceId, action),
                cancellationToken => ctx.VoiceCalibrations.SaveCalibrationAsync(calibration, cancellationToken)));
        }

        return Result.Success();

        static bool ValidMetric(double value) => double.IsFinite(value);

        static bool Equivalent(VoiceCalibration current, VoiceCalibration incoming) =>
            string.Equals(current.ConfigurationKey, incoming.ConfigurationKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.VoiceId, incoming.VoiceId, StringComparison.Ordinal)
            && current.Settings == incoming.Settings
            && string.Equals(current.Provider, incoming.Provider, StringComparison.Ordinal)
            && Canonical(current.SyllablesPerSecond) == Canonical(incoming.SyllablesPerSecond)
            && Canonical(current.SyllablesPerWord > 0 ? current.SyllablesPerWord : 1.4) == Canonical(incoming.SyllablesPerWord > 0 ? incoming.SyllablesPerWord : 1.4)
            && Canonical(current.WordsPerMinute > 0 ? current.WordsPerMinute : DerivedWpm(current)) == Canonical(incoming.WordsPerMinute > 0 ? incoming.WordsPerMinute : DerivedWpm(incoming))
            && Canonical(current.SyllablesPerMinute > 0 ? current.SyllablesPerMinute : current.SyllablesPerSecond * 60) == Canonical(incoming.SyllablesPerMinute > 0 ? incoming.SyllablesPerMinute : incoming.SyllablesPerSecond * 60)
            && current.RunCount == incoming.RunCount
            && current.ReferenceSyllableCount == incoming.ReferenceSyllableCount
            && Canonical(current.ReferenceDurationSeconds) == Canonical(incoming.ReferenceDurationSeconds)
            && current.CalibratedAt.ToUniversalTime() == incoming.CalibratedAt.ToUniversalTime()
            && Canonical(current.ValidFor.TotalDays) == Canonical(incoming.ValidFor.TotalDays);

        static double DerivedWpm(VoiceCalibration calibration)
        {
            var syllablesPerWord = calibration.SyllablesPerWord > 0 ? calibration.SyllablesPerWord : 1.4;
            var syllablesPerMinute = calibration.SyllablesPerMinute > 0
                ? calibration.SyllablesPerMinute
                : calibration.SyllablesPerSecond * 60;
            return syllablesPerWord > 0 ? syllablesPerMinute / syllablesPerWord : 0;
        }

        static double Canonical(double value) => Math.Round(value, 8, MidpointRounding.ToEven);
    }
}
