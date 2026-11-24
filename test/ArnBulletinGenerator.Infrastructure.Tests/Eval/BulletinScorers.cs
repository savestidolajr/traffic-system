using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Audio;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Duration vs target. <see cref="DurationResult.MeasuredSeconds" /> holds the real on-air length
///     when audio was synthesized (ElevenLabs alignment); otherwise the result carries only the offline
///     word-rate estimate, which is a rough proxy, not a measurement.
/// </summary>
internal static class DurationScorer
{
    private static readonly ISpeechDurationEstimator Estimator = new SpeechDurationEstimator();

    public static DurationResult Evaluate(string script, double targetSeconds, double? measuredSeconds = null) =>
        new(Estimator.EstimateDuration(script), targetSeconds, measuredSeconds);
}

internal sealed record DurationResult(double EstimatedSeconds, double TargetSeconds, double? MeasuredSeconds = null)
{
    /// <summary>
    ///     The slow-fill floor: below this fraction of target the audio pipeline
    ///     can't stretch the read to fill the slot and the bulletin falls back.
    ///     Matches DraftWordBudget's 0.9× in src.
    /// </summary>
    public const double SlotFillFloor = 0.9;

    /// <summary>Estimated seconds fall below the slow-fill floor — bulletin can't fill its slot.</summary>
    public bool IsUnderfilled => EstimatedSeconds < TargetSeconds * SlotFillFloor;

    /// <summary>Measured seconds when available, otherwise the estimate.</summary>
    public double Seconds => MeasuredSeconds ?? EstimatedSeconds;

    public bool IsMeasured => MeasuredSeconds.HasValue;

    public double DeltaSeconds => Seconds - TargetSeconds;

    /// <summary>Hard gate: fails when the estimated read can't fill the slot (underfill).</summary>
    public bool Passed => !IsUnderfilled;

    public bool WithinTolerance(double fraction) =>
        TargetSeconds > 0 && Math.Abs(DeltaSeconds) <= TargetSeconds * fraction;
}

/// <summary>Wraps the production style gate (<see cref="TrafficScriptStyleValidator" />).</summary>
internal static class StyleScorer
{
    public static StyleResult Evaluate(string script)
    {
        var result = TrafficScriptStyleValidator.Validate(script);
        return new StyleResult(result.IsValid, result.Issues);
    }
}

internal sealed record StyleResult(bool Passed, IReadOnlyList<string> Issues);

/// <summary>Wraps the production content-safety filter (<see cref="IContentSafetyFilter" />).</summary>
internal static class SafetyScorer
{
    public static async Task<SafetyResult> EvaluateAsync(IContentSafetyFilter filter, string script)
    {
        var result = await filter.CheckAsync(script, null);
        return new SafetyResult(result.IsAllowed, result.Violations, result.Warnings);
    }
}

internal sealed record SafetyResult(bool Passed, IReadOnlyList<string> Violations, IReadOnlyList<string> Warnings);
