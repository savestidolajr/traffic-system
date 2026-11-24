using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>
///     Bundles every repository/service a config-import section plan builder might need, so
///     builder signatures stay uniform regardless of how many dependencies a given section uses.
/// </summary>
internal readonly record struct ConfigImportContext(
    IScheduleRepository ScheduleRepository,
    IScheduleService ScheduleService,
    IVoiceProfileService VoiceProfiles,
    IDeliveryStyleService DeliveryStyles,
    IBulletinPlaybookService Playbooks,
    IBookendService Bookends,
    IPronunciationDictionaryService PronunciationDictionaries,
    ISafetyTermRepository SafetyTerms,
    IGuidanceRepository Guidance,
    IPriorityRoadsRepository PriorityRoads,
    IVerifiedPronunciationRepository VerifiedPronunciations,
    IOperatorAssignmentRepository OperatorAssignments,
    IVoiceCalibrationRepository VoiceCalibrations,
    IFillerStatementsRepository FillerStatements,
    IFillerAudioCacheService FillerAudioCache,
    TimeProvider TimeProvider,
    ILogger Logger,
    IMarketAccessCacheInvalidator? MarketAccessCacheInvalidator = null);
