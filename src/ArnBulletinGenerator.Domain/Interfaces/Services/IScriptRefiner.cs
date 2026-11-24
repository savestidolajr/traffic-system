using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     The sentence-scoped refiner — the pipeline's only LLM repair (2026-08-21). Whole-script
///     refinement was removed: measured over the eval corpus and live jobs, a whole-bulletin
///     rewrite degraded more reads than it fixed. Script-wide defect classes now resolve
///     deterministically (required-fixture splice, section-order relocation) or by dropping the
///     proven-defective sentence.
/// </summary>
public interface IScriptRefiner
{
    /// <summary>
    ///     Repairs a single flagged sentence using a narrow, sentence-scoped prompt.
    ///     Only the allowed evidence units are provided and the sentence's word count
    ///     acts as a strict budget constraint.
    /// </summary>
    Task<Result<DraftedSentence>> PatchSentenceAsync(
        BroadcastWithTraffic context,
        DraftedSentence sentence,
        string instruction,
        IReadOnlyList<EvidenceUnit> allowedEvidence,
        int wordBudget,
        CancellationToken cancellationToken = default);
}
