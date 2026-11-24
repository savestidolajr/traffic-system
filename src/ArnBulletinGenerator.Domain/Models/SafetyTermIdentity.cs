namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Scalar pointer from a normalized safety-term identity to its canonical row.</summary>
public sealed record SafetyTermIdentity(
    string TermId,
    SafetyTermKind Kind,
    SafetyScope Scope,
    string ScopeId,
    string NormalizedTerm,
    DateTime CreatedAtUtc,
    string? CanonicalETag = null,
    string? ETag = null)
{
    public static string NormalizeTerm(string term)
        => (term ?? string.Empty).Trim().ToLowerInvariant();

    public static string NormalizeScopeId(SafetyScope scope, string? scopeId)
        => SafetyTerm.NormalizeScopeId(scope, scopeId);
}

/// <summary>One resumable slice of canonical terms considered for identity backfill.</summary>
public sealed record SafetyTermIdentityBackfillPage(
    string? ContinuationToken,
    int Examined,
    int Written,
    int Duplicates);
