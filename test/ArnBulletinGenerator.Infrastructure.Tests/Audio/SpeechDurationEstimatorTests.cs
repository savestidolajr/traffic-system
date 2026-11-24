using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class SpeechDurationEstimatorTests(ITestOutputHelper output)
{
    private readonly SpeechDurationEstimator _estimator = new();

    [Theory]
    [InlineData("Hello world.", 0.5, 2.0)]
    [InlineData("This is a slightly longer sentence to test the syllable counting logic.", 3.5, 7.0)]
    [InlineData("Wait, what? I didn't expect that!", 2.0, 6.0)]
    public void EstimateDuration_ReturnsReasonableValues(string text, double min, double max)
    {
        double duration = _estimator.EstimateDuration(text);
        output.WriteLine($"\"{text}\" => {duration:F2}s");
        duration.ShouldBeInRange(min, max);
    }

    [Fact]
    public void MoreWords_ProduceLongerDuration()
    {
        _estimator.EstimateDuration("extraordinary circumstances happened")
            .ShouldBeGreaterThan(_estimator.EstimateDuration("hi there"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void EstimateDuration_EmptyOrWhitespace_ReturnsZero(string? text)
    {
        _estimator.EstimateDuration(text!).ShouldBe(0);
    }

    [Fact]
    public void EstimateDuration_CustomSecondsPerWord_ScalesLinearly()
    {
        string text = "one two three four five";
        double defaultDuration = _estimator.EstimateDuration(text);
        double doubleDuration = _estimator.EstimateDuration(text, 0.8);

        doubleDuration.ShouldBe(defaultDuration * 2, 0.01);
    }

    [Fact]
    public void EstimateDuration_SingleWord_ReturnsSecondsPerWord()
    {
        _estimator.EstimateDuration("hello").ShouldBe(0.4, 0.01);
    }
}
