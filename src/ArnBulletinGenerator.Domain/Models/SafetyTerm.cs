using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Whether a term tightens the deterministic content-safety gate (<see cref="Banned" />)
///     or relaxes it (<see cref="Allowed" /> exception — e.g. a suburb/road name that
///     legitimately contains a flagged substring). Unlike guidance, safety composes
///     ADDITIVELY: a narrower scope can only add prohibitions, never weaken the global
///     floor. Allowed exceptions are the one subtractive case and are Admin-gated + audited.
/// </summary>
public enum SafetyTermKind
{
    Banned = 0,
    Allowed = 1
}

/// <summary>
///     Scope of a safety term. Deliberately has NO Station level (unlike
///     <see cref="GuidanceScope" />): content safety is a network/regulatory floor, set
///     globally with optional per-market additions, never per station.
/// </summary>
public enum SafetyScope
{
    Global = 0,
    Market = 1
}

/// <summary>
///     One content-safety term — a banned word/phrase or an allowed exception — consumed by
///     the deterministic <c>IContentSafetyFilter</c>. Repository-backed so compliance can
///     maintain the list without a redeploy. The filter enforces the union (global ∪ market);
///     the engineered <c>ContentSafety:BannedWords</c> config baseline is always merged in and
///     is NOT removable through this store.
/// </summary>
public sealed record SafetyTerm(
    string Id,
    SafetyTermKind Kind,
    SafetyScope Scope,
    string ScopeId,
    string Term,
    string CreatedBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool Enabled = true,
    Option<string> Note = default)
{
    public static SafetyTerm Create(
        SafetyTermKind kind,
        SafetyScope scope,
        string? scopeId,
        string term,
        string createdBy,
        DateTime nowUtc,
        string? note = null,
        bool enabled = true)
    {
        return new SafetyTerm(
            Guid.NewGuid().ToString("N"),
            kind,
            scope,
            NormalizeScopeId(scope, scopeId),
            (term ?? string.Empty).Trim(),
            createdBy,
            nowUtc,
            nowUtc,
            enabled,
            Option.OfWhitespace(note));
    }

    /// <summary>Global terms carry no scope id; market keeps its id (trimmed, lower-invariant).</summary>
    public static string NormalizeScopeId(SafetyScope scope, string? scopeId)
        => scope == SafetyScope.Global ? string.Empty : MarketKey.Normalize(scopeId ?? string.Empty);
}
