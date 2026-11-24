using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Migration safety for the stored verdict column. Omission is the only verdict, and the only
///     thing a verdict does is keep an event off air — so a row this cannot positively recognise as
///     an omission must read as "no verdict", never as one.
/// </summary>
public sealed class TableStorageEventVerdictRepositoryTests
{
    [Theory]
    [InlineData("Omitted")]
    [InlineData("omitted")]
    // Trial rows wrote "Killed" before the verdict was renamed.
    [InlineData("Killed")]
    [InlineData("KILLED")]
    public void ParseKind_RecognisesOmissionAndItsLegacySpelling(string stored)
    {
        TableStorageEventVerdictRepository.ParseKind(stored).ShouldBe(VerdictKind.Omitted);
    }

    [Theory]
    // Written by a trust ladder that no longer exists: these vouched for an event, so treating
    // either as a verdict would silently pull a road OFF air that an operator never omitted.
    [InlineData("Confirmed")]
    [InlineData("Flagged")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    public void ParseKind_TreatsEverythingElseAsNoVerdict(string? stored)
    {
        TableStorageEventVerdictRepository.ParseKind(stored).ShouldBeNull();
    }
}
