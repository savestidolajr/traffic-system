using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>Input for creating a new pronunciation dictionary (uploads + mints version 1). An
/// explicit <see cref="Id" /> preserves identity for config imports; null generates one.</summary>
public sealed record CreatePronunciationDictionaryInput(
    string Name,
    string? Description,
    IReadOnlyList<PronunciationRule> Rules,
    string? ChangeNote,
    string? Id = null,
    PronunciationScope Scope = PronunciationScope.Station);

/// <summary>Input for editing a dictionary — a rule-set change uploads + mints a new version.</summary>
public sealed record UpdatePronunciationDictionaryInput(
    string Name,
    string? Description,
    IReadOnlyList<PronunciationRule> Rules,
    string? ChangeNote,
    string? ExpectedEtag = null,
    PronunciationScope Scope = PronunciationScope.Station);

public interface IPronunciationDictionaryService
{
    Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<VersionedLibraryListPage<PronunciationDictionary>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default);
    Task<Result<PronunciationDictionary>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<PronunciationDictionary>> CreateAsync(
        CreatePronunciationDictionaryInput input, string? createdBy, CancellationToken cancellationToken = default);

    Task<Result<PronunciationDictionary>> UpdateAsync(
        string id, UpdatePronunciationDictionaryInput input, string? editedBy,
        CancellationToken cancellationToken = default);

    Task<Result<PronunciationDictionary>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default);

    Task<Result<PronunciationDictionary>> ArchiveAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<PronunciationDictionary>> UnarchiveAsync(string id, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
///     Use cases for the shared pronunciation-dictionary library: list/get, version-minting edits
///     (create, rule edit, restore), and status toggles (archive, unarchive). Each create/edit
///     round-trips the rules to the TTS provider (<see cref="IPronunciationDictionaryProvider" />)
///     — which mints a new provider version id — then persists the dictionary with that id so the
///     correct version is attached on air. Mirrors <see cref="DeliveryStyleService" />, but owns
///     the provider round-trip the delivery library does not need.
/// </summary>
public sealed class PronunciationDictionaryService(
    IPronunciationDictionaryRepository repository,
    IPronunciationDictionaryProvider provider,
    IScheduleRepository scheduleRepository,
    TimeProvider timeProvider,
    ILogger<PronunciationDictionaryService> logger) : IPronunciationDictionaryService
{
    public Task<Result<VersionedLibraryListPage<PronunciationDictionary>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query, cancellationToken);

    public Task<Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        repository.GetManyAsync(ids, includeHistory, cancellationToken);
    public Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return repository.ListAsync(cancellationToken);
    }

    public async Task<Result<PronunciationDictionary>> GetAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var result = await repository.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return Result<PronunciationDictionary>.Failure(result.Error);

        return result.Value.Match(
            Result<PronunciationDictionary>.Success,
            () => Result<PronunciationDictionary>.Failure($"Pronunciation dictionary '{id}' not found."));
    }

    public async Task<Result<PronunciationDictionary>> CreateAsync(
        CreatePronunciationDictionaryInput input, string? createdBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Result<PronunciationDictionary>.Failure("Pronunciation dictionary name is required.");

        var rules = input.Rules ?? [];

        if (input.Scope == PronunciationScope.Global)
        {
            var clash = await EnsureNoOtherActiveGlobalAsync(input.Id, cancellationToken);
            if (clash.IsFailure)
                return ResultFailure.Propagate<PronunciationDictionary>(clash);
        }

        // Deterministic, local pre-flight BEFORE any upstream call: a single version over the
        // Azure Table property ceiling can never be saved, and discovering that after the
        // ElevenLabs upload would orphan an upstream dictionary (observed twice on dev). The
        // version ids below are placeholders sized to the provider's real ids so the serialised
        // length matches what the repository will persist to the byte.
        var candidate = new PronunciationDictionaryVersion(
            1, PendingProviderVersionId, rules, input.ChangeNote,
            timeProvider.GetUtcNow().UtcDateTime, createdBy,
            rules.Any(r => r.Type == PronunciationRuleType.Phoneme) ? PendingProviderVersionId : null);
        var fits = PronunciationDictionary.EnsureVersionFitsStorage(candidate);
        if (fits.IsFailure)
            return ResultFailure.Propagate<PronunciationDictionary>(fits);

        var created = await provider.CreateAsync(input.Name.Trim(), rules, cancellationToken);
        if (created.IsFailure)
            return Result<PronunciationDictionary>.Failure(created.Error);

        // A phoneme-carrying dictionary also mints an alias-only version of the SAME upstream
        // dictionary — the safe attach target for eleven_multilingual_v2, which would delete
        // phoneme-matched words from the audio. No phoneme rules → the full version is already
        // safe on every model.
        string? aliasOnlyVersionId = null;
        if (rules.Any(r => r.Type == PronunciationRuleType.Phoneme))
        {
            var aliasRules = rules.Where(r => r.Type == PronunciationRuleType.Alias).ToList();
            if (aliasRules.Count > 0)
            {
                var aliasResult = await provider.SetRulesAsync(
                    created.Value.DictionaryId, aliasRules, cancellationToken);
                if (aliasResult.IsFailure)
                    return await RollbackCreateAsync(created.Value.DictionaryId, aliasResult, cancellationToken);
                aliasOnlyVersionId = aliasResult.Value;
            }
        }

        var dictionary = PronunciationDictionary.Create(
            input.Name, input.Description, created.Value.DictionaryId, created.Value.VersionId, rules,
            input.ChangeNote, createdBy, timeProvider.GetUtcNow().UtcDateTime, input.Id, input.Scope,
            aliasOnlyVersionId);
        if (dictionary.IsFailure)
            return await RollbackCreateAsync(created.Value.DictionaryId, dictionary, cancellationToken);

        var save = await repository.SaveAsync(dictionary.Value, cancellationToken);
        if (save.IsFailure)
            return await RollbackCreateAsync(created.Value.DictionaryId, save, cancellationToken);

        logger.LogInformation(
            "Created pronunciation dictionary {Id} ({Name}) — ElevenLabs {DictionaryId}, alias-only version {AliasOnlyVersionId}",
            dictionary.Value.Id, dictionary.Value.Name, dictionary.Value.DictionaryId, aliasOnlyVersionId ?? "(none)");
        return Result<PronunciationDictionary>.Success(dictionary.Value);
    }

    public async Task<Result<PronunciationDictionary>> UpdateAsync(
        string id, UpdatePronunciationDictionaryInput input, string? editedBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var dictionary = existing.Value;
        if (input.ExpectedEtag is not null &&
            !string.Equals(input.ExpectedEtag, dictionary.ETag, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Update rejected for pronunciation dictionary {Id}: revision mismatch " +
                "(expected ETag {ExpectedEtag}, actual {ActualEtag})",
                id, input.ExpectedEtag, dictionary.ETag);
            return Result<PronunciationDictionary>.Failure(RevisionMismatch(id));
        }
        if (input.Scope != dictionary.Scope)
        {
            if (input.Scope == PronunciationScope.Global)
            {
                var clash = await EnsureNoOtherActiveGlobalAsync(dictionary.Id, cancellationToken);
                if (clash.IsFailure)
                    return ResultFailure.Propagate<PronunciationDictionary>(clash);
            }

            // Retarget mints no version (reach is not rule content), so the save below is what
            // persists a scope-only edit — ApplyRuleEditAsync always saves, even with no rule diff.
            dictionary.Retarget(input.Scope, timeProvider.GetUtcNow().UtcDateTime);
        }

        var desiredRules = input.Rules ?? [];

        return await ApplyRuleEditAsync(dictionary, input.Name, input.Description, desiredRules, input.ChangeNote,
            editedBy, cancellationToken);
    }

    public async Task<Result<PronunciationDictionary>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var dictionary = existing.Value;
        var source = dictionary.Versions.FirstOrDefault(v => v.Version == version);
        if (source is null)
            return Result<PronunciationDictionary>.Failure($"Pronunciation dictionary version {version} not found.");

        // Restoring re-uploads the prior version's rules, minting a fresh provider + domain version.
        return await ApplyRuleEditAsync(dictionary, dictionary.Name, dictionary.Description, source.Rules,
            $"Restored from version {version}.", restoredBy, cancellationToken);
    }

    public async Task<Result<PronunciationDictionary>> ArchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        if (existing.Value.IsGlobal && existing.Value.IsActive)
            return Result<PronunciationDictionary>.Failure(ActiveGlobalIsLoadBearing("archived"));

        existing.Value.Archive(timeProvider.GetUtcNow().UtcDateTime);
        var save = await repository.SaveAsync(existing.Value, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<PronunciationDictionary>(save);

        logger.LogInformation("Archived pronunciation dictionary {Id}", id);
        return Result<PronunciationDictionary>.Success(existing.Value);
    }

    public async Task<Result<PronunciationDictionary>> UnarchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        if (existing.Value.IsGlobal)
        {
            var clash = await EnsureNoOtherActiveGlobalAsync(existing.Value.Id, cancellationToken);
            if (clash.IsFailure)
                return ResultFailure.Propagate<PronunciationDictionary>(clash);
        }

        existing.Value.Unarchive(timeProvider.GetUtcNow().UtcDateTime);
        var save = await repository.SaveAsync(existing.Value, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<PronunciationDictionary>(save);

        logger.LogInformation("Unarchived pronunciation dictionary {Id}", id);
        return Result<PronunciationDictionary>.Success(existing.Value);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return Result.Failure(existing.Error);

        // The assignment guard cannot see a platform-wide dictionary: no schedule references one,
        // so without this check an admin could delete the dictionary every station is reading from.
        if (existing.Value.IsGlobal && existing.Value.IsActive)
            return Result.Failure(ActiveGlobalIsLoadBearing("deleted"));

        var inUse = await LibraryReferenceGuard.EnsureNotAssignedAsync(
            scheduleRepository, "pronunciation dictionary", id,
            s => s.PronunciationDictionaryId, cancellationToken);
        if (inUse.IsFailure)
            return inUse;

        var delete = await repository.DeleteAsync(id, cancellationToken);
        if (delete.IsFailure)
            return delete;

        logger.LogInformation("Deleted pronunciation dictionary {Id}", id);
        return Result.Success();
    }

    /// <summary>
    ///     Shared edit path: when the rule set changed, pushes the diff to the provider
    ///     (remove dropped rules, add/replace the rest), takes the resulting version id, and
    ///     appends a new domain version. A name/description-only change persists without a new
    ///     version. Removals and additions are ordered remove-then-add so the final version id is
    ///     the one carrying the full desired set.
    /// </summary>
    private async Task<Result<PronunciationDictionary>> ApplyRuleEditAsync(
        PronunciationDictionary dictionary, string name, string? description,
        IReadOnlyList<PronunciationRule> desiredRules, string? changeNote, string? editedBy,
        CancellationToken cancellationToken)
    {
        string versionId = dictionary.CurrentElevenLabsVersionId;
        string? aliasOnlyVersionId = null;

        if (dictionary.RulesDifferFrom(desiredRules))
        {
            // Same deterministic pre-flight as create, BEFORE any upstream call: a new version
            // that alone exceeds the storage ceiling can never be persisted, and uploading it
            // first would leave the upstream dictionary ahead of the local record with no
            // reconciliation path.
            var candidate = new PronunciationDictionaryVersion(
                dictionary.CurrentVersion + 1, PendingProviderVersionId, desiredRules, changeNote,
                timeProvider.GetUtcNow().UtcDateTime, editedBy,
                desiredRules.Any(r => r.Type == PronunciationRuleType.Phoneme) ? PendingProviderVersionId : null);
            var fits = PronunciationDictionary.EnsureVersionFitsStorage(candidate);
            if (fits.IsFailure)
                return ResultFailure.Propagate<PronunciationDictionary>(fits);

            var desiredKeys = desiredRules.Select(r => r.StringToReplace).ToHashSet(StringComparer.Ordinal);
            var removed = dictionary.CurrentRules
                .Select(r => r.StringToReplace)
                .Where(s => !desiredKeys.Contains(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (removed.Count > 0)
            {
                var removeResult = await provider.RemoveRulesAsync(dictionary.DictionaryId, removed, cancellationToken);
                if (removeResult.IsFailure)
                    return Result<PronunciationDictionary>.Failure(removeResult.Error);
                versionId = removeResult.Value;
            }

            if (desiredRules.Count > 0)
            {
                var addResult = await provider.AddRulesAsync(dictionary.DictionaryId, desiredRules, cancellationToken);
                if (addResult.IsFailure)
                    return Result<PronunciationDictionary>.Failure(addResult.Error);
                versionId = addResult.Value;
            }

            // Keep the alias-only version line current for phoneme-carrying dictionaries (see
            // CreateAsync). A dictionary whose phoneme rules were removed needs no alias-only
            // version — the full set is safe again.
            if (desiredRules.Any(r => r.Type == PronunciationRuleType.Phoneme))
            {
                var aliasRules = desiredRules.Where(r => r.Type == PronunciationRuleType.Alias).ToList();
                if (aliasRules.Count > 0)
                {
                    var aliasResult = await provider.SetRulesAsync(
                        dictionary.DictionaryId, aliasRules, cancellationToken);
                    if (aliasResult.IsFailure)
                        return Result<PronunciationDictionary>.Failure(aliasResult.Error);
                    aliasOnlyVersionId = aliasResult.Value;
                }
            }
        }

        var minted = dictionary.ApplyEdit(name, description, versionId, desiredRules, changeNote, editedBy,
            timeProvider.GetUtcNow().UtcDateTime, aliasOnlyVersionId);

        var save = await repository.SaveAsync(dictionary, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<PronunciationDictionary>(save);

        logger.LogInformation("Updated pronunciation dictionary {Id} — new version minted: {Minted}",
            dictionary.Id, minted);
        return Result<PronunciationDictionary>.Success(dictionary);
    }

    /// <summary>
    ///     Fails when another active dictionary already holds platform-wide scope.
    ///     <para>
    ///         Exactly one is deliberate. It pins the emitted locator array at a fixed two, well
    ///         inside ElevenLabs' three-locator cap, and it removes "which platform-wide
    ///         dictionary won?" as a question anyone can ask — the same reason the style-rule set
    ///         is a singleton.
    ///     </para>
    /// </summary>
    private async Task<Result> EnsureNoOtherActiveGlobalAsync(
        string? candidateId, CancellationToken cancellationToken)
    {
        var listResult = await repository.ListAsync(cancellationToken);
        if (listResult.IsFailure)
            return Result.Failure(listResult.Error);

        var incumbent = listResult.Value.FirstOrDefault(d =>
            d.IsGlobal && d.IsActive && !string.Equals(d.Id, candidateId, StringComparison.Ordinal));
        if (incumbent is null)
            return Result.Success();

        return Result.Failure(OperationalFailure.Conflict(
            "pronunciation.global_already_set",
            $"'{incumbent.Name}' is already the platform-wide pronunciation dictionary. " +
            "Demote it before making another one platform-wide."));
    }

    /// <summary>
    ///     The refusal to remove the dictionary every station is currently reading from.
    /// </summary>
    private static OperationalFailure ActiveGlobalIsLoadBearing(string action) =>
        OperationalFailure.Conflict(
            "pronunciation.global_in_use",
            $"This is the platform-wide pronunciation dictionary, so every station is reading from " +
            $"it and it cannot be {action}. Change it to station scope first.");

    private static OperationalFailure RevisionMismatch(string id) =>
        OperationalFailure.Conflict(
            "pronunciation.revision_mismatch",
            $"Conflict: the pronunciation dictionary '{id}' was modified by another request. " +
            "Reload and try again.");

    /// <summary>
    ///     ElevenLabs dictionary/version ids are 21-character alphanumeric strings (observed
    ///     across every live dictionary). The pre-flight size check serialises the candidate
    ///     version with a placeholder of this exact length so the estimate matches the persisted
    ///     size to the byte; if the provider ever changes the id format, the storage wall still
    ///     catches the residual window and the archive rollback cleans it.
    /// </summary>
    private static readonly string PendingProviderVersionId = new('x', 21);

    /// <summary>
    ///     Best-effort rollback for the residual window after a successful upstream create:
    ///     archives the orphaned dictionary (ElevenLabs has no hard delete) and ALWAYS returns
    ///     the ORIGINAL failure — a rollback that swallows the real error is worse than the
    ///     orphan it could not clean.
    /// </summary>
    private async Task<Result<PronunciationDictionary>> RollbackCreateAsync(
        string dictionaryId, Result failure, CancellationToken cancellationToken)
    {
        var archived = await provider.ArchiveAsync(dictionaryId, cancellationToken);
        if (archived.IsFailure)
        {
            logger.LogError(
                "Rollback failed for orphaned ElevenLabs pronunciation dictionary {DictionaryId}: " +
                "{RollbackError}. Original error: {Error}", dictionaryId, archived.Error, failure.Error);
        }
        else
        {
            logger.LogWarning(
                "Archived orphaned ElevenLabs pronunciation dictionary {DictionaryId} after the local " +
                "record could not be saved: {Error}", dictionaryId, failure.Error);
        }
        return ResultFailure.Propagate<PronunciationDictionary>(failure);
    }
}
