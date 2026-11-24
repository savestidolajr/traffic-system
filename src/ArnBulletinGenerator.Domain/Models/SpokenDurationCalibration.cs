namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Describes how confidently a spoken-duration prediction was matched to calibration data.
///     Exact is a four-field match; VoiceAndModel is a nearest settings row for the same voice and
///     model; VoiceOnly crosses model boundaries for the same voice.
/// </summary>
public enum SpokenDurationMatchTier
{
    Exact,
    VoiceAndModel,
    VoiceOnly
}

/// <summary>
///     Pooled token-shape calibration for spoken bulletin duration. The bulletin corpus cannot
///     identify this model on its own because every observation is a slot-constrained 45-60 word
///     read: word count and syllable count are collinear (words are about syllables/1.5), leaving
///     almost no length variation with which to fit a rate. The 11 shape coefficients are therefore
///     fitted once, pooled across all voices, from 11,869 measured word alignments spanning 0 to
///     5+ syllables, where they are identified precisely. Each voice contributes exactly one
///     free scalar fitted against bulletin durations. Leave-one-out measurement gives MAE 0.857s,
///     p90 1.75s and bias -0.03s, versus 1.094s / 2.15s for the superseded form. A per-voice
///     intercept (0.867s) and separately scaling silence from speech (0.892s) did not pay for
///     their extra parameter.
/// </summary>
public sealed record SpokenDurationCalibration
{
    public int SchemaVersion { get; init; }

    public string Formula { get; init; } = string.Empty;

    public string FittedFrom { get; init; } = string.Empty;

    public SpokenDurationShape Shape { get; init; } = new();

    public IReadOnlyList<SpokenDurationVoiceCalibration> Voices { get; init; } = [];
}

/// <summary>One pooled token-shape coefficient set, expressed as seconds at speed one.</summary>
public sealed record SpokenDurationShape
{
    public double Syllable1Sec { get; init; }
    public double Syllable2Sec { get; init; }
    public double Syllable3Sec { get; init; }
    public double Syllable4PlusSec { get; init; }
    public double ExtraSyllableSec { get; init; }
    public double PeriodSec { get; init; }
    public double CommaSec { get; init; }
    public double PerCharSec { get; init; }
    public double AlphanumericSec { get; init; }
    public double LongWordCharSec { get; init; }
    public double PerDigitSec { get; init; }

    /// <summary>
    ///     The shape sum for one read — the regressor a per-voice scale multiplies. Lives here so
    ///     the predictor and the sample writer cannot disagree about it: a captured
    ///     <c>ShapeTotal</c> is only comparable to a fitted scale if both evaluated the same eleven
    ///     terms in the same order.
    /// </summary>
    public double Total(Services.SpokenDurationFeatures.Features f) =>
        Syllable1Sec * f.Syllable1 +
        Syllable2Sec * f.Syllable2 +
        Syllable3Sec * f.Syllable3 +
        Syllable4PlusSec * f.Syllable4Plus +
        ExtraSyllableSec * f.ExtraSyllables +
        PeriodSec * f.PeriodTokens +
        CommaSec * f.CommaTokens +
        PerCharSec * f.Chars +
        AlphanumericSec * f.Alphanumeric +
        LongWordCharSec * f.LongWordChars +
        PerDigitSec * f.Digits;
}

/// <summary>One voice scale fitted against bulletin durations, with residual uncertainty.</summary>
public sealed record SpokenDurationVoiceCalibration
{
    public string VoiceId { get; init; } = string.Empty;
    public string VoiceName { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public double Speed { get; init; }
    public double PaceBoostWpm { get; init; }
    public int Samples { get; init; }
    public double Scale { get; init; }
    public double ResidualSdSec { get; init; }
}
