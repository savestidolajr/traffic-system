using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageSafetyTermRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Focuses on the <see cref="TableRepositoryBase" /> resilience contract via the safety-term repo:
///     a transport-level Azure outage (DNS/socket) surfaces — after the SDK exhausts retries — as an
///     <see cref="AggregateException" />, OUTSIDE <see cref="RequestFailedException" />. The base must
///     degrade that to a failure <see cref="Result" /> (never an unhandled throw) so the content-safety
///     filter's documented "never fail open" degrade-to-baseline path runs. Cancellation must still
///     propagate.
/// </summary>
public class TableStorageSafetyTermRepositoryTests
{
    private readonly ILogger<TableStorageSafetyTermRepository> _logger =
        Substitute.For<ILogger<TableStorageSafetyTermRepository>>();

    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageSafetyTermRepository _sut;

    public TableStorageSafetyTermRepositoryTests()
    {
        _sut = new TableStorageSafetyTermRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    private static AggregateException RetryExhaustedTransportFailure() =>
        new("Retry failed after 4 tries.", new RequestFailedException(0, "Resource temporarily unavailable"));

    [Fact]
    public async Task GetForMarketAsync_TransportFailure_DegradesToFailureNotThrow()
    {
        _tableClient.QueryAsync<SafetyTermEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(RetryExhaustedTransportFailure());

        var result = await _sut.GetForMarketAsync("sydney");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetForMarketAsync_EmptyStore_ReturnsSuccessEmpty()
    {
        _tableClient.QueryAsync<SafetyTermEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<SafetyTermEntity>());

        var result = await _sut.GetForMarketAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_TransportFailure_DegradesToFailureNotThrow()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<SafetyTermEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(RetryExhaustedTransportFailure());

        var term = new SafetyTerm(
            "id-1", SafetyTermKind.Banned, SafetyScope.Global, "global",
            "catastrophe", "tester", DateTime.UtcNow, DateTime.UtcNow, true, Option<string>.None);

        var result = await _sut.UpsertAsync(term);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetForMarketAsync_Cancellation_Propagates()
    {
        _tableClient.QueryAsync<SafetyTermEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => _sut.GetForMarketAsync("sydney"));
    }
}
