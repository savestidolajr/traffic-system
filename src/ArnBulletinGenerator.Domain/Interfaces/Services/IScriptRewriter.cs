using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Rewrites an approved bulletin in place to follow a free-form operator instruction ("drop the
///     weather and lead with the closure", "make the crash the lead", "reword this to be less
///     alarming"). This is a lightweight CONTENT pass, not a delivery pass.
///
///     Distinct from <see cref="IDeliveryWriter" />, which reshapes only the register. This writer
///     may drop, reorder, reword and re-emphasise items. Its output is a PREVIEW: the operator reads
///     it, edits it if needed, and applies it through the verbatim re-voice path. Reference data is
///     supplied so the editor understands the bulletin's material, but the operator owns the
///     requested content change and this service does not issue a grounding verdict.
///
///     The service never re-drafts from fresh traffic. It receives the persisted job context and
///     returns the current script rewritten according to the operator's instruction.
/// </summary>
public interface IScriptRewriter
{
    /// <summary>
    ///     Returns the rewritten script, or a failure when the model is unavailable or its output is
    ///     not usable. <paramref name="context" /> carries the persisted job material used as
    ///     compact reference data (corridors, incidents, weather, fuel and good runs). The method
    ///     validates the JSON shape and leaves the operator's requested content intact; content
    ///     safety is applied by the caller before the preview is returned.
    /// </summary>
    Task<Result<ScriptRewrite>> RewriteAsync(
        string currentScript,
        string operatorInstruction,
        BroadcastWithTraffic context,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null);
}

/// <summary>The rewritten script plus the plain spoken-word counts, so the UI can show the slot impact.</summary>
public sealed record ScriptRewrite(
    string Script,
    int SpokenWordsBefore,
    int SpokenWordsAfter);
/// <summary>
///     Word targets for the complete read. The rewrite sees only the editable body, so the provider
///     subtracts <see cref="PreservedWordCount" /> before presenting the target and band to the model.
/// </summary>
public sealed record RewriteWordBudget(
    int TargetWordCount,
    int MinWordCount,
    int MaxWordCount,
    int PreservedWordCount = 0);
