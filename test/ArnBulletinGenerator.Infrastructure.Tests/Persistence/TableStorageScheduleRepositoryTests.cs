using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageScheduleRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageScheduleRepositoryTests
{
    private readonly ICronParser _cronParser = Substitute.For<ICronParser>();

    private readonly ILogger<TableStorageScheduleRepository> _logger =
        Substitute.For<ILogger<TableStorageScheduleRepository>>();

    private readonly TableStorageScheduleRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageScheduleRepositoryTests()
    {
        // The REAL resolver, not a cron-only stand-in: a repository handed a
        // calendar-blind resolver computes NextRunUtc straight through a suppressed
        // date, which is the one thing the calendar exists to prevent.
        _sut = new TableStorageScheduleRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            new ScheduleOccurrenceResolver(_cronParser, _logger),
            TimeProvider.System,
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ScheduleEntity CreateEntity(string marketId = "syd", string name = "Sydney",
        bool enabled = true, DateTimeOffset? nextRunUtc = null, string? currentJobId = null)
    {
        return new ScheduleEntity
        {
            PartitionKey = "Schedule",
            RowKey = marketId,
            Name = name,
            Station = "2GB",
            Timezone = "Australia/Sydney",
            CronExpression = "0 8 * * 1-5",
            Enabled = enabled,
            VoiceProfileId = "voice-1",
            OutputPath = "/output",
            TargetDurationSeconds = 90,
            NotifyBeforePlayoutMinutes = 15,
            EscalationBeforePlayoutMinutes = 5,
            NextRunUtc = nextRunUtc,
            CurrentJobId = currentJobId,
            CoverageJson = "{\"Latitude\":-33.87,\"Longitude\":151.21,\"RadiusKm\":30}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SeedQuery(params ScheduleEntity[] entities)
    {
        _tableClient.QueryAsync<ScheduleEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    private void SeedGet(ScheduleEntity entity)
    {
        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string rowKey)
    {
        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── ListSchedulesAsync ───────────────────────────────────────────

    [Fact]
    public async Task ListSchedulesAsync_ReturnsSchedules_OrderedByName()
    {
        var mel = CreateEntity("mel", "Melbourne");
        var syd = CreateEntity();
        SeedQuery(syd, mel); // unordered

        var result = await _sut.ListSchedulesAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.First().Name.ShouldBe("Melbourne");
        result.Value.Last().Name.ShouldBe("Sydney");
    }

    [Fact]
    public async Task ListSchedulesAsync_StorageError_ReturnsFailure()
    {
        _tableClient.QueryAsync<ScheduleEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(500, "Boom"));

        var result = await _sut.ListSchedulesAsync();

        result.IsFailure.ShouldBeTrue();
    }

    // ── GetScheduleAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleAsync_Existing_ReturnsSuccess()
    {
        var entity = CreateEntity();
        SeedGet(entity);

        var result = await _sut.GetScheduleAsync("syd");

        result.IsSuccess.ShouldBeTrue();
        result.Value.MarketId.ShouldBe("syd");
        result.Value.Station.ShouldBe("2GB");
    }

    [Fact]
    public async Task GetScheduleAsync_NonExistent_ReturnsFailure()
    {
        SeedGetNotFound("missing");

        var result = await _sut.GetScheduleAsync("missing");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    // ── SaveScheduleAsync ────────────────────────────────────────────

    [Fact]
    public async Task SaveScheduleAsync_EnabledWithoutNextRun_CalculatesNextRun()
    {
        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("save-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var nextRun = Option<DateTime>.Some(DateTime.UtcNow.AddHours(1));
        _cronParser.CalculateNextRun(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(nextRun);

        var schedule = MarketSchedule.Create("save-1", "Test", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", true, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;

        var result = await _sut.SaveScheduleAsync(schedule);

        result.IsSuccess.ShouldBeTrue();
        schedule.NextRunUtc.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveScheduleAsync_DisabledSchedule_DoesNotCalculateNextRun()
    {
        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("save-2"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var schedule = MarketSchedule.Create("save-2", "Test", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", false, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;

        var result = await _sut.SaveScheduleAsync(schedule);

        result.IsSuccess.ShouldBeTrue();
        _cronParser.DidNotReceive().CalculateNextRun(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    // ── DeleteScheduleAsync ──────────────────────────────────────────

    [Fact]
    public async Task DeleteScheduleAsync_ReturnsSuccess()
    {
        var result = await _sut.DeleteScheduleAsync("del-1");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).DeleteEntityAsync(
            "Schedule", "del-1", Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    // ── TryStartJobAsync ─────────────────────────────────────────────

    [Fact]
    public async Task TryStartJobAsync_NoCurrentJob_StartsJob()
    {
        var entity = CreateEntity("start-1", currentJobId: null);
        SeedGet(entity);

        var result = await _sut.TryStartJobAsync("start-1", "job-99");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<ScheduleEntity>(), Arg.Any<ETag>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryStartJobAsync_AlreadyRunning_ReturnsFailure()
    {
        var entity = CreateEntity("busy-1", currentJobId: "existing-job");
        SeedGet(entity);

        var result = await _sut.TryStartJobAsync("busy-1", "new-job");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("already running");
    }

    [Fact]
    public async Task TryStartJobAsync_NotFound_ReturnsFailure()
    {
        SeedGetNotFound("gone");

        var result = await _sut.TryStartJobAsync("gone", "job-1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task TryStartJobAsync_ETagConflict_ReturnsFailure()
    {
        var entity = CreateEntity("conflict-1");
        SeedGet(entity);

        _tableClient.UpdateEntityAsync(
                Arg.Any<ScheduleEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"));

        var result = await _sut.TryStartJobAsync("conflict-1", "job-1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Concurrent modification");
    }

    // ── CompleteJobAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CompleteJobAsync_Success_ClearsJobAndSchedulesNext()
    {
        var entity = CreateEntity("comp-1", currentJobId: "running");
        SeedGet(entity);

        var nextRun = Option<DateTime>.Some(DateTime.UtcNow.AddHours(1));

        var result = await _sut.CompleteJobAsync("comp-1", nextRun);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<ScheduleEntity>(e => e.CurrentJobId == null),
            Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteJobAsync_NotFound_ReturnsFailure()
    {
        SeedGetNotFound("nope");

        var result = await _sut.CompleteJobAsync("nope", Option<DateTime>.None);

        result.IsFailure.ShouldBeTrue();
    }

    // ── GetDueSchedulesAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_FiltersCorrectly()
    {
        var due = CreateEntity("due-1", enabled: true, nextRunUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        var notDue = CreateEntity("notdue-1", enabled: true, nextRunUtc: DateTimeOffset.UtcNow.AddHours(1));
        var running = CreateEntity("running-1", enabled: true,
            nextRunUtc: DateTimeOffset.UtcNow.AddMinutes(-5), currentJobId: "j-1");
        SeedQuery(due, notDue, running);

        var result = await _sut.GetDueSchedulesAsync(DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(1);
        result.Value.First().MarketId.ShouldBe("due-1");
    }

    // ── Mapping roundtrip ────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGet_RoundTrips_AllFields()
    {
        var schedule = MarketSchedule.Create("rt-1", "Roundtrip", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", true, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        schedule.NextRunUtc = DateTime.UtcNow.AddHours(1);

        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("rt-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        ScheduleEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<ScheduleEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveScheduleAsync(schedule);

        captured.ShouldNotBeNull();
        SeedGet(captured!);

        var loaded = (await _sut.GetScheduleAsync("rt-1")).Value;
        loaded.Name.ShouldBe("Roundtrip");
        loaded.VoiceProfileId.ShouldBe("voice-1");
        loaded.Coverage.Latitude.ShouldBe(-33.87);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_VoiceRotationAndCursor()
    {
        var schedule = MarketSchedule.Create("rt-2", "Rotation", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", true, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["vp-a", "vp-b"], null).Value);
        schedule.VoiceRotationCursor = 3;

        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("rt-2"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        ScheduleEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<ScheduleEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveScheduleAsync(schedule);

        captured.ShouldNotBeNull();
        SeedGet(captured!);

        var loaded = (await _sut.GetScheduleAsync("rt-2")).Value;
        loaded.VoiceRotationCursor.ShouldBe(3);
        loaded.VoiceRotation.HasValue.ShouldBeTrue();
        loaded.VoiceRotation.Value.Mode.ShouldBe(VoiceSelectionMode.RoundRobin);
        loaded.VoiceRotation.Value.VoiceProfileIds.ShouldBe(["vp-a", "vp-b"]);
    }

    [Fact]
    public async Task GetSchedule_LegacyEntityWithoutRotation_ReconstitutesAsSingle()
    {
        SeedGet(CreateEntity("legacy-1"));

        var loaded = (await _sut.GetScheduleAsync("legacy-1")).Value;

        loaded.VoiceRotation.HasValue.ShouldBeFalse();
        loaded.VoiceRotationCursor.ShouldBe(0);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_FillOrder()
    {
        var schedule = MarketSchedule.Create("rt-3", "FillOrder", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", true, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        // Deliberately not the model default ([Weather, Fuel, Filler]); order must survive.
        schedule.FillOrder = [FillSource.Fuel, FillSource.Weather];

        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("rt-3"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        ScheduleEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<ScheduleEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveScheduleAsync(schedule);

        captured.ShouldNotBeNull();
        captured!.FillOrderJson.ShouldBe("[\"fuel\",\"weather\"]");
        SeedGet(captured!);

        var loaded = (await _sut.GetScheduleAsync("rt-3")).Value;
        loaded.FillOrder.ShouldBe([FillSource.Fuel, FillSource.Weather]);
    }

    [Fact]
    public async Task GetSchedule_LegacyEntityWithoutFillOrder_UsesModelDefault()
    {
        SeedGet(CreateEntity("legacy-2")); // CreateEntity never sets FillOrderJson → null column

        var loaded = (await _sut.GetScheduleAsync("legacy-2")).Value;

        loaded.FillOrder.ShouldBe([FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);
    }

    [Fact]
    public async Task GetSchedule_CorruptFillOrderJson_DropsUnknownAndDuplicates()
    {
        var entity = CreateEntity("corrupt-1");
        entity.FillOrderJson = "[\"fuel\",\"petrol\",\"fuel\",\"WEATHER \"]";
        SeedGet(entity);

        var loaded = (await _sut.GetScheduleAsync("corrupt-1")).Value;

        // "petrol" unknown → dropped; duplicate "fuel" → dropped; "WEATHER " → trimmed + case-folded.
        loaded.FillOrder.ShouldBe([FillSource.Fuel, FillSource.Weather]);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_Bookends()
    {
        var schedule = MarketSchedule.Create("rt-4", "Bookends", "2GB", "Australia/Sydney",
            "0 8 * * 1-5", true, CoverageArea.Create(-33.87, 151.21, 30).Value, "voice-1",
            "/out", 90, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        schedule.BookendId = "bk-1";
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);
        schedule.BookendRotationCursor = 2;
        schedule.BookendTokens = new Dictionary<string, string> { ["brand"] = "2GB" };

        _tableClient.GetEntityAsync<ScheduleEntity>(
                Arg.Any<string>(), Arg.Is("rt-4"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        ScheduleEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<ScheduleEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveScheduleAsync(schedule);

        captured.ShouldNotBeNull();
        SeedGet(captured!);

        var loaded = (await _sut.GetScheduleAsync("rt-4")).Value;
        loaded.BookendId.ShouldBe("bk-1");
        loaded.BookendRotation.HasValue.ShouldBeTrue();
        loaded.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.RoundRobin);
        loaded.BookendRotation.Value.BookendIds.ShouldBe(["bk-1", "bk-2"]);
        loaded.BookendRotationCursor.ShouldBe(2);
        loaded.BookendTokens["brand"].ShouldBe("2GB");
    }

    [Fact]
    public async Task GetSchedule_OldShapeRowWithLegacyBookendColumns_DefaultsUnassigned()
    {
        // A row persisted before the pair-library collapse carries IntroBookendId/OutroBookendId/
        // IntroRotationJson/OutroRotationJson columns and NO BookendId column. The Azure Tables
        // SDK ignores the unknown legacy columns during deserialization, so such a row surfaces
        // here as a ScheduleEntity with the new bookend fields null — exactly what CreateEntity
        // without them produces. Loading must not throw and must degrade to an unassigned
        // schedule (the pre-pair-library state).
        SeedGet(CreateEntity("legacy-bookends-1"));

        var loaded = (await _sut.GetScheduleAsync("legacy-bookends-1")).Value;

        loaded.BookendId.ShouldBe("");
        loaded.BookendRotation.HasValue.ShouldBeFalse();
        loaded.BookendRotationCursor.ShouldBe(0);
        loaded.BookendTokens.Count.ShouldBe(0);
    }
}
