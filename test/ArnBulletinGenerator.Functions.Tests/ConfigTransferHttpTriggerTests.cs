using System.Net;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.ConfigTransfer;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the two request-shaping guards on <c>POST /api/config/import</c>: a hard body-size
///     cap enforced before deserialization (L8), and detection of unrecognized top-level bundle
///     keys that plain DTO deserialization would otherwise silently drop (L9).
/// </summary>
public sealed class ConfigTransferHttpTriggerTests
{

    /// <summary>
    ///     A playbook service with an empty library. An unconfigured NSubstitute proxy returns a
    ///     null Task, which the import path dereferences: the stub has to answer, not merely exist.
    /// </summary>
    private static IBulletinPlaybookService EmptyPlaybooks()
    {
        var service = Substitute.For<IBulletinPlaybookService>();
        service.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<BulletinPlaybook>>.Success([]));
        return service;
    }
    private readonly IScheduleRepository _scheduleRepo = Substitute.For<IScheduleRepository>();
    private readonly IScheduleService _scheduleService = Substitute.For<IScheduleService>();
    private readonly IVoiceProfileService _voiceProfiles = Substitute.For<IVoiceProfileService>();
    private readonly IDeliveryStyleService _deliveryStyles = Substitute.For<IDeliveryStyleService>();
    private readonly IBookendService _bookends = Substitute.For<IBookendService>();
    private readonly IPronunciationDictionaryService _dictionaries = Substitute.For<IPronunciationDictionaryService>();
    private readonly ISafetyTermRepository _safetyTerms = Substitute.For<ISafetyTermRepository>();
    private readonly IGuidanceRepository _guidance = Substitute.For<IGuidanceRepository>();
    private readonly IPriorityRoadsRepository _priorityRoads = Substitute.For<IPriorityRoadsRepository>();
    private readonly IVerifiedPronunciationRepository _verifiedPronunciations =
        Substitute.For<IVerifiedPronunciationRepository>();
    private readonly IOperatorAssignmentRepository _operatorAssignments =
        Substitute.For<IOperatorAssignmentRepository>();
    private readonly IVoiceCalibrationRepository _voiceCalibrations =
        Substitute.For<IVoiceCalibrationRepository>();
    private readonly IFillerStatementsRepository _fillerStatements = Substitute.For<IFillerStatementsRepository>();
    private readonly IFillerAudioCacheService _fillerAudioCache = Substitute.For<IFillerAudioCacheService>();
    private readonly IConfigImportStore _importStore = Substitute.For<IConfigImportStore>();
    private readonly FakeTimeProvider _timeProvider = new();

    private readonly ConfigTransferHttpTrigger _sut;

    public ConfigTransferHttpTriggerTests()
    {
        // PlanAsync always lists the three shared libraries up front, regardless of which
        // sections the bundle carries — default them to empty so unconfigured calls don't
        // return an unconfigured (null) Task<Result<...>> from NSubstitute.
        _voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([]));
        _deliveryStyles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DeliveryStyle>>.Success([]));
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([]));
        _dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));
        _voiceProfiles.ListAsync(Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<VoiceProfile>>.Success(
                new VersionedLibraryListPage<VoiceProfile>([], 0)));
        _deliveryStyles.ListAsync(Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<DeliveryStyle>>.Success(
                new VersionedLibraryListPage<DeliveryStyle>([], 0)));
        _bookends.ListAsync(Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<Bookend>>.Success(
                new VersionedLibraryListPage<Bookend>([], 0)));
        _dictionaries.ListAsync(Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<PronunciationDictionary>>.Success(
                new VersionedLibraryListPage<PronunciationDictionary>([], 0)));
        var exportBuilder = new ConfigExportBuilder(
            _scheduleRepo, _voiceProfiles, _deliveryStyles, _bookends, _dictionaries,
            _safetyTerms, _guidance, _priorityRoads, _verifiedPronunciations,
            _operatorAssignments, _voiceCalibrations, _fillerStatements, TimeProvider.System);

        var importService = new ConfigImportService(
            _scheduleRepo, _scheduleService, _voiceProfiles, _deliveryStyles, EmptyPlaybooks(), _bookends, _dictionaries,
            _safetyTerms, _guidance, _priorityRoads, _verifiedPronunciations,
            _operatorAssignments, _voiceCalibrations, _fillerStatements, _fillerAudioCache,
            _timeProvider, NullLogger<ConfigImportService>.Instance);

        var options = Options.Create(new ConfigImportOptions { RequireSecondApprover = false });

        _sut = new ConfigTransferHttpTrigger(
            exportBuilder, importService, _importStore, options, _timeProvider,
            NullLogger<ConfigTransferHttpTrigger>.Instance);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    [Fact]
    public async Task Import_OversizedBody_ReturnsPayloadTooLarge()
    {
        // Comfortably over the 2 MB cap enforced before deserialization ever runs.
        string padding = new('x', 3 * 1024 * 1024);
        string body = $$"""{ "Schema": "test", "Source": "{{padding}}" }""";
        var req = MockHttpRequestData.CreatePostRaw(body, "http://localhost/api/config/import");

        var response = await _sut.Import(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        string responseBody = await MockHttpRequestData.ReadResponseBodyAsync(response);
        responseBody.ShouldContain("MB");
    }

    [Fact]
    public async Task Import_WithinSizeLimit_IsNotRejectedForSize()
    {
        const string body = """{ "Schema": "test" }""";
        var req = MockHttpRequestData.CreatePostRaw(body, "http://localhost/api/config/import?dryRun=true");

        var response = await _sut.Import(req, Ctx());

        response.StatusCode.ShouldNotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Import_UnknownTopLevelSectionKey_SurfacesAsErrorNotSilentNoOp()
    {
        // 'Schedule' (singular) is a typo for 'Schedules' — plain deserialization drops it
        // silently and the import would otherwise report an empty 0/0/0/0 no-op.
        const string body = """
            {
              "Schema": "test",
              "Schedule": [ { "MarketId": "sydney" } ]
            }
            """;
        var req = MockHttpRequestData.CreatePostRaw(body, "http://localhost/api/config/import?dryRun=true");

        var response = await _sut.Import(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string responseBody = await MockHttpRequestData.ReadResponseBodyAsync(response);
        responseBody.ShouldContain("Schedule");
        responseBody.ShouldContain("Error");
    }

    [Fact]
    public async Task Import_ValidEmptyBundle_ReportsNoErrors_DryRun()
    {
        const string body = """{ "Schema": "test" }""";
        var req = MockHttpRequestData.CreatePostRaw(body, "http://localhost/api/config/import?dryRun=true");

        var response = await _sut.Import(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string responseBody = await MockHttpRequestData.ReadResponseBodyAsync(response);
        responseBody.ShouldContain("\"Errors\":0");
    }

    [Fact]
    public async Task Approve_ReplanDriftedFromStoredReport_ReturnsConflictNamingTheDrift()
    {
        // The stored report the approver would review says Create for vp-1; by approval time the
        // profile exists (created interactively after the import was parked), so the fresh
        // re-plan says Update. The two-person gate must refuse — applying an Update nobody
        // reviewed could silently overwrite a newer interactive edit (reproduced on dev
        // 2026-08-05: approved Create, applied Update over 'ZZ BB P4 INTERACTIVE').
        var storedReport = new ConfigImportReportDto(
            DryRun: false, RequiresApproval: true, Applied: false,
            Creates: 1, Updates: 0, Unchanged: 0, Errors: 0,
            [new ConfigImportItemDto(
                ConfigSections.VoiceProfiles, "vp-1", "Drifted Voice", ConfigImportActions.Create)]);

        const string bundleJson = """
            {
              "SchemaVersion": 1,
              "VoiceProfiles": [
                { "Id": "vp-1", "Name": "Drifted Voice Changed", "BaseVoiceId": "eleven_base",
                  "Model": "eleven_v3", "Settings": {} }
              ]
            }
            """;

        _importStore.GetPendingAsync("pending-drift", Arg.Any<CancellationToken>())
            .Returns(Result<Option<PendingConfigImport>>.Success(Option<PendingConfigImport>.Some(
                new PendingConfigImport(
                    "pending-drift", "Requester One", "op-requester",
                    new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                    JsonSerializer.Serialize(storedReport, JsonSerializationDefaults.Response),
                    bundleJson))));

        // The import snapshot reader uses the bounded current-summary overload. Configure that
        // contract here so approval observes the interactive profile and detects Create → Update
        // drift before an unconfigured create thunk can be reached.
        _voiceProfiles.ListAsync(Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<VoiceProfile>>.Success(
                new VersionedLibraryListPage<VoiceProfile>([
                    VoiceProfile.Create("Drifted Voice", null, "eleven_base", "Base Voice", "eleven_v3",
                        VoiceSettings.Create().Value, null, "test",
                        new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), id: "vp-1").Value
                ], 1)));

        var req = MockHttpRequestData.CreatePostEmpty(
            "http://localhost/api/config/imports/pending/pending-drift/approve");
        req.Headers.Add(ClientPrincipal.HeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"auth_typ":"aad","claims":[{"typ":"name","val":"Approver Two"},{"typ":"oid","val":"op-approver"}]}""")));

        var response = await _sut.Approve(req, "pending-drift", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        string responseBody = await MockHttpRequestData.ReadResponseBodyAsync(response);
        responseBody.ShouldContain("was Create, now Update");
        responseBody.ShouldContain("vp-1");
        responseBody.ShouldContain("submitted for approval");
        // The drifted plan must never reach the apply stage.
        await _voiceProfiles.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<UpdateVoiceProfileInput>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _importStore.DidNotReceive().ResolveAsync("pending-drift", Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_ReplanMatchesStoredReport_AppliesNormally()
    {
        // The happy path must not regress: an identical re-plan proceeds to apply (the report
        // serialized at park time is equivalent to the fresh one).
        var storedReport = new ConfigImportReportDto(
            DryRun: false, RequiresApproval: true, Applied: false,
            Creates: 1, Updates: 0, Unchanged: 0, Errors: 0,
            [new ConfigImportItemDto(
                ConfigSections.VoiceProfiles, "vp-1", "New Voice", ConfigImportActions.Create)]);

        const string bundleJson = """
            {
              "SchemaVersion": 1,
              "VoiceProfiles": [
                { "Id": "vp-1", "Name": "New Voice", "BaseVoiceId": "eleven_base",
                  "Model": "eleven_v3", "Settings": {} }
              ]
            }
            """;

        _importStore.GetPendingAsync("pending-match", Arg.Any<CancellationToken>())
            .Returns(Result<Option<PendingConfigImport>>.Success(Option<PendingConfigImport>.Some(
                new PendingConfigImport(
                    "pending-match", "Requester One", "op-requester",
                    new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                    JsonSerializer.Serialize(storedReport, JsonSerializationDefaults.Response),
                    bundleJson))));

        _importStore.ResolveAsync("pending-match", "applied", "Approver Two", "op-approver",
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // The apply stage actually runs the create thunk for the identical plan.
        _voiceProfiles.CreateAsync(Arg.Any<CreateVoiceProfileInput>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                VoiceProfile.Create("New Voice", null, "eleven_base", "Base Voice", "eleven_v3",
                    VoiceSettings.Create().Value, null, "test",
                    new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), id: "vp-1").Value));

        var req = MockHttpRequestData.CreatePostEmpty(
            "http://localhost/api/config/imports/pending/pending-match/approve");
        req.Headers.Add(ClientPrincipal.HeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"auth_typ":"aad","claims":[{"typ":"name","val":"Approver Two"},{"typ":"oid","val":"op-approver"}]}""")));

        var response = await _sut.Approve(req, "pending-match", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string responseBody = await MockHttpRequestData.ReadResponseBodyAsync(response);
        responseBody.ShouldContain("\"Applied\":true");
    }
}
