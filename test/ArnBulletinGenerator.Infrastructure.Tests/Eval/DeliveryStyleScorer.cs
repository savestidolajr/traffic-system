using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Scores a director output against its delivery policy — the offline gate for iterating on
///     <c>prompts/director.md</c> and style presets without operator UI time. A directed bulletin is
///     valid only when (1) the spoken words are byte-for-byte unchanged from the plain script, and
///     (2) every applied tag is in the policy's allowed palette (no invented tags). These are hard
///     gates: a fail means the director regressed.
/// </summary>
internal static class DeliveryStyleScorer
{
    public static DeliveryStyleResult Evaluate(string plainScript, string taggedScript, DeliveryStylePolicy policy)
    {
        var violations = new List<string>();

        if (!DeliveryTags.WordsPreserved(plainScript, taggedScript))
            violations.Add("Spoken words changed between the plain and tagged script.");

        var allowed = policy.AllowedTags
            .Select(NormalizeTag)
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = DeliveryTags.Extract(taggedScript);
        foreach (var tag in used)
        {
            if (!allowed.Contains(NormalizeTag(tag)))
                violations.Add($"Tag not in the allowed palette: [{tag}]");
        }

        return new DeliveryStyleResult(violations, used.Count);
    }

    private static string NormalizeTag(string tag) => tag.Trim().Trim('[', ']').Trim();
}

internal sealed record DeliveryStyleResult(IReadOnlyList<string> Violations, int TagCount)
{
    public bool Passed => Violations.Count == 0;
}
