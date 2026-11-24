using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class TokenAdditiveDurationModelTests
{
    private static readonly SpokenDurationShape Shape = new()
    {
        Syllable1Sec = 0.15,
        Syllable2Sec = 0.24,
        Syllable3Sec = 0.30,
        Syllable4PlusSec = 0.35,
        ExtraSyllableSec = 0.04,
        PeriodSec = 0.40,
        CommaSec = 0.20,
        PerCharSec = 0.01,
        AlphanumericSec = 0.06,
        LongWordCharSec = 0.02,
        PerDigitSec = 0.03
    };

    private static readonly SpokenDurationVoiceCalibration[] Rows =
    [
        new() { VoiceId = "yngV6X6u07ThpMteV50h", ModelId = "eleven_v3", Speed = 0.97, PaceBoostWpm = 0, Scale = 1.00, ResidualSdSec = 1.5516 },
        new() { VoiceId = "EF2rFRNHqbfaMctIvS2k", ModelId = "eleven_v3", Speed = 0.97, PaceBoostWpm = 0, Scale = 1.10, ResidualSdSec = 3.1059 },
        new() { VoiceId = "a1SQPk93LAMHsGM41p6S", ModelId = "eleven_multilingual_v2", Speed = 0.97, PaceBoostWpm = 0, Scale = 0.90, ResidualSdSec = 1.1185 },
        new() { VoiceId = "EF2rFRNHqbfaMctIvS2k", ModelId = "eleven_v3", Speed = 1, PaceBoostWpm = 40, Scale = 1.20, ResidualSdSec = 1.6536 },
        new() { VoiceId = "081vkDcbgbIsSiFEYxT0", ModelId = "eleven_v3", Speed = 1, PaceBoostWpm = 0, Scale = 1.30, ResidualSdSec = 1.2803 },
        new() { VoiceId = "EF2rFRNHqbfaMctIvS2k", ModelId = "eleven_v3", Speed = 0.97, PaceBoostWpm = 60, Scale = 1.40, ResidualSdSec = 1.3801 }
    ];

    private static SpokenDurationCalibration Calibration(IReadOnlyList<SpokenDurationVoiceCalibration> rows) =>
        new()
        {
            SchemaVersion = 3,
            Formula = "secondsAtSpeedOne = scale * pooledShape; predictedSeconds = secondsAtSpeedOne / speed",
            FittedFrom = "tests",
            Shape = Shape,
            Voices = rows
        };

    private static double ShapeTotal(string script)
    {
        SpokenDurationFeatures.Features features = SpokenDurationFeatures.Extract(script);
        return Shape.Syllable1Sec * features.Syllable1
             + Shape.Syllable2Sec * features.Syllable2
             + Shape.Syllable3Sec * features.Syllable3
             + Shape.Syllable4PlusSec * features.Syllable4Plus
             + Shape.ExtraSyllableSec * features.ExtraSyllables
             + Shape.PeriodSec * features.PeriodTokens
             + Shape.CommaSec * features.CommaTokens
             + Shape.PerCharSec * features.Chars
             + Shape.AlphanumericSec * features.Alphanumeric
             + Shape.LongWordCharSec * features.LongWordChars
             + Shape.PerDigitSec * features.Digits;
    }

    [Fact]
    public void Predict_KnownVector_MatchesFormulaForEveryCalibratedVoice()
    {
        const string script = "I.";
        foreach (SpokenDurationVoiceCalibration row in Rows)
        {
            var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());
            var result = model.Predict(script, new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm));

            result.IsSome.ShouldBeTrue();
            result.Value.Seconds.ShouldBe(row.Scale * ShapeTotal(script) / row.Speed, 1e-6);
            result.Value.ResidualSdSec.ShouldBe(row.ResidualSdSec);
        }
    }

    [Fact]
    public void Predict_ExactMatch_ReturnsExactTierAndUnmodifiedResidual()
    {
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict("I.", new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm));

        result.IsSome.ShouldBeTrue();
        result.Value.MatchTier.ShouldBe(SpokenDurationMatchTier.Exact);
        result.Value.ResidualSdSec.ShouldBe(row.ResidualSdSec);
    }

    [Fact]
    public void Predict_SameVoiceAndModelWithDifferentSpeed_RescalesAndInflatesResidual()
    {
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict("I.", new DurationVoiceKey(row.VoiceId, row.ModelId, 1.0, 0));

        result.IsSome.ShouldBeTrue();
        result.Value.MatchTier.ShouldBe(SpokenDurationMatchTier.VoiceAndModel);
        result.Value.ResidualSdSec.ShouldBe(row.ResidualSdSec * 1.5, 1e-12);
        result.Value.Seconds.ShouldBe(row.Scale * ShapeTotal("I."), 1e-12);

        var slower = model.Predict("I.", new DurationVoiceKey(row.VoiceId, row.ModelId, 0.97, 0));
        slower.Value.Seconds.ShouldBeGreaterThan(result.Value.Seconds);
    }

    [Fact]
    public void Predict_DifferentModelForSameVoice_InflatesResidualAgain()
    {
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict("I.", new DurationVoiceKey(row.VoiceId, "different-model", row.Speed, row.PaceBoostWpm));

        result.IsSome.ShouldBeTrue();
        result.Value.MatchTier.ShouldBe(SpokenDurationMatchTier.VoiceOnly);
        result.Value.ResidualSdSec.ShouldBe(row.ResidualSdSec * 2.0, 1e-12);
    }

    [Fact]
    public void Predict_UnknownVoiceId_RemainsNone()
    {
        var model = new TokenAdditiveDurationModel(Calibration(Rows), new SpokenDurationModelOptions());

        model.Predict("I.", new DurationVoiceKey("unknown", "eleven_v3", 1, 0)).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Predict_NaNPaceBoost_IsAbsentAndMatchesZeroRowExactly()
    {
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict("I.", new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, double.NaN));

        result.IsSome.ShouldBeTrue();
        result.Value.MatchTier.ShouldBe(SpokenDurationMatchTier.Exact);
        result.Value.ResidualSdSec.ShouldBe(row.ResidualSdSec);
    }

    [Fact]
    public void Predict_CairnsSarahReferenceScript_MatchesPooledShapeVector()
    {
        const string script = "Checking out the roads...... Captain Cook Highway near Aeroglen's busy northbound with roadworks at Stratford Connection Road.";
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict(script, new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm));

        result.Value.Seconds.ShouldBe(row.Scale * ShapeTotal(script) / row.Speed, 1e-12);
    }

    [Fact]
    public void Predict_ReturnsNone_ForUncalibratedVoice()
    {
        var model = new TokenAdditiveDurationModel(Calibration(Rows), new SpokenDurationModelOptions());

        model.Predict("I.", new DurationVoiceKey("unknown", "eleven_v3", 1, 0)).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Predict_ReturnsNone_WhenDisabled()
    {
        var model = new TokenAdditiveDurationModel(
            Calibration(Rows), new SpokenDurationModelOptions { Enabled = false });

        model.Predict("I.", new DurationVoiceKey("yngV6X6u07ThpMteV50h", "eleven_v3", 0.97, 0)).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Predict_MultipliesPooledShapeByVoiceScale_AndDividesBySpeed()
    {
        const string script = "I.";
        SpokenDurationVoiceCalibration row = Rows[0] with { Speed = 0.8, Scale = 1.75 };
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());

        var result = model.Predict(script, new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm));

        result.IsSome.ShouldBeTrue();
        result.Value.Seconds.ShouldBe(row.Scale * ShapeTotal(script) / row.Speed, 1e-12);
    }

    [Fact]
    public void Predict_DividesBySpeed_AndSlowerSpeedIsLonger()
    {
        SpokenDurationVoiceCalibration row = Rows[0];
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());
        var result = model.Predict("I.", new DurationVoiceKey(row.VoiceId, row.ModelId, 0.97, 0));

        double secondsAtSpeedOne = row.Scale * ShapeTotal("I.");
        result.Value.Seconds.ShouldBe(secondsAtSpeedOne / 0.97, 1e-6);
        result.Value.Seconds.ShouldBeGreaterThan(secondsAtSpeedOne);
    }

    [Fact]
    public void Predict_ConcaveSyllableBuckets_DoNotTripleThreeSyllableWordCost()
    {
        const string oneSyllableScript = "road road road road";
        const string threeSyllableScript = "banana banana banana banana";
        SpokenDurationFeatures.Features oneSyllable = SpokenDurationFeatures.Extract(oneSyllableScript);
        SpokenDurationFeatures.Features threeSyllable = SpokenDurationFeatures.Extract(threeSyllableScript);
        oneSyllable.Syllable1.ShouldBe(4);
        threeSyllable.Syllable3.ShouldBe(4);

        SpokenDurationVoiceCalibration row = Rows[0] with { Speed = 1.0 };
        var model = new TokenAdditiveDurationModel(Calibration([row]), new SpokenDurationModelOptions());
        double oneSyllableSeconds = model.Predict(
            oneSyllableScript, new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm)).Value.Seconds;
        double threeSyllableSeconds = model.Predict(
            threeSyllableScript, new DurationVoiceKey(row.VoiceId, row.ModelId, row.Speed, row.PaceBoostWpm)).Value.Seconds;

        threeSyllableSeconds.ShouldBeLessThan(oneSyllableSeconds * 3);
    }
}
