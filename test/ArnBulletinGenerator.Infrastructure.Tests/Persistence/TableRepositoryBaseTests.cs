using System.Reflection;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Guards the first-use table provisioning contract, including the production opt-out and its
///     actionable missing-table failure.
/// </summary>
public sealed class TableRepositoryBaseTests
{
    [Fact]
    public async Task RuntimeEnsure_Default_EnsuresOnlyOnce()
    {
        var (repository, tableClient) = CreateRepository(new StorageOptions { AccountName = "test-account" });

        (await repository.ExecuteProbeAsync()).IsSuccess.ShouldBeTrue();
        (await repository.ExecuteProbeAsync()).IsSuccess.ShouldBeTrue();

        await tableClient.Received(1).CreateIfNotExistsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeEnsure_Disabled_SkipsCreate()
    {
        var (repository, tableClient) = CreateRepository(new StorageOptions
        {
            AccountName = "test-account",
            RuntimeEnsureEnabled = false
        });

        var result = await repository.ExecuteProbeAsync();

        result.IsSuccess.ShouldBeTrue();
        await tableClient.DidNotReceive().CreateIfNotExistsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeEnsure_Disabled_MissingTable_ReturnsActionableFailure()
    {
        var (repository, tableClient) = CreateRepository(new StorageOptions
        {
            AccountName = "test-account",
            RuntimeEnsureEnabled = false
        });
        tableClient.UpsertEntityAsync(
                Arg.Any<ProbeEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "The specified table does not exist.", "TableNotFound", null));

        var result = await repository.ExecuteWriteProbeAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("probe");
        result.Error.ShouldContain("runtime table creation is disabled");
        result.Error.ShouldContain("Storage:RuntimeEnsureEnabled=true");
    }

    [Fact]
    public async Task RuntimeEnsure_DisabledForAzurite_StillEnsures()
    {
        var (repository, tableClient) = CreateRepository(new StorageOptions
        {
            AccountName = string.Empty,
            RuntimeEnsureEnabled = false
        });

        (await repository.ExecuteProbeAsync()).IsSuccess.ShouldBeTrue();

        await tableClient.Received(1).CreateIfNotExistsAsync(Arg.Any<CancellationToken>());
    }

    private static (ProbeRepository Repository, TableClient TableClient) CreateRepository(StorageOptions options)
    {
        var tableClient = Substitute.For<TableClient>();
        var repository = new ProbeRepository(
            Options.Create(options),
            Substitute.For<TokenCredential>(),
            Substitute.For<ILogger>());

        var baseType = typeof(TableRepositoryBase);
        baseType.GetField("<TableClient>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(repository, tableClient);
        baseType.GetField("_tableEnsured", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(repository, false);

        return (repository, tableClient);
    }

    private sealed class ProbeRepository(
        IOptions<StorageOptions> options,
        TokenCredential credential,
        ILogger logger)
        : TableRepositoryBase("probe", options, credential, logger)
    {
        public Task<Result<int>> ExecuteProbeAsync() => ExecuteAsync(() => Task.FromResult(1));

        public Task<Result> ExecuteWriteProbeAsync() => ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(new ProbeEntity(), TableUpdateMode.Replace);
        });
    }

    private sealed class ProbeEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "probe";
        public string RowKey { get; set; } = "probe";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
