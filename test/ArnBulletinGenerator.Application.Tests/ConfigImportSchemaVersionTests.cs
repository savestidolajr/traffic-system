using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Regression tests for the SchemaVersion gate (bug-bash 2026-08-05 F-34): an import carrying
///     a SchemaVersion other than the current one (999 → 200 previously, 0 errors) must be rejected
///     at plan time with an error naming the supported version. Absent/0 deserialize to the DTO's
///     default of 1, so pre-versioning exports keep importing.
/// </summary>
public sealed class ConfigImportSchemaVersionTests
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
    private static ConfigImportService Service()
    {
        var voiceProfiles = Substitute.For<IVoiceProfileService>();
        var deliveryStyles = Substitute.For<IDeliveryStyleService>();
        var bookends = Substitute.For<IBookendService>();
        var dictionaries = Substitute.For<IPronunciationDictionaryService>();
        // PlanAsync lists the four shared libraries up front even for an empty bundle; an
        // unconfigured substitute returns a null Task, so default them to empty.
        voiceProfiles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VoiceProfile>>.Success([]));
        deliveryStyles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DeliveryStyle>>.Success([]));
        bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([]));
        dictionaries.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));

        return new ConfigImportService(
            Substitute.For<IScheduleRepository>(),
            Substitute.For<IScheduleService>(),
            voiceProfiles, deliveryStyles, EmptyPlaybooks(), bookends, dictionaries,
            Substitute.For<ISafetyTermRepository>(),
            Substitute.For<IGuidanceRepository>(),
            Substitute.For<IPriorityRoadsRepository>(),
            Substitute.For<IVerifiedPronunciationRepository>(),
            Substitute.For<IOperatorAssignmentRepository>(),
            Substitute.For<IVoiceCalibrationRepository>(),
            Substitute.For<IFillerStatementsRepository>(),
            Substitute.For<IFillerAudioCacheService>(),
            TimeProvider.System,
            NullLogger<ConfigImportService>.Instance);
    }

    [Fact]
    public async Task PlanAsync_UnsupportedSchemaVersion_IsAPlanErrorNamingSupportedVersion()
    {
        var service = Service();

        var plan = await service.PlanAsync(new ConfigBundleDto(SchemaVersion: 999), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        var item = plan.Value.Items.Single();
        item.Report.Action.ShouldBe(ConfigImportActions.Error);
        var detail = item.Report.Detail;
        detail.ShouldNotBeNull();
        detail.ShouldContain("999");
        detail.ShouldContain("Supported versions: 1");
    }

    [Fact]
    public async Task PlanAsync_DefaultedSchemaVersion_IsAccepted()
    {
        // The export emits SchemaVersion 1; a bundle that omits the field deserializes to the
        // record default (1) and must plan normally — no library reads even happen for an
        // empty bundle, so the unconfigured substitutes would NRE if the guard misfired.
        var service = Service();

        var plan = await service.PlanAsync(new ConfigBundleDto(), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task PlanAsync_ZeroSchemaVersion_IsRejected()
    {
        var service = Service();

        var plan = await service.PlanAsync(new ConfigBundleDto(SchemaVersion: 0), "test", CancellationToken.None);

        plan.IsSuccess.ShouldBeTrue();
        plan.Value.HasErrors.ShouldBeTrue();
        var detail = plan.Value.Items.Single().Report.Detail;
        detail.ShouldNotBeNull();
        detail.ShouldContain("0");
    }
}
