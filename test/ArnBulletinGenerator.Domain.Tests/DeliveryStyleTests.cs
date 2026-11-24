using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

/// <summary>Tests for the versioned delivery-style aggregate and its neutral policy.</summary>
public sealed class DeliveryStyleTests
{
    private static readonly DateTime Now = new(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);

    private static DeliveryStylePolicy Urgent(int intensity = 2) =>
        new("Urgent peak-hour read.", ["[urgent]", "[serious]"], intensity);

    [Fact]
    public void NeutralPolicy_IsNeutral()
    {
        DeliveryStylePolicy.Neutral.IsNeutral.ShouldBeTrue();
        new DeliveryStylePolicy("tone", ["[urgent]"], 0).IsNeutral.ShouldBeTrue("zero intensity => neutral");
        Urgent().IsNeutral.ShouldBeFalse();
    }

    [Fact]
    public void EmptyPalette_IsNotNeutral_ItMeansPunctuationOnly()
    {
        // Changed 2026-08-03. Every audio tag that measurably works on our voices inserts a
        // non-speech artefact - a sigh, a breath, a throat clear, a mechanical pause - and none
        // belong in a bulletin unless an operator asks for one. So the DEFAULT is no palette,
        // and the director still runs: it directs with ellipses, pacing and capitalisation.
        // Treating that as neutral short-circuited the director and made the default a no-op.
        new DeliveryStylePolicy("Calm and somber.", [], 3).IsNeutral.ShouldBeFalse();
        new DeliveryStylePolicy("Calm and somber.", [], 1).IsNeutral.ShouldBeFalse();
        // Intensity still governs: zero means do nothing, whatever the palette.
        new DeliveryStylePolicy("Calm and somber.", [], 0).IsNeutral.ShouldBeTrue();
    }

    [Fact]
    public void Create_MintsVersionOne()
    {
        var result = DeliveryStyle.Create("Drive-Time", "Punchy", Urgent(), "initial", "operator", Now);

        result.IsSuccess.ShouldBeTrue();
        var style = result.Value;
        style.Id.ShouldStartWith("ds-");
        style.CurrentVersion.ShouldBe(1);
        style.CurrentPolicy.ConfigurationKey.ShouldBe(Urgent().ConfigurationKey);
        style.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public void Create_RequiresName()
    {
        DeliveryStyle.Create("  ", null, Urgent(), null, null, Now).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(-50)]
    public void Create_RejectsOutOfRangeIntensity(int intensity)
    {
        var result = DeliveryStyle.Create("Drive-Time", null, Urgent(intensity), null, null, Now);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Invalid delivery policy intensity");
    }

    [Fact]
    public void ApplyEdit_MintsNewVersion_OnlyWhenPolicyChanges()
    {
        var style = DeliveryStyle.Create("Drive-Time", null, Urgent(2), null, null, Now).Value;

        // Same policy, just a name change → no new version.
        var minted = style.ApplyEdit("Drive-Time AM", null, Urgent(2), "rename", "ed", Now.AddMinutes(1));
        minted.ShouldBeFalse();
        style.CurrentVersion.ShouldBe(1);
        style.Name.ShouldBe("Drive-Time AM");

        // Policy change (intensity) → new version.
        var minted2 = style.ApplyEdit("Drive-Time AM", null, Urgent(3), "heavier", "ed", Now.AddMinutes(2));
        minted2.ShouldBeTrue();
        style.CurrentVersion.ShouldBe(2);
        style.CurrentPolicy.Intensity.ShouldBe(3);
    }

    [Fact]
    public void RestoreVersion_MintsCopyOfPriorPolicy()
    {
        var style = DeliveryStyle.Create("Drive-Time", null, Urgent(2), null, null, Now).Value;
        style.ApplyEdit("Drive-Time", null, Urgent(3), null, null, Now.AddMinutes(1));

        var restore = style.RestoreVersion(1, "ed", Now.AddMinutes(2));

        restore.IsSuccess.ShouldBeTrue();
        style.CurrentVersion.ShouldBe(3);
        style.CurrentPolicy.Intensity.ShouldBe(2, "restored v1's intensity");
    }

    [Fact]
    public void RestoreVersion_FailsForUnknownVersion()
    {
        var style = DeliveryStyle.Create("Drive-Time", null, Urgent(), null, null, Now).Value;
        style.RestoreVersion(99, null, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Archive_SetsStatusAndBumpsUpdatedAt_WithoutMintingVersion()
    {
        var style = DeliveryStyle.Create("Drive-Time", null, Urgent(), null, null, Now).Value;
        var initialVersionCount = style.Versions.Count;
        var initialVersion = style.CurrentVersion;

        style.Archive(Now.AddMinutes(1));

        style.Status.ShouldBe("archived");
        style.UpdatedAt.ShouldBe(Now.AddMinutes(1));
        style.CurrentVersion.ShouldBe(initialVersion);
        style.Versions.Count.ShouldBe(initialVersionCount);
    }

    [Fact]
    public void Unarchive_SetsStatusAndBumpsUpdatedAt_WithoutMintingVersion()
    {
        var style = DeliveryStyle.Create("Drive-Time", null, Urgent(), null, null, Now).Value;
        style.Archive(Now.AddMinutes(1));

        var versionCountBefore = style.Versions.Count;
        var currentVersionBefore = style.CurrentVersion;

        style.Unarchive(Now.AddMinutes(2));

        style.Status.ShouldBe("active");
        style.UpdatedAt.ShouldBe(Now.AddMinutes(2));
        style.CurrentVersion.ShouldBe(currentVersionBefore);
        style.Versions.Count.ShouldBe(versionCountBefore);
    }
}
