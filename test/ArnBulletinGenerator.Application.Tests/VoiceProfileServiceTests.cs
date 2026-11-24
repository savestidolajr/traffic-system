using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class VoiceProfileServiceTests
{
    private readonly InMemoryVoiceProfileRepository _repo = new();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();
    private readonly IVoiceRemixer _remixer = Substitute.For<IVoiceRemixer>();
    private readonly VoiceProfileService _sut;

    public VoiceProfileServiceTests()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        // Sane default for the VC-3 rollback path; tests that exercise a rollback failure
        // override this.
        _remixer.DeleteVoiceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _sut = new VoiceProfileService(_repo, _schedules, _remixer, TimeProvider.System,
            NullLogger<VoiceProfileService>.Instance);
    }

    private static CreateVoiceProfileInput CreateInput(string name = "ARN Hudson") =>
        new(name, null, "voice-1", "Hudson", "eleven_v3", VoiceSettings.Default, null);

    [Fact]
    public async Task Create_ThenGetAndList_RoundTrips()
    {
        var created = await _sut.CreateAsync(CreateInput(), "admin@arn.com.au");

        created.IsSuccess.ShouldBeTrue();
        (await _sut.GetAsync(created.Value.Id)).Value.Name.ShouldBe("ARN Hudson");
        (await _sut.ListAsync()).Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Update_MintsNewVersion_OnSettingsChange()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);
        int before = created.Value.CurrentVersion;

        var updated = await _sut.UpdateAsync(
            created.Value.Id,
            new UpdateVoiceProfileInput("ARN Hudson", null, "eleven_v3",
                VoiceSettings.Create(speed: 1.1).Value, "Faster read"), "operator@arn.com.au");

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.CurrentVersion.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Restore_MintsCopyOfPriorVersion()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);
        await _sut.UpdateAsync(created.Value.Id,
            new UpdateVoiceProfileInput("ARN Hudson", null, "eleven_v3",
                VoiceSettings.Create(speed: 1.1).Value, "v2"), null);
        int before = (await _sut.GetAsync(created.Value.Id)).Value.CurrentVersion;

        var restored = await _sut.RestoreVersionAsync(created.Value.Id, 1, "admin@arn.com.au");

        restored.IsSuccess.ShouldBeTrue();
        restored.Value.CurrentVersion.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Get_Missing_Fails()
    {
        (await _sut.GetAsync("vp-nope")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_Existing_RemovesFromGetAndList()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);

        (await _sut.DeleteAsync(created.Value.Id)).IsSuccess.ShouldBeTrue();
        (await _sut.GetAsync(created.Value.Id)).IsFailure.ShouldBeTrue();
        (await _sut.ListAsync()).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_MissingId_Fails()
    {
        (await _sut.DeleteAsync("vp-nope")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Archive_SetsStatusAndPersists()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);

        var archived = await _sut.ArchiveAsync(created.Value.Id);

        archived.IsSuccess.ShouldBeTrue();
        archived.Value.Status.ShouldBe("archived");
        // Persisted — a fresh read sees the archived status.
        (await _sut.GetAsync(created.Value.Id)).Value.Status.ShouldBe("archived");
    }

    [Fact]
    public async Task Unarchive_SetsStatusBackToActive()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);
        await _sut.ArchiveAsync(created.Value.Id);

        var unarchived = await _sut.UnarchiveAsync(created.Value.Id);

        unarchived.IsSuccess.ShouldBeTrue();
        unarchived.Value.Status.ShouldBe("active");
        (await _sut.GetAsync(created.Value.Id)).Value.Status.ShouldBe("active");
    }

    [Fact]
    public async Task Archive_MissingId_Fails()
    {
        (await _sut.ArchiveAsync("vp-nope")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Unarchive_MissingId_Fails()
    {
        (await _sut.UnarchiveAsync("vp-nope")).IsFailure.ShouldBeTrue();
    }

    // === Remix adoption — pre-flight before the mint, rollback after it (VC-3) ===

    [Fact]
    public async Task Create_AdoptingRemix_MintsVoiceAndUsesItAsTheBase()
    {
        _remixer.CreateVoiceFromPreviewAsync("gen-1", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("minted-voice-1"));

        var created = await _sut.CreateAsync(
            new CreateVoiceProfileInput("ARN Hudson", null, "voice-1", "Hudson", "eleven_v3",
                VoiceSettings.Default, null, RemixedVoiceId: "gen-1"), "admin@arn.com.au");

        created.IsSuccess.ShouldBeTrue();
        created.Value.BaseVoiceId.ShouldBe("minted-voice-1");
        created.Value.BaseVoiceName.ShouldBe("Hudson (remixed)");
        await _remixer.Received(1).CreateVoiceFromPreviewAsync("gen-1", "Hudson (remixed)", "Hudson (remixed)",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_AdoptingRemix_SaveFails_DeletesMintedVoiceAndSurfacesOriginalError()
    {
        // The residual window of VC-3: a late local failure after a successful provider mint must
        // roll the orphan back (delete — unlike pronunciation dictionaries, voices have a hard
        // delete) and ALWAYS surface the ORIGINAL error, never a rollback failure masking it.
        _remixer.CreateVoiceFromPreviewAsync("gen-1", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("minted-voice-1"));
        _repo.FailNextSaveWith(OperationalFailure.InvalidInput("storage.unavailable", "Storage blip"));

        var created = await _sut.CreateAsync(
            new CreateVoiceProfileInput("ARN Hudson", null, "voice-1", "Hudson", "eleven_v3",
                VoiceSettings.Default, null, RemixedVoiceId: "gen-1"), "admin@arn.com.au");

        created.IsFailure.ShouldBeTrue();
        await _remixer.Received(1).DeleteVoiceAsync("minted-voice-1", Arg.Any<CancellationToken>());
        created.FailureMetadata.HasValue.ShouldBeTrue();
        created.FailureMetadata.Value.Code.ShouldBe("storage.unavailable");
        (await _sut.ListAsync()).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_AdoptingRemix_OversizedChangeNote_PreFlightRejectsBeforeMinting()
    {
        var created = await _sut.CreateAsync(
            new CreateVoiceProfileInput("ARN Hudson", null, "voice-1", "Hudson", "eleven_v3",
                VoiceSettings.Default, new string('n', 33_000), RemixedVoiceId: "gen-1"), "admin@arn.com.au");

        created.IsFailure.ShouldBeTrue();
        created.FailureMetadata.HasValue.ShouldBeTrue();
        created.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
        created.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        // Pre-flightable failure: the mint must never have happened.
        await _remixer.DidNotReceiveWithAnyArgs()
            .CreateVoiceFromPreviewAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Create_AdoptingRemix_MintFails_ReturnsErrorWithNothingToRollBack()
    {
        _remixer.CreateVoiceFromPreviewAsync("gen-1", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure("ElevenLabs unavailable"));

        var created = await _sut.CreateAsync(
            new CreateVoiceProfileInput("ARN Hudson", null, "voice-1", "Hudson", "eleven_v3",
                VoiceSettings.Default, null, RemixedVoiceId: "gen-1"), "admin@arn.com.au");

        created.IsFailure.ShouldBeTrue();
        created.Error.ShouldContain("ElevenLabs unavailable");
        await _remixer.DidNotReceiveWithAnyArgs().DeleteVoiceAsync(default!, default);
    }

    [Fact]
    public async Task Update_AdoptingRemix_SaveFails_DeletesMintedVoiceAndSurfacesOriginalError()
    {
        var created = await _sut.CreateAsync(CreateInput(), null);
        _remixer.CreateVoiceFromPreviewAsync("gen-1", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("minted-voice-2"));
        _repo.FailNextSaveWith(OperationalFailure.InvalidInput("storage.unavailable", "Storage blip"));

        var updated = await _sut.UpdateAsync(created.Value.Id,
            new UpdateVoiceProfileInput("ARN Hudson", null, "eleven_v3", VoiceSettings.Default,
                "Adopted remix", BaseVoiceName: "Hudson", RemixedVoiceId: "gen-1"), "operator@arn.com.au");

        updated.IsFailure.ShouldBeTrue();
        updated.Error.ShouldContain("Storage blip");
        await _remixer.Received(1).DeleteVoiceAsync("minted-voice-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AdoptingRemix_MissingProfile_ReturnsNotFoundWithoutMinting()
    {
        _remixer.CreateVoiceFromPreviewAsync("gen-1", Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("minted-voice-3"));

        var updated = await _sut.UpdateAsync("vp-nope",
            new UpdateVoiceProfileInput("ARN Hudson", null, "eleven_v3", VoiceSettings.Default,
                "Adopted remix", RemixedVoiceId: "gen-1"), "operator@arn.com.au");

        updated.IsFailure.ShouldBeTrue();
        updated.Error.ShouldContain("not found");
        // Existence is a pre-flightable failure: nothing minted, nothing to delete.
        await _remixer.DidNotReceiveWithAnyArgs()
            .CreateVoiceFromPreviewAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Update_SaveFailure_PreservesTypedDispositionToTheServiceBoundary()
    {
        // Finding 50: the trimmer's over-ceiling refusal is a typed InvalidInput. It must
        // survive from the repository Result through the service so the HTTP edge can map it
        // to 400 instead of a misleading 500.
        var created = await _sut.CreateAsync(CreateInput(), null);
        _repo.FailNextSaveWith(OperationalFailure.InvalidInput(
            "versionhistory.entry_too_large",
            "This voice profile is too large to save — a single version is 33,340 characters "
            + "against a 32,768 limit. Remove or shorten some entries and save again."));

        var updated = await _sut.UpdateAsync(
            created.Value.Id,
            new UpdateVoiceProfileInput("ARN Hudson", null, "eleven_v3",
                VoiceSettings.Create(speed: 1.1).Value, "Over the ceiling"), null);

        updated.IsFailure.ShouldBeTrue();
        updated.FailureMetadata.HasValue.ShouldBeTrue();
        updated.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
        updated.FailureMetadata.Value.Code.ShouldBe("versionhistory.entry_too_large");
        updated.Error.ShouldContain("too large to save");
    }

    private sealed class InMemoryVoiceProfileRepository : IVoiceProfileRepository
    {
        private readonly Dictionary<string, VoiceProfile> _profiles = new(StringComparer.Ordinal);
        private OperationalFailure? _nextSaveFailure;

        public void FailNextSaveWith(OperationalFailure failure) => _nextSaveFailure = failure;

        public Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<VoiceProfile>>.Success(
                _profiles.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()));

        public Task<Result<Option<VoiceProfile>>> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.TryGetValue(id, out var profile)
                ? Result<Option<VoiceProfile>>.Success(Option<VoiceProfile>.Some(profile))
                : Result<Option<VoiceProfile>>.Success(Option<VoiceProfile>.None));

        public Task<Result> SaveAsync(VoiceProfile profile, CancellationToken cancellationToken = default)
        {
            if (_nextSaveFailure is not null)
            {
                var failure = _nextSaveFailure;
                _nextSaveFailure = null;
                return Task.FromResult(Result.Failure(failure));
            }

            _profiles[profile.Id] = profile;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _profiles.Remove(id);
            return Task.FromResult(Result.Success());
        }
    }
}
