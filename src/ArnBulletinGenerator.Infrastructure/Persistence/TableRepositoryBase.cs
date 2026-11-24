using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Base class for Azure Table Storage repositories, providing shared
///     table client creation, query helpers, and Result-pattern error handling.
/// </summary>
public abstract class TableRepositoryBase(
    string tableName,
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger logger)
{
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private volatile bool _tableEnsured;
    private bool RuntimeEnsureActive =>
        options.Value.RuntimeEnsureEnabled || options.Value.IsLocalDevelopment;
    protected TableClient TableClient { get; } = TableClientFactory.Create(tableName, options, credential);

    /// <summary>
    ///     Lazily ensures the underlying Azure table exists, replacing the
    ///     previous synchronous <c>CreateIfNotExists</c> that blocked during DI construction.
    ///     Hosted accounts may disable first-use provisioning; Azurite always ensures its tables.
    /// </summary>
    protected async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_tableEnsured) return;

        await _ensureTableLock.WaitAsync(cancellationToken);
        try
        {
            if (!_tableEnsured && RuntimeEnsureActive)
            {
                await TableClient.CreateIfNotExistsAsync(cancellationToken);
                _tableEnsured = true;
            }
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    // ─── Result-wrapping helpers ─────────────────────────────────────

    /// <summary>
    ///     Executes an async operation that returns a value, wrapping it in <see cref="Result{T}" />.
    ///     Any <see cref="RequestFailedException" /> is caught and returned as a failure.
    /// </summary>
    protected async Task<Result<T>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            await EnsureTableExistsAsync();
            var value = await operation();
            return Result<T>.Success(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            if (IsMissingTable(ex) && !RuntimeEnsureActive)
                return Result<T>.Failure(MissingTableMessage());

            logger.LogError(ex,
                "Azure Table Storage operation failed. Table: {Table}, Status: {Status}, ErrorCode: {ErrorCode}",
                tableName, ex.Status, ex.ErrorCode);
            return Result<T>.Failure(SanitizeStorageMessage(ex.Message));
        }
        catch (Exception ex)
        {
            // Transport-level failures (DNS/socket/connection) surface as AggregateException after the
            // SDK exhausts retries — outside RequestFailedException. Degrade to a failure Result so the
            // Result-pattern contract holds and callers (e.g. the content-safety filter) never see a throw.
            logger.LogError(ex,
                "Azure Table Storage operation failed (transport/unexpected). Table: {Table}", tableName);
            return Result<T>.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Executes an async void operation, wrapping it in <see cref="Result" />.
    /// </summary>
    protected async Task<Result> ExecuteAsync(Func<Task> operation)
    {
        try
        {
            await EnsureTableExistsAsync();
            await operation();
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            if (IsMissingTable(ex) && !RuntimeEnsureActive)
                return Result.Failure(MissingTableMessage());

            logger.LogError(ex,
                "Azure Table Storage operation failed. Table: {Table}, Status: {Status}, ErrorCode: {ErrorCode}",
                tableName, ex.Status, ex.ErrorCode);
            return Result.Failure(SanitizeStorageMessage(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Azure Table Storage operation failed (transport/unexpected). Table: {Table}", tableName);
            return Result.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Strips the diagnostic block Azure appends to every storage error — <c>RequestId:</c> and
    ///     <c>Time:</c> lines — which otherwise reaches the operator verbatim in an API response.
    ///     The human-readable first line is preserved deliberately: <see cref="Http" />-layer mapping
    ///     still matches on "not found" and "conflict" in the message text, so replacing it wholesale
    ///     would silently turn 404s and 409s into 500s.
    /// </summary>
    private static bool IsMissingTable(RequestFailedException ex) =>
        string.Equals(ex.ErrorCode, "TableNotFound", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("table does not exist", StringComparison.OrdinalIgnoreCase);

    private string MissingTableMessage() =>
        $"Table '{tableName}' is missing; runtime table creation is disabled. " +
        "Set Storage:RuntimeEnsureEnabled=true to provision it.";

    internal static string SanitizeStorageMessage(string message)
    {
        int cut = message.IndexOf("\nRequestId:", StringComparison.Ordinal);
        if (cut < 0) cut = message.IndexOf("RequestId:", StringComparison.Ordinal);
        return (cut < 0 ? message : message[..cut]).TrimEnd();
    }
    // ─── Query helpers ───────────────────────────────────────────────

    /// <summary>
    ///     Queries the table with an OData filter and materializes all matching entities.
    /// </summary>
    protected async Task<List<T>> QueryEntitiesAsync<T>(string? filter, CancellationToken ct = default)
        where T : class, ITableEntity, new()
    {
        await EnsureTableExistsAsync();

        var results = new List<T>();
        var query = TableClient.QueryAsync<T>(filter, cancellationToken: ct);

        await foreach (var entity in query) results.Add(entity);

        return results;
    }

    /// <summary>
    ///     Queries with a property projection so read models do not transfer large JSON columns
    ///     they will not inspect.
    /// </summary>
    protected async Task<List<T>> QueryEntitiesAsync<T>(
        string? filter,
        CancellationToken ct,
        IEnumerable<string>? select)
        where T : class, ITableEntity, new()
    {
        await EnsureTableExistsAsync(ct);

        var results = new List<T>();
        var query = TableClient.QueryAsync<T>(filter, select: select, cancellationToken: ct);
        await foreach (var entity in query)
            results.Add(entity);
        return results;
    }

    /// <summary>
    ///     Gets a single entity by partition/row key, returning <see cref="Option{T}.None" /> when not found.
    ///     Non-404 <see cref="RequestFailedException" /> is caught and returned as a failure.
    /// </summary>
    protected Task<Result<Option<T>>> GetEntityOrDefaultAsync<T>(
        string partitionKey,
        string rowKey,
        CancellationToken ct = default) where T : class, ITableEntity, new()
        => GetEntityOrDefaultAsync<T>(partitionKey, rowKey, select: null, ct);

    /// <summary>
    ///     Gets one entity by key with an optional property projection. Point lookups and summary
    ///     reads can avoid transferring the aggregate's large JSON columns.
    /// </summary>
    protected async Task<Result<Option<T>>> GetEntityOrDefaultAsync<T>(
        string partitionKey,
        string rowKey,
        IEnumerable<string>? select,
        CancellationToken ct = default) where T : class, ITableEntity, new()
    {
        try
        {
            await EnsureTableExistsAsync();
            var response = await TableClient.GetEntityAsync<T>(
                partitionKey, rowKey, select, ct);
            return Result<Option<T>>.Success(Option<T>.Some(response.Value));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return IsMissingTable(ex) && !RuntimeEnsureActive
                ? Result<Option<T>>.Failure(MissingTableMessage())
                : Result<Option<T>>.Success(Option<T>.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,
                "Azure Table Storage lookup failed. Table: {Table}, PartitionKey: {PartitionKey}, RowKey: {RowKey}, Status: {Status}, ErrorCode: {ErrorCode}",
                tableName, partitionKey, rowKey, ex.Status, ex.ErrorCode);
            return Result<Option<T>>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Azure Table Storage lookup failed (transport/unexpected). Table: {Table}, PartitionKey: {PartitionKey}, RowKey: {RowKey}",
                tableName, partitionKey, rowKey);
            return Result<Option<T>>.Failure(ex.Message);
        }
    }
}
