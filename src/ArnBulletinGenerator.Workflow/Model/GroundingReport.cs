namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>
///     Atomic entailment over a finished read. Three states, not two: a claim nothing contradicts is
///     not thereby supported.
///     <para>
///         The predecessor reviewer conflated these — "APPROVE unless you can show a sentence
///         contradicts the data" — which let unsupported colour through while its structured output
///         forced a verdict before any reasoning. This report carries no verdict at all: the caller
///         decides what an unsupported claim is worth, and the model only says what it found.
///     </para>
/// </summary>
public sealed record GroundingReport(IReadOnlyList<AtomicClaim> Claims)
{
    public static GroundingReport Empty { get; } = new([]);

    public IReadOnlyList<AtomicClaim> Contradicted =>
        Claims.Where(c => c.Status == ClaimStatus.Contradicted).ToList();

    public IReadOnlyList<AtomicClaim> Unsupported =>
        Claims.Where(c => c.Status == ClaimStatus.NotSupported).ToList();

    public bool IsClean => Contradicted.Count == 0 && Unsupported.Count == 0;
}

/// <param name="SentenceOrder">Which sentence carries the claim.</param>
/// <param name="Claim">The claim, restated atomically by the model.</param>
/// <param name="Support">Evidence ids that entail it. Empty when unsupported or contradicted.</param>
public sealed record AtomicClaim(
    int SentenceOrder,
    string Claim,
    ClaimStatus Status,
    IReadOnlyList<string> Support,
    string? Note = null);

public enum ClaimStatus
{
    Supported,
    NotSupported,
    Contradicted
}
