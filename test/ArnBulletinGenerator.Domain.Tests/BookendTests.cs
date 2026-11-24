using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

/// <summary>Tests for the versioned bookend aggregate — one library of intro/outro pairs.</summary>
public sealed class BookendTests
{
    private static readonly DateTime Now = new(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);

    private static BookendTemplate Pair(
        string intro = "Looking over {city} today",
        string outro = "That's it for {city} traffic.") => new(intro, outro);

    [Fact]
    public void Create_MintsVersionOne_WithBkPrefixedId()
    {
        var bookend = Bookend.Create("Melbourne Drive", "Warm open", Pair(), "initial", "operator", Now);
        bookend.IsSuccess.ShouldBeTrue();
        bookend.Value.Id.ShouldStartWith("bk-");
        bookend.Value.CurrentVersion.ShouldBe(1);
        bookend.Value.CurrentTemplate.ConfigurationKey.ShouldBe(Pair().ConfigurationKey);
        bookend.Value.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public void Create_RequiresName()
    {
        Bookend.Create("  ", null, Pair(), null, null, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_RejectsPairWithBothLinesBlank()
    {
        Bookend.Create("Open", null, new BookendTemplate("   ", ""), null, null, Now)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_AcceptsIntroOnly()
    {
        var bookend = Bookend.Create("Open", null, new BookendTemplate("Looking over {city} today", ""),
            null, null, Now);
        bookend.IsSuccess.ShouldBeTrue();
        bookend.Value.CurrentTemplate.HasIntro.ShouldBeTrue();
        bookend.Value.CurrentTemplate.HasOutro.ShouldBeFalse();
    }

    [Fact]
    public void Create_AcceptsOutroOnly()
    {
        var bookend = Bookend.Create("Close", null, new BookendTemplate("", "That's it for {city} traffic."),
            null, null, Now);
        bookend.IsSuccess.ShouldBeTrue();
        bookend.Value.CurrentTemplate.HasIntro.ShouldBeFalse();
        bookend.Value.CurrentTemplate.HasOutro.ShouldBeTrue();
    }

    [Fact]
    public void Create_RejectsOversizedIntro()
    {
        var tooLong = new string('x', BookendTemplate.MaxTextLength + 1);
        var result = Bookend.Create("Open", null, new BookendTemplate(tooLong, "Outro"), null, null, Now);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain($"at most {BookendTemplate.MaxTextLength}");
    }

    [Fact]
    public void Create_RejectsOversizedOutro()
    {
        var tooLong = new string('x', BookendTemplate.MaxTextLength + 1);
        var result = Bookend.Create("Open", null, new BookendTemplate("Intro", tooLong), null, null, Now);
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("outro must be at most");
    }

    [Fact]
    public void Create_TrimsNameAndBothLines()
    {
        var bookend = Bookend.Create("  Open  ", null,
            Pair("  Looking over {city} today  ", "  That's it for {city} traffic.  "),
            null, null, Now).Value;

        bookend.Name.ShouldBe("Open");
        bookend.CurrentTemplate.IntroText.ShouldBe("Looking over {city} today");
        bookend.CurrentTemplate.OutroText.ShouldBe("That's it for {city} traffic.");
    }

    [Fact]
    public void ApplyEdit_MintsNewVersion_OnlyWhenALineChanges()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;

        // Same lines, just a name change → no new version.
        var minted = bookend.ApplyEdit("Melbourne Drive AM", null, Pair(), "rename", "ed", Now.AddMinutes(1));
        minted.ShouldBeFalse();
        bookend.CurrentVersion.ShouldBe(1);
        bookend.Name.ShouldBe("Melbourne Drive AM");

        // Outro change → new version.
        var minted2 = bookend.ApplyEdit("Melbourne Drive AM", null,
            Pair(outro: "That's all from {city} traffic."), "new closer", "ed", Now.AddMinutes(2));
        minted2.ShouldBeTrue();
        bookend.CurrentVersion.ShouldBe(2);
        bookend.CurrentTemplate.OutroText.ShouldBe("That's all from {city} traffic.");

        // Intro change → new version too.
        var minted3 = bookend.ApplyEdit("Melbourne Drive AM", null,
            Pair("Looking over the {city} region today"), "new opener", "ed", Now.AddMinutes(3));
        minted3.ShouldBeTrue();
        bookend.CurrentVersion.ShouldBe(3);
        bookend.CurrentTemplate.IntroText.ShouldBe("Looking over the {city} region today");
    }

    [Fact]
    public void ApplyEdit_WhitespaceOnlyLineChange_DoesNotMint()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;

        var minted = bookend.ApplyEdit("Melbourne Drive", null,
            Pair("  Looking over {city} today  ", "That's it for {city} traffic."),
            null, null, Now.AddMinutes(1));

        minted.ShouldBeFalse();
        bookend.CurrentVersion.ShouldBe(1);
        bookend.CurrentTemplate.IntroText.ShouldBe("Looking over {city} today");
    }

    [Fact]
    public void RestoreVersion_MintsCopyOfPriorTemplate()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;
        bookend.ApplyEdit("Melbourne Drive", null, Pair("Looking over the {city} region today"),
            null, null, Now.AddMinutes(1));

        var restored = bookend.RestoreVersion(1, "ed", Now.AddMinutes(2));

        restored.IsSuccess.ShouldBeTrue();
        bookend.CurrentVersion.ShouldBe(3);
        bookend.CurrentTemplate.IntroText.ShouldBe("Looking over {city} today");
        bookend.CurrentTemplate.OutroText.ShouldBe("That's it for {city} traffic.");
    }

    [Fact]
    public void RestoreVersion_FailsForUnknownVersion()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;

        var restored = bookend.RestoreVersion(99, "ed", Now.AddMinutes(1));

        restored.IsFailure.ShouldBeTrue();
        restored.Error.ShouldContain("version 99");
    }

    [Fact]
    public void Archive_SetsStatusAndBumpsUpdatedAt_WithoutMintingVersion()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;
        var initialVersion = bookend.CurrentVersion;
        var versionsCount = bookend.Versions.Count;

        bookend.Archive(Now.AddMinutes(1));

        bookend.Status.ShouldBe("archived");
        bookend.CurrentVersion.ShouldBe(initialVersion);
        bookend.Versions.Count.ShouldBe(versionsCount);
        bookend.UpdatedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Unarchive_SetsStatusAndBumpsUpdatedAt_WithoutMintingVersion()
    {
        var bookend = Bookend.Create("Melbourne Drive", null, Pair(), null, null, Now).Value;
        bookend.Archive(Now.AddMinutes(1));
        var initialVersion = bookend.CurrentVersion;
        var versionsCount = bookend.Versions.Count;

        bookend.Unarchive(Now.AddMinutes(2));

        bookend.Status.ShouldBe("active");
        bookend.CurrentVersion.ShouldBe(initialVersion);
        bookend.Versions.Count.ShouldBe(versionsCount);
        bookend.UpdatedAt.ShouldBe(Now.AddMinutes(2));
    }
}
