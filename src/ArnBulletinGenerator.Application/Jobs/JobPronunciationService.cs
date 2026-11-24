using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>The dictionary as it now stands, and what the operator's correction did to it.</summary>
public sealed record JobAliasEdit(PronunciationDictionary Dictionary, AliasEditOutcome Outcome);

public interface IJobPronunciationService
{
    /// <summary>
    ///     Records one word-or-phrase → respelling correction against the dictionary a given
    ///     bulletin aired with, minting a new dictionary version.
    /// </summary>
    Task<Result<JobAliasEdit>> AddAliasRuleAsync(
        string jobId, string word, string replacement, string? editedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     The review cockpit's pronunciation fix, as a job-scoped use case.
///     <para>
///         It exists so the correction an operator makes while listening is an OPERATOR action:
///         the caller names a bulletin, not a library item, so the HTTP boundary can apply that
///         job's market guard. Editing the library directly
///         (<c>PUT /pronunciation-dictionaries/{id}</c>) stays Admin, because a library item is
///         shared across stations and carries no market of its own to scope against.
///     </para>
///     <para>
///         The read-modify-write lives here rather than in the browser: the whole-list PUT means a
///         one-word-or-phrase fix has to be replayed against the current rule set if someone else
///         edited the dictionary in between, and doing that server-side costs one retry instead of
///         a round trip the operator waits through.
///     </para>
///     <para>
///         It targets the STATION layer only, and refuses the platform-wide one outright. A
///         correction made while listening to one market's bulletin must not silently change
///         what every other station says, and the market guard this route relies on has
///         nothing to check against for a dictionary that belongs to no market.
///     </para>
/// </summary>
public sealed class JobPronunciationService(
    IJobRepository jobs,
    IPronunciationDictionaryService dictionaries,
    ILogger<JobPronunciationService> logger) : IJobPronunciationService
{
    public const string NoDictionaryOnJob =
        "This bulletin aired with no pronunciation dictionary, so there is no rule set to correct.";

    public const string GlobalOnlyDictionaryOnJob =
        "This bulletin aired with the platform-wide pronunciation dictionary only. Ask an " +
        "administrator to give this station its own dictionary, or to change the platform-wide one.";

    public const string DictionaryNotInLibrary =
        "The dictionary this bulletin aired with is no longer in the pronunciation library.";

    /// <summary>One replay is enough: a second conflict means sustained contention, not a race.</summary>
    private const int MaxReplays = 1;

    public async Task<Result<JobAliasEdit>> AddAliasRuleAsync(
        string jobId, string word, string replacement, string? editedBy,
        CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetJobAsync(jobId, cancellationToken);
        if (job.IsFailure)
            return job.PropagateFailure<JobAliasEdit>();

        if (job.Value.PronunciationDictionaryId.IsNone)
        {
            return Result<JobAliasEdit>.Failure(
                job.Value.GlobalPronunciationDictionaryId.HasValue
                    ? GlobalOnlyDictionaryOnJob
                    : NoDictionaryOnJob);
        }

        string airedDictionaryId = job.Value.PronunciationDictionaryId.Value;

        for (int attempt = 0; ; attempt++)
        {
            var library = await dictionaries.ListAsync(cancellationToken);
            if (library.IsFailure)
                return library.PropagateFailure<JobAliasEdit>();

            // The job records the PROVIDER dictionary id (what actually aired), so the library item
            // is the one owning that resource — not whatever the station is assigned today.
            var target = library.Value.FirstOrDefault(d =>
                string.Equals(d.DictionaryId, airedDictionaryId, StringComparison.Ordinal));
            if (target is null)
                return Result<JobAliasEdit>.Failure(DictionaryNotInLibrary);

            // Defends the rule directly, not just via the job's station field: whatever route led
            // here, an operator never edits the dictionary every other station is reading from.
            if (target.IsGlobal)
                return Result<JobAliasEdit>.Failure(GlobalOnlyDictionaryOnJob);

            var edit = PronunciationRuleSetEditor.WithAlias(target.CurrentRules, word, replacement);
            if (edit.IsFailure)
                return edit.PropagateFailure<JobAliasEdit>();

            if (edit.Value.Outcome == AliasEditOutcome.AlreadyCorrect)
                return Result<JobAliasEdit>.Success(new JobAliasEdit(target, AliasEditOutcome.AlreadyCorrect));

            string note = edit.Value.Outcome == AliasEditOutcome.Changed
                ? $"Changed \"{word.Trim()}\" from bulletin review"
                : $"Added \"{word.Trim()}\" from bulletin review";

            var updated = await dictionaries.UpdateAsync(
                target.Id,
                new UpdatePronunciationDictionaryInput(
                    target.Name, target.Description, edit.Value.Rules, note, target.ETag, target.Scope),
                editedBy,
                cancellationToken);

            if (updated.IsSuccess)
            {
                logger.LogInformation(
                    "Job {JobId}: {Outcome} pronunciation rule for '{Word}' on dictionary {DictionaryId} (v{Version})",
                    jobId, edit.Value.Outcome, word, target.Id, updated.Value.CurrentVersion);
                return Result<JobAliasEdit>.Success(new JobAliasEdit(updated.Value, edit.Value.Outcome));
            }

            if (attempt >= MaxReplays || !IsRevisionConflict(updated))
                return updated.PropagateFailure<JobAliasEdit>();

            logger.LogWarning(
                "Job {JobId}: dictionary {DictionaryId} changed under the '{Word}' fix — replaying once",
                jobId, target.Id, word);
        }
    }

    private static bool IsRevisionConflict(Result<PronunciationDictionary> result) =>
        result.FailureMetadata.Match(f => f.Disposition == FailureDisposition.Conflict, () => false);
}
