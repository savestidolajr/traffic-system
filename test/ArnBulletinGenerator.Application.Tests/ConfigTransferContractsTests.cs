using Shouldly;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Covers the config-bundle wire→domain conversions in <c>ConfigTransferContracts.cs</c>.
/// </summary>
public sealed class ConfigTransferContractsTests
{
    [Theory]
    [InlineData(99999, 3)]
    [InlineData(-50, 0)]
    [InlineData(2, 2)]
    public void DeliveryStylePolicyDto_ToDomain_ClampsIntensityToValidRange(int intensity, int expected)
    {
        var dto = new DeliveryStylePolicyDto("Calm read.", ["[calm]"], intensity);

        var policy = dto.ToDomain();

        policy.Intensity.ShouldBe(expected);
    }

    // ── Approve-gate plan equivalence (bug-bash 2026-08-05 F2) ──────

    private static ConfigImportItemDto Item(string section, string key, string action) =>
        new(section, key, key, action);

    private static ConfigImportReportDto Report(params ConfigImportItemDto[] items) => new(
        DryRun: false, RequiresApproval: true, Applied: false,
        Creates: items.Count(i => i.Action == ConfigImportActions.Create),
        Updates: items.Count(i => i.Action == ConfigImportActions.Update),
        Unchanged: items.Count(i => i.Action == ConfigImportActions.Unchanged),
        Errors: items.Count(i => i.Action == ConfigImportActions.Error),
        items);

    [Fact]
    public void Report_IdenticalItemsAndCounts_AreEquivalent()
    {
        var a = Report(Item(ConfigSections.Schedules, "sydney", ConfigImportActions.Create));
        var b = Report(Item(ConfigSections.Schedules, "sydney", ConfigImportActions.Create));

        a.IsEquivalentTo(b).ShouldBeTrue();
        a.DescribeDrift(b).ShouldBeEmpty();
    }

    [Fact]
    public void Report_CreateVsUpdate_IsNotEquivalentAndNamesTheDrift()
    {
        var stored = Report(Item(ConfigSections.Schedules, "zz-x", ConfigImportActions.Create));
        var fresh = Report(Item(ConfigSections.Schedules, "zz-x", ConfigImportActions.Update));

        stored.IsEquivalentTo(fresh).ShouldBeFalse();
        stored.DescribeDrift(fresh).ShouldContain("Schedules zz-x was Create, now Update");
    }

    [Fact]
    public void Report_ItemAddedOrRemoved_IsNotEquivalentAndNamesIt()
    {
        var stored = Report(Item(ConfigSections.Schedules, "a", ConfigImportActions.Create));
        var fresh = Report(
            Item(ConfigSections.Schedules, "a", ConfigImportActions.Create),
            Item(ConfigSections.Schedules, "b", ConfigImportActions.Create));

        stored.IsEquivalentTo(fresh).ShouldBeFalse();
        stored.DescribeDrift(fresh).ShouldContain("Schedules b was absent, now Create");

        var removed = Report(
            Item(ConfigSections.Schedules, "a", ConfigImportActions.Create),
            Item(ConfigSections.Schedules, "b", ConfigImportActions.Update));
        removed.DescribeDrift(Report(Item(ConfigSections.Schedules, "a", ConfigImportActions.Create)))
            .ShouldContain("Schedules b was Update, now absent");
    }

    [Fact]
    public void Report_DuplicatedItemRows_DifferByMultisetNotByOrder()
    {
        // Two identical (section, key, action) rows in one report vs one in the other is a real
        // shape change; reordering the same multiset is not.
        var duplicated = Report(
            Item(ConfigSections.Guidance, "g-1", ConfigImportActions.Create),
            Item(ConfigSections.Guidance, "g-1", ConfigImportActions.Create));
        var single = Report(Item(ConfigSections.Guidance, "g-1", ConfigImportActions.Create));

        duplicated.IsEquivalentTo(single).ShouldBeFalse();

        var reorderedA = Report(
            Item(ConfigSections.Schedules, "a", ConfigImportActions.Create),
            Item(ConfigSections.Schedules, "b", ConfigImportActions.Update));
        var reorderedB = Report(
            Item(ConfigSections.Schedules, "b", ConfigImportActions.Update),
            Item(ConfigSections.Schedules, "a", ConfigImportActions.Create));
        reorderedA.IsEquivalentTo(reorderedB).ShouldBeTrue();
    }

    [Fact]
    public void Report_SameItemsButDifferingCounters_IsNotEquivalent()
    {
        // Counters are normally a pure tally of item actions, but the equivalence contract is
        // counts AND items — a report assembled with wrong counters must still be caught.
        var a = Report(Item(ConfigSections.Schedules, "a", ConfigImportActions.Create));
        var b = a with { Creates = 2 };

        a.IsEquivalentTo(b).ShouldBeFalse();
        a.DescribeDrift(b).ShouldContain("creates 1 → 2");
    }
}
