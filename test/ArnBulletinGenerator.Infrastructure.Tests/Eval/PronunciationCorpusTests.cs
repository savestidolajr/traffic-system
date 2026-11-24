using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Transcribes the captured ElevenLabs audio corpus with local Whisper and scores pronunciation
///     against each sample's script. Proves the scorer end-to-end on real synthesized audio.
///     <para>
///         Excluded from CI (<c>Category=Integration</c>); needs the local Whisper venv and is slow.
///         Run with: <c>ARN_PRON_TRANSCRIBE=1 dotnet test --filter "FullyQualifiedName~PronunciationCorpus"</c>.
///     </para>
/// </summary>
public sealed class PronunciationCorpusTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Pronunciation_TranscribeCorpus_AndScore()
    {
        var transcriber = WhisperTranscriber.CreateIfAvailable();
        if (Environment.GetEnvironmentVariable("ARN_PRON_TRANSCRIBE") != "1" || transcriber is null)
        {
            output.WriteLine("Skipped: set ARN_PRON_TRANSCRIBE=1 and ensure .venv/bin/whisper exists.");
            return;
        }

        string corpusRoot = CorpusPaths.ElevenLabsCorpusRoot();
        var metaFiles = Directory.Exists(corpusRoot)
            ? Directory.EnumerateFiles(corpusRoot, "*.json", SearchOption.AllDirectories).OrderBy(f => f).ToList()
            : [];
        metaFiles.ShouldNotBeEmpty("ElevenLabs corpus is empty — run the capture first.");

        string transcriptDir = Path.Combine(Path.GetTempPath(), "arn-pron", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transcriptDir);

        int processed = 0;
        foreach (string metaFile in metaFiles)
        {
            string metaJson = await File.ReadAllTextAsync(metaFile, CancellationToken.None);
            var meta = JsonSerializer.Deserialize<SampleMeta>(metaJson, CaseInsensitive);
            if (meta is null)
                continue;

            string wav = Path.Combine(Path.GetDirectoryName(metaFile)!, meta.AudioFile);
            if (!File.Exists(wav))
            {
                output.WriteLine($"{meta.Slug}: audio missing ({meta.AudioFile})");
                continue;
            }

            string transcript = await transcriber.TranscribeFileAsync(wav, transcriptDir, CancellationToken.None);
            var result = PronunciationScorer.Evaluate(meta.Script, transcript);
            processed++;

            output.WriteLine(
                $"{meta.Slug,-16} terms {result.TermsMatched}/{result.TermsChecked}  " +
                $"WER {result.WordErrorRate:P0}  {(result.Passed ? "PASS" : "SUSPECT: " + string.Join(", ", result.SuspectTerms))}");
            output.WriteLine($"  transcript: {transcript.Trim()}");
        }

        processed.ShouldBeGreaterThan(0, "no audio samples were transcribed");
    }

    private sealed record SampleMeta(string Slug, string Script, string AudioFile);
}
