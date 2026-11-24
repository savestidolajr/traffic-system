using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic anti-drumbeat gate: flags a bulletin that leans on the contrast connector
///     "while" (or "whereas") more than once. Real ATN copy is built from crisp, mostly-discrete
///     anchored sentences with light ADDITIVE connectives ("and", "also", "as well", "meanwhile");
///     a contrast connector earns its place at most once, for a genuine good-vs-bad turn. The
///     polish stage was observed re-introducing "while" between two same-state roads even though
///     the draft template bans it (observed live: "Dacey Avenue is under speed, while Flowerdale
///     Road has a slight hold-up") — a prompt rule the writer honoured but the polisher did not.
///     Counting the connector in CODE lets the polish gate revert a polish that regresses on it
///     and drives a refine when the draft itself over-leans, the same gate shape as the sibling
///     detectors. Detect-only by design: a safe mechanical rewrite of a discourse connector is not
///     possible without knowing whether each use is a real contrast, so this scores, never edits.
/// </summary>
public static partial class ConnectorFrequencyDetector
{
    // At most ONE contrast connector per read. ATN almost never stacks them.
    private const int MaxContrastConnectors = 1;
    [GeneratedRegex(@"\b(while|whereas)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Contrast();

    public readonly record struct ConnectorFrequencyResult(
        bool HasOveruse,
        string Feedback,
        int Count,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public ConnectorFrequencyResult(bool hasOveruse, string feedback, int count)
            : this(hasOveruse, feedback, count, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static ConnectorFrequencyResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ConnectorFrequencyResult(false, string.Empty, 0, []);

        int totalCount = Contrast().Matches(script).Count;
        if (totalCount <= MaxContrastConnectors)
            return new ConnectorFrequencyResult(false, string.Empty, totalCount, []);

        var sentences = ScriptSentenceSplitter.Split(script);
        var matchingOrders = new List<int>();
        for (int i = 0; i < sentences.Count; i++)
        {
            if (Contrast().IsMatch(sentences[i]))
                matchingOrders.Add(i + 1);
        }

        string feedback =
            $"CONNECTOR_REPEAT: the contrast word \"while\"/\"whereas\" is used {totalCount} times (max {MaxContrastConnectors}). " +
            "ATN copy is crisp and additive — join roads in the SAME state with \"and\"/\"also\"/\"as well\", and keep " +
            "\"while\" for at most one genuine good-vs-bad turn.";
        // Flag starting from the 2nd occurrence so the 1st is kept
        var issues = (matchingOrders.Count > 1 ? matchingOrders.Skip(1) : matchingOrders)
            .Select(order => ReviewIssue.ForSentence(order, "CONNECTOR_FREQUENCY", feedback))
            .ToList();

        return new ConnectorFrequencyResult(true, feedback, totalCount, issues);
    }
}
