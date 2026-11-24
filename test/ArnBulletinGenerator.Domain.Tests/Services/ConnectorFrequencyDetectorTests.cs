using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ConnectorFrequencyDetectorTests
{
    [Theory]
    [InlineData("The M1 is slow, while the M2 is clear.")]                          // one contrast — allowed
    [InlineData("The M1 is slow and the M2 is clear.")]                              // additive — allowed
    [InlineData("Meanwhile, the M1 is slow, and meanwhile the M2 too.")]            // "meanwhile" is not "while"
    public void Detect_allows_up_to_one_contrast(string script) =>
        ConnectorFrequencyDetector.Detect(script).HasOveruse.ShouldBeFalse();

    [Theory]
    [InlineData("The M1 is slow, while the M2 crawls, while the M3 is shut.", 2)]
    [InlineData("A is clear whereas B is slow, while C is heavy.", 2)]
    public void Detect_flags_repeated_contrast_connectors(string script, int expected)
    {
        var result = ConnectorFrequencyDetector.Detect(script);
        result.HasOveruse.ShouldBeTrue();
        result.Count.ShouldBe(expected);
    }

    [Fact]
    public void Detect_empty_script_is_clean() =>
        ConnectorFrequencyDetector.Detect("").HasOveruse.ShouldBeFalse();
}
