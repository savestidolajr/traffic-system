using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>Wire↔domain mapping for pronunciation rules.</summary>
public static class PronunciationRuleMapping
{
    /// <summary>
    ///     Parses a wire scope value. Blank means Station — that is what every dictionary written
    ///     before the platform-wide layer existed actually was. Returns false only for a value
    ///     that is present but unrecognised, so a typo is reported rather than silently defaulted.
    /// </summary>
    public static bool TryParseScope(string? value, out PronunciationScope scope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            scope = PronunciationScope.Station;
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out scope);
    }

    /// <summary>The rejection message for an unrecognised wire scope value.</summary>
    public static string UnrecognisedScope(string? value) =>
        $"Scope '{value}' is not recognised. Use 'Station' or 'Global'.";

    public static Result<List<PronunciationRule>> ParseRules(IReadOnlyList<PronunciationRuleDto>? dtos)
    {
        var rules = new List<PronunciationRule>();
        if (dtos is null)
            return Result<List<PronunciationRule>>.Success(rules);

        // Two alias rules for one word is never intentional: the provider applies one and the
        // other is silently dead, so an operator "correcting" a word a second time sees no
        // change and corrects it again. Three stacked Toorak rules were reported this way on
        // dev (2026-07-28). Enforced here because this is the single choke point for the
        // studio editor, the review-cockpit quick-fix, and config import alike.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in dtos)
        {
            var type = (d.Type ?? "alias").Trim().ToLowerInvariant();
            var parsed = type switch
            {
                "phoneme" => PronunciationRule.CreatePhoneme(d.StringToReplace ?? "", d.Replacement ?? "",
                    d.Alphabet ?? "ipa", d.CaseSensitive, d.WordBoundaries),
                "alias" => PronunciationRule.CreateAlias(d.StringToReplace ?? "", d.Replacement ?? "",
                    d.CaseSensitive, d.WordBoundaries),
                _ => Result<PronunciationRule>.Failure($"Unknown rule type '{d.Type}'. Use 'alias' or 'phoneme'.")
            };
            if (parsed.IsFailure)
                return Result<List<PronunciationRule>>.Failure(parsed.Error);

            var rule = parsed.Value;
            string key = $"{rule.Type}|{rule.StringToReplace}";
            if (seen.TryGetValue(key, out string? existing))
                return Result<List<PronunciationRule>>.Failure(
                    $"'{rule.StringToReplace}' has more than one {type} rule ('{existing}' and " +
                    $"'{rule.Replacement}'). Only one would be applied — keep the correct one and remove the rest.");
            seen[key] = rule.Replacement;

            rules.Add(rule);
        }

        return Result<List<PronunciationRule>>.Success(rules);
    }
}
