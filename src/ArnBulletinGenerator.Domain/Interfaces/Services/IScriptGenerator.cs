using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Turns fetched traffic/weather/fuel context into a spoken bulletin script, sized to the voice's speaking rate and slot duration.</summary>
public interface IScriptGenerator
{
    Task<Result<DraftedScript>> GenerateScriptAsync(BroadcastWithTraffic context,
        CancellationToken cancellationToken = default);

    Task<Result<DraftedScript>> GenerateScriptAsync(BroadcastWithTraffic context, double syllablesPerSecond,
        VoiceSettings settings, CancellationToken cancellationToken = default);

    Task<Result<DraftedScript>> GenerateScriptAsync(BroadcastWithTraffic context, double syllablesPerSecond,
        VoiceSettings settings, string? promptTemplate, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revises an already composed read against measured audio in the original composer
    ///     conversation. Implementations that cannot preserve a composer thread may return a
    ///     failure; the caller then retains the ordinary fresh-generation fallback.
    /// </summary>
    Task<Result<DraftedScript>> ReviseForMeasuredDurationAsync(
        BroadcastWithTraffic context,
        string composerThreadId,
        string currentRead,
        double measuredSeconds,
        double syllablesPerSecond,
        VoiceSettings settings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<DraftedScript>.Failure(
            "This script generator cannot continue a composer conversation."));

}
