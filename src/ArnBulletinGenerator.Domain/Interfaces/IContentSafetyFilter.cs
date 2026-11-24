using System.Threading;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Screens a generated script for content that must not air (e.g. legally sensitive or unverified allegations) before it reaches synthesis.</summary>
public interface IContentSafetyFilter
{
    Task<ContentSafetyResult> CheckAsync(string scriptText, string? market, CancellationToken cancellationToken = default);
}

public sealed record ContentSafetyResult(
    bool IsAllowed,
    string[] Violations,
    string[] Warnings)
{
    public static ContentSafetyResult Allowed() => new(true, [], []);

    public static ContentSafetyResult Blocked(string[] violations, string[]? warnings = null) =>
        new(false, violations, warnings ?? []);
}
