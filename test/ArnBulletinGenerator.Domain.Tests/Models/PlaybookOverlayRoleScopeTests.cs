using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     One station playbook configures several agents, so each edit says which agent it is for by
///     qualifying its block id. The qualifier lives in the KEY rather than in a field beside the
///     overlay because the field version was lost by four separate hand-copied conversions in one
///     afternoon, each presenting identically: the edit applied to the wrong prompt and returned as
///     a refusal, so the station saw a saved playbook that changed nothing.
/// </summary>
public sealed class PlaybookOverlayRoleScopeTests
{
    private static PlaybookOverlay Overlay() => new()
    {
        Rewrite = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["writeIncidents/voice.roadworks"] = "call it cone country",
            ["writeFlow/voice.congestion"] = "call it chockers",
            ["compose2/voice.variety"] = "vary it more"
        },
        Disable = ["writeFlow/voice.clear-runs", "compose2/length.ladder"],
        Reorder = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["writeIncidents/voice.crash"] = 0
        },
        Add = [new PlaybookBlock("writeFlow/station.voice.1", PlaybookSection.Voice, "extra", 9, false)]
    };

    [Fact]
    public void EachRoleSeesOnlyItsOwnEdits()
    {
        PlaybookOverlay incidents = Overlay().ForRole("writeIncidents");

        incidents.Rewrite.Keys.ShouldBe(["voice.roadworks"]);
        incidents.Reorder.Keys.ShouldBe(["voice.crash"]);
        incidents.Disable.ShouldBeEmpty();
        incidents.Add.ShouldBeEmpty();
    }

    [Fact]
    public void TheQualifierIsStrippedSoBlockIdsStillMatchTheShippedPrompt()
    {
        // Apply() matches on the prompt's own ids, so a qualifier left in place would refuse
        // every edit — the same silent outcome as losing the role entirely.
        PlaybookOverlay flow = Overlay().ForRole("writeFlow");

        flow.Rewrite.ShouldContainKey("voice.congestion");
        flow.Disable.ShouldBe(["voice.clear-runs"]);
        flow.Add.ShouldHaveSingleItem().Id.ShouldBe("station.voice.1");
    }

    [Fact]
    public void OneRolesEditsNeverLeakIntoAnother()
    {
        // The failure this whole design exists to prevent: a desk edit landing on the composer,
        // whose prompt has none of those block ids.
        PlaybookOverlay composer = Overlay().ForRole("compose2");

        composer.Rewrite.Keys.ShouldBe(["voice.variety"]);
        composer.Rewrite.ShouldNotContainKey("voice.roadworks");
        composer.Disable.ShouldBe(["length.ladder"]);
    }

    [Fact]
    public void UnqualifiedKeysStillApply_SoPlaybooksWrittenBeforeQualificationKeepWorking()
    {
        // Their block ids exist in exactly one prompt, so offering them to every role costs a
        // no-op refusal elsewhere rather than a misapplied edit.
        var legacy = new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["voice.variety"] = "legacy edit"
            }
        };

        legacy.ForRole("compose2").Rewrite.ShouldContainKey("voice.variety");
        legacy.ForRole("writeFlow").Rewrite.ShouldContainKey("voice.variety");
    }

    [Fact]
    public void AnEmptyOverlayNarrowsToNothingForEveryRole()
    {
        foreach (string role in BulletinPlaybook.SupportedRoles)
            PlaybookOverlay.None.ForRole(role).IsEmpty.ShouldBeTrue(role);
    }
}
