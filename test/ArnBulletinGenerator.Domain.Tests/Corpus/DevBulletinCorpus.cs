using System.Text.Json;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Tests.Corpus;

/// <summary>
///     Repository-root discovery. Mirrors the helper in the Infrastructure tests' <c>CorpusPaths</c>
///     (and the Architecture tests' structure guard): walk up from the test output directory until a
///     folder containing <c>ArnBulletinGenerator.slnx</c> is found.
/// </summary>
public static class CorpusPaths
{
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArnBulletinGenerator.slnx")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate repository root (ArnBulletinGenerator.slnx not found above the test output directory).");
    }

    /// <summary>Root directory for the captured dev-bulletin history corpus.</summary>
    public static string DevBulletinsRoot() => Path.Combine(RepoRoot(), "test", "corpus", "dev-bulletins");
}

/// <summary>One replayable persisted job, with the writer context rebuilt for gate replay.</summary>
public sealed record DevBulletinJob(
    string JobId,
    string Era,
    string Market,
    string Status,
    double TargetSeconds,
    double? MeasuredSeconds,
    string ScriptText,
    BroadcastWithTraffic Context);

/// <summary>An era from <c>MANIFEST.json</c>, in manifest order.</summary>
public sealed record DevBulletinEra(string Era, string FromUtc, string Meaning);

/// <summary>Load outcome: the replayable jobs plus the skip ledger.</summary>
public sealed record DevBulletinCorpusLoad(
    IReadOnlyList<DevBulletinJob> Jobs,
    IReadOnlyList<DevBulletinEra> Eras,
    int TotalJobs,
    int SkippedNoScript,
    int SkippedNoSnapshot,
    int SkippedDeserializeFailure,
    IReadOnlyList<string> DeserializeFailureDetails);

/// <summary>
///     Loads the captured dev-bulletin history under <c>test/corpus/dev-bulletins/</c>: the
///     manifest for era metadata, then each <c>jobs/&lt;jobId&gt;.json</c> row for the script and
///     the <c>GeneratorInputJson</c> writer-input snapshot. The corpus is a diagnostic and
///     regression-net asset, not a correctness fixture — the scripts are historical LLM output
///     and will not reproduce — but the snapshot is the exact writer input, so every
///     deterministic gate can be re-run over history as of today's code.
/// </summary>
public static class DevBulletinCorpus
{
    public static DevBulletinCorpusLoad Load(string? rootOverride = null)
    {
        string root = rootOverride ?? CorpusPaths.DevBulletinsRoot();
        var manifest = JsonSerializer.Deserialize<ManifestFile>(
            File.ReadAllText(Path.Combine(root, "MANIFEST.json")), CorpusJson.Options)
            ?? throw new InvalidOperationException($"MANIFEST.json under {root} is empty or invalid.");

        var eras = manifest.Eras.Select(e => new DevBulletinEra(e.Era, e.FromUtc, e.Meaning)).ToList();
        var jobs = new List<DevBulletinJob>(manifest.Jobs.Count);
        int noScript = 0;
        int noSnapshot = 0;
        int deserializeFailure = 0;
        var failureDetails = new List<string>();

        foreach (var manifestJob in manifest.Jobs)
        {
            string rowPath = Path.Combine(root, "jobs", $"{manifestJob.JobId}.json");
            if (!File.Exists(rowPath))
            {
                noSnapshot++;
                continue;
            }

            var row = JsonSerializer.Deserialize<JobRowFile>(File.ReadAllText(rowPath), CorpusJson.Options);
            if (row is null || string.IsNullOrWhiteSpace(row.ScriptText))
            {
                noScript++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.GeneratorInputJson))
            {
                noSnapshot++;
                continue;
            }

            try
            {
                // Deserialise the persisted snapshot directly into the domain snapshot type, then
                // use its canonical ToContext rebuild (the same path the fallback-replay harness
                // uses) so every gate sees the exact context the writer saw. Where the snapshot
                // lacks a field a gate needs, the model default applies — the notable one is
                // FillOrder: every historical job predates the snapshot persisting the station's
                // fill order, so BroadcastContextBase.FillOrder stays at its default (weather,
                // fuel, minor traffic, filler) and SectionOrderDetector resolves weather-then-fuel
                // for those jobs (called out next to that gate's number in the scan report).
                var snapshot = JsonSerializer.Deserialize<GeneratorInputSnapshot>(
                    row.GeneratorInputJson, CorpusJson.Options)
                    ?? throw new InvalidOperationException("GeneratorInputJson deserialised to null");
                jobs.Add(new DevBulletinJob(
                    manifestJob.JobId,
                    manifestJob.Era,
                    manifestJob.Market,
                    manifestJob.Status,
                    manifestJob.TargetSec,
                    manifestJob.MeasuredSec,
                    row.ScriptText,
                    snapshot.ToContext(manifestJob.JobId)));
            }
            catch (Exception ex)
            {
                deserializeFailure++;
                if (failureDetails.Count < 5)
                    failureDetails.Add($"{manifestJob.JobId}: {ex.Message}");
            }
        }

        return new DevBulletinCorpusLoad(jobs, eras, manifest.Jobs.Count, noScript, noSnapshot,
            deserializeFailure, failureDetails);
    }

    private sealed class ManifestFile
    {
        public List<ManifestEra> Eras { get; init; } = [];
        public List<ManifestJob> Jobs { get; init; } = [];
    }

    private sealed class ManifestEra
    {
        public string Era { get; init; } = "";
        public string FromUtc { get; init; } = "";
        public string Meaning { get; init; } = "";
    }

    private sealed class ManifestJob
    {
        public string JobId { get; init; } = "";
        public string Era { get; init; } = "";
        public string Market { get; init; } = "";
        public string Status { get; init; } = "";
        public double TargetSec { get; init; }
        public double? MeasuredSec { get; init; }
    }

    /// <summary>The two persisted columns the replay needs; the rest of the row is not read.</summary>
    private sealed class JobRowFile
    {
        public string? ScriptText { get; init; }
        public string? GeneratorInputJson { get; init; }
    }
}

/// <summary>
///     Case-insensitive options matching the persistence contract the corpus was written with
///     (<c>JsonDefaults.Storage</c>): JSON casing is tolerated, unknown/computed JSON properties
///     (e.g. <c>HasAnySource</c>, <c>RequiresWeather</c>, <c>RequiresFuel</c> on
///     <c>Content</c>) are ignored, and <c>Option&lt;T&gt;</c> is bound by the mirror converter
///     below.
/// </summary>
public static class CorpusJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new CorpusOptionJsonConverterFactory() }
    };
}

/// <summary>
///     Mirror of <c>ArnBulletinGenerator.Infrastructure.Serialization.OptionJsonConverterFactory</c> —
///     the exact converter contract <c>JsonDefaults.Storage</c> uses to persist
///     <c>GeneratorInputJson</c> (None = JSON null, Some = the inner value). The Domain test
///     project must not reference Infrastructure, so the mirror lives here to keep snapshot
///     deserialisation byte-compatible with production.
/// </summary>
public sealed class CorpusOptionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionJsonConverter<>).MakeGenericType(innerType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class OptionJsonConverter<T> : JsonConverter<Option<T>>
    {
        public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return Option<T>.None;

            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return value is null ? Option<T>.None : Option<T>.Some(value);
        }

        public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
        {
            if (!value.HasValue)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
