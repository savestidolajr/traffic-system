using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Maps the domain progress document to the PascalCase HTTP contract. Keeping the projection in
///     Application makes timestamp normalization and wire enum names independently unit-testable.
/// </summary>
public static class JobProgressMappingExtensions
{
    /// <summary>Creates the operator-facing progress DTO with optional market duration history.</summary>
    public static JobProgressDto ToProgressDto(
        this JobProgressDocument document,
        IReadOnlyDictionary<string, double>? typicalSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var events = document.Events
            .Select(progressEvent => new JobProgressEventDto(
                IsoTimestamp(progressEvent.At),
                progressEvent.Attempt,
                progressEvent.Stage,
                progressEvent.Kind,
                progressEvent.Detail,
                progressEvent.Counts,
                progressEvent.DurationMs,
                progressEvent.Model,
                progressEvent.Route))
            .ToList();

        IReadOnlyDictionary<string, double>? effectiveTypical = typicalSeconds ?? document.TypicalSeconds;
        IReadOnlyDictionary<string, double>? typical = effectiveTypical is null
            ? null
            : new Dictionary<string, double>(effectiveTypical, StringComparer.Ordinal);

        return new JobProgressDto(
            document.JobId,
            document.Status,
            document.Stage,
            IsoTimestamp(document.StageStartedAt),
            document.Attempt,
            document.MaxAttempts,
            IsoTimestamp(document.UpdatedAt),
            document.Version,
            events,
            typical);
    }

    private static string IsoTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : value ?? string.Empty;
}
