using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     WS1 content selection: schedule-level persistence semantics and DTO mapping.
/// </summary>
public class ScheduleContentSelectionTests
{
    private readonly ScheduleService _sut = new(
        Substitute.For<IScheduleRepository>(),
        Substitute.For<ICronParser>(),
        TimeProvider.System,
        Substitute.For<ILogger<ScheduleService>>());

    private static CreateScheduleCommand CreateCommand(Option<BulletinContentSelection> content = default)
    {
        return new CreateScheduleCommand(
            "gold-coast", "Hot Tomato", "Hot Tomato", "Australia/Brisbane", "*/15 * * * *",
            true, CoverageArea.Create(-28.0, 153.4, 25).Value, "voice-1", "gold-coast/traffic/",
            30, ContentSelection: content);
    }

    [Fact]
    public void CreateSchedule_CarriesContentSelection()
    {
        var selection = new BulletinContentSelection(Traffic: true, Weather: true, Fuel: false);

        var result = _sut.CreateSchedule(CreateCommand(Option<BulletinContentSelection>.Some(selection)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ContentSelection.HasValue.ShouldBeTrue();
        result.Value.ContentSelection.Value.Weather.ShouldBeTrue();
        result.Value.ContentSelection.Value.Fuel.ShouldBeFalse();
    }

    [Fact]
    public void UpdateSchedule_WithoutContent_PreservesExistingSelection()
    {
        var existing = _sut.CreateSchedule(CreateCommand(
            Option<BulletinContentSelection>.Some(new BulletinContentSelection(true, true, true)))).Value;

        var updated = _sut.UpdateSchedule(existing, new UpdateScheduleCommand("gold-coast",
            Name: Option<string>.Some("Renamed"))).Value;

        updated.ContentSelection.HasValue.ShouldBeTrue();
        updated.ContentSelection.Value.Weather.ShouldBeTrue();
        updated.ContentSelection.Value.Fuel.ShouldBeTrue();
    }

    [Fact]
    public void UpdateSchedule_WithContent_OverridesSelection()
    {
        var existing = _sut.CreateSchedule(CreateCommand()).Value;

        var updated = _sut.UpdateSchedule(existing, new UpdateScheduleCommand("gold-coast",
            ContentSelection: Option<BulletinContentSelection>.Some(
                new BulletinContentSelection(Traffic: false, Weather: true, Fuel: false)))).Value;

        updated.ContentSelection.HasValue.ShouldBeTrue();
        updated.ContentSelection.Value.Traffic.ShouldBeFalse();
        updated.ContentSelection.Value.Weather.ShouldBeTrue();
    }

    [Fact]
    public void ContentSelectionDto_RoundTrip_DefaultsApply()
    {
        // Nulls fall back to traffic-only defaults
        new ContentSelectionDto().ToSelection().ShouldBe(BulletinContentSelection.Default);
        new ContentSelectionDto(Weather: true).ToSelection()
            .ShouldBe(new BulletinContentSelection(Traffic: true, Weather: true, Fuel: false));

        // Domain → DTO → Domain is lossless
        var selection = new BulletinContentSelection(false, true, true);
        selection.ToDto().ToSelection().ShouldBe(selection);
    }

    [Fact]
    public void ToSelection_DefaultsBothRequiredFlagsFalse()
    {
        var selection = new ContentSelectionDto(Traffic: true, Weather: true, Fuel: true).ToSelection();

        selection.WeatherRequired.ShouldBeFalse();
        selection.FuelRequired.ShouldBeFalse();
    }

    [Fact]
    public void ContentSelectionDto_RoundTrip_CarriesRequiredFlagsLosslessly()
    {
        // Domain → DTO keeps the raw flags (not the computed RequiresX), so an export/import
        // round-trip preserves the operator's fixture choice exactly.
        var selection = new BulletinContentSelection(Traffic: true, Weather: true, Fuel: true,
            WeatherRequired: true, FuelRequired: true);

        var dto = selection.ToDto();

        dto.WeatherRequired.ShouldBe(true);
        dto.FuelRequired.ShouldBe(true);
        dto.ToSelection().ShouldBe(selection);

        // Mapping is lossless even for the invalid required-without-selected combo; the domain
        // record neutralises it in RequiresWeather so it can never gate an aired section.
        new ContentSelectionDto(Traffic: true, Weather: false, WeatherRequired: true)
            .ToSelection().RequiresWeather.ShouldBeFalse();
    }

    [Fact]
    public void AllSourcesDisabled_HasNoSource()
    {
        new BulletinContentSelection(false, false, false).HasAnySource.ShouldBeFalse();
        BulletinContentSelection.Default.HasAnySource.ShouldBeTrue();
    }
}
