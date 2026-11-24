using System.Reflection;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Loads the immutable fitted spoken-duration calibration from the Infrastructure assembly.
///     Keeping the JSON embedded avoids a runtime file-system layout and makes a deployment's
///     coefficients part of the assembly it executes.
/// </summary>
public static class EmbeddedSpokenDurationCalibration
{
    // Schema v3 uses one pooled token shape and one scale per voice.
    private const string ResourceSuffix = "Audio.calibration.spoken-duration-v3.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Loads the generated v3 calibration or fails fast if the assembly is incomplete.</summary>
    public static SpokenDurationCalibration Load()
    {
        Assembly assembly = typeof(EmbeddedSpokenDurationCalibration).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedded spoken-duration calibration resource ending in '{ResourceSuffix}' was not found.");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded calibration resource '{resourceName}' could not be opened.");

        return JsonSerializer.Deserialize<SpokenDurationCalibration>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded spoken-duration calibration was empty.");
    }
}
