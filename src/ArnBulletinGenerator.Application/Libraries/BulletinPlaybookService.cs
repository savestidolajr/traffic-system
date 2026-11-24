using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>
/// Input for creating a playbook. An explicit <see cref="Id" /> preserves identity for config
/// imports; null generates one.
/// </summary>
public sealed record CreatePlaybookInput(
    string Name,
    string? Description,
    string Role,
    PlaybookOverlay Overlay,
    string? ChangeNote,
    string? Id = null);

/// <summary>Input for editing a playbook. An overlay change mints a new version.</summary>
public sealed record UpdatePlaybookInput(
    string Name,
    string? Description,
    PlaybookOverlay Overlay,
    string? ChangeNote);

public interface IBulletinPlaybookService
{
    Task<Result<IReadOnlyList<BulletinPlaybook>>> ListAsync(CancellationToken ct = default);
    Task<Result<VersionedLibraryListPage<BulletinPlaybook>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> GetAsync(string id, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> CreateAsync(CreatePlaybookInput input, string? createdBy, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> UpdateAsync(string id, UpdatePlaybookInput input, string? editedBy, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> RestoreVersionAsync(string id, int version, string? restoredBy, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> ArchiveAsync(string id, CancellationToken ct = default);
    Task<Result<BulletinPlaybook>> UnarchiveAsync(string id, CancellationToken ct = default);
    Task<Result> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>
///     Use cases for the station bulletin-playbook library: list/get, version-minting edits,
///     restores, status toggles, and guarded deletion. Persistence and version invariants remain in
///     <see cref="IBulletinPlaybookRepository" /> and <see cref="BulletinPlaybook" />.
/// </summary>
public sealed class BulletinPlaybookService(
    IBulletinPlaybookRepository repository,
    IScheduleRepository scheduleRepository,
    IShippedPlaybookProvider shippedPlaybooks,
    TimeProvider timeProvider,
    ILogger<BulletinPlaybookService> logger) : IBulletinPlaybookService
{
    public Task<Result<VersionedLibraryListPage<BulletinPlaybook>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken ct = default) =>
        repository.ListAsync(query, ct);

    public Task<Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false, CancellationToken ct = default) =>
        repository.GetManyAsync(ids, includeHistory, ct);
    public Task<Result<IReadOnlyList<BulletinPlaybook>>> ListAsync(CancellationToken ct = default)
    {
        return repository.ListAsync(ct);
    }

    public async Task<Result<BulletinPlaybook>> GetAsync(string id, CancellationToken ct = default)
    {
        var result = await repository.GetAsync(id, ct);
        if (result.IsFailure)
            return result.PropagateFailure<BulletinPlaybook>();

        return result.Value.Match(
            Result<BulletinPlaybook>.Success,
            () => Result<BulletinPlaybook>.Failure($"Bulletin playbook '{id}' not found."));
    }

    public async Task<Result<BulletinPlaybook>> CreateAsync(
        CreatePlaybookInput input, string? createdBy, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var validation = ValidateOverlay(input.Role, input.Overlay);
        if (validation.IsFailure)
            return Result<BulletinPlaybook>.Failure(validation.Error);
        var created = BulletinPlaybook.Create(input.Name, input.Description, input.Role, input.Overlay,
            input.ChangeNote, createdBy, now, input.Id);
        if (created.IsFailure)
            return created;

        var save = await repository.SaveAsync(created.Value, ct);
        if (save.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(save);

        logger.LogInformation("Created bulletin playbook {Id} ({Name})", created.Value.Id, created.Value.Name);
        return Result<BulletinPlaybook>.Success(created.Value);
    }

    public async Task<Result<BulletinPlaybook>> UpdateAsync(
        string id, UpdatePlaybookInput input, string? editedBy, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        if (existing.IsFailure)
            return existing;

        var playbook = existing.Value;
        var validation = ValidateOverlay(playbook.Role, input.Overlay);
        if (validation.IsFailure)
            return Result<BulletinPlaybook>.Failure(validation.Error);
        bool minted = playbook.ApplyEdit(input.Name, input.Description, input.Overlay, input.ChangeNote, editedBy,
            timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(playbook, ct);
        if (save.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(save);

        logger.LogInformation("Updated bulletin playbook {Id}, new version minted: {Minted}", id, minted);
        return Result<BulletinPlaybook>.Success(playbook);
    }

    public async Task<Result<BulletinPlaybook>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        if (existing.IsFailure)
            return existing;

        var playbook = existing.Value;
        var source = playbook.Versions.FirstOrDefault(v => v.Version == version);
        if (source is not null)
        {
            var validation = ValidateOverlay(playbook.Role, source.Overlay);
            if (validation.IsFailure)
                return Result<BulletinPlaybook>.Failure(validation.Error);
        }

        var restore = playbook.RestoreVersion(version, restoredBy, timeProvider.GetUtcNow().UtcDateTime);
        if (restore.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(restore);

        var save = await repository.SaveAsync(playbook, ct);
        if (save.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(save);

        logger.LogInformation("Restored bulletin playbook {Id} to a new version from v{Version}", id, version);
        return Result<BulletinPlaybook>.Success(playbook);
    }

    private Result ValidateOverlay(string role, PlaybookOverlay overlay)
    {
        role = string.IsNullOrWhiteSpace(role) ? "writer" : role.Trim();
        var shipped = shippedPlaybooks.GetDefault(role);
        if (shipped.IsFailure)
            return shipped;

        var applied = shipped.Value.Apply(overlay);
        return applied.Refused.Count == 0
            ? Result.Success()
            : Result.Failure($"Playbook overlay refused: {string.Join("; ", applied.Refused)}");
    }

    public async Task<Result<BulletinPlaybook>> ArchiveAsync(string id, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        if (existing.IsFailure)
            return existing;

        var playbook = existing.Value;
        playbook.Archive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(playbook, ct);
        if (save.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(save);

        logger.LogInformation("Archived bulletin playbook {Id}", id);
        return Result<BulletinPlaybook>.Success(playbook);
    }

    public async Task<Result<BulletinPlaybook>> UnarchiveAsync(string id, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        if (existing.IsFailure)
            return existing;

        var playbook = existing.Value;
        playbook.Unarchive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(playbook, ct);
        if (save.IsFailure)
            return ResultFailure.Propagate<BulletinPlaybook>(save);

        logger.LogInformation("Unarchived bulletin playbook {Id}", id);
        return Result<BulletinPlaybook>.Success(playbook);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        if (existing.IsFailure)
            return existing.FailureMetadata.HasValue
                ? Result.Failure(existing.FailureMetadata.Value)
                : Result.Failure(existing.Error);

        var inUse = await LibraryReferenceGuard.EnsureNotAssignedAsync(
            scheduleRepository, "bulletin playbook", id, s => s.PlaybookId, ct);
        if (inUse.IsFailure)
            return inUse;

        var delete = await repository.DeleteAsync(id, ct);
        if (delete.IsFailure)
            return delete;

        logger.LogInformation("Deleted bulletin playbook {Id}", id);
        return Result.Success();
    }
}
