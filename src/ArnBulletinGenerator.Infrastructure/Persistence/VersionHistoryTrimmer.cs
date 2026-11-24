using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Keeps an append-only version history inside Azure Table's single-property ceiling.
///     <para>
///     Every versioned library — voice profiles, delivery styles, pronunciation dictionaries and
///     style rules — serialises its ENTIRE version history into one <c>VersionsJson</c> property,
///     and Azure Table caps a property at 64KB / 32,768 UTF-16 chars. Unbounded growth does not
///     degrade, it DEADLOCKS: shrinking the history is itself a write, and the write is what the
///     ceiling rejects. Once a record overflows it can never be edited again through the API, and
///     <c>restore</c> — also an appending write — fails with it.
///     </para>
///     <para>
///     Both halves are observed, not theoretical. Melbourne Roads reached 31,551 of 32,768 chars on
///     dev (2026-07-28) and the next save was rejected. A throwaway delivery style was then driven
///     to the wall deliberately (2026-07-28): six content-varying edits reached 31,456 chars, the
///     next returned HTTP 500 from storage, and after two further minimal edits even a four-character
///     change failed. The global style-rule set was found sitting at 30,866 chars — 94.2% of the
///     ceiling — one ordinary edit away from the same fate.
///     </para>
///     Trimming the OLDEST snapshots keeps the record writable. The current version is never dropped.
/// </summary>
internal static class VersionHistoryTrimmer
{
    /// <summary>
    ///     Budget for the serialised history, held below the 32,768-char ceiling so the entity's
    ///     other properties and one further full version still fit on the next save.
    /// </summary>
    internal const int VersionsJsonBudget = 24_000;

    /// <summary>
    ///     Newest versions that fit <see cref="VersionsJsonBudget" />, oldest dropped first. The
    ///     most recent version is always retained even if it alone exceeds the budget — a record
    ///     with no versions cannot be reconstituted and would vanish from its library.
    /// </summary>
    internal static IReadOnlyList<T> TrimToFit<T>(
        IReadOnlyList<T> versions, Func<T, int> versionNumber, out string json)
    {
        var ordered = versions.OrderBy(versionNumber).ToList();
        while (true)
        {
            json = JsonSerializer.Serialize(ordered, JsonDefaults.Storage);
            if (json.Length <= VersionsJsonBudget || ordered.Count <= 1)
                return ordered;
            ordered.RemoveAt(0);
        }
    }

    /// <summary>
    ///     Azure Table's hard per-property ceiling. Trimming keeps us under
    ///     <see cref="VersionsJsonBudget" />; this is the wall itself. Single source of truth:
    ///     the domain constant the service's pre-provider size check serialises against.
    /// </summary>
    internal const int PropertyCeiling = ArnBulletinGenerator.Domain.Models.PronunciationDictionary.StoragePropertyCeiling;

    /// <summary>
    ///     Serialises the history for storage, trimming to fit and warning when snapshots are
    ///     dropped. Silent trimming would make the loss unnoticeable, so the discard is always
    ///     logged with the retained range.
    ///     <para>
    ///     Fails when the CURRENT version alone exceeds the ceiling — trimming cannot help, because
    ///     dropping the current version would make the record unreconstitutable and it would vanish
    ///     from its library. Storage would otherwise reject the write with an opaque HTTP 500
    ///     carrying a raw Azure payload, so the caller gets an actionable message instead.
    ///     </para>
    /// </summary>
    internal static bool TrySerialize<T>(
        IReadOnlyList<T> versions, Func<T, int> versionNumber, ILogger logger, string library, string id,
        out string json, out OperationalFailure? failure)
    {
        var retained = TrimToFit(versions, versionNumber, out json);
        if (retained.Count < versions.Count)
            logger.LogWarning(
                "{Library} {Id}: dropped {Dropped} oldest version snapshot(s) to stay under the Azure "
                + "Table property limit; retained v{From}-v{To}.",
                library, id, versions.Count - retained.Count,
                versionNumber(retained[0]), versionNumber(retained[^1]));

        if (json.Length > PropertyCeiling)
        {
            logger.LogError(
                "{Library} {Id}: a single version is {Size} chars, past the {Ceiling}-char Azure Table "
                + "property limit. Refusing the write so the record stays editable.",
                library, id, json.Length, PropertyCeiling);
            failure = OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large",
                $"This {library.ToLowerInvariant()} is too large to save — a single version is "
                + $"{json.Length:N0} characters against a {PropertyCeiling:N0} limit. Remove or shorten "
                + "some entries and save again.");
            return false;
        }

        failure = null;
        return true;
    }
}
