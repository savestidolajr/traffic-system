using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class BookendServiceTests
{
    private readonly InMemoryBookendRepository _repo = new();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly BookendService _sut;

    public BookendServiceTests()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        _sut = new BookendService(_repo, _schedules, TimeProvider.System,
            NullLogger<BookendService>.Instance);
    }

    private const string IntroText = "Looking over {city} today";
    private const string OutroText = "That's it for {city} traffic.";

    private static CreateBookendInput CreateInput(string name, string? id = null,
        string intro = IntroText, string outro = OutroText) =>
        new(name, null, intro, outro, null, null, id);

    private static MarketSchedule Schedule(string marketId = "melbourne") =>
        MarketSchedule.Create(marketId, "Melbourne", "KIIS 101.1", "Australia/Melbourne", "0 0 * * * *", true,
            CoverageArea.Create(-37.81, 144.96, 50).Value, "voice-1", "out", 30, 15, 5,
            DateTime.UtcNow, DateTime.UtcNow).Value;

    [Fact]
    public async Task Create_ThenGetAndList_RoundTrips()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        created.IsSuccess.ShouldBeTrue();

        var fetched = await _sut.GetAsync(created.Value.Id);
        fetched.IsSuccess.ShouldBeTrue();
        fetched.Value.Name.ShouldBe("Drive-Time Pair");
        fetched.Value.CurrentTemplate.IntroText.ShouldBe(IntroText);
        fetched.Value.CurrentTemplate.OutroText.ShouldBe(OutroText);

        (await _sut.ListAsync()).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Create_ExplicitId_IsPreserved()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair", id: "bk-imported"));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Id.ShouldBe("bk-imported");
    }

    [Theory]
    [InlineData("That's all from {city} traffic, powered by")]
    [InlineData("That's all from {city} traffic, brought to you by.")]
    [InlineData("That's all from {city} traffic and")]
    [InlineData("That's all from {city} traffic with…")]
    [InlineData("That's all from {city} traffic —")]
    public async Task Create_RejectsIncompleteOutro(string outro)
    {
        var result = await _sut.CreateAsync(CreateInput("Incomplete Pair", outro: outro));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("outro");
        (await _sut.ListAsync()).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_AcceptsCompleteOutroWithoutFinalPunctuation()
    {
        var result = await _sut.CreateAsync(
            CreateInput("Complete Pair", outro: "That's all from {city} traffic"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentTemplate.OutroText.ShouldBe("That's all from {city} traffic");
    }

    [Fact]
    public async Task Update_MintsNewVersion_WhenEitherLineChanges()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;

        var updated = await _sut.UpdateAsync(new UpdateBookendInput(
            id, "Drive-Time Pair", null, IntroText, "That's all from {city} traffic.", "new closer", "ed"));

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.CurrentVersion.ShouldBe(2);
        updated.Value.CurrentTemplate.OutroText.ShouldBe("That's all from {city} traffic.");
    }

    [Fact]
    public async Task Update_RejectsIncompleteOutro_WithoutMutatingOrSaving()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;

        var updated = await _sut.UpdateAsync(new UpdateBookendInput(
            id, "Renamed Pair", "changed", IntroText,
            "That's all from {city} traffic, powered by", "bad edit", "ed"));

        updated.IsFailure.ShouldBeTrue();
        updated.Error.ShouldContain("outro");

        var persisted = await _sut.GetAsync(id);
        persisted.IsSuccess.ShouldBeTrue();
        persisted.Value.Name.ShouldBe("Drive-Time Pair");
        persisted.Value.Description.ShouldBeNull();
        persisted.Value.CurrentVersion.ShouldBe(1);
        persisted.Value.CurrentTemplate.OutroText.ShouldBe(OutroText);
    }

    [Fact]
    public async Task Update_WhitespaceOnlyChange_DoesNotMint()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;

        var updated = await _sut.UpdateAsync(new UpdateBookendInput(
            id, "Drive-Time Pair", null, $"  {IntroText}  ", OutroText, null, "ed"));

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.CurrentVersion.ShouldBe(1);
    }

    [Fact]
    public async Task Restore_MintsCopyOfPriorVersion()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;
        await _sut.UpdateAsync(new UpdateBookendInput(
            id, "Drive-Time Pair", null, "Looking over the {city} region today", OutroText, null, null));

        var restored = await _sut.RestoreVersionAsync(id, 1, "ed");

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.CurrentVersion.ShouldBe(3);
        restored.Value.CurrentTemplate.IntroText.ShouldBe(IntroText);
    }

    [Fact]
    public async Task Restore_RejectsIncompleteHistoricalOutro_WithoutMutating()
    {
        var now = new DateTime(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);
        var legacy = Bookend.Reconstitute(
            "bk-legacy", "Legacy Pair", null, "active", now, now, null, [],
            [new BookendVersion(1,
                new BookendTemplate("Looking over {city} today", "That's all from {city} traffic, powered by"),
                null, now, null)]);
        await _repo.SaveAsync(legacy);

        var restored = await _sut.RestoreVersionAsync("bk-legacy", 1, "ed");

        restored.IsFailure.ShouldBeTrue();
        restored.Error.ShouldContain("outro");
        legacy.CurrentVersion.ShouldBe(1);
        legacy.Versions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Get_Missing_Fails()
    {
        (await _sut.GetAsync("bk-nope")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_Existing_RemovesFromGetAndList()
    {
        var deletedPair = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var retainedPair = await _sut.CreateAsync(CreateInput("Weekend Pair"));

        var deleted = await _sut.DeleteAsync(deletedPair.Value.Id);

        deleted.IsSuccess.ShouldBeTrue();
        (await _sut.GetAsync(deletedPair.Value.Id)).IsFailure.ShouldBeTrue();

        var listedIds = (await _sut.ListAsync()).Value.Select(b => b.Id).ToList();
        listedIds.ShouldNotContain(deletedPair.Value.Id);
        listedIds.ShouldContain(retainedPair.Value.Id);
    }

    [Fact]
    public async Task Delete_MissingId_FailsWithNotFound()
    {
        var result = await _sut.DeleteAsync("bk-nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task Delete_BlockedWhenReferencedByPrimaryId()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;

        var schedule = Schedule();
        schedule.BookendId = id;
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));

        var result = await _sut.DeleteAsync(id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("still assigned");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Code.ShouldBe("library.in_use");
    }

    [Fact]
    public async Task Delete_BlockedWhenReferencedByRotationSetOnly()
    {
        // The widened guard: the schedule's PRIMARY bookend id is empty, but its rotation policy
        // lists this pair — deleting it would leave a dangling rotation member.
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;

        var schedule = Schedule();
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, [id], null).Value);
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));

        var result = await _sut.DeleteAsync(id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("still assigned");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Code.ShouldBe("library.in_use");
    }

    [Fact]
    public async Task Delete_BlockedWhenProjectedSummaryReferencesRotationMember()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;
        var summary = new ScheduleReferenceSummary(
            "melbourne", "KIIS 101.1", "", "", "", "", "",
            [], [id]);
        _schedules.ListScheduleReferenceSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<ScheduleReferenceSummary>>.Success([summary]));

        var result = await _sut.DeleteAsync(id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("still assigned");
        result.FailureMetadata.Value.Code.ShouldBe("library.in_use");
        await _schedules.DidNotReceive().ListSchedulesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WhenProjectedScheduleReadFails_FailsClosed()
    {
        var created = await _sut.CreateAsync(CreateInput("Spare Pair"));
        var id = created.Value.Id;
        _schedules.ListScheduleReferenceSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<ScheduleReferenceSummary>>.Failure("projection unavailable"));

        var result = await _sut.DeleteAsync(id);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Could not verify");
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Retryable);
        (await _sut.GetAsync(id)).IsSuccess.ShouldBeTrue();
        await _schedules.DidNotReceive().ListSchedulesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_AllowedWhenNoScheduleReferences()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));

        var result = await _sut.DeleteAsync(created.Value.Id);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Archive_SetsStatusAndPersists()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;
        var initialVersion = created.Value.CurrentVersion;
        var versionsCount = created.Value.Versions.Count;

        var archived = await _sut.ArchiveAsync(id, "ed");

        archived.IsSuccess.ShouldBeTrue();
        archived.Value.Status.ShouldBe("archived");
        archived.Value.CurrentVersion.ShouldBe(initialVersion);
        archived.Value.Versions.Count.ShouldBe(versionsCount);

        // Verify persisted.
        var fetched = await _sut.GetAsync(id);
        fetched.Value.Status.ShouldBe("archived");
    }

    [Fact]
    public async Task Unarchive_SetsStatusBackToActive()
    {
        var created = await _sut.CreateAsync(CreateInput("Drive-Time Pair"));
        var id = created.Value.Id;
        await _sut.ArchiveAsync(id, null);

        var unarchived = await _sut.UnarchiveAsync(id, null);

        unarchived.IsSuccess.ShouldBeTrue();
        unarchived.Value.Status.ShouldBe("active");

        var fetched = await _sut.GetAsync(id);
        fetched.Value.Status.ShouldBe("active");
    }

    [Fact]
    public async Task Archive_MissingId_Fails()
    {
        var result = await _sut.ArchiveAsync("bk-nope", null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Unarchive_MissingId_Fails()
    {
        var result = await _sut.UnarchiveAsync("bk-nope", null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_PreservesTypedFailureMetadata_FromRepositorySave()
    {
        // A repository save failure carrying a typed InvalidInput (e.g. VersionHistoryTrimmer's
        // over-ceiling single version) must survive the service boundary — dropping it would make
        // a permanent client-input error look like a retryable server fault (HTTP 500).
        var repo = Substitute.For<IBookendRepository>();
        var existing = Bookend.Create("Drive-Time Pair", null,
            new BookendTemplate(IntroText, OutroText), null, null, DateTime.UtcNow).Value;
        repo.GetAsync("bk-1", Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.Some(existing)));
        repo.SaveAsync(Arg.Any<Bookend>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large", "This bookend is too large to save.")));

        var service = new BookendService(repo, _schedules, TimeProvider.System,
            NullLogger<BookendService>.Instance);
        var result = await service.UpdateAsync(
            new UpdateBookendInput("bk-1", "Drive-Time Pair", null, IntroText,
                "That's all from {city} traffic.", null, "ed"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("This bookend is too large to save.");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    [Fact]
    public async Task Archive_PreservesTypedFailureMetadata_FromRepositorySave()
    {
        var repo = Substitute.For<IBookendRepository>();
        var existing = Bookend.Create("Drive-Time Pair", null,
            new BookendTemplate(IntroText, OutroText), null, null, DateTime.UtcNow).Value;
        repo.GetAsync("bk-1", Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.Some(existing)));
        repo.SaveAsync(Arg.Any<Bookend>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large", "This bookend is too large to save.")));

        var service = new BookendService(repo, _schedules, TimeProvider.System,
            NullLogger<BookendService>.Instance);
        var result = await service.ArchiveAsync("bk-1", null);

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    private sealed class InMemoryBookendRepository : IBookendRepository
    {
        private readonly List<Bookend> _store = [];

        public Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<Bookend>>.Success(_store.ToList()));

        public Task<Result<Option<Bookend>>> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Result<Option<Bookend>>.Success(
                _store.FirstOrDefault(b => b.Id == id) is { } match
                    ? Option<Bookend>.Some(match)
                    : Option<Bookend>.None));

        public Task<Result> SaveAsync(Bookend bookend, CancellationToken ct = default)
        {
            _store.RemoveAll(b => b.Id == bookend.Id);
            _store.Add(bookend);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken ct = default)
        {
            _store.RemoveAll(b => b.Id == id);
            return Task.FromResult(Result.Success());
        }
    }
}
