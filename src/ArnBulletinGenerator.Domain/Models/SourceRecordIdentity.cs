namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A source-native identifier for one observation of a traffic event. <see cref="RecordId" />
///     is the provider's current record identifier; <see cref="OriginalRecordId" /> is the stable
///     event identifier when a provider exposes revision chains (for example, HERE's
///     <c>originalId</c>). The source scope is explicit because record IDs are not globally unique.
/// </summary>
public sealed record SourceRecordIdentity(
    string SourceId,
    string RecordId,
    string? OriginalRecordId = null)
{
    /// <summary>
    ///     Resolves the stable event key for stateful consumers. Any stable original identifier
    ///     wins before source priority because a current record id can change between revisions;
    ///     HERE is preferred only when no source exposes a stable original id. The source is part of
    ///     the key because provider record ids are not globally unique.
    /// </summary>
    public static string? StableEventKey(IReadOnlyList<SourceRecordIdentity>? identities)
    {
        if (identities is not { Count: > 0 })
            return null;

        var identity = identities
            .Where(i => !string.IsNullOrWhiteSpace(i.SourceId)
                        && !string.IsNullOrWhiteSpace(i.RecordId))
            .OrderBy(i => string.IsNullOrWhiteSpace(i.OriginalRecordId) ? 1 : 0)
            .ThenBy(i => string.Equals(i.SourceId, EvidenceSources.Here, StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1)
            .ThenBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.OriginalRecordId ?? i.RecordId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (identity is null)
            return null;

        bool hasStableId = !string.IsNullOrWhiteSpace(identity.OriginalRecordId);
        string recordId = hasStableId ? identity.OriginalRecordId! : identity.RecordId;
        return $"{identity.SourceId}:{(hasStableId ? "original" : "record")}:{recordId}";
    }

    /// <summary>
    ///     THE event-key space. Every stateful consumer of a traffic record — the Event Ledger and
    ///     its Desk verdicts, the incident-state store, and the Desk's omission enforcement — must
    ///     key by this, and only this, or a key written by one is invisible to another.
    ///     <para>
    ///         Learned the hard way (test env, 2026-08-24): the Desk wrote verdicts as
    ///         <c>here:original:{originalId}</c> (this shape, via the ledger fold) while the
    ///         enforcement filter compared the raw <c>TrafficIncident.Id</c>. Two independent
    ///         mismatches — the source-qualified prefix, and current-vs-original record id — so
    ///         <c>0 matching</c> was logged on every single fetch across every market and an
    ///         operator's omission never once kept an event off air.
    ///     </para>
    ///     <paramref name="fallbackKey" /> is the record's own identifier, used only when no source
    ///     supplied an identity (a legacy row or a fixture) — both sides of a comparison then fall
    ///     back the same way, so the key space stays single.
    /// </summary>
    public static string EventKeyFor(
        IReadOnlyList<SourceRecordIdentity>? identities, string fallbackKey) =>
        StableEventKey(identities) ?? fallbackKey;
}
