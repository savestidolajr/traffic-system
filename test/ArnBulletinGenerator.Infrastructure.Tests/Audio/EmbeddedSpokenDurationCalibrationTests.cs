using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public sealed class EmbeddedSpokenDurationCalibrationTests
{
    /// <summary>
    ///     Schema v3 uses one pooled token shape and one scale per voice. These assertions pin the
    ///     contract and its concave syllable shape, not fitted coefficient values.
    /// </summary>
    [Fact]
    public void Load_ParsesEmbeddedCalibrationWithExpectedVoiceFleet()
    {
        var calibration = EmbeddedSpokenDurationCalibration.Load();

        calibration.SchemaVersion.ShouldBe(3);
        calibration.Shape.ShouldNotBeNull();
        calibration.Voices.Count.ShouldBeGreaterThanOrEqualTo(7);

        SpokenDurationShape shape = calibration.Shape;
        shape.Syllable1Sec.ShouldBeGreaterThan(0);
        shape.Syllable2Sec.ShouldBeGreaterThan(shape.Syllable1Sec);
        shape.Syllable3Sec.ShouldBeGreaterThan(shape.Syllable2Sec);
        shape.Syllable4PlusSec.ShouldBeGreaterThan(shape.Syllable3Sec);
        shape.Syllable2Sec.ShouldBeLessThan(2 * shape.Syllable1Sec);
        shape.Syllable3Sec.ShouldBeLessThan(3 * shape.Syllable1Sec);
        shape.ExtraSyllableSec.ShouldBeGreaterThanOrEqualTo(0);
        shape.PeriodSec.ShouldBeGreaterThan(shape.CommaSec);
        shape.CommaSec.ShouldBeGreaterThan(0);
        shape.PerCharSec.ShouldBeGreaterThan(0);
        (shape.PerCharSec + shape.LongWordCharSec).ShouldBeGreaterThan(0);
        shape.LongWordCharSec.ShouldNotBe(0);
        shape.AlphanumericSec.ShouldBeGreaterThan(0);
        shape.PerDigitSec.ShouldBeGreaterThan(0);

        foreach (var row in calibration.Voices)
        {
            row.Scale.ShouldBeGreaterThan(0);
            row.ResidualSdSec.ShouldBeGreaterThan(0);
        }
    }
}
