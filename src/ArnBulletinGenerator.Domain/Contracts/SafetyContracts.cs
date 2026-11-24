using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>Wire DTO for creating/replacing a safety term (banned word or allowed exception).</summary>
public sealed record UpsertSafetyTermRequest(
    SafetyTermKind Kind,
    SafetyScope Scope,
    string? ScopeId,
    string Term,
    string? Note = null,
    string? Id = null,
    bool? Enabled = null);

/// <summary>Wire DTO returned for a safety term.</summary>
public sealed record SafetyTermDto(
    string Id,
    string Kind,
    string Scope,
    string ScopeId,
    string Term,
    bool Enabled,
    string? Note,
    string CreatedBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SafetyTermDto From(SafetyTerm t) => new(
        t.Id, t.Kind.ToString(), t.Scope.ToString(), t.ScopeId, t.Term,
        t.Enabled,
        t.Note.Match(n => (string?)n, () => null),
        t.CreatedBy, t.CreatedAtUtc, t.UpdatedAtUtc);
}
