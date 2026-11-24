using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Functions.ConfigTransfer;

/// <summary>
///     Builds import-shaped configuration bundles by delegating all reads to the bounded,
///     dependency-aware <see cref="ConfigSnapshotReader" />. Keeping the existing constructor
///     shape preserves the Functions composition root and focused test fixtures; the reader is
///     owned by this builder and is scoped with it.
/// </summary>
public sealed class ConfigExportBuilder(
    IScheduleRepository scheduleRepository,
    IVoiceProfileService voiceProfiles,
    IDeliveryStyleService deliveryStyles,
    IBookendService bookends,
    IPronunciationDictionaryService pronunciationDictionaries,
    ISafetyTermRepository safetyTerms,
    IGuidanceRepository guidance,
    IPriorityRoadsRepository priorityRoads,
    IVerifiedPronunciationRepository verifiedPronunciations,
    IOperatorAssignmentRepository operatorAssignments,
    IVoiceCalibrationRepository voiceCalibrations,
    IFillerStatementsRepository fillerStatements,
    TimeProvider timeProvider)
{
    private readonly ConfigSnapshotReader snapshotReader = new(
        scheduleRepository, voiceProfiles, deliveryStyles, bookends, pronunciationDictionaries,
        safetyTerms, guidance, priorityRoads, verifiedPronunciations, operatorAssignments,
        voiceCalibrations, fillerStatements, timeProvider);

    public Task<Result<ConfigBundleDto>> BuildAsync(
        IReadOnlySet<string> sections, string schemaUrl, CancellationToken ct) =>
        snapshotReader.ReadAsync(sections, schemaUrl, ct);
}
