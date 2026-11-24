using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageSynthesisHistoryRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageSynthesisHistoryRepositoryTests
{
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly ILogger<TableStorageSynthesisHistoryRepository> _logger =
        Substitute.For<ILogger<TableStorageSynthesisHistoryRepository>>();
    private readonly TableStorageSynthesisHistoryRepository _sut;

    public TableStorageSynthesisHistoryRepositoryTests()
    {
        _sut = new TableStorageSynthesisHistoryRepository(
            TableClientTestHelper.StubOptions(), TableClientTestHelper.StubCredential(), _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public void Count_ProjectsOnlyKeysAndCountsContinuationPages()
    {
        IEnumerable<string>? selected = null;
        _tableClient.QueryAsync<SynthesisHistoryEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Do<IEnumerable<string>?>(value => selected = value), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable<SynthesisHistoryEntity>([Entity(1), Entity(2)]));

        int count = _sut.Count;

        count.ShouldBe(2);
        selected.ShouldNotBeNull();
        selected!.ShouldBe([nameof(SynthesisHistoryEntity.PartitionKey), nameof(SynthesisHistoryEntity.RowKey)]);
    }

    [Fact]
    public void GetHistory_StopsAtCapAndLogsWarning()
    {
        var entities = Enumerable.Range(1, 10_001).Select(Entity).ToArray();
        _tableClient.QueryAsync<SynthesisHistoryEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable<SynthesisHistoryEntity>(entities));

        var history = _sut.GetHistory("voice", "model");

        history.Count.ShouldBe(10_000);
        _logger.ReceivedCalls().Any(call => call.GetArguments()
                .Any(argument => argument?.ToString()?.Contains("safety cap", StringComparison.OrdinalIgnoreCase) == true))
            .ShouldBeTrue();
    }

    private static SynthesisHistoryEntity Entity(int number) => new()
    {
        PartitionKey = "voice_model_1.00",
        RowKey = number.ToString("D10"),
        VoiceId = "voice",
        ModelId = "model",
        SynthesizedAt = DateTimeOffset.UtcNow.AddSeconds(number)
    };
}
