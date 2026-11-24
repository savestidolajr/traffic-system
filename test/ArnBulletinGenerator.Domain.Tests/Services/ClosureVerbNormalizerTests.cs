using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ClosureVerbNormalizerTests
{
    [Fact]
    public void Normalize_LowercaseShut_RewritesToLowercaseClosed()
    {
        ClosureVerbNormalizer.Normalize("the M1 is shut at Kent Road")
            .ShouldBe("the M1 is closed at Kent Road");
    }

    [Fact]
    public void Normalize_CapitalizedShut_PreservesCapitalizedCasing()
    {
        ClosureVerbNormalizer.Normalize("Shut between A and B")
            .ShouldBe("Closed between A and B");
    }

    [Fact]
    public void Normalize_MultipleOccurrences_RewritesEveryOne()
    {
        ClosureVerbNormalizer.Normalize("shut at Kent Road, also shut near the bridge, Shut overnight")
            .ShouldBe("closed at Kent Road, also closed near the bridge, Closed overnight");
    }

    [Theory]
    [InlineData("The centre lane is shutdown for maintenance.")]
    [InlineData("Roller shutters are being installed on the shopfront.")]
    public void Normalize_WordBoundaryViolatingText_ReturnedUnchanged(string script)
    {
        ClosureVerbNormalizer.Normalize(script).ShouldBe(script);
    }

    [Fact]
    public void Normalize_NoShutInText_ReturnedUnchanged()
    {
        const string script = "Traffic is flowing well on the M1 this morning.";

        ClosureVerbNormalizer.Normalize(script).ShouldBe(script);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnedUnchanged()
    {
        ClosureVerbNormalizer.Normalize(string.Empty).ShouldBe(string.Empty);
    }
}
