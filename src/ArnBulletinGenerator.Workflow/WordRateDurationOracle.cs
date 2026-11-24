using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow;

/// <summary>
///     A duration oracle backed by a single words-per-minute rate.
///     <para>
///         Deliberately the simplest thing that satisfies the port, and deliberately honest about
///         being a placeholder. The deployed pipeline predicts spoken duration from FINISHED prose
///         using a rate calibrated per voice against measured reads; this workflow has to answer
///         before prose exists, which is strictly less information. That gap is the single largest
///         accuracy risk in the design, so it is isolated behind <see cref="IDurationOracle" /> where
///         it can be replaced by a calibrated implementation without touching a stage.
///     </para>
///     <para>
///         The default 170 wpm is measured, not assumed. Five deployed 30-second reads replayed on
///         2026-08-26 aired at 148, 158, 166, 186 and 190 wpm (brisbane, adelaide, goldcoast,
///         sydney, melbourne), a mean of 169.6. The previous 150 default under-budgeted every slot
///         by roughly 12 per cent, which on a 30s slot is the difference between a 75-word and an
///         84-word target and showed up as a systematic underrun in the first live run.
///     </para>
///     <para>
///         The spread across those five markets is 148 to 190, so a single rate cannot serve them
///         all: the per-market variation is larger than the tolerance the fit loop is chasing. This
///         default is the honest global mean, and a per-voice calibrated implementation behind the
///         same port is the real fix.
///     </para>
/// </summary>
public sealed class WordRateDurationOracle(double wordsPerMinute = 170.0) : IDurationOracle
{
    private readonly double _wordsPerSecond = Math.Max(0.1, wordsPerMinute) / 60.0;

    public double PredictSeconds(string text) => SecondsForWords(CountWords(text));

    public double SecondsForWords(int words) => Math.Max(0, words) / _wordsPerSecond;

    public int WordsForSeconds(double seconds) => (int)Math.Round(Math.Max(0, seconds) * _wordsPerSecond);

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
