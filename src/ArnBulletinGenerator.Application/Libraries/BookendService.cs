using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>Input for creating a new bookend pair (mints version 1). An explicit
/// <see cref="Id" /> preserves identity for config imports; null generates one.</summary>
public sealed record CreateBookendInput(
    string Name,
    string? Description,
    string IntroText,
    string OutroText,
    string? ChangeNote,
    string? Actor,
    string? Id = null);

/// <summary>Input for editing a bookend pair — a change to either line mints a new version.</summary>
public sealed record UpdateBookendInput(
    string Id,
    string Name,
    string? Description,
    string IntroText,
    string OutroText,
    string? ChangeNote,
    string? Actor);

public interface IBookendService
{
    Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<VersionedLibraryListPage<Bookend>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, Option<Bookend>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default);
    Task<Result<Bookend>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<Bookend>> CreateAsync(
        CreateBookendInput input, CancellationToken cancellationToken = default);

    Task<Result<Bookend>> UpdateAsync(
        UpdateBookendInput input, CancellationToken cancellationToken = default);

    Task<Result<Bookend>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default);

    Task<Result<Bookend>> ArchiveAsync(
        string id, string? actor, CancellationToken cancellationToken = default);

    Task<Result<Bookend>> UnarchiveAsync(
        string id, string? actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
///     Use cases for the shared bookend library of intro/outro pairs: list/get, version-minting
///     edits (create, text edit, restore), and status toggles (archive, unarchive). Persistence
///     and the versioning invariants live in <see cref="IBookendRepository" /> and
///     <see cref="Bookend" /> (mirrors <see cref="DeliveryStyleService" />). Deletion additionally
///     refuses any bookend a schedule still references — by primary id OR by a rotation set — so
///     a phrase cannot be pulled out from under a station that rotates it
///     (see <see cref="LibraryReferenceGuard" />).
/// </summary>
public sealed class BookendService(
    IBookendRepository repository,
    IScheduleRepository scheduleRepository,
    TimeProvider timeProvider,
    ILogger<BookendService> logger) : IBookendService
{
    public Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return repository.ListAsync(cancellationToken);
    }
    public Task<Result<VersionedLibraryListPage<Bookend>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default) =>
        repository.ListAsync(query, cancellationToken);

    public Task<Result<IReadOnlyDictionary<string, Option<Bookend>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        repository.GetManyAsync(ids, includeHistory, cancellationToken);

    public async Task<Result<Bookend>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await repository.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return result.PropagateFailure<Bookend>();

        return result.Value.Match(
            Result<Bookend>.Success,
            () => Result<Bookend>.Failure($"Bookend '{id}' not found."));
    }

    public async Task<Result<Bookend>> CreateAsync(
        CreateBookendInput input, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = Bookend.Create(input.Name, input.Description,
            new BookendTemplate(input.IntroText, input.OutroText),
            input.ChangeNote, input.Actor, now, input.Id);
        if (created.IsFailure)
            return created;

        var save = await repository.SaveAsync(created.Value, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<Bookend>(save);

        logger.LogInformation("Created bookend {Id} ({Name})", created.Value.Id, created.Value.Name);
        return Result<Bookend>.Success(created.Value);
    }

    public async Task<Result<Bookend>> UpdateAsync(
        UpdateBookendInput input, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(input.Id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var bookend = existing.Value;
        var template = new BookendTemplate(input.IntroText, input.OutroText);
        var validation = template.Validate();
        if (validation.IsFailure)
            return Result<Bookend>.Failure(validation.Error);

        var minted = bookend.ApplyEdit(input.Name, input.Description, template,
            input.ChangeNote, input.Actor, timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(bookend, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<Bookend>(save);

        logger.LogInformation("Updated bookend {Id} — new version minted: {Minted}", input.Id, minted);
        return Result<Bookend>.Success(bookend);
    }

    public async Task<Result<Bookend>> RestoreVersionAsync(
        string id, int version, string? restoredBy, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var bookend = existing.Value;
        var historical = bookend.Versions.FirstOrDefault(v => v.Version == version);
        if (historical is not null)
        {
            var validation = historical.Template.Validate();
            if (validation.IsFailure)
                return Result<Bookend>.Failure(validation.Error);
        }

        var restore = bookend.RestoreVersion(version, restoredBy, timeProvider.GetUtcNow().UtcDateTime);
        if (restore.IsFailure)
            return ResultFailure.Propagate<Bookend>(restore);

        var save = await repository.SaveAsync(bookend, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<Bookend>(save);

        logger.LogInformation("Restored bookend {Id} to a new version from v{Version}", id, version);
        return Result<Bookend>.Success(bookend);
    }

    public async Task<Result<Bookend>> ArchiveAsync(
        string id, string? actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var bookend = existing.Value;
        bookend.Archive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(bookend, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<Bookend>(save);

        logger.LogInformation("Archived bookend {Id} (by {Actor})", id, actor ?? "unknown");
        return Result<Bookend>.Success(bookend);
    }

    public async Task<Result<Bookend>> UnarchiveAsync(
        string id, string? actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing;

        var bookend = existing.Value;
        bookend.Unarchive(timeProvider.GetUtcNow().UtcDateTime);

        var save = await repository.SaveAsync(bookend, cancellationToken);
        if (save.IsFailure)
            return ResultFailure.Propagate<Bookend>(save);

        logger.LogInformation("Unarchived bookend {Id} (by {Actor})", id, actor ?? "unknown");
        return Result<Bookend>.Success(bookend);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing.IsFailure)
            return existing.FailureMetadata.HasValue
                ? Result.Failure(existing.FailureMetadata.Value)
                : Result.Failure(existing.Error);

        // Wider than the delivery-style guard: a bookend is in use when any schedule references
        // it as the primary id OR via its rotation policy (round-robin set or time-of-day window),
        // so a phrase that only rotates cannot be deleted out from under a station.
        var inUse = await LibraryReferenceGuard.EnsureNotAssignedAsync(
            scheduleRepository, "bookend", id,
            s => s.BookendIds, cancellationToken);
        if (inUse.IsFailure)
            return inUse;

        var delete = await repository.DeleteAsync(id, cancellationToken);
        if (delete.IsFailure)
            return delete;

        logger.LogInformation("Deleted bookend {Id}", id);
        return Result.Success();
    }
}
