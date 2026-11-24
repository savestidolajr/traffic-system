using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageSafetyTermIdentityRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageSafetyTermIdentityRepositoryTests
{
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageSafetyTermIdentityRepository _sut;

    public TableStorageSafetyTermIdentityRepositoryTests()
    {
        _sut = new TableStorageSafetyTermIdentityRepository(
            TableClientTestHelper.StubOptions(),
            Options.Create(new SafetyTermIdentityOptions()),
            TableClientTestHelper.StubCredential(),
            Substitute.For<ILogger<TableStorageSafetyTermIdentityRepository>>());
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Theory]
    [InlineData(SafetyScope.Global, "", SafetyTermKind.Banned, "  ROAD CLOSURE  ", "SafetyIdentity_Global_", "Banned|road closure")]
    [InlineData(SafetyScope.Market, " SYDNEY ", SafetyTermKind.Allowed, "A/B?C", "SafetyIdentity_Market_sydney", "Allowed|a%2Fb%3Fc")]
    public void IdentityKeys_AreScopedAndNormalized(
        SafetyScope scope, string scopeId, SafetyTermKind kind, string term,
        string expectedPartition, string expectedRow)
    {
        PartitionFor(scope, scopeId).ShouldBe(expectedPartition);
        RowKeyFor(kind, term).ShouldBe(expectedRow);
    }

    [Fact]
    public async Task FindAsync_IsSinglePointRead()
    {
        _tableClient.GetEntityAsync<TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(new TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity
            {
                PartitionKey = "SafetyIdentity_Global_",
                RowKey = "Banned|cat",
                TermId = "term-1",
                Kind = "Banned",
                Scope = "Global",
                ScopeId = "",
                NormalizedTerm = "cat",
                CreatedAtUtc = DateTime.UnixEpoch
            }));

        var result = await _sut.FindAsync(SafetyScope.Global, "", SafetyTermKind.Banned, " CAT ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.TermId.ShouldBe("term-1");
        _tableClient.DidNotReceive().QueryAsync<TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity>(
            Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_SkipsDuplicateIdentityAndReportsIt()
    {
        _tableClient.GetEntityAsync<TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(new TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity
            {
                TermId = "existing", Kind = "Banned", Scope = "Global", ScopeId = "",
                NormalizedTerm = "cat", PartitionKey = "SafetyIdentity_Global_", RowKey = "Banned|cat"
            }));

        var result = await _sut.ReconcileAsync([
            new SafetyTerm("duplicate", SafetyTermKind.Banned, SafetyScope.Global, "", "CAT", "test",
                DateTime.UnixEpoch, DateTime.UnixEpoch)]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Duplicates.ShouldBe(1);
        await _tableClient.DidNotReceive().AddEntityAsync(
            Arg.Any<TableStorageSafetyTermIdentityRepository.SafetyTermIdentityEntity>(), Arg.Any<CancellationToken>());
    }
}
