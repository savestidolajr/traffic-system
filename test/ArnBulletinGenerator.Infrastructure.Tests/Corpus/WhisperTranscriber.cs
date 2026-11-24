using System.Diagnostics;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Transcribes WAV audio with the local Whisper CLI (<c>.venv/bin/whisper</c>). Reusable by the
///     ElevenLabs corpus pronunciation test and the live eval.
///     <para>
///         Strictly resource-bounded so it never starves the desktop: the process is pinned to a small
///         set of CPUs via <c>taskset</c>, deprioritized via <c>nice</c>, its math-library thread pools are
///         capped, decoding is greedy (<c>--beam_size 1</c>), and it is killed if it overruns a wall-clock
///         budget. Whisper is prompted with Australian place names so it transcribes them correctly.
///     </para>
/// </summary>
internal sealed class WhisperTranscriber
{
    public const string AustralianPlacesPrompt =
        "Warringah Freeway, Church Street, Walker Street, Sommerville Road, M4, M80, Ring Road, " +
        "Princes Freeway, Mordialloc Freeway, Smith Street, Pacific Motorway, Morala Avenue, Coast Highway, " +
        "Sydney Harbour Bridge, Parramatta, Tullamarine, Monash Freeway, Gold Coast, Woolloomooloo, " +
        "Mona Vale, Wahroonga, Coomera, Mooloolaba.";

    private const int TimeoutSeconds = 300;

    private readonly string _whisperPath;
    private readonly string _initialPrompt;
    private readonly int _threads;
    private readonly string _model;

    public WhisperTranscriber(string whisperPath, string? initialPrompt = null)
    {
        _whisperPath = whisperPath;
        _initialPrompt = initialPrompt ?? AustralianPlacesPrompt;
        _threads = ResolveThreadBudget();
        _model = Environment.GetEnvironmentVariable("ARN_WHISPER_MODEL") is { Length: > 0 } m ? m : "base";
    }

    /// <summary>Returns a transcriber when local Whisper is available, otherwise null.</summary>
    public static WhisperTranscriber? CreateIfAvailable(string? initialPrompt = null)
    {
        string path = Path.Combine(CorpusPaths.RepoRoot(), ".venv", "bin", "whisper");
        return File.Exists(path) ? new WhisperTranscriber(path, initialPrompt) : null;
    }

    /// <summary>Transcribes in-memory WAV bytes, using a throwaway temp directory.</summary>
    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "arn-whisper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string wavPath = Path.Combine(directory, "audio.wav");
        await File.WriteAllBytesAsync(wavPath, wavBytes, cancellationToken);
        try
        {
            return await TranscribeFileAsync(wavPath, directory, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>Transcribes a WAV file, writing Whisper's <c>.txt</c> output into <paramref name="outputDir" />.</summary>
    public async Task<string> TranscribeFileAsync(string wavPath, string outputDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);

        string[] whisperArgs =
        [
            wavPath, "--model", _model, "--language", "en", "--task", "transcribe",
            "--output_format", "txt", "--output_dir", outputDir, "--fp16", "False",
            "--verbose", "False", "--threads", _threads.ToString(),
            "--beam_size", "1", "--best_of", "1", "--temperature", "0",
            "--initial_prompt", _initialPrompt
        ];

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        ConfineResources(startInfo, whisperArgs);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start the Whisper process.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            // Timed out — return whatever (if anything) was written rather than hanging.
        }

        await Task.WhenAll(stdout, stderr);

        string transcriptPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(wavPath) + ".txt");
        return File.Exists(transcriptPath)
            ? await File.ReadAllTextAsync(transcriptPath, cancellationToken)
            : string.Empty;
    }

    /// <summary>
    ///     Wraps the Whisper invocation in <c>taskset</c> (pin to a CPU subset) and <c>nice</c> (low
    ///     priority), and caps math-library thread pools via environment. Falls back gracefully if the
    ///     wrappers are unavailable.
    /// </summary>
    private void ConfineResources(ProcessStartInfo startInfo, string[] whisperArgs)
    {
        foreach (string variable in new[]
                 {
                     "OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS",
                     "NUMEXPR_NUM_THREADS", "VECLIB_MAXIMUM_THREADS"
                 })
            startInfo.Environment[variable] = _threads.ToString();

        string? taskset = ResolveBinary("taskset");
        string? nice = ResolveBinary("nice");

        if (taskset is not null)
        {
            startInfo.FileName = taskset;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"0-{_threads - 1}");
            if (nice is not null)
            {
                startInfo.ArgumentList.Add(nice);
                startInfo.ArgumentList.Add("-n");
                startInfo.ArgumentList.Add("19");
            }

            startInfo.ArgumentList.Add(_whisperPath);
        }
        else if (nice is not null)
        {
            startInfo.FileName = nice;
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("19");
            startInfo.ArgumentList.Add(_whisperPath);
        }
        else
        {
            startInfo.FileName = _whisperPath;
        }

        foreach (string arg in whisperArgs)
            startInfo.ArgumentList.Add(arg);
    }

    private static int ResolveThreadBudget()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("ARN_WHISPER_THREADS"), out int configured) && configured > 0)
            return configured;

        // Leave the overwhelming majority of cores for the desktop.
        return Math.Clamp(Environment.ProcessorCount / 8, 1, 2);
    }

    private static string? ResolveBinary(string name)
    {
        foreach (string dir in new[] { "/usr/bin", "/bin", "/usr/local/bin" })
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
