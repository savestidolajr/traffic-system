using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class DeliveryStyleServiceTests
{
    private readonly InMemoryDeliveryStyleRepository _repo = new();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly DeliveryStyleService _sut;

    public DeliveryStyleServiceTests()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        _sut = new DeliveryStyleService(_repo, _schedules, TimeProvider.System,
            NullLogger<DeliveryStyleService>.Instance);
    }

    private static DeliveryStylePolicy Urgent(int intensity = 2) =>
        new("Urgent peak-hour read.", ["[urgent]", "[serious]"], intensity);

    [Fact]
    public async Task Create_ThenGetAndList_RoundTrips()
    {
        var created = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", "Punchy", Urgent(), "initial"), "operator");
        created.IsSuccess.ShouldBeTrue();

        var fetched = await _sut.GetAsync(created.Value.Id);
        fetched.IsSuccess.ShouldBeTrue();
        fetched.Value.Name.ShouldBe("Drive-Time");
        fetched.Value.CurrentPolicy.Intensity.ShouldBe(2);

        (await _sut.ListAsync()).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Update_MintsNewVersion_OnPolicyChange()
    {
        var created = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", null, Urgent(2), null), null);
        var id = created.Value.Id;

        var updated = await _sut.UpdateAsync(id,
            new UpdateDeliveryStyleInput("Drive-Time", null, Urgent(3), "heavier"), "ed");

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.CurrentVersion.ShouldBe(2);
        updated.Value.CurrentPolicy.Intensity.ShouldBe(3);
    }

    [Fact]
    public async Task Restore_MintsCopyOfPriorVersion()
    {
        var created = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", null, Urgent(2), null), null);
        var id = created.Value.Id;
        await _sut.UpdateAsync(id, new UpdateDeliveryStyleInput("Drive-Time", null, Urgent(3), null), null);

        var restored = await _sut.RestoreVersionAsync(id, 1, "ed");

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.CurrentVersion.ShouldBe(3);
        restored.Value.CurrentPolicy.Intensity.ShouldBe(2);
    }

    [Fact]
    public async Task Get_Missing_Fails()
    {
        (await _sut.GetAsync("ds-nope")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_Existing_RemovesFromGetAndList()
    {
        var deletedStyle = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", null, Urgent(), null), null);
        var retainedStyle = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Weekend", null, Urgent(1), null), null);

        var deleted = await _sut.DeleteAsync(deletedStyle.Value.Id);

        deleted.IsSuccess.ShouldBeTrue();
        (await _sut.GetAsync(deletedStyle.Value.Id)).IsFailure.ShouldBeTrue();

        var listedIds = (await _sut.ListAsync()).Value.Select(style => style.Id).ToList();
        listedIds.ShouldNotContain(deletedStyle.Value.Id);
        listedIds.ShouldContain(retainedStyle.Value.Id);
    }

    [Fact]
    public async Task Delete_MissingId_FailsWithNotFound()
    {
        var result = await _sut.DeleteAsync("ds-nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task Archive_SetsStatusAndPersists()
    {
        var created = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", null, Urgent(), null), null);
        var id = created.Value.Id;
        var initialVersion = created.Value.CurrentVersion;
        var versionsCount = created.Value.Versions.Count;

        var archived = await _sut.ArchiveAsync(id);

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
        var created = await _sut.CreateAsync(
            new CreateDeliveryStyleInput("Drive-Time", null, Urgent(), null), null);
        var id = created.Value.Id;
        await _sut.ArchiveAsync(id);

        var unarchived = await _sut.UnarchiveAsync(id);

        unarchived.IsSuccess.ShouldBeTrue();
        unarchived.Value.Status.ShouldBe("active");

        var fetched = await _sut.GetAsync(id);
        fetched.Value.Status.ShouldBe("active");
    }

    [Fact]
    public async Task Archive_MissingId_Fails()
    {
        var result = await _sut.ArchiveAsync("ds-nope");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Unarchive_MissingId_Fails()
    {
        var result = await _sut.UnarchiveAsync("ds-nope");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_PreservesTypedFailureMetadata_FromRepositorySave()
    {
        // A repository save failure carrying a typed InvalidInput (e.g. VersionHistoryTrimmer's
        // over-ceiling single version) must survive the service boundary — dropping it would make
        // a permanent client-input error look like a retryable server fault (HTTP 500).
        var repo = Substitute.For<IDeliveryStyleRepository>();
        var existing = DeliveryStyle.Create("Drive-Time", null, Urgent(), null, null, DateTime.UtcNow).Value;
        repo.GetAsync("ds-1", Arg.Any<CancellationToken>())
            .Returns(Result<Option<DeliveryStyle>>.Success(Option<DeliveryStyle>.Some(existing)));
        repo.SaveAsync(Arg.Any<DeliveryStyle>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large", "This delivery style is too large to save.")));

        var service = new DeliveryStyleService(repo, _schedules, TimeProvider.System,
            NullLogger<DeliveryStyleService>.Instance);
        var result = await service.UpdateAsync("ds-1",
            new UpdateDeliveryStyleInput("Drive-Time", null, Urgent(3), "heavier"), "ed");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("This delivery style is too large to save.");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    [Fact]
    public async Task Archive_PreservesTypedFailureMetadata_FromRepositorySave()
    {
        var repo = Substitute.For<IDeliveryStyleRepository>();
        var existing = DeliveryStyle.Create("Drive-Time", null, Urgent(), null, null, DateTime.UtcNow).Value;
        repo.GetAsync("ds-1", Arg.Any<CancellationToken>())
            .Returns(Result<Option<DeliveryStyle>>.Success(Option<DeliveryStyle>.Some(existing)));
        repo.SaveAsync(Arg.Any<DeliveryStyle>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large", "This delivery style is too large to save.")));

        var service = new DeliveryStyleService(repo, _schedules, TimeProvider.System,
            NullLogger<DeliveryStyleService>.Instance);
        var result = await service.ArchiveAsync("ds-1");

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    private sealed class InMemoryDeliveryStyleRepository : IDeliveryStyleRepository
    {
        private readonly Dictionary<string, DeliveryStyle> _store = new();

        public Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<DeliveryStyle>>.Success(_store.Values.ToList()));

        public Task<Result<Option<DeliveryStyle>>> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Option<DeliveryStyle>>.Success(
                _store.TryGetValue(id, out var s) ? Option<DeliveryStyle>.Some(s) : Option<DeliveryStyle>.None));

        public Task<Result> SaveAsync(DeliveryStyle style, CancellationToken cancellationToken = default)
        {
            _store[style.Id] = style;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _store.Remove(id);
            return Task.FromResult(Result.Success());
        }
    }
}
