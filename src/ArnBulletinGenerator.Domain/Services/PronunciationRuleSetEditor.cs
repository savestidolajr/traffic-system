using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>What a word-or-phrase alias edit did to a rule set.</summary>
public enum AliasEditOutcome
{
    /// <summary>The word had no rule; one was appended.</summary>
    Added = 0,

    /// <summary>The word already had a rule and its respelling was replaced.</summary>
    Changed = 1,

    /// <summary>The word's rule already said exactly this; the rule set is unchanged.</summary>
    AlreadyCorrect = 2
}

/// <summary>The rule set after an alias edit, and what the edit did.</summary>
public sealed record AliasEdit(IReadOnlyList<PronunciationRule> Rules, AliasEditOutcome Outcome);

/// <summary>
///     Applies one word-or-phrase → respelling correction to a dictionary's rule set — what an operator
///     means when they fix a word or road phrase they just heard read wrong. Two constraints are
///     load-bearing and both were learned on air:
///     <list type="number">
///         <item>
///             <b>Alias only.</b> A phoneme rule is ignored by <c>eleven_v3</c> and silently DROPS
///             every matched word on <c>eleven_multilingual_v2</c>, so one well-meant fix can take
///             the whole dictionary off the read (see
///             <see cref="PronunciationRule.PhonemeRulesSafeFor" />).
///         </item>
///         <item>
///             <b>Replace the target's rule, never append a second one.</b> The provider applies one
///             rule per target, so appending leaves the original in force and the correction is
///             silently dead — which had operators "fixing" the same word again and again, stacking
///             up rules (observed on dev, 2026-07-28).
///         </item>
///     </list>
/// </summary>
public static class PronunciationRuleSetEditor
{
    public static Result<AliasEdit> WithAlias(
        IReadOnlyList<PronunciationRule> rules, string word, string replacement)
    {
        var alias = PronunciationRule.CreateAlias(word, replacement);
        if (alias.IsFailure)
            return Result<AliasEdit>.Failure(alias.Error);

        var next = rules.ToList();
        int index = next.FindIndex(r =>
            r.Type == PronunciationRuleType.Alias && SameWord(r.StringToReplace, word));

        if (index < 0)
        {
            next.Add(alias.Value);
            return Result<AliasEdit>.Success(new AliasEdit(next, AliasEditOutcome.Added));
        }

        var existing = next[index];
        if (string.Equals(existing.Replacement, alias.Value.Replacement, StringComparison.Ordinal))
            return Result<AliasEdit>.Success(new AliasEdit(rules, AliasEditOutcome.AlreadyCorrect));

        // Keep the existing rule's own spelling of the matched word and its match flags: the
        // operator is correcting how the word SOUNDS, not re-deciding case sensitivity or word
        // boundaries someone set deliberately.
        next[index] = existing with { Replacement = alias.Value.Replacement };
        return Result<AliasEdit>.Success(new AliasEdit(next, AliasEditOutcome.Changed));
    }

    private static bool SameWord(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
