using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Rewrites an approved bulletin into a register an operator described in one plain sentence
///     ("talk about this more calmly, this is about a death").
///
///     Distinct from <see cref="IScriptDirector" />, and the distinction is the whole point. The
///     director may only ADD tags, capitals and punctuation — its word-immutability makes it safe to
///     run unattended inside synthesis, but it caps the achievable effect, because on this voice the
///     WORDS are the strongest delivery lever (verified by listening, 2026-08-03: mood tags are
///     inert, artefact tags air as literal noises, and rewriting the copy is what actually lands).
///
///     This writer therefore MAY reshape the wording. That makes it operator-facing only: its output
///     is returned for review, never applied automatically, and it reaches air through the normal
///     re-voice path where the safety reviewer sees the changed words.
/// </summary>
public interface IDeliveryWriter
{
    /// <summary>
    ///     Returns the rewritten script, or a failure when the model is unavailable or its output
    ///     breaks a hard constraint (a banned tag, a lost fact, or a word count outside the supplied
    ///     calibrated slot budget). Callers show the result to the operator; they never air it
    ///     unreviewed.
    /// </summary>
    Task<Result<DeliveryRewrite>> RewriteAsync(
        string approvedScript,
        string operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default,
        RewriteWordBudget? wordBudget = null);
}

/// <summary>The rewrite plus the checks that were run on it, so the UI can be honest about them.</summary>
public sealed record DeliveryRewrite(
    string Script,
    int SpokenWordsBefore,
    int SpokenWordsAfter,
    IReadOnlyList<string> Tags,
    /// <summary>Facts (road names, numbers) present before but missing after — MUST be empty.</summary>
    IReadOnlyList<string> MissingFacts);
