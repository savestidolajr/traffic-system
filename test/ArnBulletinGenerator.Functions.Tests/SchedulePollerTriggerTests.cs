using ArnBulletinGenerator.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Functions.Triggers.Timers;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class SchedulePollerTriggerTests
{
    private readonly ISchedulePollerService _pollerService = Substitute.For<ISchedulePollerService>();
    private readonly SchedulePollerTrigger _sut;

    public SchedulePollerTriggerTests()
    {
        _sut = new SchedulePollerTrigger(_pollerService, NullLogger<SchedulePollerTrigger>.Instance);
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    [Fact]
    public async Task Run_TimerFires_CallsPollerService()
    {
        _pollerService.ProcessDueSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.Run(new TimerInfo(), Ctx());

        await _pollerService.Received(1).ProcessDueSchedulesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_PollerServiceThrows_SwallowsAndLogsRatherThanPoisoningTimer()
    {
        _pollerService.ProcessDueSchedulesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Schedule DB down"));

        // The trigger now catches non-cancellation exceptions so a single tick failure
        // doesn't poison the timer's health metrics. Next tick will retry naturally.
        await _sut.Run(new TimerInfo(), Ctx());

        await _pollerService.Received(1).ProcessDueSchedulesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_PassesCancellationTokenFromFunctionContext()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(token);

        _pollerService.ProcessDueSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _sut.Run(new TimerInfo(), context);

        await _pollerService.Received(1).ProcessDueSchedulesAsync(token);
    }

    [Fact]
    public async Task Run_CancellationRequested_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(cts.Token);

        _pollerService.ProcessDueSchedulesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _sut.Run(new TimerInfo(), context);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }
}
