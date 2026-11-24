using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     The capture contract. A sample is only useful if the <c>ShapeTotal</c> it stores is the same
///     quantity the predictor multiplies a fitted scale against — otherwise a live scale and an
///     embedded scale are not comparable and the fit silently drifts.
/// </summary>
public sealed class SpokenDurationSampleTests
{
    private const string Read =
        "Now looking over Sydney. Mamre Road is closed at Solander Drive in St Clair. "
        + "Partly cloudy, 15 degrees.";

    private static SpokenDurationCalibration Calibration(double scale) => new()
    {
        Shape = new SpokenDurationShape
        {
            Syllable1Sec = 0.161356, Syllable2Sec = 0.205113, Syllable3Sec = 0.253884,
            Syllable4PlusSec = 0.314055, ExtraSyllableSec = 0.024263, PeriodSec = 0.832724,
            CommaSec = 0.568894, PerCharSec = 0.135732, AlphanumericSec = 0.416571,
            LongWordCharSec = -0.032767, PerDigitSec = 0.316503
        },
        Voices =
        [
            new SpokenDurationVoiceCalibration
            {
                VoiceId = "v1", ModelId = "eleven_v3", Speed = 0.97,
                PaceBoostWpm = 20, Samples = 9, Scale = scale, ResidualSdSec = 1.72
            }
        ]
    };

    [Fact]
    public void CapturedShapeTotal_ReproducesThePredictorsOwnEstimate()
    {
        const double scale = 0.4288;
        var cal = Calibration(scale);
        var model = new TokenAdditiveDurationModel(cal, new SpokenDurationModelOptions { Enabled = true });

        var predicted = model.Predict(Read, new DurationVoiceKey("v1", "eleven_v3", 0.97, 20));
        predicted.HasValue.ShouldBeTrue();

        // What the capture path stores, and what a fit would then multiply back out.
        double capturedShapeTotal = cal.Shape.Total(SpokenDurationFeatures.Extract(Read));
        double fromCapture = scale * capturedShapeTotal / 0.97;

        fromCapture.ShouldBe(predicted.Value.Seconds, 1e-9,
            "a captured ShapeTotal must be the same regressor the predictor uses");
    }

    [Fact]
    public void ImpliedScale_InvertsThePredictionSoAFitRecoversTheScale()
    {
        const double scale = 0.4288;
        var cal = Calibration(scale);
        double shapeTotal = cal.Shape.Total(SpokenDurationFeatures.Extract(Read));

        // A read that came back at exactly the predicted duration must imply exactly the fitted
        // scale — this is the identity the whole capture-then-fit loop rests on.
        var sample = new SpokenDurationSample
        {
            VoiceId = "v1", ModelId = "eleven_v3", Speed = 0.97, PaceBoostWpm = 20,
            ShapeTotal = shapeTotal,
            NaturalSeconds = scale * shapeTotal / 0.97,
            CapturedAt = DateTimeOffset.UnixEpoch
        };

        sample.ImpliedScale.ShouldBe(scale, 1e-9);
    }

    [Fact]
    public void Fingerprint_SeparatesPaceBoostBecausePoolingWasMeasuredWorse()
    {
        // Leave-one-out on the real corpus: pooling pb0 with pb20 moved MAE 1.623s -> 1.743s, and
        // the boosted reads run at 136 wpm against 144. The key must keep them apart.
        string pb0 = SpokenDurationSample.Format("v1", "eleven_v3", 0.97, 0);
        string pb20 = SpokenDurationSample.Format("v1", "eleven_v3", 0.97, 20);

        pb0.ShouldNotBe(pb20);
        pb20.ShouldBe("v1|eleven_v3|sp0.97|pb20");
    }

    [Fact]
    public void ImpliedScale_IsZeroRatherThanInfiniteOnAnUnmeasurableRead()
    {
        var sample = new SpokenDurationSample
        {
            VoiceId = "v1", ModelId = "eleven_v3", Speed = 1.0, PaceBoostWpm = 0,
            ShapeTotal = 0, NaturalSeconds = 12.0, CapturedAt = DateTimeOffset.UnixEpoch
        };

        sample.ImpliedScale.ShouldBe(0);
    }
}
