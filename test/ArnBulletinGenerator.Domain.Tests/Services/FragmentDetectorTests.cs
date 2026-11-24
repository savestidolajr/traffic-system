using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class FragmentDetectorTests
{
    [Fact]
    public void Flags_stray_full_stop_before_lowercase_while()
    {
        const string script =
            "Works at Webster Road slow Gympie Road. while the Bruce has a citybound lane blocked at Kessels Road.";

        var result = FragmentDetector.Detect(script);

        result.HasFragment.ShouldBeTrue();
        result.Feedback.ShouldContain("FRAGMENT");
        result.Feedback.ShouldContain("while");
    }

    [Fact]
    public void Does_not_flag_capitalized_While_after_period()
    {
        FragmentDetector.Detect("Gympie Road is slow. While the Bruce is clear.")
            .HasFragment.ShouldBeFalse();
    }

    [Fact]
    public void Flags_stray_full_stop_before_lowercase_with()
    {
        FragmentDetector.Detect("The M1 is congested. with the Bruce backing up.")
            .HasFragment.ShouldBeTrue();
    }

    [Fact]
    public void Flags_stray_full_stop_before_lowercase_and()
    {
        FragmentDetector.Detect("Webster Road is slow. and the Gympie is backed up.")
            .HasFragment.ShouldBeTrue();
    }

    [Fact]
    public void Flags_stray_full_stop_before_lowercase_but()
    {
        FragmentDetector.Detect("The M1 is backed up. but traffic is clearing on the Bruce.")
            .HasFragment.ShouldBeTrue();
    }

    [Fact]
    public void Passes_a_clean_script()
    {
        FragmentDetector.Detect("The M1 is busy through the city. The Bruce motorway is clear.")
            .HasFragment.ShouldBeFalse();
    }

    [Fact]
    public void Repair_fuses_stray_stop_into_comma()
    {
        const string input = "Gympie Road. while the Bruce";

        FragmentDetector.Repair(input).ShouldBe("Gympie Road, while the Bruce");
    }

    [Fact]
    public void Repair_preserves_legitimate_sentence_boundary()
    {
        const string input = "Gympie Road is slow. While the Bruce is clear.";

        FragmentDetector.Repair(input).ShouldBe(input);
    }

    [Fact]
    public void Handles_empty_script()
    {
        FragmentDetector.Detect(string.Empty).HasFragment.ShouldBeFalse();
        FragmentDetector.Detect(null!).HasFragment.ShouldBeFalse();
        FragmentDetector.Detect("   ").HasFragment.ShouldBeFalse();
    }

    [Fact]
    public void Flags_multiple_fragments()
    {
        const string script =
            "Webster Road is slow. while the Bruce has a citybound lane blocked. and the Gympie is backed up.";

        FragmentDetector.Detect(script).HasFragment.ShouldBeTrue();
    }
}
