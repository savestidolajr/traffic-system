using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = "Operator" (single partition for all operator assignments).
// RowKey = AAD Object ID (oid), used as-is — stable across token renewals.
//
// PartitionKey = "SwaAlias". A linked SWA backend sees only the per-app userId in
// X-MS-CLIENT-PRINCIPAL, never the caller's Entra oid, while assignment rows are keyed by oid.
// Sign-in (rolesSource) is the one moment both ids are present, so the mapping is captured there
// and consulted whenever market scope has to be resolved from the SWA userId alone.

public sealed class TableStorageOperatorAssignmentRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageOperatorAssignmentRepository> logger)
    : TableRepositoryBase("operatorAssignments", options, credential, logger), IOperatorAssignmentRepository
{
    private const string PK = "Operator";
    private const string AliasPK = "SwaAlias";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<Option<OperatorAssignment>>> GetAsync(
        string objectId, CancellationToken cancellationToken = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<OperatorAssignmentEntity>(PK, objectId, cancellationToken);

        if (entityResult.IsFailure)
            return Result<Option<OperatorAssignment>>.Failure(entityResult.Error);

        return entityResult.Value.Match(
            e => Result<Option<OperatorAssignment>>.Success(MapToDomain(e)),
            () => Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
    }

    public async Task<Result<IReadOnlyList<OperatorAssignment>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<OperatorAssignmentEntity>(
                $"PartitionKey eq '{PK}'", cancellationToken);
            return entities.Select(MapToDomain).ToList() as IReadOnlyList<OperatorAssignment>;
        });
    }

    public async Task<Result> UpsertAsync(
        OperatorAssignment assignment, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;

        try
        {
            await TableClient.CreateIfNotExistsAsync(cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to ensure operator assignment table exists");
            return Result.Failure(ex.Message);
        }

        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var response =
                    await TableClient.GetEntityAsync<OperatorAssignmentEntity>(
                        PK, assignment.ObjectId, cancellationToken: cancellationToken);
                var entity = response.Value;

                // A blank display name means "not supplied", never "erase it". The station-scoping
                // dialog sends markets only, so writing it through renamed the person to their raw
                // UPN in the console until they next signed in.
                if (!string.IsNullOrWhiteSpace(assignment.DisplayName))
                    entity.DisplayName = assignment.DisplayName;
                entity.MarketsJson = JsonSerializer.Serialize(assignment.Markets, JsonOptions);
                entity.UpdatedAtUtc = assignment.UpdatedAtUtc;
                entity.UpdatedBy = assignment.UpdatedBy;

                // Sign-in fields are supplied only by the sign-in path. An admin editing markets
                // (or a config import) sends null for all three, which means "unchanged" — writing
                // them through would erase the identity we captured at the person's last sign-in.
                if (assignment.UserPrincipalName is not null)
                    entity.UserPrincipalName = assignment.UserPrincipalName;
                if (assignment.RolesAtSignIn is not null)
                    entity.SignInRolesJson = JsonSerializer.Serialize(assignment.RolesAtSignIn, JsonOptions);
                if (assignment.LastSignInUtc is not null)
                    entity.LastSignInUtc = assignment.LastSignInUtc;

                await TableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity doesn't exist yet — create it. A storage failure here must still
                // fail soft (the outer try's catch blocks do not cover this branch).
                try
                {
                    await TableClient.UpsertEntityAsync(MapToEntity(assignment), TableUpdateMode.Replace, cancellationToken);
                    return Result.Success();
                }
                catch (RequestFailedException createEx)
                {
                    logger.LogError(createEx, "Create failed in UpsertAsync for operator {ObjectId}", assignment.ObjectId);
                    return Result.Failure(createEx.Message);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                logger.LogWarning("ETag conflict on UpsertAsync for operator {ObjectId}, attempt {Attempt}. Retrying.",
                    assignment.ObjectId, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogWarning(
                    "ETag conflict (retries exhausted) on UpsertAsync for operator {ObjectId}.", assignment.ObjectId);
                return Result.Failure(
                    "Conflict: the operator assignment was modified by another request. Reload and try again.");
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Table Storage operation failed in UpsertAsync for operator {ObjectId}",
                    assignment.ObjectId);
                return Result.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in UpsertAsync for operator {ObjectId}", assignment.ObjectId);
                return Result.Failure(ex.Message);
            }

        return Result.Failure(
            $"Failed to upsert operator '{assignment.ObjectId}' after {maxRetries} attempts due to concurrent modifications");
    }

    public async Task<Result> DeleteAsync(
        string objectId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            try
            {
                await TableClient.DeleteEntityAsync(PK, objectId, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Idempotent — entity already deleted.
            }
        });
    }

    /// <inheritdoc />
    public async Task<Result<Option<string>>> GetObjectIdByAliasAsync(
        string swaUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(swaUserId))
            return Result<Option<string>>.Success(Option<string>.None);

        var entityResult = await GetEntityOrDefaultAsync<SwaAliasEntity>(AliasPK, swaUserId, cancellationToken);

        if (entityResult.IsFailure)
            return Result<Option<string>>.Failure(entityResult.Error);

        return entityResult.Value.Match(
            e => string.IsNullOrWhiteSpace(e.ObjectId)
                ? Result<Option<string>>.Success(Option<string>.None)
                : Result<Option<string>>.Success(Option<string>.Some(e.ObjectId)),
            () => Result<Option<string>>.Success(Option<string>.None));
    }

    /// <inheritdoc />
    public async Task<Result> UpsertDirectoryAliasAsync(
        string swaUserId, string objectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(swaUserId) || string.IsNullOrWhiteSpace(objectId))
        {
            logger.LogDebug("Directory alias not recorded: swaUserId or objectId is blank");
            return Result.Success();
        }

        var entityResult = await GetEntityOrDefaultAsync<SwaAliasEntity>(AliasPK, swaUserId, cancellationToken);
        if (entityResult.IsFailure)
            return Result.Failure(entityResult.Error);

        if (entityResult.Value.IsSome)
        {
            var existing = entityResult.Value.Value;

            if (string.Equals(existing.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                return Result.Success();

            // First writer wins: the incoming pair originates from a sign-in payload, and refusing
            // to repoint an established alias stops a later caller from hijacking an existing
            // person's mapping.
            logger.LogWarning(
                "Not repointing SWA user {SwaUserId}: existing alias points at object id {ExistingObjectId}, sign-in presented {IncomingObjectId}",
                swaUserId, existing.ObjectId, objectId);
            return Result.Success();
        }

        return await ExecuteAsync(async () =>
        {
            try
            {
                await TableClient.AddEntityAsync(
                    new SwaAliasEntity { PartitionKey = AliasPK, RowKey = swaUserId, ObjectId = objectId },
                    cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // A concurrent identical sign-in inserted the alias first — not a failure.
            }
        });
    }

    // ─── Mapping ─────────────────────────────────────────────────────

    private OperatorAssignment MapToDomain(OperatorAssignmentEntity e)
    {
        var markets = SafeJson.Deserialize<string[]>(e.MarketsJson, logger, $"operator {e.RowKey} markets", JsonOptions)
                      ?? [];
        var signInRoles = e.SignInRolesJson is null
            ? null
            : SafeJson.Deserialize<string[]>(
                e.SignInRolesJson, logger, $"operator {e.RowKey} sign-in roles", JsonOptions);

        return new OperatorAssignment(
            e.RowKey ?? string.Empty,
            e.DisplayName ?? string.Empty,
            markets,
            e.UpdatedAtUtc.UtcDateTime,
            e.UpdatedBy ?? string.Empty,
            e.UserPrincipalName,
            signInRoles,
            e.LastSignInUtc?.UtcDateTime);
    }

    private static OperatorAssignmentEntity MapToEntity(OperatorAssignment a)
    {
        return new OperatorAssignmentEntity
        {
            PartitionKey = PK,
            RowKey = a.ObjectId,
            DisplayName = a.DisplayName,
            MarketsJson = JsonSerializer.Serialize(a.Markets, JsonOptions),
            UpdatedAtUtc = a.UpdatedAtUtc,
            UpdatedBy = a.UpdatedBy,
            UserPrincipalName = a.UserPrincipalName,
            SignInRolesJson = a.RolesAtSignIn is null
                ? null
                : JsonSerializer.Serialize(a.RolesAtSignIn, JsonOptions),
            LastSignInUtc = a.LastSignInUtc
        };
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class OperatorAssignmentEntity : ITableEntity
    {
        public string DisplayName { get; set; } = string.Empty;
        public string MarketsJson { get; set; } = "[]";
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string? UserPrincipalName { get; set; }
        public string? SignInRolesJson { get; set; }
        public DateTimeOffset? LastSignInUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    internal sealed class SwaAliasEntity : ITableEntity
    {
        public string ObjectId { get; set; } = string.Empty;
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
