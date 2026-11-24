using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     The station configuration an ad-hoc broadcast inherits from the schedule its location
///     resolved to. Every field is optional: a location matching no configured station yields
///     <see cref="None" /> and the bulletin airs on platform defaults, exactly as before.
/// </summary>
/// <remarks>
///     Voice is deliberately NOT here. The operator picks it explicitly in the trigger studio
///     (pre-filled from the station's assigned profile), so the request already carries the
///     intended voice and overriding it would discard a deliberate choice. Station identity and
///     timezone are also left off: attaching them would enrol manual bulletins in same-day
///     continuity, which is a separate editorial decision, not part of "sound like this station".
/// </remarks>
public sealed record AdHocStationConfig
{
    /// <summary>No configured station backed this location: platform defaults apply.</summary>
    public static AdHocStationConfig None { get; } = new();

    public DeliveryStylePolicy? DeliveryStyle { get; init; }
    public string? DeliveryStyleId { get; init; }
    public int? DeliveryStyleVersion { get; init; }
    public PlaybookOverlay? Playbook { get; init; }
    public PronunciationLocator? PronunciationLocator { get; init; }
    public PronunciationLocator? GlobalPronunciationLocator { get; init; }
    public ResolvedBookends? Bookends { get; init; }
    public IReadOnlyList<FillSource>? FillOrder { get; init; }
    public BulletinContentSelection? Content { get; init; }
}

/// <summary>
///     Resolves the station configuration for a MANUAL broadcast (<c>POST /api/broadcast</c>,
///     the console's "Trigger now") whose free-text location matched a configured schedule.
/// </summary>
/// <remarks>
///     <para>
///         Before this existed, the manual path built its queue message from the request alone,
///         so a trigger on a fully configured station aired with NO delivery style, NO
///         pronunciation dictionary, NO bookends and the platform-default fill order, while the
///         console said only "Generates immediately". On dev that was 12 of 14 stations carrying
///         a style and a dictionary and 2 carrying bookends: the operator heard a read that was
///         not their station's and had nothing on screen to explain it (2026-08-10 UI audit).
///     </para>
///     <para>
///         Resolution delegates to the same <see cref="StationConfigResolver" /> the scheduled
///         poller uses, so a manual bulletin and a scheduled one for the same station resolve
///         their speech configuration identically — including the archived-dictionary and
///         unloadable-style fallbacks. Rotation cursors are NOT advanced: a manual trigger is not
///         a scheduled run, so it reads the current rotation position without consuming it.
///     </para>
/// </remarks>
public interface IAdHocStationConfigResolver
{
    /// <param name="marketId">Canonical market id already resolved from the request's location.</param>
    /// <param name="voiceModel">
    ///     The effective TTS model for this run. Pronunciation layering depends on it (phoneme
    ///     rules are unsafe on some models), so it must be the model that will actually synthesise.
    /// </param>
    Task<AdHocStationConfig> ResolveAsync(
        string marketId, string? voiceModel, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdHocStationConfigResolver" />
public sealed class AdHocStationConfigResolver : IAdHocStationConfigResolver
{
    private readonly IScheduleRepository _schedules;
    private readonly StationConfigResolver _resolver;
    private readonly ILogger<AdHocStationConfigResolver> _logger;

    public AdHocStationConfigResolver(
        IScheduleRepository schedules,
        IModelSelector modelSelector,
        IVoiceProfileService voiceProfileService,
        IVoiceProvider voiceProvider,
        IDeliveryStyleService deliveryStyleService,
        IBulletinPlaybookService playbookService,
        IBookendService bookendService,
        IPronunciationDictionaryService pronunciationDictionaryService,
        IOptions<BroadcastOptions> broadcastOptions,
        TimeProvider timeProvider,
        ILogger<AdHocStationConfigResolver> logger)
    {
        _schedules = schedules;
        _logger = logger;
        _resolver = new StationConfigResolver(
            modelSelector, voiceProfileService, voiceProvider, deliveryStyleService, playbookService,
            bookendService, pronunciationDictionaryService, broadcastOptions, timeProvider, logger);
    }

    public async Task<AdHocStationConfig> ResolveAsync(
        string marketId, string? voiceModel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId)) return AdHocStationConfig.None;

        var scheduleResult = await _schedules.GetScheduleAsync(marketId, cancellationToken);
        if (scheduleResult.IsFailure)
        {
            // Fail-soft, deliberately: an unreadable schedule must not block an ad-hoc bulletin.
            // The bulletin airs on platform defaults, which is the pre-existing behaviour.
            _logger.LogWarning(
                "[AdHoc] Could not read schedule '{MarketId}' to resolve station config: {Error}. "
                + "Bulletin will air with platform-default speech configuration.",
                marketId, scheduleResult.Error);
            return AdHocStationConfig.None;
        }

        var schedule = scheduleResult.Value;
        var delivery = await _resolver.ResolveDeliveryStyleAsync(schedule, cancellationToken);
        var playbook = await _resolver.ResolvePlaybookAsync(schedule, cancellationToken);
        var pronunciation = await _resolver.ResolvePronunciationStackAsync(
            schedule, voiceModel ?? string.Empty, cancellationToken);
        var bookends = await _resolver.ResolveBookendsAsync(schedule, cancellationToken);

        var config = new AdHocStationConfig
        {
            DeliveryStyle = delivery.Policy.Match(p => (DeliveryStylePolicy?)p, () => null),
            DeliveryStyleId = delivery.Id.Match(i => (string?)i, () => null),
            DeliveryStyleVersion = delivery.Version.Match(v => (int?)v, () => null),
            Playbook = playbook,
            PronunciationLocator = pronunciation.Station.Match(l => (PronunciationLocator?)l, () => null),
            GlobalPronunciationLocator = pronunciation.Global.Match(l => (PronunciationLocator?)l, () => null),
            Bookends = bookends.Match(b => (ResolvedBookends?)b, () => null),
            FillOrder = schedule.FillOrder is { Count: > 0 } ? schedule.FillOrder : null,
            Content = schedule.ContentSelection.Match(c => (BulletinContentSelection?)c, () => null)
        };

        _logger.LogInformation(
            "[AdHoc] Station config for '{MarketId}': delivery style {StyleId}, station dictionary {DictId}, "
            + "bookends {Bookends}, fill order {FillOrder}",
            marketId,
            config.DeliveryStyleId ?? "(none)",
            config.PronunciationLocator?.DictionaryId ?? "(none)",
            config.Bookends is null ? "(none)" : "resolved",
            config.FillOrder is null ? "(default)" : string.Join(",", config.FillOrder));
        return config;
    }
}
