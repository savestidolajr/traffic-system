using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     One sentence of a structured Draft response. <see cref="EvidenceIds"/> are committed BEFORE
///     <see cref="Text"/> (JSON property order in the schema), so the model names the facts it will
///     voice before writing the words — a citation is chosen, not back-filled. Each id is drawn from
///     the per-request strict-mode enum of real <c>EvidenceUnit</c> ids, so it is referentially
///     guaranteed to exist.
///     <para>
///     <see cref="Alternates"/> carries additional phrasings of THE SAME <see cref="EvidenceIds"/>.
///     That shared-evidence rule is the load-bearing invariant: because every alternate voices
///     exactly the facts already cited, swapping one in can never change what the bulletin claims,
///     so a deterministic detector may substitute a phrasing instead of spending an LLM refine on
///     it. Ordered best-first; <see cref="Text"/> is the model's own preference and alternates are
///     fallbacks, so an empty list simply means "no cheaper repair available".
///     </para>
/// </summary>
internal sealed record DraftSentence(
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("alternates")] IReadOnlyList<string> Alternates = null!)
{
    /// <summary>Every phrasing of this sentence, the model's preference first.</summary>
    public IEnumerable<string> AllPhrasings()
    {
        yield return Text;
        foreach (var alternate in Alternates ?? [])
            if (!string.IsNullOrWhiteSpace(alternate))
                yield return alternate;
    }
}

/// <summary>The structured Draft/citation-fix response envelope: an ordered array of sentences.</summary>
internal sealed record DraftResponse(
    [property: JsonPropertyName("sentences")] IReadOnlyList<DraftSentence> Sentences);
/// <summary>One phrasing returned by the structured Refine response.</summary>
internal sealed record RefineVariant(
    [property: JsonPropertyName("style")] string Style,
    [property: JsonPropertyName("text")] string Text);

/// <summary>One sentence returned by the structured Refine response.</summary>
internal sealed record RefineSentence(
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("variants")] IReadOnlyList<RefineVariant>? Variants = null);

/// <summary>The structured Refine response envelope.</summary>
internal sealed record RefineResponse(
    [property: JsonPropertyName("sentences")] IReadOnlyList<RefineSentence> Sentences);
