using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     Terminal fallback stand-in: records the orchestrator's content-limited fallback decision
///     (the signal under test) and applies the filler-cart transition so the job reaches the same
///     FallbackUsed end state it did in dev — without a same-day-bulletin store.
/// </summary>
internal sealed class RecordingFallbackService(IJobRepository jobs) : IBulletinFallbackService
{
    public ConcurrentQueue<(string JobId, string Reason)> Decisions { get; } = new();

    public async Task<FallbackOutcome> TryApplyFallbackAsync(
        Job job, string reason, CancellationToken cancellationToken = default)
    {
        Decisions.Enqueue((job.JobId, reason));
        var applied = job.ApplyFillerFallback("https://replay.local/filler-cart.wav", DateTime.UtcNow, "replay", reason);
        if (applied.IsSuccess)
            await jobs.SaveJobAsync(job);
        return new FallbackOutcome(FallbackKind.FillerCart);
    }

    public Task<Result> ApplyFillerCartAsync(
        Job job, string reason, string by = "system", CancellationToken cancellationToken = default)
    {
        // Replay stand-in: the operator path never runs in these harnesses, so mirror the
        // same applied-and-persisted transition the chain above produces.
        var applied = job.ApplyFillerFallback("https://replay.local/filler-cart.wav", DateTime.UtcNow, by, reason);
        return applied.IsSuccess
            ? jobs.SaveJobAsync(job, cancellationToken)
            : Task.FromResult(Result.Failure(applied.Error));
    }
}

/// <summary>In-memory voice calibration store; the calibration itself runs through the simulated synthesizer.</summary>
internal sealed class InMemoryVoiceCalibrationRepository : IVoiceCalibrationRepository
{
    private readonly ConcurrentDictionary<string, VoiceCalibration> _store = new();

    public Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(string voiceId, VoiceSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<Option<VoiceCalibration>>.Success(
            _store.TryGetValue(Key(voiceId, settings), out var calibration)
                ? Option<VoiceCalibration>.Some(calibration)
                : Option<VoiceCalibration>.None));
    }

    public Task<Result<IReadOnlyList<VoiceCalibration>>> ListAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<VoiceCalibration>>.Success(_store.Values.ToList()));

    public Task<Result> SaveCalibrationAsync(VoiceCalibration calibration, CancellationToken cancellationToken)
    {
        _store[Key(calibration.VoiceId, calibration.Settings)] = calibration;
        return Task.FromResult(Result.Success());
    }

    private static string Key(string voiceId, VoiceSettings? settings) =>
        settings is null
            ? voiceId
            : $"{voiceId}|{settings.Speed:F2}|{settings.Stability:F2}|{settings.SimilarityBoost:F2}|{settings.Style:F2}|{settings.UseSpeakerBoost}";
}
