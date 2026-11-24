using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
/// Persists the normalized safety-term identity index. The index is deliberately separate from
/// canonical terms: identity claims are conditional and never require a complete safetyTerms scan.
/// </summary>
public sealed class TableStorageSafetyTermIdentityRepository(
    IOptions<StorageOptions> storageOptions,
    IOptions<SafetyTermIdentityOptions> options,
    TokenCredential credential,
    ILogger<TableStorageSafetyTermIdentityRepository> logger)
    : TableRepositoryBase("safetyTermIndex", storageOptions, credential, logger), ISafetyTermIdentityRepository
{
    private readonly SafetyTermIdentityOptions _options = options.Value;

    public bool ReadEnabled => _options.ReadEnabled && !_options.KillSwitch;
    public bool WriteEnabled => _options.WriteEnabled && !_options.KillSwitch;
    public bool KillSwitch => _options.KillSwitch;

    public async Task<Result<Option<SafetyTermIdentity>>> FindAsync(
        SafetyScope scope, string scopeId, SafetyTermKind kind, string term,
        CancellationToken ct = default)
    {
        if (!ReadEnabled) return Result<Option<SafetyTermIdentity>>.Success(Option<SafetyTermIdentity>.None);
        var result = await GetEntityOrDefaultAsync<SafetyTermIdentityEntity>(
            PartitionFor(scope, scopeId), RowKeyFor(kind, term), ct);
        return result.Map(entity => entity.HasValue
            ? Option<SafetyTermIdentity>.Some(Map(entity.Value))
            : Option<SafetyTermIdentity>.None);
    }

    public async Task<Result<SafetyTermIdentity>> ReserveAsync(
        SafetyScope scope, string scopeId, SafetyTermKind kind, string term,
        string candidateTermId, string createdBy, DateTime createdAtUtc,
        CancellationToken ct = default)
    {
        if (!ReadEnabled && !WriteEnabled)
            return Result<SafetyTermIdentity>.Failure("Safety term identity index is disabled.");

        var existing = await FindAsync(scope, scopeId, kind, term, ct);
        if (existing.IsFailure) return Result<SafetyTermIdentity>.Failure(existing.Error);
        if (existing.Value.HasValue) return Result<SafetyTermIdentity>.Success(existing.Value.Value);

        if (!WriteEnabled)
        {
            return Result<SafetyTermIdentity>.Success(new SafetyTermIdentity(
                candidateTermId, kind, scope, SafetyTermIdentity.NormalizeScopeId(scope, scopeId),
                SafetyTermIdentity.NormalizeTerm(term), createdAtUtc));
        }

        var candidate = new SafetyTermIdentity(
            candidateTermId, kind, scope, SafetyTermIdentity.NormalizeScopeId(scope, scopeId),
            SafetyTermIdentity.NormalizeTerm(term), createdAtUtc);
        var added = await ExecuteAsync(async () => { await TableClient.AddEntityAsync(MapToEntity(candidate), ct); });
        if (added.IsSuccess) return Result<SafetyTermIdentity>.Success(candidate);
        if (!added.Error.Contains("conflict", StringComparison.OrdinalIgnoreCase)
            && !added.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            && !added.Error.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            return Result<SafetyTermIdentity>.Failure(added.Error);

        // A concurrent creator won the identity claim; resolve its pointer below.

        var winner = await FindAsync(scope, scopeId, kind, term, ct);
        if (winner.IsSuccess && winner.Value.HasValue)
            return Result<SafetyTermIdentity>.Success(winner.Value.Value);
        return Result<SafetyTermIdentity>.Failure(
            winner.IsFailure ? winner.Error : "Conflict: safety term identity claim was lost and could not be reloaded.");
    }

    public async Task<Result> UpsertAsync(SafetyTermIdentity identity, CancellationToken ct = default)
    {
        if (!WriteEnabled) return Result.Success();
        return await ExecuteAsync(async () =>
        {
            var current = await GetEntityOrDefaultAsync<SafetyTermIdentityEntity>(
                PartitionFor(identity.Scope, identity.ScopeId), RowKeyFor(identity.Kind, identity.NormalizedTerm), ct);
            if (current.IsFailure) throw new InvalidOperationException(current.Error);

            if (!current.Value.HasValue)
            {
                await TableClient.AddEntityAsync(MapToEntity(identity), ct);
                return;
            }

            if (!string.Equals(current.Value.Value.TermId, identity.TermId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Conflict: normalized safety-term identity points to a different canonical term.");

            var entity = MapToEntity(identity);
            entity.ETag = current.Value.Value.ETag;
            await TableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
        });
    }
    public async Task<Result> DeleteForTermAsync(
        SafetyScope scope, string scopeId, string termId, CancellationToken ct = default)
    {
        if (!WriteEnabled) return Result.Success();
        return await ExecuteAsync(async () =>
        {
            string partition = PartitionFor(scope, scopeId);
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and TermId eq {termId}");
            var entities = await QueryEntitiesAsync<SafetyTermIdentityEntity>(filter, ct);
            foreach (var entity in entities)
            {
                try
                {
                    await TableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, ct);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Idempotent delete: a concurrent cleanup already removed the pointer.
                }
            }
        });
    }

    public async Task<Result<SafetyTermIdentityBackfillPage>> ReconcileAsync(
        IReadOnlyList<SafetyTerm> canonicalTerms, CancellationToken ct = default)
    {
        if (!WriteEnabled)
            return Result<SafetyTermIdentityBackfillPage>.Success(new(null, canonicalTerms.Count, 0, 0));

        int written = 0;
        int duplicates = 0;
        foreach (var term in canonicalTerms)
        {
            ct.ThrowIfCancellationRequested();
            var identity = new SafetyTermIdentity(
                term.Id, term.Kind, term.Scope, term.ScopeId,
                SafetyTermIdentity.NormalizeTerm(term.Term), term.CreatedAtUtc);
            var current = await FindAsync(term.Scope, term.ScopeId, term.Kind, term.Term, ct);
            if (current.IsFailure) return Result<SafetyTermIdentityBackfillPage>.Failure(current.Error);
            if (current.Value.HasValue && current.Value.Value.TermId != term.Id)
            {
                duplicates++;
                continue;
            }

            var result = await UpsertAsync(identity, ct);
            if (result.IsFailure) return Result<SafetyTermIdentityBackfillPage>.Failure(result.Error);
            if (!current.Value.HasValue) written++;
        }

        return Result<SafetyTermIdentityBackfillPage>.Success(
            new(null, canonicalTerms.Count, written, duplicates));
    }

    public async Task<Result<Option<SafetyTermIdentity>>> FindByCanonicalIdAsync(
        SafetyScope scope, string scopeId, string termId, CancellationToken ct = default)
    {
        if (!ReadEnabled) return Result<Option<SafetyTermIdentity>>.Success(Option<SafetyTermIdentity>.None);
        return await ExecuteAsync(async () =>
        {
            string partition = PartitionFor(scope, scopeId);
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and TermId eq {termId}");
            var entities = await QueryEntitiesAsync<SafetyTermIdentityEntity>(filter, ct);
            var entity = entities.FirstOrDefault();
            return entity is null
                ? Option<SafetyTermIdentity>.None
                : Option<SafetyTermIdentity>.Some(Map(entity));
        });
    }

    internal static string PartitionFor(SafetyScope scope, string scopeId)
        => $"SafetyIdentity_{scope}_{SafetyTermIdentity.NormalizeScopeId(scope, scopeId)}";

    internal static string RowKeyFor(SafetyTermKind kind, string term)
        => $"{kind}|{Escape(SafetyTermIdentity.NormalizeTerm(term))}";

    private static string Escape(string value)
        => value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("/", "%2F", StringComparison.Ordinal)
            .Replace("\\", "%5C", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal);

    private static SafetyTermIdentity Map(SafetyTermIdentityEntity entity) => new(
        entity.TermId,
        Enum.TryParse<SafetyTermKind>(entity.Kind, out var kind) ? kind : SafetyTermKind.Banned,
        Enum.TryParse<SafetyScope>(entity.Scope, out var scope) ? scope : SafetyScope.Global,
        entity.ScopeId,
        entity.NormalizedTerm,
        entity.CreatedAtUtc,
        entity.CanonicalETag,
        entity.ETag.ToString());

    private static SafetyTermIdentityEntity MapToEntity(SafetyTermIdentity identity) => new()
    {
        PartitionKey = PartitionFor(identity.Scope, identity.ScopeId),
        RowKey = RowKeyFor(identity.Kind, identity.NormalizedTerm),
        TermId = identity.TermId,
        Kind = identity.Kind.ToString(),
        Scope = identity.Scope.ToString(),
        ScopeId = identity.ScopeId,
        NormalizedTerm = identity.NormalizedTerm,
        CreatedAtUtc = DateTime.SpecifyKind(identity.CreatedAtUtc, DateTimeKind.Utc),
        CanonicalETag = identity.CanonicalETag
    };

    internal sealed class SafetyTermIdentityEntity : ITableEntity
    {
        public string TermId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ScopeId { get; set; } = string.Empty;
        public string NormalizedTerm { get; set; } = string.Empty;
        public string? CanonicalETag { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
