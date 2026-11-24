using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The result of choosing a sentence phrasing. <see cref="VariantHelped"/> is true only when
///     an alternate, rather than the model's preferred <c>Text</c>, supplied a passing phrasing.
/// </summary>
public readonly record struct VariantSelection(string Phrasing, bool VariantHelped)
{
    public bool UsedAlternate => VariantHelped;
}

/// <summary>
///     Selects a deterministic sentence variant without changing its committed evidence ids.
///     Callers provide the gate predicate; this class has no mutable state or editorial policy.
/// </summary>
public static class VariantSelector
{
    /// <summary>
    ///     Returns the first phrasing accepted by <paramref name="passes"/> in model-preferred order.
    ///     When none passes, returns <see cref="DraftedSentence.Text"/> unchanged and reports that no
    ///     alternate helped.
    /// </summary>
    public static VariantSelection Select(DraftedSentence sentence, Func<string, bool> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);

        bool preferred = true;
        IEnumerable<string> phrasings = [sentence.Text, .. (sentence.Variants ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Text))
            .Select(v => v.Text)];
        foreach (string phrasing in phrasings)
        {
            if (passes(phrasing))
                return new VariantSelection(phrasing, !preferred);

            preferred = false;
        }

        return new VariantSelection(sentence.Text, VariantHelped: false);
    }
}
