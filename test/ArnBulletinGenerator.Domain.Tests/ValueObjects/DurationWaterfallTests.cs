using ArnBulletinGenerator.Domain.ValueObjects;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.ValueObjects;

public class DurationWaterfallTests
{
    [Fact]
    public void WordCountAccuracyPercent_PerfectEstimate_Returns100()
    {
        var wf = new DurationWaterfall(60, 60, 60, 60, 150, 150);
        wf.WordCountAccuracyPercent.ShouldBe(100.0);
    }

    [Fact]
    public void WordCountAccuracyPercent_FinalSecondsZero_ReturnsZero()
    {
        var wf = new DurationWaterfall(60, 50, 55, 0, 150, 150);
        wf.WordCountAccuracyPercent.ShouldBe(0.0);
    }

    [Fact]
    public void WordCountAccuracyPercent_OverEstimate_CalculatesCorrectly()
    {
        // WordCountEstimated=70, Final=60 → error=10/60=16.67% → accuracy=83.33%
        var wf = new DurationWaterfall(60, 60, 70, 60, 150, 150);
        wf.WordCountAccuracyPercent.ShouldBe(83.33, 0.01);
    }

    [Fact]
    public void WordCountAccuracyPercent_UnderEstimate_CalculatesCorrectly()
    {
        // WordCountEstimated=50, Final=60 → error=10/60=16.67% → accuracy=83.33%
        var wf = new DurationWaterfall(60, 60, 50, 60, 150, 150);
        wf.WordCountAccuracyPercent.ShouldBe(83.33, 0.01);
    }

    [Fact]
    public void WordCountAccuracyPercent_VeryInaccurate_ClampedAtZero()
    {
        // WordCountEstimated=200, Final=60 → error=140/60=233% → 100-233=-133 → clamped to 0
        var wf = new DurationWaterfall(60, 60, 200, 60, 150, 150);
        wf.WordCountAccuracyPercent.ShouldBe(0.0);
    }

    [Fact]
    public void SyllableAccuracyPercent_PerfectEstimate_Returns100()
    {
        var wf = new DurationWaterfall(60, 60, 60, 60, 150, 150);
        wf.SyllableAccuracyPercent.ShouldBe(100.0);
    }

    [Fact]
    public void SyllableAccuracyPercent_FinalSecondsZero_ReturnsZero()
    {
        var wf = new DurationWaterfall(60, 55, 50, 0, 150, 150);
        wf.SyllableAccuracyPercent.ShouldBe(0.0);
    }

    [Fact]
    public void SyllableAccuracyPercent_CalculatesFromEstimatedSeconds()
    {
        // EstimatedSeconds=54, Final=60 → error=6/60=10% → accuracy=90%
        var wf = new DurationWaterfall(60, 54, 50, 60, 150, 150);
        wf.SyllableAccuracyPercent.ShouldBe(90.0);
    }

    [Fact]
    public void SyllableAccuracyPercent_VeryInaccurate_ClampedAtZero()
    {
        var wf = new DurationWaterfall(60, 300, 60, 60, 150, 150);
        wf.SyllableAccuracyPercent.ShouldBe(0.0);
    }

    [Fact]
    public void BothAccuracies_IndependentOfEachOther()
    {
        // WordCountEstimated=55, EstimatedSeconds=65, Final=60
        var wf = new DurationWaterfall(60, 65, 55, 60, 150, 150);

        // WordCount: |55-60|/60 = 8.33% → 91.67%
        wf.WordCountAccuracyPercent.ShouldBe(91.67, 0.01);
        // Syllable: |65-60|/60 = 8.33% → 91.67%
        wf.SyllableAccuracyPercent.ShouldBe(91.67, 0.01);
    }
}
