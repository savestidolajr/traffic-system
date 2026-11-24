using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Store for ARN's numeric editorial settings. One row per (Scope, ScopeId); resolution across
///     scopes is <see cref="EditorialSettings.Resolve" />'s job, so this stays a plain reader/writer.
/// </summary>
public interface IEditorialSettingsRepository
{
    /// <summary>
    ///     Every row that could apply to <paramref name="market" /> (+ optional
    ///     <paramref name="station" />): global, that market's, and that station's.
    /// </summary>
    Task<Result<IReadOnlyList<EditorialSettings>>> GetApplicableAsync(
        string market,
        Option<string> station,
        CancellationToken cancellationToken = default);

    /// <summary>The row stored at one scope, or null when nothing is configured there.</summary>
    Task<Result<EditorialSettings?>> GetAsync(
        EditorialScope scope,
        string scopeId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the row at its scope.</summary>
    Task<Result> SaveAsync(EditorialSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Removes the row, reverting that scope to the level below (ultimately the shipped defaults).</summary>
    Task<Result> DeleteAsync(
        EditorialScope scope,
        string scopeId,
        CancellationToken cancellationToken = default);
}
