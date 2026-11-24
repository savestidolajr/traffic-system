using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageVerifiedPronunciationRepositoryTests
{
    private readonly ILogger<TableStorageVerifiedPronunciationRepository> _logger =
        Substitute.For<ILogger<TableStorageVerifiedPronunciationRepository>>();

    private readonly TableStorageVerifiedPronunciationRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageVerifiedPronunciationRepositoryTests()
    {
        _sut = new TableStorageVerifiedPronunciationRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public async Task SetForMarketAsync_ListSerializesOverCeiling_ReturnsTypedInvalidInput_BeforeAnyStorageCall()
    {
        // VC-4: 200 fully-valid entries (80-char word + 200-char note) serialize to well past the
        // 32,768-char Azure Table property ceiling. The pre-flight must refuse with a typed
        // InvalidInput BEFORE touching storage — the eventual write would otherwise fail with an
        // opaque 500 from Azure.
        var words = Enumerable.Range(1, ConfigValidationLimits.MaxVerifiedPronunciations)
            .Select(i => new VerifiedPronunciation(
                $"Word{i}".PadRight(ConfigValidationLimits.MaxVerifiedPronunciationLength, 'x'),
                new string('n', ConfigValidationLimits.MaxVerifiedPronunciationNoteLength),
                true, "test", DateTime.UtcNow))
            .ToList();

        var result = await _sut.SetForMarketAsync("melbourne", words, "test", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
        result.Error.ShouldContain("too large to save");
        result.Error.ShouldContain(PronunciationDictionary.StoragePropertyCeiling.ToString("N0"));
        await _tableClient.DidNotReceive().GetEntityAsync<TableStorageVerifiedPronunciationRepository.VerifiedPronunciationsEntity>(
            "VerifiedPronunciations", "melbourne", Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetForMarketAsync_ListFitsCeiling_ProceedsToStorage()
    {
        // Control: a normal list is NOT rejected by the pre-flight and reaches storage (creates
        // the row when none exists) — the guard must not over-reject.
        var words = new List<VerifiedPronunciation>
        {
            new("Keilor", "sounds right", true, "test", DateTime.UtcNow)
        };
        _tableClient.GetEntityAsync<TableStorageVerifiedPronunciationRepository.VerifiedPronunciationsEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
        _tableClient.UpsertEntityAsync(
                Arg.Any<TableStorageVerifiedPronunciationRepository.VerifiedPronunciationsEntity>(),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.StubResponse());

        var result = await _sut.SetForMarketAsync("melbourne", words, "test", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<TableStorageVerifiedPronunciationRepository.VerifiedPronunciationsEntity>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }
}
