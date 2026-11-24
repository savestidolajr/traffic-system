using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic restatement gate for structured drafts. A sentence commits its evidence ids
///     before its prose, so two non-empty, equal evidence-id sets assert the same facts even when
///     their wording differs. The later sentence is safe to remove because it contributes no
///     unique cited evidence.
/// </summary>
public static class EvidenceRestatementDetector
{
    public readonly record struct EvidenceRestatementResult(
        bool HasRestatement,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public EvidenceRestatementResult(bool hasRestatement, string feedback)
            : this(hasRestatement, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    /// <summary>
    ///     Finds pairs of sentences with the same non-empty evidence-id set. Comparison is exact
    ///     set equality: order and duplicate ids do not matter, but a strict subset is allowed.
    ///     A subset may add useful context or a narrower detail, while mere overlap is expressly
    ///     not enough because grouped and detailed sentences can legitimately share evidence.
    /// </summary>
    public static EvidenceRestatementResult Detect(IReadOnlyList<DraftedSentence>? sentences)
    {
        var duplicatePairs = FindDuplicatePairs(sentences);
        if (duplicatePairs.Count == 0)
            return new EvidenceRestatementResult(false, string.Empty, []);

        string offending = string.Join("; ", duplicatePairs.Select(pair =>
            $"sentence {pair.First.Order} \"{pair.First.Text}\" and " +
            $"sentence {pair.Second.Order} \"{pair.Second.Text}\""));
        string feedback =
            $"RESTATEMENT: {offending} cite the same evidence and repeat the same facts in different words. " +
            "Keep the first sentence and remove the later duplicate; do not pad the read when the evidence is exhausted.";

        var issues = duplicatePairs.Select(pair =>
        {
            int? order = pair.Second.Order;
            string reason =
                $"RESTATEMENT: sentence {pair.Second.Order} cites the same evidence as sentence {pair.First.Order}. " +
                "Remove or replace this duplicate sentence.";
            return order is > 0
                ? ReviewIssue.ForSentence(order.Value, "EVIDENCE_RESTATEMENT", reason)
                : ReviewIssue.ForUnresolvedSentence("EVIDENCE_RESTATEMENT", reason);
        }).ToList();

        return new EvidenceRestatementResult(true, feedback, issues);
    }

    /// <summary>
    ///     Drops every later sentence in an equal-evidence group while retaining its first
    ///     occurrence. This is a companion repair for the structured list used by the review loop.
    /// </summary>
    public static IReadOnlyList<DraftedSentence>? RepairSentences(IReadOnlyList<DraftedSentence>? sentences)
    {
        var duplicateIndexes = FindDuplicateIndexes(sentences);
        if (duplicateIndexes.Count == 0 || sentences is null)
            return sentences;

        return sentences.Where((_, index) => !duplicateIndexes.Contains(index)).ToList();
    }

    /// <summary>
    ///     Removes later duplicate sentence text from the assembled script. The repair is guarded
    ///     against deleting the only parsed sentence or leaving empty text; if the structured text
    ///     cannot be located, the original script is retained and the blocking gate remains visible.
    /// </summary>
    public static string Repair(string script, IReadOnlyList<DraftedSentence>? sentences)
    {
        var duplicateIndexes = FindDuplicateIndexes(sentences);
        if (duplicateIndexes.Count == 0 || string.IsNullOrWhiteSpace(script) || sentences is null)
            return script;

        string repaired = script;
        foreach (int duplicateIndex in duplicateIndexes.OrderByDescending(index => index))
        {
            string text = sentences[duplicateIndex].Text?.Trim() ?? string.Empty;
            if (text.Length == 0 || ScriptSentenceSplitter.Split(repaired).Count <= 1)
                continue;

            int start = repaired.LastIndexOf(text, StringComparison.Ordinal);
            if (start < 0)
                continue;

            int end = start + text.Length;
            if (end < repaired.Length && char.IsWhiteSpace(repaired[end]))
                end++;
            else if (start > 0 && char.IsWhiteSpace(repaired[start - 1]))
                start--;

            string candidate = repaired.Remove(start, end - start).Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
                repaired = candidate;
        }

        return repaired;
    }

    private static List<(DraftedSentence First, DraftedSentence Second)> FindDuplicatePairs(
        IReadOnlyList<DraftedSentence>? sentences)
    {
        var pairs = new List<(DraftedSentence First, DraftedSentence Second)>();
        if (sentences is null || sentences.Count < 2)
            return pairs;

        var evidenceSets = sentences.Select(EvidenceSet).ToList();
        for (int i = 0; i < sentences.Count - 1; i++)
        {
            if (evidenceSets[i].Count == 0)
                continue;

            for (int j = i + 1; j < sentences.Count; j++)
            {
                if (evidenceSets[i].SetEquals(evidenceSets[j]))
                    pairs.Add((sentences[i], sentences[j]));
            }
        }

        return pairs;
    }

    private static HashSet<int> FindDuplicateIndexes(IReadOnlyList<DraftedSentence>? sentences)
    {
        var duplicateIndexes = new HashSet<int>();
        if (sentences is null || sentences.Count < 2)
            return duplicateIndexes;

        var evidenceSets = sentences.Select(EvidenceSet).ToList();
        for (int i = 0; i < sentences.Count - 1; i++)
        {
            if (evidenceSets[i].Count == 0)
                continue;

            for (int j = i + 1; j < sentences.Count; j++)
            {
                if (evidenceSets[i].SetEquals(evidenceSets[j]))
                    duplicateIndexes.Add(j);
            }
        }

        return duplicateIndexes;
    }

    private static HashSet<string> EvidenceSet(DraftedSentence sentence) =>
        (sentence.EvidenceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
}
