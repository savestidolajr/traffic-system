using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class VoiceCalibrationTests
{
    private static readonly DateTime FixedTime = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static VoiceCalibration CreateCalibration()
    {
        return new VoiceCalibration("key-1", "voice-1", VoiceSettings.Default);
    }

    [Fact]
    public void RecordMeasurement_FirstRun_SetsValuesDirectly()
    {
        var cal = CreateCalibration();

        cal.RecordMeasurement(5.0, 2.5, utcNow: FixedTime);

        cal.RunCount.ShouldBe(1);
        cal.SyllablesPerSecond.ShouldBe(5.0);
        cal.SyllablesPerWord.ShouldBe(2.5);
        cal.SyllablesPerMinute.ShouldBe(300.0); // 5 * 60
        cal.WordsPerMinute.ShouldBe(120.0); // 300 / 2.5
    }

    [Fact]
    public void RecordMeasurement_SecondRun_AppliesEMA_ThenClampsToTheDriftLimit()
    {
        var cal = CreateCalibration();
        cal.RecordMeasurement(10.0, 2.0, utcNow: FixedTime);

        cal.RecordMeasurement(4.0, 3.0, FixedTime, emaAlpha: 0.5);

        cal.RunCount.ShouldBe(2);
        // Raw EMA would be 0.5*4 + 0.5*10 = 7.0, a 30% move on one measurement. The drift limiter
        // caps it at 10% of the previous value: 10.0 -> 9.0.
        cal.SyllablesPerSecond.ShouldBe(9.0, 0.001);
        // Raw EMA 0.5*3 + 0.5*2 = 2.5 -> clamped up to 2.0 * 1.10 = 2.2.
        cal.SyllablesPerWord.ShouldBe(2.2, 0.001);
    }

    [Fact]
    public void RecordMeasurement_OneOutlierCannotMoveAnEstablishedRateFar()
    {
        // The live failure: a single out-of-family write moved Dolly Male Young's row 138.3 -> 186.1
        // wpm (+35%) while every honest measurement in that window read 136-147, and the job that
        // then read the spiked value burned 4 eleven_v3 renders. One bad write must cost at most one
        // drift step.
        var cal = CreateCalibration();
        cal.RecordMeasurement(3.55, 1.55, utcNow: FixedTime);   // ~137 wpm, a realistic warm row
        double before = cal.WordsPerMinute;

        cal.RecordMeasurement(7.70, 1.55, FixedTime);           // ~298 wpm — the impossible write

        cal.WordsPerMinute.ShouldBeLessThanOrEqualTo(before * (1.0 + VoiceCalibration.MaxPerMeasurementDrift) + 1e-9);
        (cal.WordsPerMinute / before).ShouldBeLessThan(1.11);
    }

    [Fact]
    public void RecordMeasurement_DriftLimitIsARateLimit_NotALock()
    {
        // A genuine, sustained rate change must still be trackable — just gradually. Ten writes of a
        // 20% faster rate get most of the way there.
        var cal = CreateCalibration();
        cal.RecordMeasurement(4.0, 1.5, utcNow: FixedTime);
        for (int i = 0; i < 10; i++)
            cal.RecordMeasurement(4.8, 1.5, FixedTime);

        cal.SyllablesPerSecond.ShouldBeGreaterThan(4.6);
        cal.SyllablesPerSecond.ShouldBeLessThanOrEqualTo(4.8);
    }

    [Fact]
    public void RecordMeasurement_MultipleRuns_Converges()
    {
        var cal = CreateCalibration();

        // Apply 10 identical measurements — should converge to 6.0
        for (int i = 0; i < 10; i++)
            cal.RecordMeasurement(6.0, 2.0, FixedTime, emaAlpha: 0.3);

        cal.SyllablesPerSecond.ShouldBe(6.0, 0.01);
        cal.SyllablesPerWord.ShouldBe(2.0, 0.01);
    }

    [Fact]
    public void RecordMeasurement_RecordsReferenceValues()
    {
        var cal = CreateCalibration();

        cal.RecordMeasurement(5.0, 2.0, referenceDuration: 3.5, referenceSyllableCount: 18, utcNow: FixedTime);

        cal.ReferenceDurationSeconds.ShouldBe(3.5);
        cal.ReferenceSyllableCount.ShouldBe(18);
    }

    [Fact]
    public void RecordMeasurement_ZeroSyllablesPerWord_SkipsWordsPerMinute()
    {
        var cal = CreateCalibration();

        cal.RecordMeasurement(5.0, 0.0, utcNow: FixedTime);

        cal.WordsPerMinute.ShouldBe(0.0);
    }

    [Fact]
    public void IsExpired_WhenWithinValidFor_ReturnsFalse()
    {
        var cal = CreateCalibration();
        cal.CalibratedAt = FixedTime;
        cal.ValidFor = TimeSpan.FromDays(30);

        cal.IsExpiredAt(FixedTime).ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_WhenPastValidFor_ReturnsTrue()
    {
        var cal = CreateCalibration();
        cal.CalibratedAt = FixedTime.AddDays(-31);
        cal.ValidFor = TimeSpan.FromDays(30);

        cal.IsExpiredAt(FixedTime).ShouldBeTrue();
    }
}
