using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Workflow.Tests.Fakes;

/// <summary>
///     One word costs exactly one second.
///     <para>
///         Structural tests care about which stories survive a budget and how the loop reacts, not
///         about how fast anyone talks. Driving them through the real rate coupled them to the
///         estimator's arithmetic: recalibrating 150 to 170 wpm on 2026-08-26, from five measured
///         deployed reads, broke two tests that were asserting nothing about speech rate. With this
///         oracle a target of 30 seconds means a budget of 30 words and nothing else moves.
///     </para>
/// </summary>
public sealed class OneWordPerSecondOracle : IDurationOracle
{
    public double PredictSeconds(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public double SecondsForWords(int words) => Math.Max(0, words);

    public int WordsForSeconds(double seconds) => (int)Math.Round(Math.Max(0, seconds));
}
