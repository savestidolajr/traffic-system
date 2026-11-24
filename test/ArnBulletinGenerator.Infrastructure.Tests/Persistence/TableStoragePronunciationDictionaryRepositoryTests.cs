using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStoragePronunciationDictionaryRepositoryTests
{
    private readonly ILogger<TableStoragePronunciationDictionaryRepository> _logger =
        Substitute.For<ILogger<TableStoragePronunciationDictionaryRepository>>();
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStoragePronunciationDictionaryRepository _sut;

    public TableStoragePronunciationDictionaryRepositoryTests()
    {
        _sut = new TableStoragePronunciationDictionaryRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    private static PronunciationRule Alias(string word, string replacement) =>
        PronunciationRule.CreateAlias(word, replacement).Value;

    private static PronunciationDictionary Dictionary(string? etag = null)
    {
        var dictionary = PronunciationDictionary.Create(
            "Sydney Roads", null, "eleven-dict-1", "eleven-v1",
            [Alias("Coomera", "Koomera")], "initial", "operator", DateTime.UtcNow,
            "pd-sydney").Value;
        dictionary.ETag = etag;
        return dictionary;
    }

    private static TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity Entity(
        string etag = "etag-v1")
    {
        var version = new PronunciationDictionaryVersion(
            1, "eleven-v1", [Alias("Coomera", "Koomera")], "initial", DateTime.UtcNow, "operator");
        return new TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity
        {
            PartitionKey = "PronunciationDictionary",
            RowKey = "pd-sydney",
            DictionaryId = "eleven-dict-1",
            Name = "Sydney Roads",
            Status = "active",
            CurrentVersion = 1,
            AssignedStationsJson = "[]",
            VersionsJson = JsonSerializer.Serialize(new[] { version }),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ETag = new ETag(etag)
        };
    }

    [Fact]
    public async Task Save_NewDictionary_UsesUnconditionalUpsert()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveAsync(Dictionary());

        result.IsSuccess.ShouldBeTrue(result.Error);
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.DidNotReceive().UpdateEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_WithMatchingEtag_UsesConditionalUpdate()
    {
        _tableClient.UpdateEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveAsync(Dictionary("etag-v1"));

        result.IsSuccess.ShouldBeTrue(result.Error);
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Is<ETag>(e => e.ToString() == "etag-v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.DidNotReceive().UpsertEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_WithStaleEtag_ReturnsMachineCheckableConflict()
    {
        _tableClient.UpdateEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response>(
                new RequestFailedException(412, "Precondition Failed")));

        var result = await _sut.SaveAsync(Dictionary("stale-etag"));

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(
            ArnBulletinGenerator.Domain.Errors.FailureDisposition.Conflict);
        result.FailureMetadata.Value.Code.ShouldBe("pronunciation.revision_mismatch");
        await _tableClient.Received(3).UpdateEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_WithMatchingEtag_RefreshesAggregateEtagFromTheWrite()
    {
        // Bug-bash 2026-08-05 finding 27: after a successful conditional write the aggregate
        // kept the PRE-write revision, so a mutation response advertised a revision that no
        // longer existed — a client chaining a conditional edit off it got a spurious conflict.
        const string writtenEtag = "W/\"datetime'2026-08-05T12%3A46%3A03.9067774Z'\"";
        _tableClient.UpdateEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Response>(new EtagResponse(writtenEtag)));

        var dictionary = Dictionary("etag-v1");
        var result = await _sut.SaveAsync(dictionary);

        result.IsSuccess.ShouldBeTrue(result.Error);
        dictionary.ETag.ShouldBe(writtenEtag);
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Is<ETag>(e => e.ToString() == "etag-v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_NewDictionary_RefreshesAggregateEtagFromTheCreate()
    {
        // Same defect on the create path: a fresh entity's ETag stayed null, so the first
        // conditional edit chained off the create response always conflicted.
        _tableClient.UpsertEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Response>(new EtagResponse("W/\"etag-new\"")));

        var dictionary = Dictionary();
        var result = await _sut.SaveAsync(dictionary);

        result.IsSuccess.ShouldBeTrue(result.Error);
        dictionary.ETag.ShouldBe("W/\"etag-new\"");
    }

    [Fact]
    public async Task Save_WithMatchingEtag_UpdateResponseWithoutEtag_KeepsAggregateEtag()
    {
        // A response without an ETag header (e.g. an unusual provider path) must not blank the
        // aggregate's revision.
        _tableClient.UpdateEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var dictionary = Dictionary("etag-v1");
        var result = await _sut.SaveAsync(dictionary);

        result.IsSuccess.ShouldBeTrue(result.Error);
        dictionary.ETag.ShouldBe("etag-v1");
    }

    [Fact]
    public async Task LoadedThenSaved_WithoutConcurrentModification_RoundTripsEtag()
    {
        var entity = Entity();
        _tableClient.GetEntityAsync<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(
                Arg.Is("PronunciationDictionary"), Arg.Is("pd-sydney"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
        _tableClient.UpdateEntityAsync(
                Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var loaded = await _sut.GetAsync("pd-sydney");
        loaded.IsSuccess.ShouldBeTrue();
        loaded.Value.HasValue.ShouldBeTrue();
        loaded.Value.Value.ETag.ShouldBe("etag-v1");

        var saved = await _sut.SaveAsync(loaded.Value.Value);

        saved.IsSuccess.ShouldBeTrue(saved.Error);
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<TableStoragePronunciationDictionaryRepository.PronunciationDictionaryEntity>(),
            Arg.Is<ETag>(e => e.ToString() == "etag-v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        var reloaded = await _sut.GetAsync("pd-sydney");
        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.HasValue.ShouldBeTrue();
        reloaded.Value.Value.ETag.ShouldBe("etag-v1");
    }

    /// <summary>
    ///     Minimal <see cref="Response" /> carrying an ETag header — mirrors the Azure SDK's own
    ///     MockResponse pattern (protected overrides + NotNullWhen) so a conditional-write
    ///     response exposes a real parsed ETag to the repository under test.
    /// </summary>
    private sealed class EtagResponse(string etag) : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get => Stream.Null; set { } }
        public override string ClientRequestId { get => ""; set { } }
        public override void Dispose() { }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase) ? etag : null;
            return value is not null;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase)
                ? new[] { etag }
                : null;
            return values is not null;
        }

        protected override bool ContainsHeader(string name) =>
            string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase);

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            yield return new HttpHeader("ETag", etag);
        }
    }
}
