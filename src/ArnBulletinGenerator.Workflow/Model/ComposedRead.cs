namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>
///     A whole bulletin, written in one pass, with the evidence each sentence drew on.
///     <para>
///         Sentence boundaries are retained for audit and targeted repair, but they are NOT the unit
///         of composition: nothing downstream reorders them, drops one, or rewrites one in isolation
///         on a style complaint. The composer owns the discourse.
///     </para>
/// </summary>
public sealed record ComposedRead(IReadOnlyList<ComposedSentence> Sentences)
{
    public static ComposedRead Empty { get; } = new([]);

    /// <summary>The read as spoken, sentences joined in order.</summary>
    public string Script => string.Join(" ", Sentences.Select(s => s.Text.Trim()).Where(t => t.Length > 0));

    public int WordCount => Script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <param name="Order">1-based position in the read.</param>
/// <param name="Text">One finished spoken sentence.</param>
/// <param name="EvidenceIds">Every id this sentence asserts something about.</param>
/// <param name="StoryId">The fitted story this realises, for repair routing. Null when unattributed.</param>
/// <param name="Section">The bulletin segment the sentence voices; null when it voices none.</param>
public sealed record ComposedSentence(
    int Order,
    string Text,
    IReadOnlyList<string> EvidenceIds,
    string? StoryId = null,
    Domain.Models.BulletinSegment? Section = null);
