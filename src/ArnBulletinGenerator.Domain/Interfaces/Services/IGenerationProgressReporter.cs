using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Best-effort observer for structured, append-only generation progress. Implementations must
///     never throw into the generation pipeline or make a provider failure look like a bulletin
///     failure.
/// </summary>
public interface IGenerationProgressReporter
{
    ValueTask StatusChanged(JobStatus status);
    ValueTask StageStarted(ProgressStage stage, string detail, IReadOnlyDictionary<string, double>? counts = null);
    ValueTask StageCompleted(ProgressStage stage, string detail, IReadOnlyDictionary<string, double>? counts = null,
        int? durationMs = null);
    ValueTask SubStep(ProgressStage stage, string detail, IReadOnlyDictionary<string, double>? counts = null);
    ValueTask AttemptStarted(int attempt, int maxAttempts, string detail);
    ValueTask AttemptFailed(string detail);
    ValueTask LlmCall(ProgressStage stage, string route, string model, int durationMs);
    ValueTask Ready(string detail);
    ValueTask Failed(string detail);
}

/// <summary>Creates one progress reporter per job and exposes existing reporters to status updates.</summary>
public interface IGenerationProgressReporterFactory
{
    IGenerationProgressReporter Create(string jobId, string market, JobStatus status);
    IGenerationProgressReporter? TryGet(string jobId);
}
