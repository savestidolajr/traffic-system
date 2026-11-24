using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     One captured dev generation scenario under <c>test/corpus/fallback-replay/</c>: the
///     reconstructed writer input (<see cref="GeneratorInputSnapshot" />), the job's voice
///     configuration, the outcome the DEV pipeline produced (per-attempt targets/word counts/natural
///     durations parsed from the job's LogsJson), and per-fixture reconstruction-fidelity notes.
///     Replayed through the REAL orchestrator generation loop (real LLM agents, simulated
///     ElevenLabs durations) by <see cref="FallbackReplayTests" />. The outcome status is deliberately
///     polarity-bearing: <c>FallbackUsed</c> means DEV did not air the job; other terminal statuses
///     (for example <c>Published</c>) mean it did.
/// </summary>
internal sealed record FallbackReplayFixture(
    string JobId,
    string Market,
    string? Station,
    DateTimeOffset? CapturedAtUtc,
    FallbackDevOutcome DevOutcome,
    FallbackFixtureFidelity Fidelity,
    FallbackVoiceConfig VoiceConfig,
    GeneratorInputSnapshot Snapshot,
    ReplayCoverage? Coverage = null,
    IReadOnlyList<TrafficCorridor>? FullCorridorPool = null)
{
    public static string CorpusRoot() => Path.Combine(CorpusPaths.RepoRoot(), "test", "corpus", "fallback-replay");

    public static string SynthesisHistorySeedPath() => Path.Combine(CorpusRoot(), "synthesis-history.json");

    public static IReadOnlyList<FallbackReplayFixture> LoadAll()
    {
        string root = CorpusRoot();
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith("synthesis-history.json", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(Load)
            .ToList();
    }

    public static FallbackReplayFixture Load(string path) =>
        JsonSerializer.Deserialize<FallbackReplayFixture>(File.ReadAllText(path), JsonDefaults.Storage)
        ?? throw new InvalidOperationException($"Empty/invalid fallback replay fixture: {path}");

    /// <summary>Rewrite the committed fixture (used by the one-time flow-pool capture).</summary>
    public void Save() => File.WriteAllText(
        Path.Combine(CorpusRoot(), $"{JobId}.json"),
        JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonDefaults.Storage) { WriteIndented = true }));
}

/// <summary>What the dev pipeline did with this exact input (parsed from the job's LogsJson).</summary>
internal sealed record FallbackDevOutcome(
    string Status,
    string? FallbackKind,
    int Attempts,
    IReadOnlyList<int> TargetWordsSeq,
    IReadOnlyList<int?> SynthWordsSeq,
    IReadOnlyList<double?> NaturalSecSeq,
    double? BestNaturalSec,
    int ReviewFeedbackCount,
    IReadOnlyList<string> SynthFailures);

/// <summary>
///     Which parts of the writer input are exact vs reconstructed/lost (fallback overwrite +
///     unpersisted columns). The station-config provenance prevents a replay report from
///     presenting live reconstruction as if the historical job had persisted those values.
/// </summary>
internal sealed record FallbackFixtureFidelity(
    int IncidentsRecovered,
    int IncidentsSynthesized,
    string WeatherSource,
    string FuelSource,
    bool GoodRunsAndFlowPoolLost,
    string StationConfigSource = "none");

/// <summary>Coverage circle from the job record — used to (re)capture the live flow pool.</summary>
internal sealed record ReplayCoverage(double Latitude, double Longitude, double? RadiusKm);

/// <summary>The job's persisted voice configuration (radioJobs.VoiceConfigJson).</summary>
internal sealed record FallbackVoiceConfig(
    string? Provider,
    string VoiceId,
    string? VoiceName,
    string? Model,
    VoiceSettings? Settings,
    double PaceBoostWpm);
