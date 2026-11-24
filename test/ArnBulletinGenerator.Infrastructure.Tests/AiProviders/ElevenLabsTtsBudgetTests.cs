using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public sealed class ElevenLabsTtsBudgetTests
{
    [Fact]
    public async Task AcquireAsync_AllowsTwoConcurrentOperations_AndTimesOutTheThird()
    {
        var budget = new ElevenLabsTtsBudget(operationTimeout: TimeSpan.FromMilliseconds(100));
        using var first = await budget.AcquireAsync(CancellationToken.None);
        using var second = await budget.AcquireAsync(CancellationToken.None);

        var third = budget.AcquireAsync(CancellationToken.None).AsTask();

        await Should.ThrowAsync<TimeoutException>(async () => await third);
    }

    [Fact]
    public async Task LeaseTokenIsCancelledAtOperationDeadline_AndPermitIsReturnedOnDispose()
    {
        var budget = new ElevenLabsTtsBudget(operationTimeout: TimeSpan.FromMilliseconds(20));
        var lease = await budget.AcquireAsync(CancellationToken.None);

        lease.Token.IsCancellationRequested.ShouldBeFalse();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Task.Delay(TimeSpan.FromSeconds(1), lease.Token));

        lease.Dispose();
        using var replacement = await budget.AcquireAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CallerCancellationBeforeAcquireDoesNotStrandPermit()
    {
        var budget = new ElevenLabsTtsBudget(operationTimeout: TimeSpan.FromSeconds(1));
        using var first = await budget.AcquireAsync(CancellationToken.None);
        using var second = await budget.AcquireAsync(CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await budget.AcquireAsync(cancellation.Token).AsTask());

        first.Dispose();
        second.Dispose();
        using var replacement = await budget.AcquireAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AcquireAsync_HonoursConfiguredPermitCount()
    {
        var budget = new ElevenLabsTtsBudget(
            Options.Create(new ElevenLabsOptions { MaxConcurrentRequests = 3 }),
            operationTimeout: TimeSpan.FromMilliseconds(100));
        using var first = await budget.AcquireAsync(CancellationToken.None);
        using var second = await budget.AcquireAsync(CancellationToken.None);
        using var third = await budget.AcquireAsync(CancellationToken.None);

        await Should.ThrowAsync<TimeoutException>(async () =>
            await budget.AcquireAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task AcquireAsync_ClampsConfiguredPermitCountToOne()
    {
        var budget = new ElevenLabsTtsBudget(
            Options.Create(new ElevenLabsOptions { MaxConcurrentRequests = 0 }),
            operationTimeout: TimeSpan.FromMilliseconds(100));
        using var first = await budget.AcquireAsync(CancellationToken.None);

        await Should.ThrowAsync<TimeoutException>(async () =>
            await budget.AcquireAsync(CancellationToken.None).AsTask());
    }
}
