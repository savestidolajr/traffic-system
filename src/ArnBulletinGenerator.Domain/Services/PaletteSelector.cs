using System.Text;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The ATN road-feel vocabulary rendered into the draft prompt with each tier's phrase order
///     SHUFFLED per job seed. The draft agent is a reasoning model that does not expose temperature
///     as a diversity lever (see <see cref="BlueprintSelector" />), and an LLM carries a strong
///     ordering/primacy bias — a palette printed in a fixed order makes it reach for the same
///     first-listed phrase every run ("a touch slow" for every minor road). Varying the presentation
///     order per seed makes that bias land on different phrases across jobs, giving genuine lexical
///     variety while staying DETERMINISTIC per job (a read is reproducible for diagnostics and
///     replay) — the same seeded-specification approach <see cref="BlueprintSelector" /> uses for
///     structural variety. The phrases themselves are examples, not a closed set; the prompt still
///     tells the writer it may use any natural equivalent the data's severity supports.
/// </summary>
public static partial class PaletteSelector
{
    private sealed record Tier(string Label, string[] Phrases);
    private const string Introduction =
        "Draw from the Australian Traffic Network's real on-air vocabulary below, or anything " +
        "semantically aligned — these are examples, not a fixed list. The point is variety and " +
        "road-feel over data labels.";

    /// <summary>
    ///     Renders the palette block for the draft prompt from editable tier vocabulary. Each tier's
    ///     phrases are shuffled by <paramref name="jobSeed" /> — stable per job, varied across jobs.
    /// </summary>
    public static string Render(string jobSeed, string vocabularyMarkdown)
    {
        Tier[] tiers = Parse(vocabularyMarkdown);
        int h = StableHash(jobSeed);
        var sb = new StringBuilder();
        sb.AppendLine(Introduction);
        for (int i = 0; i < tiers.Length; i++)
        {
            Tier tier = tiers[i];
            string[] order = Shuffle(tier.Phrases, unchecked((h * 31) + (i * 97) + 1));
            sb.Append("- ").Append(tier.Label).Append(": ").AppendLine(string.Join(", ", order));
        }

        return sb.ToString().TrimEnd();
    }

    private static Tier[] Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            throw new ArgumentException("Road-feel palette vocabulary cannot be empty.", nameof(markdown));

        var tiers = new List<Tier>();
        string? label = null;
        var phrases = new List<string>();
        foreach (string rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddTier(tiers, label, phrases);
                label = line[3..].Trim();
                phrases = [];
                continue;
            }

            if (label is not null && line.Length > 0)
                phrases.AddRange(line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        AddTier(tiers, label, phrases);
        if (tiers.Count == 0)
            throw new ArgumentException("Road-feel palette vocabulary must contain tier headings.", nameof(markdown));
        return tiers.ToArray();
    }

    private static void AddTier(List<Tier> tiers, string? label, List<string> phrases)
    {
        if (label is null)
            return;
        if (phrases.Count == 0)
            throw new ArgumentException($"Road-feel palette tier '{label}' has no phrases.");
        tiers.Add(new Tier(label, phrases.ToArray()));
    }

    /// <summary>Deterministic Fisher-Yates using a small LCG so the order is stable across processes.</summary>
    private static string[] Shuffle(string[] source, int seed)
    {
        var arr = (string[])source.Clone();
        uint state = unchecked(((uint)seed * 2654435761u) + 2246822519u);
        for (int i = arr.Length - 1; i > 0; i--)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            // Map into [0, i] from the HIGH bits (multiply-shift / Lemire). An LCG's LOW bits are
            // non-random, so `state % (i+1)` biases the shuffle toward keeping element 0 first —
            // measured live: the first-listed minor phrase led 50% of seeds instead of ~11%.
            int j = (int)(((ulong)state * (uint)(i + 1)) >> 32);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return arr;
    }

    // Stable across processes and runs (unlike string.GetHashCode, which is randomised per run) so a
    // given job's palette order is reproducible for diagnostics and replay.
    private static int StableHash(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
            return 0;
        unchecked
        {
            int h = 17;
            foreach (char c in seed)
                h = (h * 31) + c;
            return h;
        }
    }
}
