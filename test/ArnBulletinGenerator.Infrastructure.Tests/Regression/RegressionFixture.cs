using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     A committed regression case under <c>test/corpus/regressions/</c>: the complete generator input
///     (<see cref="GeneratorInputSnapshot" />) that produced a bulletin we were unhappy with, plus the
///     original script and why it was flagged. Replayed through the live generator (see
///     <see cref="RegressionEval" />) so a prompt/code change can be measured against the real bad case
///     with the data held constant.
/// </summary>
internal sealed record RegressionFixture(
    string Label,
    string Reason,
    string SourceJobId,
    string OriginalScript,
    DateTimeOffset CapturedAtUtc,
    GeneratorInputSnapshot Snapshot)
{
    /// <summary>
    ///     Every committed fixture, or just the labels named in <c>ARN_EVAL_FIXTURES</c> (comma-separated,
    ///     matched on the file name without its extension).
    ///     <para>
    ///     The filter exists so an A/B can replay a CHOSEN subset twice — once per code arm — instead of
    ///     the whole corpus: every extra fixture in an arm is a real Azure OpenAI generation, and a
    ///     five-market comparison should not pay for ten.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<RegressionFixture> LoadAll()
    {
        string root = CorpusPaths.RegressionCorpusRoot();
        if (!Directory.Exists(root))
            return [];

        var wanted = (Environment.GetEnvironmentVariable("ARN_EVAL_FIXTURES") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(f => wanted.Count == 0 ||
                        wanted.Contains(Path.GetFileNameWithoutExtension(f)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(Load)
            .ToList();
    }

    public static RegressionFixture Load(string path) =>
        JsonSerializer.Deserialize<RegressionFixture>(File.ReadAllText(path), JsonDefaults.Storage)
        ?? throw new InvalidOperationException($"Empty/invalid regression fixture: {path}");

    /// <summary>Pretty-printed JSON for the committed fixture file (Option-aware, indented).</summary>
    public string ToJson() => JsonSerializer.Serialize(this,
        new JsonSerializerOptions(JsonDefaults.Storage) { WriteIndented = true });
}
