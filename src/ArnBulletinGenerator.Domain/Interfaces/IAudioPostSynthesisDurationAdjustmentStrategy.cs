namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Input to a post-synthesis duration adjustment attempt: the script, the rendered audio, and how far its actual duration is from the target.</summary>
public sealed record AudioPostSynthesisAdjustmentContext(
    string Script,
    byte[] Audio,
    double TargetDurationSeconds,
    double ActualDurationSeconds,
    int Attempt,
    int MaxAttempts);

public sealed record AudioPostSynthesisAdjustmentProposal(
    byte[] Audio,
    string Reason);

public interface IAudioPostSynthesisDurationAdjustmentStrategy
{
    string Name { get; }

    Task<AudioPostSynthesisAdjustmentProposal?> TryAdjustAsync(
        AudioPostSynthesisAdjustmentContext context,
        CancellationToken cancellationToken = default);
}
