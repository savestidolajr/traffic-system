using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>Input for creating a new voice variant (mints version 1). An explicit
/// <see cref="Id" /> preserves identity for config imports; null generates one.
/// <see cref="RemixedVoiceId" />, when set, adopts a text-remix preview: the service mints the
/// permanent voice at the provider FIRST and uses it as the variant's base — so the same
/// pre-flight/rollback contract that guards the other provider round-trips applies here.</summary>
public sealed record CreateVoiceProfileInput(
    string Name,
    string? Description,
    string BaseVoiceId,
    string BaseVoiceName,
    string Model,
    VoiceSettings Settings,
    string? ChangeNote,
    double PaceBoostWpm = 0,
    string? Id = null,
    string? RemixedVoiceId = null,
    string? RemixDescription = null);

/// <summary>Input for editing a variant — a settings/model/pace change mints a new version.
/// <see cref="RemixedVoiceId" />, when set, adopts a text-remix preview exactly like Create
/// (mint → swap the variant's base voice → mint a new version).</summary>
public sealed record UpdateVoiceProfileInput(
    string Name,
    string? Description,
    string Model,
    VoiceSettings Settings,
    string? ChangeNote,
    string? NewBaseVoiceId = null,
    string? NewBaseVoiceName = null,
    double PaceBoostWpm = 0,
    string? BaseVoiceName = null,
    string? RemixedVoiceId = null,
    string? RemixDescription = null);

public interface IVoiceProfileService
{
    Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<VersionedLibraryListPage<VoiceProfile>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default);
    Task<Result<VoiceProfile>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<VoiceProfile>> CreateAsync(
        CreateVoiceProfileInput input, string? createdBy, CancellationToken cancellationToken = default);

    Task<Result<VoiceProfile>> UpdateAsync(
        string id, UpdateVoiceProfileInput input, string? editedBy, CancellationToken cancellationToken = default);

    Task<Result<VoiceProfile>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default);

    Task<Result<VoiceProfile>> ArchiveAsync(
        string id, CancellationToken cancellationToken = default);

    Task<Result<VoiceProfile>> UnarchiveAsync(
        string id, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
///     Use cases for the shared voice library: list/get plus the version-minting edits
///     (create, settings edit, restore). Persistence and the versioning invariants live in
///     <see cref="IVoiceProfileRepository" /> and <see cref="VoiceProfile" />; calibration
///     surfacing and audio preview are handled at the HTTP edge where the synth deps live.
///     Adopting a text-remix preview mints a permanent provider voice, so the service owns the
///     same pre-flight-before-provider-call + rollback contract as
///     <see cref="PronunciationDictionaryService" /> — an adopted remix whose local record
///     cannot be saved must not leak a billed orphan voice.
/// </summary>
public sealed class VoiceProfileService(
    IVoiceProfileRepository repository,
    IScheduleRepository scheduleRepository,
    IVoiceRemixer voiceRemixer,
    TimeProvider timeProvider,
    ILogger<VoiceProfileService> logger,
    IVoiceCatalogCacheInvalidator? voiceCatalogCacheInvalidator = null) : IVoiceProfileService
{
    public Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return repository.ListAsync(cancellationToken);
    }
    public Task<Result<VersionedLibraryListPage<VoiceProfile>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query, cancellationToken);

    public Task<Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        repository.GetManyAsync(ids, includeHistory, cancellationToken);

    public async Task<Result<VoiceProfile>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await repository.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return Result<VoiceProfile>.Failure(result.Error);

        return result.Value.Match(
            Result<VoiceProfile>.Success,
            () => Result<VoiceProfile>.Failure($"Voice profile '{id}' not found."));
    }

    public async Task<Result<VoiceProfile>> CreateAsync(
        CreateVoiceProfileInput input, string? createdBy, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Deterministic, local pre-flight BEFORE any upstream call: a single version over the
        // Azure Table property ceiling can never be saved, and discovering that after the
        // ElevenLabs mint would orphan a billed voice (the class of leak the pronunciation
        // pre-flight closed). An adopted remix's base voice id is unknown until minted, so the
        // candidate is serialised with a placeholder sized to ElevenLabs' real voice ids — the
        // same estimate the pronunciation service uses for its provider-issued ids.
        var candidate = new VoiceProfileVersion(
            1, input.Settings, input.Model, input.ChangeNote, now, createdBy, null,
            input.Settings.GetConfigurationKey(
                string.IsNullOrWhiteSpace(input.RemixedVoiceId) ? input.BaseVoiceId : PendingMintedVoiceId),
            input.PaceBoostWpm);
        var fits = VoiceProfile.EnsureVersionFitsStorage(candidate);
        if (fits.IsFailure)
            return ResultFailure.Propagate<VoiceProfile>(fits);

        string? mintedVoiceId = null;
        var baseVoiceId = input.BaseVoiceId;
        var baseVoiceName = input.BaseVoiceName;
        // Adopted remix → mint the previewed voice and use it as the variant's base. The mint is
        // the FIRST provider call, so a mint failure has nothing to roll back yet.
        if (!string.IsNullOrWhiteSpace(input.RemixedVoiceId))
        {
            var mintName = RemixedName(input.BaseVoiceName);
            var minted = await voiceRemixer.CreateVoiceFromPreviewAsync(
                input.RemixedVoiceId, mintName, input.RemixDescription ?? mintName, cancellationToken);
            if (minted.IsFailure)
                return Result<VoiceProfile>.Failure(minted.Error);
            mintedVoiceId = minted.Value;
            voiceCatalogCacheInvalidator?.InvalidateVoiceCatalog();
            baseVoiceId = minted.Value;
            baseVoiceName = mintName;
        }

        var created = VoiceProfile.Create(input.Name, input.Description, baseVoiceId, baseVoiceName,
            input.Model, input.Settings, input.ChangeNote, createdBy, now, input.PaceBoostWpm, input.Id);
        if (created.IsFailure)
            return mintedVoiceId is null
                ? Result<VoiceProfile>.Failure(created.Error)
                : await RollbackCreateAsync(mintedVoiceId, created, cancellationToken);

        var save = await repository.SaveAsync(created.Value, cancellationToken);
        if (save.IsFailure)
        {
            // Preserve the typed disposition (e.g. the trimmer's InvalidInput over-ceiling
            // refusal) so the HTTP edge can map it to 400 instead of a misleading 500.
            if (mintedVoiceId is not null)
                return await RollbackCreateAsync(mintedVoiceId, save, cancellationToken);
            return ResultFailure.Propagate<VoiceProfile>(save);
        }

        logger.LogInformation("Created voice profile {Id} ({Name})", created.Value.Id, created.Value.Name);
        return Result<VoiceProfile>.Success(created.Value);
    }

    public async Task<Result<VoiceProfile>> UpdateAsync(
        string id, UpdateVoiceProfileInput input, string? editedBy, CancellationToken cancellationToken = default)
    {
        // Existence is checked BEFORE any provider call — a missing profile must never mint a
        // voice that then has no record to attach to.
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var profile = existing.Value;

        // Same deterministic pre-flight as Create, sized to the version this edit will mint. A
        // non-adoption edit keeps the profile's real base voice id; an adoption's id is the
        // placeholder until minted.
        var candidate = new VoiceProfileVersion(
            profile.CurrentVersion + 1, input.Settings, input.Model, input.ChangeNote,
            timeProvider.GetUtcNow().UtcDateTime, editedBy, null,
            input.Settings.GetConfigurationKey(string.IsNullOrWhiteSpace(input.RemixedVoiceId)
                ? input.NewBaseVoiceId ?? profile.BaseVoiceId
                : PendingMintedVoiceId),
            input.PaceBoostWpm);
        var fits = VoiceProfile.EnsureVersionFitsStorage(candidate);
        if (fits.IsFailure)
            return ResultFailure.Propagate<VoiceProfile>(fits);

        string? mintedVoiceId = null;
        // An adopted remix swaps the base voice; ApplyEdit then mints a new version for it.
        if (!string.IsNullOrWhiteSpace(input.RemixedVoiceId))
        {
            var mintName = RemixedName(input.BaseVoiceName);
            var mintedRemix = await voiceRemixer.CreateVoiceFromPreviewAsync(
                input.RemixedVoiceId, mintName, input.RemixDescription ?? mintName, cancellationToken);
            if (mintedRemix.IsFailure)
                return Result<VoiceProfile>.Failure(mintedRemix.Error);
            voiceCatalogCacheInvalidator?.InvalidateVoiceCatalog();
            mintedVoiceId = mintedRemix.Value;
            profile.AdoptBaseVoice(mintedRemix.Value, mintName);
        }
        if (!string.IsNullOrWhiteSpace(input.NewBaseVoiceId))
            profile.AdoptBaseVoice(input.NewBaseVoiceId, input.NewBaseVoiceName);

        var minted = profile.ApplyEdit(input.Name, input.Description, input.Model, input.Settings,
            input.ChangeNote, editedBy, timeProvider.GetUtcNow().UtcDateTime, input.PaceBoostWpm);
        if (minted.IsFailure)
            return mintedVoiceId is null
                ? Result<VoiceProfile>.Failure(minted.Error)
                : await RollbackCreateAsync(mintedVoiceId, minted, cancellationToken);

        var save = await repository.SaveAsync(profile, cancellationToken);
        if (save.IsFailure)
        {
            if (mintedVoiceId is not null)
                return await RollbackCreateAsync(mintedVoiceId, save, cancellationToken);
            return ResultFailure.Propagate<VoiceProfile>(save);
        }

        logger.LogInformation("Updated voice profile {Id} — new version minted: {Minted}", id, minted.Value);
        return Result<VoiceProfile>.Success(profile);
    }

    public async Task<Result<VoiceProfile>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var profile = existing.Value;
        var restore = profile.RestoreVersion(version, restoredBy, timeProvider.GetUtcNow().UtcDateTime);
        if (restore.IsFailure)
            return Result<VoiceProfile>.Failure(restore.Error);

        var save = await repository.SaveAsync(profile, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<VoiceProfile>(save);

        logger.LogInformation("Restored voice profile {Id} to a new version from v{Version}", id, version);
        return Result<VoiceProfile>.Success(profile);
    }

    public async Task<Result<VoiceProfile>> ArchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var profile = existing.Value;
        profile.Archive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(profile, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<VoiceProfile>(save);

        logger.LogInformation("Archived voice profile {Id}", id);
        return Result<VoiceProfile>.Success(profile);
    }

    public async Task<Result<VoiceProfile>> UnarchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var profile = existing.Value;
        profile.Unarchive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(profile, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<VoiceProfile>(save);

        logger.LogInformation("Unarchived voice profile {Id}", id);
        return Result<VoiceProfile>.Success(profile);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return Result.Failure(existing.Error);

        var inUse = await LibraryReferenceGuard.EnsureNotAssignedAsync(
            scheduleRepository, "voice profile", id, s => s.VoiceProfileIds, cancellationToken);
        if (inUse.IsFailure)
            return inUse;

        var delete = await repository.DeleteAsync(id, cancellationToken);
        if (delete.IsFailure)
            return delete;

        logger.LogInformation("Deleted voice profile {Id}", id);
        return Result.Success();
    }

    /// <summary>The minted voice's display name: the original base voice name marked as remixed.</summary>
    private static string RemixedName(string? baseVoiceName)
    {
        var name = string.IsNullOrWhiteSpace(baseVoiceName) ? "Voice" : baseVoiceName.Trim();
        const string suffix = " (remixed)";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name : name + suffix;
    }

    /// <summary>
    ///     ElevenLabs voice ids are 20-character alphanumeric strings (observed across every live
    ///     voice, e.g. "oKDz3nEzNfwKIVvUwPs0"). The pre-flight size check serialises the candidate
    ///     version with a placeholder of this exact length so the estimate matches the persisted
    ///     size to the byte; if the provider ever changes the id format, the storage wall still
    ///     catches the residual window and the delete rollback cleans it.
    /// </summary>
    private static readonly string PendingMintedVoiceId = new('x', 20);

    /// <summary>
    ///     Best-effort rollback for the residual window after a successful upstream mint: deletes
    ///     the orphaned voice (unlike pronunciation dictionaries, ElevenLabs voices have a hard
    ///     delete) and ALWAYS returns the ORIGINAL failure — a rollback that swallows the real
    ///     error is worse than the orphan it could not clean.
    /// </summary>
    private async Task<Result<VoiceProfile>> RollbackCreateAsync(
        string mintedVoiceId, Result failure, CancellationToken cancellationToken)
    {
        var deleted = await voiceRemixer.DeleteVoiceAsync(mintedVoiceId, cancellationToken);
        voiceCatalogCacheInvalidator?.InvalidateVoiceCatalog();
        if (deleted.IsFailure)
        {
            logger.LogError(
                "Rollback failed for orphaned ElevenLabs remixed voice {VoiceId}: " +
                "{RollbackError}. Original error: {Error}", mintedVoiceId, deleted.Error, failure.Error);
        }
        else
        {
            logger.LogWarning(
                "Deleted orphaned ElevenLabs remixed voice {VoiceId} after the local record could not " +
                "be saved: {Error}", mintedVoiceId, failure.Error);
        }
        return ResultFailure.Propagate<VoiceProfile>(failure);
    }
}
