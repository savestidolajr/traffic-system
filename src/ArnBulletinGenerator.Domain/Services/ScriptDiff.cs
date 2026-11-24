using System.Text;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>One contiguous wording change: the original words and their replacement. Either side
/// may be empty for a pure insertion or deletion.</summary>
public sealed record ScriptDiffHunk(string Before, string After);

/// <summary>
///     Deterministic word-level diff between two bulletin scripts. Tokens are whitespace-split
///     words compared ordinally, so punctuation and casing changes count as wording edits while
///     whitespace-only reflow does not.
/// </summary>
public static class ScriptDiff
{
    /// <summary>Whether two scripts carry the same words after whitespace collapsing.</summary>
    public static bool NormalizedEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>
    ///     Word-token LCS diff; consecutive non-matching runs merge into one hunk. Standard
    ///     O(n·m) dynamic programming — scripts are at most a few hundred words.
    /// </summary>
    public static IReadOnlyList<ScriptDiffHunk> Compute(string original, string edited)
    {
        string[] a = Tokenize(original);
        string[] b = Tokenize(edited);

        // LCS length table.
        int[,] lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        for (int j = b.Length - 1; j >= 0; j--)
            lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                ? lcs[i + 1, j + 1] + 1
                : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var hunks = new List<ScriptDiffHunk>();
        var before = new List<string>();
        var after = new List<string>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length
                && string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                Flush(hunks, before, after);
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                after.Add(b[y]);
                y++;
            }
            else
            {
                before.Add(a[x]);
                x++;
            }
        }

        Flush(hunks, before, after);
        return hunks;
    }

    /// <summary>Renders hunks one per line as <c>"before" -&gt; "after"</c>, with an empty side
    /// shown as <c>(nothing)</c>.</summary>
    public static string Render(IReadOnlyList<ScriptDiffHunk> hunks)
    {
        var sb = new StringBuilder();
        foreach (var hunk in hunks)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(hunk.Before.Length > 0 ? $"\"{hunk.Before}\"" : "(nothing)");
            sb.Append(" -> ");
            sb.Append(hunk.After.Length > 0 ? $"\"{hunk.After}\"" : "(nothing)");
        }

        return sb.ToString();
    }

    private static void Flush(List<ScriptDiffHunk> hunks, List<string> before, List<string> after)
    {
        if (before.Count == 0 && after.Count == 0)
            return;
        hunks.Add(new ScriptDiffHunk(string.Join(' ', before), string.Join(' ', after)));
        before.Clear();
        after.Clear();
    }

    private static string[] Tokenize(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Normalize(string text) => string.Join(' ', Tokenize(text));
}
