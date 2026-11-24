using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     The numeric half of ARN's editorial control. The per-field overlay is the part worth pinning:
///     whole-record precedence would mean a station tuning one cap silently discarded every other
///     choice its market had made.
/// </summary>
public class EditorialSettingsTests
{
    private static EditorialSettings At(
        EditorialScope scope, string scopeId, int? maxCorridors = null, int? driveTime = null,
        int? majorTierCap = null) =>
        new(scope, scopeId, DriveTimeHeadlineThresholdMinutes: driveTime, MaxCorridors: maxCorridors,
            MajorTierCap: majorTierCap);

    [Fact]
    public void Nothing_configured_leaves_every_field_unset_so_consumers_keep_their_defaults()
    {
        var resolved = EditorialSettings.Resolve([], "sydney", Option<string>.None);

        resolved.MaxCorridors.ShouldBeNull();
        resolved.DriveTimeHeadlineThresholdMinutes.ShouldBeNull();
        resolved.MajorTierCap.ShouldBeNull();
    }

    [Fact]
    public void A_narrower_scope_wins_per_field_and_inherits_the_rest()
    {
        var stored = new[]
        {
            At(EditorialScope.Global, string.Empty, maxCorridors: 5, driveTime: 5),
            At(EditorialScope.Market, "sydney", maxCorridors: 8)
        };

        var resolved = EditorialSettings.Resolve(stored, "sydney", Option<string>.None);

        resolved.MaxCorridors.ShouldBe(8);           // market's own choice
        resolved.DriveTimeHeadlineThresholdMinutes.ShouldBe(5); // inherited, not discarded
    }

    [Fact]
    public void Station_overlays_market_which_overlays_global()
    {
        var stored = new[]
        {
            At(EditorialScope.Global, string.Empty, maxCorridors: 5, driveTime: 5, majorTierCap: 4),
            At(EditorialScope.Market, "sydney", maxCorridors: 8, driveTime: 8),
            At(EditorialScope.Station, "kiis-90s", maxCorridors: 10)
        };

        var resolved = EditorialSettings.Resolve(stored, "sydney", Option<string>.Some("kiis-90s"));

        resolved.MaxCorridors.ShouldBe(10);                     // station
        resolved.DriveTimeHeadlineThresholdMinutes.ShouldBe(8); // market
        resolved.MajorTierCap.ShouldBe(4);                      // global
    }

    [Fact]
    public void Another_markets_row_never_applies()
    {
        var resolved = EditorialSettings.Resolve(
            [At(EditorialScope.Market, "melbourne", maxCorridors: 12)], "sydney", Option<string>.None);

        resolved.MaxCorridors.ShouldBeNull();
    }

    [Fact]
    public void A_station_row_is_ignored_for_a_market_only_broadcast()
    {
        var resolved = EditorialSettings.Resolve(
            [At(EditorialScope.Station, "kiis-90s", maxCorridors: 12)], "sydney", Option<string>.None);

        resolved.MaxCorridors.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26)]
    public void An_out_of_range_corridor_cap_is_rejected(int cap)
    {
        // A zero or negative cap would hand the writer an empty board and read as "no traffic today"
        // rather than as an error, so this has to fail at the edge instead of at air.
        At(EditorialScope.Market, "sydney", maxCorridors: cap).Validate().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_sane_corridor_cap_is_accepted()
    {
        At(EditorialScope.Market, "sydney", maxCorridors: 8).Validate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Continuity_caps_may_be_zero_because_silencing_a_category_is_a_real_choice()
    {
        // Unlike the corridor cap, zero here means "never voice cleared items", which is a legitimate
        // editorial position rather than an empty bulletin.
        new EditorialSettings(EditorialScope.Market, "sydney", ContinuityClearedCap: 0)
            .Validate().IsSuccess.ShouldBeTrue();
    }

    private static EditorialSettings WithSources(
        EditorialScope scope, string scopeId, params string[]? disabled) =>
        new(scope, scopeId, DisabledTrafficSources: disabled);

    [Fact]
    public void Every_source_is_consulted_when_nothing_is_disabled()
    {
        var unset = At(EditorialScope.Market, "sydney");

        foreach (string source in OfficialTrafficSources.All)
            unset.IsTrafficSourceEnabled(source).ShouldBeTrue(source);
    }

    [Fact]
    public void A_disabled_source_is_not_consulted_and_its_siblings_still_are()
    {
        var settings = WithSources(EditorialScope.Station, "kiis-90s", OfficialTrafficSources.TrafficSa);

        settings.IsTrafficSourceEnabled(OfficialTrafficSources.TrafficSa).ShouldBeFalse();
        settings.IsTrafficSourceEnabled(OfficialTrafficSources.LiveTrafficNsw).ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_source_id_is_rejected_at_save_time()
    {
        var result = WithSources(EditorialScope.Market, "adelaide", "traffic-sa").Validate();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("traffic-sa");
    }

    [Fact]
    public void Known_source_ids_are_accepted()
    {
        WithSources(EditorialScope.Market, "sydney",
                OfficialTrafficSources.TrafficSa, OfficialTrafficSources.TasAlert)
            .Validate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_station_that_sets_no_denylist_inherits_its_markets()
    {
        var stored = new[]
        {
            WithSources(EditorialScope.Market, "sydney", OfficialTrafficSources.TrafficSa),
            At(EditorialScope.Station, "kiis-90s", maxCorridors: 10)
        };

        var resolved = EditorialSettings.Resolve(stored, "sydney", Option<string>.Some("kiis-90s"));

        resolved.IsTrafficSourceEnabled(OfficialTrafficSources.TrafficSa).ShouldBeFalse();
    }

    /// <summary>
    ///     The distinction the nullable denylist exists for: an EMPTY list is a deliberate "consult
    ///     everything" that must override a market's denylist, where null means "inherit it". Without
    ///     this a station could never opt back IN to a source its market had switched off.
    /// </summary>
    [Fact]
    public void An_empty_station_denylist_overrides_the_markets_rather_than_inheriting_it()
    {
        var stored = new[]
        {
            WithSources(EditorialScope.Market, "sydney", OfficialTrafficSources.TrafficSa),
            WithSources(EditorialScope.Station, "kiis-90s", [])
        };

        var resolved = EditorialSettings.Resolve(stored, "sydney", Option<string>.Some("kiis-90s"));

        resolved.IsTrafficSourceEnabled(OfficialTrafficSources.TrafficSa).ShouldBeTrue();
    }

    [Fact]
    public void Normalizing_dedupes_lowercases_and_orders_by_the_canonical_registry()
    {
        var settings = WithSources(EditorialScope.Global, string.Empty,
            "TASALERT", "trafficsa", " TrafficSA ", "tasalert");

        settings.NormalizeDisabledTrafficSources()
            .ShouldBe([OfficialTrafficSources.TrafficSa, OfficialTrafficSources.TasAlert]);
    }

    [Fact]
    public void Normalizing_keeps_null_null_because_inherit_is_not_the_same_as_empty()
    {
        At(EditorialScope.Market, "sydney").NormalizeDisabledTrafficSources().ShouldBeNull();
        WithSources(EditorialScope.Market, "sydney", []).NormalizeDisabledTrafficSources()
            .ShouldNotBeNull().ShouldBeEmpty();
    }
}
