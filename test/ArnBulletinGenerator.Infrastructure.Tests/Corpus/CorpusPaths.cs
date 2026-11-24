namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Resolves on-disk locations for the recorded external-API corpus.
///     The corpus lives in the source tree (committed), not under bin/, so it is
///     versioned alongside the code that produced it.
/// </summary>
internal static class CorpusPaths
{
    /// <summary>
    ///     Walks up from the test assembly's base directory to the repository root,
    ///     identified by the solution file.
    /// </summary>
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ArnBulletinGenerator.slnx")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate repository root (ArnBulletinGenerator.slnx not found above the test output directory).");
    }

    /// <summary>Root directory for the recorded HERE API corpus.</summary>
    public static string HereCorpusRoot() => Path.Combine(RepoRoot(), "test", "corpus", "here");

    /// <summary>Root directory for the recorded ElevenLabs audio corpus.</summary>
    public static string ElevenLabsCorpusRoot() => Path.Combine(RepoRoot(), "test", "corpus", "elevenlabs");

    /// <summary>Root directory for committed regression fixtures (captured bad/unsatisfactory bulletins).</summary>
    public static string RegressionCorpusRoot() => Path.Combine(RepoRoot(), "test", "corpus", "regressions");

    /// <summary>Root directory for the duration-prediction corpus.</summary>
    public static string DurationCorpusRoot() => Path.Combine(RepoRoot(), "test", "corpus", "duration");
}
