using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>Input for creating a new delivery style (mints version 1). An explicit
/// <see cref="Id" /> preserves identity for config imports; null generates one.</summary>
public sealed record CreateDeliveryStyleInput(
    string Name,
    string? Description,
    DeliveryStylePolicy Policy,
    string? ChangeNote,
    string? Id = null);

/// <summary>Input for editing a style — a policy change mints a new version.</summary>
public sealed record UpdateDeliveryStyleInput(
    string Name,
    string? Description,
    DeliveryStylePolicy Policy,
    string? ChangeNote);

public interface IDeliveryStyleService
{
    Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<VersionedLibraryListPage<DeliveryStyle>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default);
    Task<Result<DeliveryStyle>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<DeliveryStyle>> CreateAsync(
        CreateDeliveryStyleInput input, string? createdBy, CancellationToken cancellationToken = default);

    Task<Result<DeliveryStyle>> UpdateAsync(
        string id, UpdateDeliveryStyleInput input, string? editedBy, CancellationToken cancellationToken = default);

    Task<Result<DeliveryStyle>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default);

    Task<Result<DeliveryStyle>> ArchiveAsync(
        string id, CancellationToken cancellationToken = default);

    Task<Result<DeliveryStyle>> UnarchiveAsync(
        string id, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
///     Use cases for the shared delivery-style library: list/get, version-minting edits
///     (create, policy edit, restore), and status toggles (archive, unarchive).
///     Persistence and the versioning invariants live in
///     <see cref="IDeliveryStyleRepository" /> and <see cref="DeliveryStyle" /> (mirrors
///     <see cref="VoiceProfileService" />).
/// </summary>
public sealed class DeliveryStyleService(
    IDeliveryStyleRepository repository,
    IScheduleRepository scheduleRepository,
    TimeProvider timeProvider,
    ILogger<DeliveryStyleService> logger) : IDeliveryStyleService
{
    public Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return repository.ListAsync(cancellationToken);
    }
    public Task<Result<VersionedLibraryListPage<DeliveryStyle>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query, cancellationToken);

    public Task<Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        repository.GetManyAsync(ids, includeHistory, cancellationToken);

    public async Task<Result<DeliveryStyle>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await repository.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return result.PropagateFailure<DeliveryStyle>();

        return result.Value.Match(
            Result<DeliveryStyle>.Success,
            () => Result<DeliveryStyle>.Failure($"Delivery style '{id}' not found."));
    }

    public async Task<Result<DeliveryStyle>> CreateAsync(
        CreateDeliveryStyleInput input, string? createdBy, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = DeliveryStyle.Create(input.Name, input.Description, input.Policy, input.ChangeNote, createdBy, now,
            input.Id);
        if (created.IsFailure)
            return created;

        var save = await repository.SaveAsync(created.Value, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(save);

        logger.LogInformation("Created delivery style {Id} ({Name})", created.Value.Id, created.Value.Name);
        return Result<DeliveryStyle>.Success(created.Value);
    }

    public async Task<Result<DeliveryStyle>> UpdateAsync(
        string id, UpdateDeliveryStyleInput input, string? editedBy, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var style = existing.Value;
        var minted = style.ApplyEdit(input.Name, input.Description, input.Policy, input.ChangeNote, editedBy,
            timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(style, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(save);

        logger.LogInformation("Updated delivery style {Id} — new version minted: {Minted}", id, minted);
        return Result<DeliveryStyle>.Success(style);
    }

    public async Task<Result<DeliveryStyle>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var style = existing.Value;
        var restore = style.RestoreVersion(version, restoredBy, timeProvider.GetUtcNow().UtcDateTime);
        if (restore.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(restore);

        var save = await repository.SaveAsync(style, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(save);

        logger.LogInformation("Restored delivery style {Id} to a new version from v{Version}", id, version);
        return Result<DeliveryStyle>.Success(style);
    }

    public async Task<Result<DeliveryStyle>> ArchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var style = existing.Value;
        style.Archive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(style, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(save);

        logger.LogInformation("Archived delivery style {Id}", id);
        return Result<DeliveryStyle>.Success(style);
    }

    public async Task<Result<DeliveryStyle>> UnarchiveAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var style = existing.Value;
        style.Unarchive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(style, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<DeliveryStyle>(save);

        logger.LogInformation("Unarchived delivery style {Id}", id);
        return Result<DeliveryStyle>.Success(style);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing.FailureMetadata.HasValue
                ? Result.Failure(existing.FailureMetadata.Value)
                : Result.Failure(existing.Error);

        var inUse = await LibraryReferenceGuard.EnsureNotAssignedAsync(
            scheduleRepository, "delivery style", id, s => s.DeliveryStyleId, cancellationToken);
        if (inUse.IsFailure)
            return inUse;

        var delete = await repository.DeleteAsync(id, cancellationToken);
        if (delete.IsFailure)
            return delete;

        logger.LogInformation("Deleted delivery style {Id}", id);
        return Result.Success();
    }
}
