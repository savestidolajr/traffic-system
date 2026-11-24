using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists per-voice, per-settings speech-rate calibration so duration prediction does not need to recalibrate on every synthesis.</summary>
public interface IVoiceCalibrationRepository
{
    /// <summary>Reads a deduplicated set of calibration keys for one profile-list request.</summary>
    async Task<Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>> GetManyAsync(
        IReadOnlyCollection<VoiceCalibrationLookup> lookups,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, Option<VoiceCalibration>>(StringComparer.Ordinal);
        foreach (var lookup in lookups.Distinct())
        {
            var modelId = string.IsNullOrWhiteSpace(lookup.ModelId) ? "unknown" : lookup.ModelId.Trim();
            var result = modelId.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                ? await GetCalibrationAsync(lookup.VoiceId, lookup.Settings, cancellationToken)
                : await GetCalibrationAsync(lookup.VoiceId, lookup.Settings, cancellationToken, modelId);
            if (result.IsFailure)
                return Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>.Failure(result.Error);
            values[lookup.Key] = result.Value;
        }

        return Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>.Success(values);
    }

    Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(string voiceId, VoiceSettings settings,
        CancellationToken cancellationToken);

    /// <summary>Model-aware key used by broadcast calibration work.</summary>
    async Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(
        string voiceId, VoiceSettings settings, CancellationToken cancellationToken, string modelId)
        => await GetCalibrationAsync(voiceId, settings, cancellationToken);

    /// <summary>Returns every persisted calibration for export and administrative inspection.</summary>
    Task<Result<IReadOnlyList<VoiceCalibration>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveCalibrationAsync(VoiceCalibration calibration, CancellationToken cancellationToken);
}
