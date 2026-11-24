using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     <see cref="IFillerAudioCacheService" /> stand-in for the local replay harness. The REAL
///     implementation (<c>FillerAudioCacheService</c>) needs Table Storage + Blob Storage to
///     cache/resolve filler audio — infrastructure this in-memory harness deliberately has none
///     of. Without a substitute, every <see cref="ResolveAsync" /> call fails against the dummy
///     "test" storage account (fail-fast, <c>Storage:MaxRetries=0</c>), <c>resolvedFiller</c> in
///     <c>BroadcastOrchestrator</c> stays empty, and filler top-up silently never engages —
///     even with <c>ARN_REPLAY_FILLER=1</c> resolving real statements from the repository.
///     <para>
///     This fake reuses the already-registered <see cref="IAudioSynthesizer" />
///     (<see cref="SimulatedElevenLabsSynthesizer" />, fitted from real dev synthesis history) to
///     "synthesize" filler text the same way traffic audio is faked — so filler duration is
///     estimated on the SAME model as the rest of the simulation, not a separate ad-hoc guess.
///     </para>
/// </summary>
internal sealed class SimulatedFillerAudioCacheService(IAudioSynthesizer synthesizer, IDurationEstimator durationEstimator)
    : IFillerAudioCacheService
{
    public async Task<Result<ResolvedFillerAudio>> ResolveAsync(
        FillerStatement statement, string voiceId, VoiceSettings settings, string model,
        CancellationToken cancellationToken = default)
    {
        var synth = await synthesizer.SynthesizeAsync(
            statement.Text,
            Option<VoiceSettings>.Some(settings),
            Option<string>.Some(voiceId),
            string.IsNullOrWhiteSpace(model) ? Option<string>.None : Option<string>.Some(model),
            cancellationToken);

        if (synth.IsFailure)
            return Result<ResolvedFillerAudio>.Failure(synth.Error);

        double duration = durationEstimator.GetDuration(synth.Value.Audio);
        return Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio(synth.Value.Audio, duration, []));
    }

    public Task<Result> InvalidateAsync(string fillerStatementId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public async Task<Result<ResolvedCartClip>> ResolveCartAsync(
        string scriptText, string voiceId, VoiceSettings settings, string model,
        CancellationToken cancellationToken = default)
    {
        var synth = await synthesizer.SynthesizeAsync(
            scriptText,
            Option<VoiceSettings>.Some(settings),
            Option<string>.Some(voiceId),
            string.IsNullOrWhiteSpace(model) ? Option<string>.None : Option<string>.Some(model),
            cancellationToken);

        if (synth.IsFailure)
            return Result<ResolvedCartClip>.Failure(synth.Error);

        double duration = durationEstimator.GetDuration(synth.Value.Audio);
        string cacheKey = FillerCartAudioKey.Build(scriptText, voiceId, settings, model);
        return Result<ResolvedCartClip>.Success(new ResolvedCartClip(
            $"local://filler-cache/{FillerCartAudioKey.CartStatementId}/{cacheKey}.wav",
            synth.Value.Audio, duration, []));
    }
}
