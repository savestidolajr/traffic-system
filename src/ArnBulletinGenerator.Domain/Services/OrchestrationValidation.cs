using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Mapping;
using ArnBulletinGenerator.Domain.Models;
using static ArnBulletinGenerator.Domain.Models.Option;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Validates orchestration request DTOs and converts them to domain types with fail-fast validation.
/// </summary>
public static partial class OrchestrationValidation
{
    public static Result<ValidatedOrchestrationRequestDto> Validate(OrchestrationRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Location))
            return Result<ValidatedOrchestrationRequestDto>.Failure("Location is required");

        if (dto.TargetDurationSeconds is null or <= 0)
            return Result<ValidatedOrchestrationRequestDto>.Failure(
                "TargetDurationSeconds is required and must be positive");

        if (dto.Coverage is null) return Result<ValidatedOrchestrationRequestDto>.Failure("Coverage is required");

        var coverageResult = CoverageMapping.ToDomain(dto.Coverage);
        if (coverageResult.IsFailure)
            return Result<ValidatedOrchestrationRequestDto>.Failure(coverageResult.Error);
        var coverage = coverageResult.Value;

        // A bulletin must name the voice it airs in. Blank strings collapse to None via
        // OfWhitespace, so "" / "   " count as absent — an under-specified trigger is rejected
        // here rather than silently falling back to a configured default voice, which is how
        // dev bulletins ended up airing in an unassigned voice under a stale hardcoded name.
        var voiceId = OfWhitespace(dto.VoiceId);
        var voiceProfileId = OfWhitespace(dto.VoiceProfileId);
        if (voiceId.IsNone && voiceProfileId.IsNone)
            return Result<ValidatedOrchestrationRequestDto>.Failure(
                "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");

        var voiceSettings = Option<VoiceSettings>.None;
        if (dto.VoiceSettings is not null)
        {
            var voiceResult = dto.VoiceSettings.ToDomain();
            if (voiceResult.IsFailure)
                return Result<ValidatedOrchestrationRequestDto>.Failure(voiceResult.Error);
            voiceSettings = Option<VoiceSettings>.Some(voiceResult.Value);
        }

        BulletinContentSelection? content = null;
        if (dto.Content is not null)
        {
            var segmentOrder = dto.Content.SegmentOrder?.Select(raw =>
                Enum.TryParse<BulletinSegment>(raw, ignoreCase: true, out var segment)
                    ? segment
                    : (BulletinSegment)(-1)).ToList();
            TrafficRunningOrder trafficOrder = Enum.TryParse(
                dto.Content.TrafficOrder, ignoreCase: true, out TrafficRunningOrder parsedTrafficOrder)
                ? parsedTrafficOrder
                : dto.Content.TrafficOrder is null
                    ? TrafficRunningOrder.WorstFirst
                    : (TrafficRunningOrder)(-1);
            content = new BulletinContentSelection(
                dto.Content.Traffic ?? true,
                dto.Content.Weather ?? false,
                dto.Content.Fuel ?? false,
                dto.Content.WeatherRequired ?? false,
                dto.Content.FuelRequired ?? false,
                segmentOrder,
                trafficOrder);
            if (!content.HasAnySource)
                return Result<ValidatedOrchestrationRequestDto>.Failure(
                    "At least one content source (traffic, weather, fuel) must be enabled.");
            // Required implies selected (same rule as the schedule PUT): a fixture flag on a
            // source that is not enabled would silently never air, which is the exact contract
            // violation "always include" exists to prevent.
            if (content.WeatherRequired && !content.Weather)
                return Result<ValidatedOrchestrationRequestDto>.Failure(
                    "WeatherRequired is set but weather is not enabled as a content source.");
            if (content.FuelRequired && !content.Fuel)
                return Result<ValidatedOrchestrationRequestDto>.Failure(
                    "FuelRequired is set but fuel is not enabled as a content source.");
            if (!content.HasValidSegmentOrder)
                return Result<ValidatedOrchestrationRequestDto>.Failure(
                    "SegmentOrder must be a unique permutation of enabled content segments.");
            if (!content.HasValidTrafficOrder)
                return Result<ValidatedOrchestrationRequestDto>.Failure(
                    "TrafficOrder must be WorstFirst or WorstLast.");
        }

        IReadOnlyList<FillSource>? fillOrder = null;
        if (dto.FillOrder is { Count: > 0 })
        {
            var parsed = new List<FillSource>(dto.FillOrder.Count);
            foreach (string raw in dto.FillOrder)
            {
                FillSource? source = raw?.Trim().ToLowerInvariant() switch
                {
                    "weather" => FillSource.Weather,
                    "fuel" => FillSource.Fuel,
                    "minortraffic" => FillSource.MinorTraffic,
                    "filler" => FillSource.Filler,
                    _ => null
                };
                if (source is null)
                    return Result<ValidatedOrchestrationRequestDto>.Failure(
                        $"Unknown fill source '{raw}'. Valid values: weather, fuel, minorTraffic, filler.");
                if (parsed.Contains(source.Value))
                    return Result<ValidatedOrchestrationRequestDto>.Failure(
                        $"Duplicate fill source '{raw}' in FillOrder.");
                parsed.Add(source.Value);
            }
            fillOrder = parsed;
        }

        return Result<ValidatedOrchestrationRequestDto>.Success(new ValidatedOrchestrationRequestDto
        {
            TargetDurationSeconds = dto.TargetDurationSeconds.Value,
            Location = dto.Location,
            Coverage = coverage,
            VoiceId = voiceId,
            VoiceProfileId = voiceProfileId,
            VoiceName = OfWhitespace(dto.VoiceName),
            VoiceProvider = OfWhitespace(dto.VoiceProvider),
            VoiceModel = OfWhitespace(dto.VoiceModel),
            VoiceSettings = voiceSettings,
            ScriptOnly = dto.ScriptOnly,
            Content = content,
            FillOrder = fillOrder
        });
    }
}

/// <summary>
///     Validated orchestration request - all fields guaranteed present.
/// </summary>
public sealed record ValidatedOrchestrationRequestDto
{
    public required double TargetDurationSeconds { get; init; }
    public required string Location { get; init; }
    public required CoverageArea Coverage { get; init; }
    public Option<string> VoiceId { get; init; }
    public Option<string> VoiceProfileId { get; init; }
    public Option<string> VoiceName { get; init; }
    public Option<string> VoiceProvider { get; init; }
    public Option<string> VoiceModel { get; init; }
    public Option<VoiceSettings> VoiceSettings { get; init; }
    public bool ScriptOnly { get; init; }

    /// <summary>WS1 content sources; null = traffic-only default.</summary>
    public BulletinContentSelection? Content { get; init; }

    /// <summary>Per-station fill priority; null = the BroadcastContext default (weather, fuel, filler).</summary>
    public IReadOnlyList<FillSource>? FillOrder { get; init; }
}
