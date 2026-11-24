using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Local-development broadcast output sink that logs save/publish calls instead of writing
///     audio anywhere, for running the pipeline without blob storage configured.
/// </summary>
public sealed class ConsoleBroadcastOutput(ILogger<ConsoleBroadcastOutput> logger) : IBroadcastOutput
{
    public Task<Result> SaveAsync(byte[] audioData, string identifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Simulating audio save for {Identifier}. Data length: {Length} bytes.", identifier,
            audioData.Length);

        // In a real console mock, we might just print that it was "saved"
        // or write to a local temp file. For now, we follow the plan's intent of a sink.
        return Task.FromResult(Result.Success());
    }

    public Task<Result> MarkPublishedAsync(string audioFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Simulating published tag for {AudioFileName}.", audioFileName);
        return Task.FromResult(Result.Success());
    }
}
