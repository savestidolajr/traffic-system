using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class VoiceProfileTests
{
    private static readonly DateTime Now = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);
    private const string VoiceId = "oKDz3nEzNfwKIVvUwPs0";

    private static VoiceSettings Settings(double stability = 0.5, double similarity = 0.9) =>
        VoiceSettings.Create(stability, similarity, 0.0, true, 1.0).Value;

    private static VoiceProfile NewProfile(VoiceSettings? settings = null) =>
        VoiceProfile.Create("ARN Hudson — Natural", "Pilot default.", VoiceId, "ARN Hudson",
            "eleven_v3", settings ?? Settings(), "Initial variant.", "admin@arn.com.au", Now).Value;

    [Fact]
    public void Create_MintsVersionOne_WithConfigurationKeyAndCurrentVersion()
    {
        var profile = NewProfile();

        profile.Versions.Count.ShouldBe(1);
        profile.CurrentVersion.ShouldBe(1);
        profile.Versions[0].Version.ShouldBe(1);
        profile.Versions[0].ConfigurationKey.ShouldBe(Settings().GetConfigurationKey(VoiceId));
        profile.Status.ShouldBe("active");
        profile.Id.ShouldStartWith("vp-");
    }

    [Fact]
    public void Create_BlankName_Fails()
    {
        var result = VoiceProfile.Create("  ", null, VoiceId, "ARN Hudson", "eleven_v3", Settings(),
            null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("name");
    }

    [Fact]
    public void ApplyEdit_SettingsChanged_MintsNewVersion()
    {
        var profile = NewProfile(Settings(stability: 0.5));

        var minted = profile.ApplyEdit("ARN Hudson — Punchy", "Tighter read.", "eleven_v3",
            Settings(stability: 0.7), "Raised stability.", "editor@arn.com.au", Now.AddMinutes(5));

        minted.Value.ShouldBeTrue();
        profile.Versions.Count.ShouldBe(2);
        profile.CurrentVersion.ShouldBe(2);
        profile.Versions[1].Settings.Stability.ShouldBe(0.7);
        profile.Versions[1].ChangeNote.ShouldBe("Raised stability.");
        profile.Name.ShouldBe("ARN Hudson — Punchy");
    }

    [Fact]
    public void ApplyEdit_OnlyNameChanged_DoesNotMintVersion()
    {
        var profile = NewProfile(Settings(stability: 0.5));

        var minted = profile.ApplyEdit("Renamed", "New description.", "eleven_v3",
            Settings(stability: 0.5), "rename", "editor@arn.com.au", Now.AddMinutes(5));

        minted.Value.ShouldBeFalse();
        profile.Versions.Count.ShouldBe(1);
        profile.CurrentVersion.ShouldBe(1);
        profile.Name.ShouldBe("Renamed");
        profile.Description.ShouldBe("New description.");
    }

    [Fact]
    public void ApplyEdit_ModelChanged_MintsNewVersion()
    {
        var profile = NewProfile();

        var minted = profile.ApplyEdit("ARN Hudson — Natural", "Pilot default.", "eleven_multilingual_v2",
            Settings(), "Switched model.", "editor@arn.com.au", Now.AddMinutes(5));

        minted.Value.ShouldBeTrue();
        profile.Versions.Count.ShouldBe(2);
        profile.Versions[1].Model.ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public void AdoptBaseVoice_ThenApplyEdit_SwapsBaseVoiceAndMintsVersion()
    {
        var profile = NewProfile(Settings(stability: 0.5));
        const string remixedVoiceId = "remixed-abc123";

        profile.AdoptBaseVoice(remixedVoiceId, "ARN Hudson (remixed)");
        // Same settings/model as v1 — only the base voice changed, which must still mint a version.
        var minted = profile.ApplyEdit("ARN Hudson — Energetic", "More punch.", "eleven_v3",
            Settings(stability: 0.5), "Adopted remix.", "editor@arn.com.au", Now.AddMinutes(5));

        minted.Value.ShouldBeTrue();
        profile.BaseVoiceId.ShouldBe(remixedVoiceId);
        profile.BaseVoiceName.ShouldBe("ARN Hudson (remixed)");
        profile.Versions.Count.ShouldBe(2);
        profile.Versions[1].ConfigurationKey.ShouldBe(
            Settings(stability: 0.5).GetConfigurationKey(remixedVoiceId));
    }

    [Fact]
    public void AdoptBaseVoice_SameOrBlankId_LeavesBaseVoiceUnchanged()
    {
        var profile = NewProfile();

        profile.AdoptBaseVoice(VoiceId, "ignored");
        profile.AdoptBaseVoice("  ", "ignored");

        profile.BaseVoiceId.ShouldBe(VoiceId);
        profile.BaseVoiceName.ShouldBe("ARN Hudson");
    }

    [Fact]
    public void RestoreVersion_MintsNewVersionFromSource()
    {
        var profile = NewProfile(Settings(stability: 0.5));
        profile.ApplyEdit("p", null, "eleven_v3", Settings(stability: 0.9), "v2", "e", Now.AddMinutes(5));

        var result = profile.RestoreVersion(1, "editor@arn.com.au", Now.AddMinutes(10));

        result.IsSuccess.ShouldBeTrue();
        profile.Versions.Count.ShouldBe(3);
        profile.CurrentVersion.ShouldBe(3);
        // The restored version copies version 1's settings (stability 0.5), not the latest (0.9).
        profile.Versions[2].Settings.Stability.ShouldBe(0.5);
        profile.Versions[2].ChangeNote.ShouldBe("Restored from version 1.");
    }

    [Fact]
    public void RestoreVersion_UnknownVersion_Fails()
    {
        var profile = NewProfile();

        var result = profile.RestoreVersion(99, "e", Now.AddMinutes(5));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("99");
        profile.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public void Reconstitute_RoundTripsState()
    {
        var original = NewProfile();
        original.ApplyEdit("p", null, "eleven_v3", Settings(stability: 0.8), "v2", "e", Now.AddMinutes(5));

        var rebuilt = VoiceProfile.Reconstitute(original.Id, original.Name, original.Description,
            original.BaseVoiceId, original.BaseVoiceName, original.Provider, original.Status,
            original.CreatedAt, original.UpdatedAt, original.CreatedBy, [.. original.AssignedStations],
            [.. original.Versions]);

        rebuilt.Id.ShouldBe(original.Id);
        rebuilt.CurrentVersion.ShouldBe(2);
        rebuilt.Versions.Count.ShouldBe(2);
    }
    [Fact]
    public void Create_PaceBoostWpm_Valid_CarriesToVersion1()
    {
        var result = VoiceProfile.Create("Punchy", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), "boosted", "admin@arn.com.au", Now, paceBoostWpm: 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Versions[0].PaceBoostWpm.ShouldBe(20);
        result.Value.CurrentVersion.ShouldBe(1);
    }

    // Bound to the constant, not a literal: the cap has moved twice (40 -> 60 when it was found to
    // sit below every calibrated voice's saturation point) and a hard-coded copy makes each move a
    // multi-file edit that silently keeps asserting the OLD contract if one site is missed.
    private static string OutOfRangeFragment => $"0 and {VoiceProfile.MaxPaceBoostWpm:0}";

    [Fact]
    public void Create_PaceBoostWpm_Negative_Fails()
    {
        var result = VoiceProfile.Create("Punchy", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), "boosted", "admin@arn.com.au", Now, paceBoostWpm: -1);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(OutOfRangeFragment);
    }

    [Fact]
    public void Create_PaceBoostWpm_AboveMax_Fails()
    {
        var result = VoiceProfile.Create("Punchy", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), "boosted", "admin@arn.com.au", Now,
            paceBoostWpm: VoiceProfile.MaxPaceBoostWpm + 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(OutOfRangeFragment);
    }

    [Fact]
    public void Create_PaceBoostWpm_ExactlyZero_Succeeds()
    {
        VoiceProfile.Create("No Boost", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), null, "admin@arn.com.au", Now, paceBoostWpm: 0)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_PaceBoostWpm_ExactlyMax_Succeeds()
    {
        var result = VoiceProfile.Create("Max Boost", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), null, "admin@arn.com.au", Now,
            paceBoostWpm: VoiceProfile.MaxPaceBoostWpm);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Versions[0].PaceBoostWpm.ShouldBe(VoiceProfile.MaxPaceBoostWpm);
    }

    [Fact]
    public void ApplyEdit_PaceBoostOnlyChange_MintsNewVersion()
    {
        // Create with paceBoostWpm=0, then edit with paceBoostWpm=15 and identical settings/model.
        var profile = NewProfile();
        profile.Versions.Count.ShouldBe(1);

        var minted = profile.ApplyEdit(profile.Name, profile.Description, "eleven_v3",
            Settings(), "Boosted pace.", "admin@arn.com.au", Now.AddMinutes(5), paceBoostWpm: 15);

        minted.Value.ShouldBeTrue();
        profile.Versions.Count.ShouldBe(2);
        profile.Versions[1].PaceBoostWpm.ShouldBe(15);
    }

    [Fact]
    public void ApplyEdit_PaceBoostOutOfRange_FailsAndLeavesUnchanged()
    {
        var profile = NewProfile();
        var versionCountBefore = profile.Versions.Count;
        var updatedAtBefore = profile.UpdatedAt;

        var minted = profile.ApplyEdit(profile.Name, profile.Description, "eleven_v3",
            Settings(), "bad boost", "admin@arn.com.au", Now.AddMinutes(5), paceBoostWpm: 999);

        minted.IsFailure.ShouldBeTrue();
        minted.Error.ShouldContain(OutOfRangeFragment);
        // Profile completely unchanged — no version added, UpdatedAt unchanged.
        profile.Versions.Count.ShouldBe(versionCountBefore);
        profile.UpdatedAt.ShouldBe(updatedAtBefore);
    }

    [Fact]
    public void ApplyEdit_PaceBoostBoundaryZero_Succeeds()
    {
        var profile = NewProfile();
        profile.ApplyEdit(profile.Name, profile.Description, "eleven_v3",
            Settings(), "reset", "admin@arn.com.au", Now.AddMinutes(5), paceBoostWpm: 0)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ApplyEdit_PaceBoostBoundaryMax_Succeeds()
    {
        var profile = NewProfile();
        var minted = profile.ApplyEdit(profile.Name, profile.Description, "eleven_v3",
            Settings(), "max boost", "admin@arn.com.au", Now.AddMinutes(5), paceBoostWpm: 40);

        minted.Value.ShouldBeTrue();
        profile.Versions[^1].PaceBoostWpm.ShouldBe(40);
    }

    [Fact]
    public void RestoreVersion_CarriesPaceBoostWpm()
    {
        // Create with paceBoostWpm=25, edit to paceBoostWpm=10, then restore v1 (which had 25).
        var profile = VoiceProfile.Create("Punchy", null, VoiceId, "ARN Hudson",
            "eleven_v3", Settings(), "boosted", "admin@arn.com.au", Now, paceBoostWpm: 25).Value;
        profile.ApplyEdit("Punchy", null, "eleven_v3", Settings(), "lowered", "admin@arn.com.au",
            Now.AddMinutes(5), paceBoostWpm: 10);

        profile.RestoreVersion(1, "admin@arn.com.au", Now.AddMinutes(10));

        // The restored version (v3) should carry v1's PaceBoostWpm (25), not v2's (10).
        profile.Versions[^1].PaceBoostWpm.ShouldBe(25);
    }

}
