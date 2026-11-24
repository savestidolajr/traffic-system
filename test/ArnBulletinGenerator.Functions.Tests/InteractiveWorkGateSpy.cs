using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Functions.Tests;

internal sealed class InteractiveWorkGateSpy : IInteractiveWorkGate
{
    public List<InteractiveWorkKey> LlmKeys { get; } = [];
    public List<InteractiveWorkKey> TtsKeys { get; } = [];
    public List<InteractiveWorkRequest> Requests { get; } = [];
    public bool FailLlm { get; set; }
    public string FailureMessage { get; set; } = "interactive gate rejected work";

    public Task<Result<T>> ExecuteLlmAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        LlmKeys.Add(key);
        return FailLlm
            ? Task.FromResult(Result<T>.Failure(FailureMessage))
            : work(cancellationToken);
    }

    public Task<Result<T>> ExecuteTtsAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        TtsKeys.Add(key);
        return work(cancellationToken);
    }

    public Task<Result<T>> ExecuteAsync<T>(
        InteractiveWorkRequest request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return work(cancellationToken);
    }
}
