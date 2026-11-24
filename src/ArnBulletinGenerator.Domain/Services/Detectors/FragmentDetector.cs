using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "fragment" gate. A stray full stop before a lowercase clause-connector
///     welds a dependent clause into a broken sentence ("...Gympie Road. while the Bruce...") —
///     the inverse of a run-on (a misplaced stop, not a missing one). Detection finds the
///     connector-scoped pattern; mechanical repair fuses the stray stop with a comma.
///     <para>
///         This is a superset of the general LowercaseSentenceStartRegex in
///         <see cref="ArnBulletinGenerator.Application.Validation.TrafficScriptStyleValidator" />,
///         scoped specifically to clause-connectors and including a safe mechanical repair.
///     </para>
/// </summary>
public static partial class FragmentDetector
{
    // The stray full stop before a lowercase clause-connector. The capture group
    // grabs the connector plus everything that follows up to the next terminal.
    // This is the FRAGMENT: detection regex ported from the Python lab scoring.py.
    [GeneratedRegex(@"[.!?]\s+((?:while|with|and|but|though|although|as|plus)\b)")]
    private static partial Regex FragmentDetectionRegex();

    // The repair regex: same pattern as detection but used for replacement.
    // Delegates to the detection regex — patterns are byte-identical.
    private static Regex FragmentRepairRegex() => FragmentDetectionRegex();

    public readonly record struct FragmentResult(
        bool HasFragment,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public FragmentResult(bool hasFragment, string feedback)
            : this(hasFragment, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    /// <summary>
    ///     Detect a stray full stop before a lowercase clause-connector that
    ///     welds a dependent clause into a broken sentence.
    /// </summary>
    public static FragmentResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FragmentResult(false, string.Empty, []);

        var match = FragmentDetectionRegex().Match(script);
        if (!match.Success)
            return new FragmentResult(false, string.Empty, []);

        var connector = match.Groups[1].Value.Trim();
        string feedback =
            $"FRAGMENT: \"{connector}\" - a clause welded into a broken sentence by a stray full stop before a lowercase connector";

        int? order = null;
        var sentences = ScriptSentenceSplitter.Split(script);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            if (sentence.StartsWith(connector, StringComparison.OrdinalIgnoreCase))
            {
                order = i + 1;
                break;
            }
        }

        var issue = order is > 0
            ? ReviewIssue.ForSentence(order.Value, "FRAGMENT", feedback)
            : ReviewIssue.ForUnresolvedSentence("FRAGMENT", feedback);

        return new FragmentResult(true, feedback, [issue]);
    }

    /// <summary>
    ///     Mechanically repair the stray full stop by fusing it with a comma.
    ///     Safe: ". while" → ", while" — the sentence keeps its meaning, the clause
    ///     is no longer broken off as its own sentence.
    /// </summary>
    public static string Repair(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        return FragmentRepairRegex().Replace(script, m =>
        {
            // The regex matches "[.!?] <connector>". The Python spec replaces the
            // entire match with ", <connector>" — dropping the stray full stop.
            // "Road. while" → "Road, while".
            var connector = m.Groups[1].Value;
            return $", {connector}";
        });
    }
}
