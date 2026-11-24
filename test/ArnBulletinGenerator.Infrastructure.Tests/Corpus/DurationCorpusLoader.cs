using System.Text.Json;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

internal sealed record DurationCorpusManifest
{
    public int SchemaVersion { get; init; }
    public DurationCorpusCounts Counts { get; init; } = new();
    public List<DurationVoiceSummary> Voices { get; init; } = [];
    public DurationNoiseFloor IrreducibleNoise { get; init; } = new();
}

internal sealed record DurationCorpusCounts
{
    public int Total { get; init; }
    public int Clean { get; init; }
    public int Fingerprints { get; init; }
    public int Tokens { get; init; }
}

internal sealed record DurationNoiseFloor
{
    public string Headline { get; init; } = string.Empty;
    public DurationSameInputTtsVariance SameInputTtsVariance { get; init; } = new();
    public DurationHistoricalCrossJobVariance HistoricalCrossJobVariance { get; init; } = new();
    public DurationSeedVerdict SeedVerdict { get; init; } = new();
}

internal sealed record DurationSameInputTtsVariance
{
    public string MeasuredOn { get; init; } = string.Empty;
    public DurationVarianceStats Unseeded { get; init; } = new();
    public List<DurationSeededVarianceStats> Seeded { get; init; } = [];
    public double OneSigmaAt22s { get; init; }
    public double BestAchievableMaeAt22s { get; init; }
    public string Note { get; init; } = string.Empty;
}

internal record DurationVarianceStats
{
    public int N { get; init; }
    public double MeanSec { get; init; }
    public double SdSec { get; init; }
    public double Cv { get; init; }
    public double RangeSec { get; init; }
}

internal sealed record DurationSeededVarianceStats : DurationVarianceStats
{
    public int Seed { get; init; }
}

internal sealed record DurationHistoricalCrossJobVariance
{
    public string MeasuredOn { get; init; } = string.Empty;
    public double PooledCoefficientOfVariation { get; init; }
    public double OneSigmaAt22s { get; init; }
    public double WorstObservedRangePct { get; init; }
    public string Interpretation { get; init; } = string.Empty;
}

internal sealed record DurationSeedVerdict
{
    public bool ReliablyDeterministic { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public double BetweenSeedShiftSec { get; init; }
    public double BetweenSeedShiftPct { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

internal record DurationVoiceSummary
{
    public string File { get; set; } = string.Empty;
    public string VoiceId { get; init; } = string.Empty;
    public string VoiceName { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public double Stability { get; init; }
    public double SimilarityBoost { get; init; }
    public double Style { get; init; }
    public double Speed { get; init; }
    public double PaceBoostWpm { get; init; }
    public int Total { get; init; }
    public int Clean { get; init; }
    public int Tokens { get; init; }
}

internal sealed record DurationVoiceFile : DurationVoiceSummary
{
    public List<DurationBulletin> Bulletins { get; init; } = [];
}

internal sealed record DurationBulletin
{
    public string JobId { get; init; } = string.Empty;
    public string Market { get; init; } = string.Empty;
    public double TargetSec { get; init; }
    public double NaturalSec { get; init; }
    public double FittedSec { get; init; }
    public double Stretch { get; init; }
    public bool Clean { get; init; }
    public List<string> ExcludedBecause { get; init; } = [];
    public string SpokenText { get; init; } = string.Empty;
    public DurationFeaturesJson Features { get; init; } = new();
    public List<DurationToken> Tokens { get; init; } = [];
}

internal sealed record DurationFeaturesJson
{
    public int Syl { get; init; }
    public int Words { get; init; }
    public int Ellipsis { get; init; }
    public int Period { get; init; }
    public int Comma { get; init; }
    public int Dash { get; init; }
    public int CostlyTags { get; init; }
    public int Tags { get; init; }
}

internal sealed record DurationToken
{
    public string W { get; init; } = string.Empty;
    public double S { get; init; }
    public double E { get; init; }
}

internal sealed record DurationCorpus
{
    public required DurationCorpusManifest Manifest { get; init; }
    public required IReadOnlyList<DurationVoiceFile> Voices { get; init; }
}

internal static class DurationCorpusLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool TryLoad(out DurationCorpus? corpus, out string reason)
    {
        string root;
        try
        {
            root = CorpusPaths.DurationCorpusRoot();
        }
        catch (Exception ex)
        {
            corpus = null;
            reason = $"Could not locate repository root: {ex.Message}";
            return false;
        }

        string manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            corpus = null;
            reason = $"Duration corpus is absent: {manifestPath}";
            return false;
        }

        var manifest = JsonSerializer.Deserialize<DurationCorpusManifest>(
            File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new JsonException("manifest.json deserialised to null");
        var voices = new List<DurationVoiceFile>(manifest.Voices.Count);
        foreach (DurationVoiceSummary summary in manifest.Voices)
        {
            string path = Path.Combine(root, summary.File);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Manifest voice file is missing: {summary.File}", path);
            var voice = JsonSerializer.Deserialize<DurationVoiceFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException($"Voice file deserialised to null: {summary.File}");
            voice.File = summary.File;
            voices.Add(voice);
        }

        corpus = new DurationCorpus { Manifest = manifest, Voices = voices };
        reason = string.Empty;
        return true;
    }
}
