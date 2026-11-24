using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class VaguenessDetectorTests
{
    [Theory]
    [InlineData("Parramatta Road and a couple more spots have minor delays.")]
    [InlineData("There are a few minor hold-ups around town.")]
    [InlineData("The M1 is slow and the rest is clear.")]
    [InlineData("A few other roads are slow as well.")]
    [InlineData("Delays elsewhere on the network.")]
    public void Detect_flags_vague_unsourced_filler(string script) =>
        VaguenessDetector.Detect(script).HasVagueness.ShouldBeTrue();

    [Theory]
    [InlineData("Punt Road, Hoddle Street and Church Street are all at a standstill.")] // grouped NAMED roads
    [InlineData("Queues back about a couple of kays on the M1.")]                       // "a couple of kays" = distance
    [InlineData("There's a breakdown with a few brake lights on the M4.")]              // "a few brake lights" not a vague frame
    [InlineData("Dacey Avenue and Parramatta Road have a few delays.")]   // bare quantity on named roads
    [InlineData("The M4 has a few hold-ups near the tunnel.")]            // bare quantity on a named road
    [InlineData("The Monash is moving nicely.")]
    public void Detect_leaves_named_and_concrete_copy_untouched(string script) =>
        VaguenessDetector.Detect(script).HasVagueness.ShouldBeFalse();
}
