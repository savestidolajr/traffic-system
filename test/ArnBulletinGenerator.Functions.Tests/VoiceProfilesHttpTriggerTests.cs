using System.Net;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the request-body guards added to <see cref="VoiceProfilesHttpTrigger" />'s
///     Create/Update: Name/Description length caps (M2) and a case-insensitive duplicate-name
///     guard on Create (L6). Full CRUD behaviour is exercised via the service layer's own tests.
/// </summary>
public sealed class VoiceProfilesHttpTriggerTests
{
    private readonly IVoiceProfileService _voiceProfiles = Substitute.For<IVoiceProfileService>();
    private readonly IVoiceCalibrationRepository _calibrationRepository = Substitute.For<IVoiceCalibrationRepository>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly IVoiceRemixer _voiceRemixer = Substitute.For<IVoiceRemixer>();
    private readonly VoiceProfilesHttpTrigger _sut;

    public VoiceProfilesHttpTriggerTests()
    {
        _sut = new VoiceProfilesHttpTrigger(
            _voiceProfiles,
            _calibrationRepository,
            _voiceProvider,
            Options.Create(new ElevenLabsOptions()));

        // Default: no existing profiles, so duplicate-name tests that don't care about it never
        // hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([]));
        // Default: no calibration on file, so a successful Create/Update response's DTO mapping
        // never hits an unconfigured (null) Task<Result<...>> from NSubstitute.
        _calibrationRepository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        // Default: an empty provider catalogue, so base-voice-name reconciliation falls back to
        // the profile's stored name instead of hitting an unconfigured substitute.
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success([]));
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static CreateVoiceProfileRequestDto ValidCreateRequest(string name = "ARN Hudson") =>
        new(name, "A description.", "voice-1", "Hudson", "eleven_v3", new VoiceSettingsDto());

    private static VoiceProfile ExistingProfile(string name) =>
        VoiceProfile.Create(name, null, "voice-1", "Hudson", "eleven_v3", VoiceSettings.Default,
            null, null, DateTime.UtcNow).Value;

    private static Voice ProviderVoice(string id, string name) =>
        new(id, name, "professional", "desc", new Dictionary<string, string> { ["provider"] = "ElevenLabs" });

    /// <summary>
    ///     A profile's stored BaseVoiceName is a snapshot from creation time. When the provider has
    ///     since renamed the voice, the API must serve the provider's CURRENT name, not the stale
    ///     copy — the same drift that had a config default reading "ARN Hudson" for a voice
    ///     ElevenLabs calls "Peter Michael - Australian Male VO".
    /// </summary>
    [Fact]
    public async Task List_StoredBaseVoiceNameIsStale_ServesTheProvidersCurrentName()
    {
        _voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([ExistingProfile("Pilot Variant")]));
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success([ProviderVoice("voice-1", "Renamed At The Provider")]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Renamed At The Provider");
        body.ShouldNotContain("\"Hudson\"");
    }

    /// <summary>Fail-soft: an unreachable provider must not blank the name or fail the request.</summary>
    [Fact]
    public async Task List_ProviderUnreachable_FallsBackToTheStoredBaseVoiceName()
    {
        _voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([ExistingProfile("Pilot Variant")]));
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Failure("ElevenLabs unreachable"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Hudson");
    }

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest(new string('a', ConfigValidationLimits.VoiceProfileNameMax + 1));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Create_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with
        {
            Description = new string('d', ConfigValidationLimits.VoiceProfileDescriptionMax + 1)
        };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Fact]
    public async Task Create_ChangeNoteTooLong_ReturnsBadRequestWithoutMinting()
    {
        // VC-3 pre-flight: the version note is the only unbounded field a version carries, so an
        // oversized note must be a typed 400 at the trigger and never reach the provider mint.
        var request = ValidCreateRequest() with
        {
            ChangeNote = new string('n', ConfigValidationLimits.VoiceProfileChangeNoteMax + 1),
            RemixedVoiceId = "gen-1"
        };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Change note must be at most");
        await _voiceRemixer.DidNotReceiveWithAnyArgs()
            .CreateVoiceFromPreviewAsync(default!, default!, default!, default);
        await _voiceProfiles.DidNotReceiveWithAnyArgs()
            .CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Update_ChangeNoteTooLong_ReturnsBadRequestWithoutMinting()
    {
        var request = new UpdateVoiceProfileRequestDto(
            "ARN Hudson", null, "eleven_v3", new VoiceSettingsDto(),
            ChangeNote: new string('n', ConfigValidationLimits.VoiceProfileChangeNoteMax + 1),
            RemixedVoiceId: "gen-1");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/voices/profiles/vp-1"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Change note must be at most");
        await _voiceRemixer.DidNotReceiveWithAnyArgs()
            .CreateVoiceFromPreviewAsync(default!, default!, default!, default);
        await _voiceProfiles.DidNotReceiveWithAnyArgs()
            .UpdateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([ExistingProfile("ARN Hudson")]));

        // Case-insensitive match against the existing profile's name.
        var request = ValidCreateRequest("arn hudson");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("already exists");
        await _voiceProfiles.DidNotReceive().CreateAsync(
            Arg.Any<CreateVoiceProfileInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ValidRequest_Succeeds()
    {
        var created = ExistingProfile("ARN Hudson");
        _voiceProfiles.CreateAsync(Arg.Any<CreateVoiceProfileInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(created));

        var request = ValidCreateRequest();

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/voices/profiles"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_NameTooLong_ReturnsBadRequest()
    {
        var request = new UpdateVoiceProfileRequestDto(
            new string('a', ConfigValidationLimits.VoiceProfileNameMax + 1), "d", "eleven_v3", new VoiceSettingsDto());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/voices/profiles/vp-1"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Update_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = new UpdateVoiceProfileRequestDto(
            "ARN Hudson", new string('d', ConfigValidationLimits.VoiceProfileDescriptionMax + 1),
            "eleven_v3", new VoiceSettingsDto());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/voices/profiles/vp-1"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    // === Archive / Unarchive (finding 49) ===

    [Fact]
    public async Task Archive_SetsStatusToArchivedInTheResponse()
    {
        var archived = ExistingProfile("ARN Hudson");
        archived.Archive(DateTime.UtcNow);
        _voiceProfiles.ArchiveAsync("vp-1", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(archived));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Archive(MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices/profiles/vp-1/archive"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Status\":\"archived\"", Case.Insensitive);
    }

    [Fact]
    public async Task Unarchive_SetsStatusBackToActiveInTheResponse()
    {
        var archived = ExistingProfile("ARN Hudson");
        archived.Archive(DateTime.UtcNow);
        archived.Unarchive(DateTime.UtcNow);
        _voiceProfiles.UnarchiveAsync("vp-1", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(archived));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Unarchive(MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices/profiles/vp-1/unarchive"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Status\":\"active\"", Case.Insensitive);
    }

    [Fact]
    public async Task Archive_MissingId_ReturnsBadRequest()
    {
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Archive(MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices/profiles/archive"),
                "  ", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        await _voiceProfiles.DidNotReceiveWithAnyArgs().ArchiveAsync(default!, default);
    }

    [Fact]
    public async Task Archive_UnknownProfile_ReturnsNotFound()
    {
        _voiceProfiles.ArchiveAsync("vp-nope", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("Voice profile 'vp-nope' not found."));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Archive(MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices/profiles/vp-nope/archive"),
                "vp-nope", Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    // === Over-ceiling typed failure → 400 (finding 50) ===

    [Fact]
    public async Task Update_OverCeilingTypedFailure_ReturnsBadRequestNotInternalError()
    {
        // The trimmer's over-ceiling refusal is a typed InvalidInput; dropping the disposition
        // used to surface it as a misleading 500. The typed path must yield 400.
        _voiceProfiles.UpdateAsync(Arg.Any<string>(), Arg.Any<UpdateVoiceProfileInput>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large",
                "This voice profile is too large to save — a single version is 33,340 characters "
                + "against a 32,768 limit. Remove or shorten some entries and save again.")));
        var request = new UpdateVoiceProfileRequestDto(
            "ARN Hudson", null, "eleven_v3", new VoiceSettingsDto());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/voices/profiles/vp-1"),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("too large to save");
    }
}
