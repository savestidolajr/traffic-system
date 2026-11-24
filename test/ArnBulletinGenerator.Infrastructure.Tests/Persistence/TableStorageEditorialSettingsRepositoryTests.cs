using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageEditorialSettingsRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageEditorialSettingsRepositoryTests
{
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageEditorialSettingsRepository _sut;

    public TableStorageEditorialSettingsRepositoryTests()
    {
        _sut = new TableStorageEditorialSettingsRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            Substitute.For<ILogger<TableStorageEditorialSettingsRepository>>());
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public async Task Save_Maps_AllSlotFitOverridesToEntity()
    {
        var settings = new EditorialSettings(
            EditorialScope.Market,
            "sydney",
            MaxSlowDownFactor: 1.2,
            MaxSpeedUpFactor: 1.25,
            MaxSilencePadSeconds: 1.4,
            MaxSafeOverrunTrimSeconds: 8.0);
        EditorialSettingsEntity? captured = null;
        ConfigureSave(e => captured = e);

        var save = await _sut.SaveAsync(settings);

        save.IsSuccess.ShouldBeTrue(save.Error);
        captured.ShouldNotBeNull();
        captured!.MaxSlowDownFactor.ShouldBe(1.2);
        captured.MaxSpeedUpFactor.ShouldBe(1.25);
        captured.MaxSilencePadSeconds.ShouldBe(1.4);
        captured.MaxSafeOverrunTrimSeconds.ShouldBe(8.0);
    }

    [Fact]
    public async Task Get_Maps_AllSlotFitOverridesFromEntity()
    {
        var entity = new EditorialSettingsEntity
        {
            PartitionKey = "EditorialSettings",
            RowKey = "Market:sydney",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            MaxSlowDownFactor = 1.2,
            MaxSpeedUpFactor = 1.25,
            MaxSilencePadSeconds = 1.4,
            MaxSafeOverrunTrimSeconds = 8.0
        };
        ConfigureGet(entity);

        var loaded = await _sut.GetAsync(EditorialScope.Market, "sydney");

        loaded.IsSuccess.ShouldBeTrue(loaded.Error);
        loaded.Value.ShouldNotBeNull();
        loaded.Value!.MaxSlowDownFactor.ShouldBe(1.2);
        loaded.Value.MaxSpeedUpFactor.ShouldBe(1.25);
        loaded.Value.MaxSilencePadSeconds.ShouldBe(1.4);
        loaded.Value.MaxSafeOverrunTrimSeconds.ShouldBe(8.0);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_UnsetSlotFitOverridesAsNull()
    {
        var settings = new EditorialSettings(EditorialScope.Global, string.Empty);
        EditorialSettingsEntity? captured = null;
        ConfigureSave(e => captured = e);

        var save = await _sut.SaveAsync(settings);

        save.IsSuccess.ShouldBeTrue(save.Error);
        captured.ShouldNotBeNull();
        captured!.MaxSlowDownFactor.ShouldBeNull();
        captured.MaxSpeedUpFactor.ShouldBeNull();
        captured.MaxSilencePadSeconds.ShouldBeNull();
        captured.MaxSafeOverrunTrimSeconds.ShouldBeNull();
        ConfigureGet(captured);

        var loaded = await _sut.GetAsync(EditorialScope.Global, string.Empty);

        loaded.IsSuccess.ShouldBeTrue(loaded.Error);
        loaded.Value.ShouldNotBeNull();
        loaded.Value!.MaxSlowDownFactor.ShouldBeNull();
        loaded.Value.MaxSpeedUpFactor.ShouldBeNull();
        loaded.Value.MaxSilencePadSeconds.ShouldBeNull();
        loaded.Value.MaxSafeOverrunTrimSeconds.ShouldBeNull();
    }

    /// <summary>
    ///     The trap this test exists for: FillOrder shipped with its whole wire path in place and
    ///     silently dead-ended at storage, so every PUT read back as the model default (decisions.md
    ///     2026-07-06 addendum). A denylist that does not survive the table is worse than no feature,
    ///     because the console would show a source switched off while every bulletin still fetched it.
    /// </summary>
    [Fact]
    public async Task SaveAndGet_RoundTrips_TheDisabledSourceDenylist()
    {
        var settings = new EditorialSettings(
            EditorialScope.Station,
            "kiis-90s",
            DisabledTrafficSources: [OfficialTrafficSources.TrafficSa, OfficialTrafficSources.TasAlert]);
        EditorialSettingsEntity? captured = null;
        ConfigureSave(e => captured = e);

        var save = await _sut.SaveAsync(settings);

        save.IsSuccess.ShouldBeTrue(save.Error);
        captured.ShouldNotBeNull();
        captured!.DisabledTrafficSourcesJson.ShouldNotBeNullOrWhiteSpace();
        ConfigureGet(captured);

        var loaded = await _sut.GetAsync(EditorialScope.Station, "kiis-90s");

        loaded.IsSuccess.ShouldBeTrue(loaded.Error);
        loaded.Value!.DisabledTrafficSources
            .ShouldBe([OfficialTrafficSources.TrafficSa, OfficialTrafficSources.TasAlert]);
        loaded.Value.IsTrafficSourceEnabled(OfficialTrafficSources.TrafficSa).ShouldBeFalse();
        loaded.Value.IsTrafficSourceEnabled(OfficialTrafficSources.QldTraffic).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAndGet_DistinguishesAnEmptyDenylistFromAnUnsetOne()
    {
        EditorialSettingsEntity? captured = null;
        ConfigureSave(e => captured = e);
        await _sut.SaveAsync(new EditorialSettings(
            EditorialScope.Station, "kiis-90s", DisabledTrafficSources: []));
        ConfigureGet(captured!);

        var empty = await _sut.GetAsync(EditorialScope.Station, "kiis-90s");
        empty.Value!.DisabledTrafficSources.ShouldNotBeNull().ShouldBeEmpty();

        ConfigureSave(e => captured = e);
        await _sut.SaveAsync(new EditorialSettings(EditorialScope.Station, "kiis-90s"));
        captured!.DisabledTrafficSourcesJson.ShouldBeNull();
        ConfigureGet(captured);

        var unset = await _sut.GetAsync(EditorialScope.Station, "kiis-90s");
        unset.Value!.DisabledTrafficSources.ShouldBeNull();
    }

    /// <summary>
    ///     A corrupt column must not take a station off air, and must degrade to "inherit" rather
    ///     than to "disable nothing" — those are different claims about ARN's configuration.
    /// </summary>
    [Fact]
    public async Task Get_TreatsAnUnparseableDenylistAsInherit()
    {
        ConfigureGet(new EditorialSettingsEntity
        {
            PartitionKey = "EditorialSettings",
            RowKey = "Market:sydney",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DisabledTrafficSourcesJson = "{not json"
        });

        var loaded = await _sut.GetAsync(EditorialScope.Market, "sydney");

        loaded.IsSuccess.ShouldBeTrue(loaded.Error);
        loaded.Value!.DisabledTrafficSources.ShouldBeNull();
    }


    private void ConfigureSave(Action<EditorialSettingsEntity> capture)
    {
        _tableClient.UpsertEntityAsync(
                Arg.Do<EditorialSettingsEntity>(capture),
                TableUpdateMode.Replace,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
    }

    private void ConfigureGet(EditorialSettingsEntity entity)
    {
        _tableClient.GetEntityAsync<EditorialSettingsEntity>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }
}
