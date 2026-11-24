using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class InteractiveWorkGateTests
{
    [Fact]
    public async Task SamePrincipalAndNormalizedKeySharesOneInFlightCall()
    {
        var gate = new InteractiveWorkGate(Options.Create(new InteractiveWorkOptions()));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var firstKey = new InteractiveWorkKey("operator-a", "preview", "r1", "read  this", "model-a");
        var duplicateKey = new InteractiveWorkKey("OPERATOR-A", "PREVIEW", "r1", "read this", "MODEL-A");

        Task<Result<string>> Work(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult(true);
            return release.Task;
        }

        Task<Result<string>> first = gate.ExecuteLlmAsync(firstKey, Work);
        await started.Task;
        Task<Result<string>> duplicate = gate.ExecuteLlmAsync(duplicateKey, Work);
        release.SetResult(Result<string>.Success("preview"));

        (await first).Value.ShouldBe("preview");
        (await duplicate).Value.ShouldBe("preview");
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task PrincipalRevisionModelAndPoolDifferencesDoNotShareResults()
    {
        var gate = new InteractiveWorkGate(Options.Create(new InteractiveWorkOptions()));
        int calls = 0;

        Task<Result<string>> Work(CancellationToken _)
        {
            int number = Interlocked.Increment(ref calls);
            return Task.FromResult(Result<string>.Success($"result-{number}"));
        }

        var baseKey = new InteractiveWorkKey("operator-a", "direct", "r1", "same", "model-a");
        (await gate.ExecuteLlmAsync(baseKey, Work)).Value.ShouldBe("result-1");
        // Each difference is part of the key. The TTS pool is also independent from LLM.
        (await gate.ExecuteLlmAsync(baseKey with { PrincipalId = "operator-b" }, Work)).Value.ShouldBe("result-2");
        (await gate.ExecuteLlmAsync(baseKey with { Revision = "r2" }, Work)).Value.ShouldBe("result-3");
        (await gate.ExecuteLlmAsync(baseKey with { Model = "model-b" }, Work)).Value.ShouldBe("result-4");
        (await gate.ExecuteTtsAsync(baseKey, Work)).Value.ShouldBe("result-5");
        calls.ShouldBe(5);
    }

    [Fact]
    public async Task CancelledWaiterReleasesItsPermitAndDoesNotCacheFailure()
    {
        var gate = new InteractiveWorkGate(Options.Create(new InteractiveWorkOptions { LlmPoolSize = 1 }));
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        Task<Result<string>> First(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            firstStarted.TrySetResult(true);
            return releaseFirst.Task;
        }

        Task<Result<string>> Immediate(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Result<string>.Success("third"));
        }

        var firstKey = new InteractiveWorkKey("operator-a", "preview", "r1", "first");
        var waitingKey = new InteractiveWorkKey("operator-a", "preview", "r1", "waiting");
        var thirdKey = new InteractiveWorkKey("operator-a", "preview", "r1", "third");
        Task<Result<string>> first = gate.ExecuteLlmAsync(firstKey, First);
        await firstStarted.Task;

        using var waitingCancellation = new CancellationTokenSource();
        Task<Result<string>> waiting = gate.ExecuteLlmAsync(waitingKey, Immediate, waitingCancellation.Token);
        waitingCancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);

        releaseFirst.SetResult(Result<string>.Success("first"));
        (await first).Value.ShouldBe("first");
        (await gate.ExecuteLlmAsync(thirdKey, Immediate)).Value.ShouldBe("third");
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task FailuresAreNotCachedAndCacheBypassStillUsesDeadline()
    {
        var gate = new InteractiveWorkGate(Options.Create(new InteractiveWorkOptions()));
        int failures = 0;
        Task<Result<string>> Failed(CancellationToken _)
        {
            Interlocked.Increment(ref failures);
            return Task.FromResult(Result<string>.Failure("provider unavailable"));
        }

        var key = new InteractiveWorkKey("operator-a", "rewrite", "r1", "same");
        (await gate.ExecuteLlmAsync(key, Failed)).IsFailure.ShouldBeTrue();
        (await gate.ExecuteLlmAsync(key, Failed)).IsFailure.ShouldBeTrue();
        failures.ShouldBe(2);

        var timed = await gate.ExecuteAsync(
            new InteractiveWorkRequest(
                InteractiveWorkPool.Llm,
                key with { Operation = "rewrite" },
                TimeSpan.FromMilliseconds(30),
                BypassCache: true),
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result<string>.Success("must not complete");
            });
        timed.IsFailure.ShouldBeTrue();
        timed.Error.ShouldContain("timed out");
    }
}
