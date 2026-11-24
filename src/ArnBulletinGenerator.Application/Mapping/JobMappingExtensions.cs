using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Response mapping: Domain Job → DTO conversions for HTTP responses.
///     ToSummaryDto() for list views, ToDetailDto() for detail views.
/// </summary>
public static class JobMappingExtensions
{
    private const string Iso = "o";

    /// <summary>
    ///     Maps a <see cref="Job" /> to a summary DTO for list views. Measured duration and its
    ///     delta from target are carried when the job has them and left null otherwise, on the
    ///     one flat DTO — see <see cref="JobSummaryDto" /> for why this is not a type hierarchy.
    /// </summary>
    public static JobSummaryDto ToSummaryDto(this Job job)
    {
        return new JobSummaryDto
        {
            JobId = job.JobId,
            CreatedAt = job.CreatedAt.ToString(Iso),
            Station = job.Station,
            Market = job.Market,
            TargetDurationSec = job.TargetDurationSeconds,
            Status = job.Status.ToStatusString(),
            LastUpdatedAt = job.LastUpdatedAt.ToString(Iso),
            Etag = job.ETag,
            MeasuredDurationSec = job.MeasuredDurationSec.Match(v => (double?)v, () => null),
            DeltaSec = job.MeasuredDurationSec.Match(
                v => (double?)(v - job.TargetDurationSeconds), () => null)
        };
    }

    /// <summary>Maps the storage-efficient list projection to the public list response.</summary>
    public static JobSummaryDto ToSummaryDto(this JobListItem item)
    {
        return new JobSummaryDto
        {
            JobId = item.JobId,
            CreatedAt = item.CreatedAt.ToString(Iso),
            Station = item.Station,
            Market = item.Market,
            TargetDurationSec = item.TargetDurationSeconds,
            Status = item.Status.ToStatusString(),
            LastUpdatedAt = item.LastUpdatedAt.ToString(Iso),
            Etag = item.ETag,
            MeasuredDurationSec = item.MeasuredDurationSec,
            DeltaSec = item.DeltaSeconds
        };
    }

    /// <summary>
    ///     Maps a <see cref="Job" /> to the appropriate detail DTO based on its current status.
    ///     <paramref name="audioUrl" /> is the job's CURRENT audio (what aired);
    ///     <paramref name="supersededTakeAudioUrl" /> is the separately-resolved audio of the
    ///     bulletin a fallback replaced, empty when there is none or it could not be resolved.
    /// </summary>
    public static JobDetailDtoBase ToDetailDto(this Job job, string audioUrl, string supersededTakeAudioUrl)
    {
        var bp = BuildBaseProperties(job, supersededTakeAudioUrl);

        return job.Status switch
        {
            JobStatus.Requested => ApplyBase(new RequestedJobDto(), bp),

            JobStatus.HereFetched => ApplyBase(new TrafficFetchedJobDto
            {
                Traffic = MapTrafficData(job)
            }, bp),

            JobStatus.IncidentsSelected or JobStatus.AwaitingCuration => ApplyBase(new IncidentsSelectedJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job)
            }, bp),
            JobStatus.ScriptReady => ApplyBase(new ScriptReadyJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job)
            }, bp),

            JobStatus.SynthesizingAudio => ApplyBase(new SynthesizingAudioJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl)
            }, bp),

            JobStatus.AudioReady => ApplyBase(new AudioReadyJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl)
            }, bp),

            JobStatus.WaitingApproval => ApplyBase(new WaitingApprovalJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl)
            }, bp),

            JobStatus.Approved => ApplyBase(new ApprovedJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Approval = MapApproval(job)
            }, bp),

            JobStatus.DeliveryPending => ApplyBase(new DeliveryPendingJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Delivery = new DeliveryPendingDataDto(
                    job.DeliveryKind.Match(k => k.ToString(), () => DeliveryKind.Publish.ToString()),
                    job.DeliveryIdempotencyKey ?? string.Empty,
                    job.DeliveryReviewedEtag ?? job.ETag ?? string.Empty,
                    job.DeliveryAttemptCount,
                    job.DeliveryRequestedBy ?? "unknown")
            }, bp),

            JobStatus.Rejected => ApplyBase(new RejectedJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Rejection = MapRejection(job),
                RejectionHistory = MapRejectionHistory(job)
            }, bp),

            JobStatus.Published => ApplyBase(new PublishedJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Approval = MapApproval(job),
                Publish = new PublishDataDto(
                    "broadcast",
                    job.PublishedAt.HasValue ? job.PublishedAt.Value.ToString(Iso) : job.LastUpdatedAt.ToString(Iso),
                    job.PublishedBy ?? "unknown"),
                // A published job that was covered keeps saying so: the deadline sweep and the
                // operator's own override both end here, and "what actually aired" must not
                // depend on reading the free-text log. Keyed on the REASON, not the source job
                // id: the standby cart has no source bulletin to point at
                // (Job.ApplyFillerFallback leaves it None) and was invisible here at first.
                Fallback = job.FallbackReason.HasValue ? MapFallback(job) : null
            }, bp),

            JobStatus.FallbackUsed => ApplyBase(new FallbackJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Fallback = MapFallback(job)
            }, bp),

            JobStatus.Failed => ApplyBase(new FailedJobDto
            {
                Failure = MapFailure(job),
                Traffic = HasTrafficData(job) ? MapTrafficData(job) : null,
                SelectedIncidents = HasTrafficData(job) ? MapIncidentDtos(job) : null,
                Script = job.ScriptText.HasValue ? MapScript(job) : null,
                Audio = job.AudioUrl.HasValue && !string.IsNullOrWhiteSpace(audioUrl)
                    ? MapAudio(job, audioUrl)
                    : null
            }, bp),

            JobStatus.Missed => ApplyBase(new MissedJobDto
            {
                Traffic = MapTrafficData(job),
                SelectedIncidents = MapIncidentDtos(job),
                Script = MapScript(job),
                Audio = MapAudio(job, audioUrl),
                Missed = new MissedDataDto(
                    job.MissedDeadlineUtc.Match(d => d.ToString(Iso), () => string.Empty),
                    job.LastUpdatedAt.ToString(Iso))
            }, bp),

            _ => ApplyBase(new RequestedJobDto(), bp)
        };
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    ///     Applies all base properties from <see cref="BaseProperties" /> to a DTO via the record <c>with</c> expression.
    ///     Eliminates per-case base property assignment repetition.
    /// </summary>
    private static T ApplyBase<T>(T dto, BaseProperties bp) where T : JobDetailDtoBase
    {
        return dto with
        {
            JobId = bp.JobId,
            Status = bp.Status,
            Station = bp.Station,
            Market = bp.Market,
            TargetDurationSec = bp.TargetDurationSec,
            CreatedAt = bp.CreatedAt,
            LastUpdatedAt = bp.LastUpdatedAt,
            Input = bp.Input,
            Logs = bp.Logs,
            SourceDiagnostics = bp.SourceDiagnostics,
            StatusHistory = bp.StatusHistory,
            Content = bp.Content,
            Weather = bp.Weather,
            Fuel = bp.Fuel,
            GoodRuns = bp.GoodRuns,
            Coverage = bp.Coverage,
            PronunciationDictionaryId = bp.PronunciationDictionaryId,
            PronunciationVersionId = bp.PronunciationVersionId,
            GlobalPronunciationDictionaryId = bp.GlobalPronunciationDictionaryId,
            GlobalPronunciationVersionId = bp.GlobalPronunciationVersionId,
            PreReview = bp.PreReview,
            Etag = bp.Etag,
            CurationDeadlineUtc = bp.CurationDeadlineUtc,
            SupersededTake = bp.SupersededTake
        };
    }

    private static string ToStatusString(this JobStatus status)
    {
        return status switch
        {
            JobStatus.Requested => "REQUESTED",
            JobStatus.HereFetched => "HERE_FETCHED",
            JobStatus.IncidentsSelected => "INCIDENTS_SELECTED",
            JobStatus.AwaitingCuration => "AWAITING_CURATION",
            JobStatus.ScriptReady => "SCRIPT_READY",
            JobStatus.SynthesizingAudio => "SYNTHESIZING_AUDIO",
            JobStatus.AudioReady => "AUDIO_READY",
            JobStatus.WaitingApproval => "WAITING_APPROVAL",
            JobStatus.Approved => "APPROVED",
            JobStatus.Rejected => "REJECTED",
            JobStatus.Published => "PUBLISHED",
            JobStatus.FallbackUsed => "FALLBACK_USED",
            JobStatus.Failed => "FAILED",
            JobStatus.Missed => "MISSED",
            JobStatus.DeliveryPending => "DELIVERY_PENDING",
            JobStatus.Cancelled => "CANCELLED",
            _ => status.ToString().ToUpperInvariant()
        };
    }

    private static readonly IReadOnlyDictionary<string, JobStatus> WireToStatus =
        Enum.GetValues<JobStatus>().ToDictionary(s => s.ToStatusString(), s => s, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Parses a comma-separated list of wire status strings (e.g. "WAITING_APPROVAL,FAILED")
    ///     into the matching <see cref="JobStatus" /> values. Blank/absent input means "no status
    ///     filter" (empty list). An unrecognised token fails the whole parse — surfaced by the
    ///     caller as a 400 — rather than silently degrading to the unfiltered dataset.
    /// </summary>
    public static Result<IReadOnlyList<JobStatus>> ParseStatusList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Result<IReadOnlyList<JobStatus>>.Success([]);

        var list = new List<JobStatus>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!WireToStatus.TryGetValue(token, out var status))
                return Result<IReadOnlyList<JobStatus>>.Failure($"Unrecognized status filter: '{token}'");
            if (!list.Contains(status))
                list.Add(status);
        }
        return Result<IReadOnlyList<JobStatus>>.Success(list);
    }

    private static BaseProperties BuildBaseProperties(Job job, string supersededTakeAudioUrl)
    {
        return new BaseProperties(
            job.JobId,
            job.Status.ToStatusString(),
            job.Station,
            job.Market,
            job.TargetDurationSeconds,
            job.CreatedAt.ToString(Iso),
            job.LastUpdatedAt.ToString(Iso),
            new JobInputDto(
                "traffic",
                new TimeWindowDto(
                    // The generation window for this bulletin, ordered as a valid interval:
                    // From = when standby coverage would kick in (FallbackDeadlineUtc, strictly
                    // BEFORE the slot by construction), To = the scheduled air slot
                    // (ScheduledForUtc). These were previously mapped the other way round,
                    job.FallbackDeadlineUtc.Match(d => d.ToString(Iso), () => string.Empty),
                    job.ScheduledForUtc.Match(d => d.ToString(Iso), () => string.Empty)),
                new BrandingDto(job.Station, string.Empty),
                job.VoiceConfig.Match(
                    vc => new VoiceConfigDto(
                        vc.Provider,
                        vc.VoiceId,
                        vc.VoiceName,
                        vc.Model,
                        vc.Settings.Match(
                            s => (VoiceSettingsDto?)s.ToDto(),
                            () => null)),
                    () => new VoiceConfigDto("unknown", string.Empty, string.Empty, string.Empty))),
            job.Logs.Select(l => new LogEntryDto(
                l.At.ToString(Iso),
                l.Level,
                l.Message)).ToList(),
            job.SourceDiagnostics.Select(d => new SourceFetchDiagnosticDto(
                d.SourceId,
                d.Outcome.ToString(),
                d.Detail,
                d.DataAsOfUtc?.ToString(Iso),
                d.StaleSeconds)).ToList(),
            job.StatusHistory.Select(s => new StatusHistoryItemDto(
                s.Status.ToStatusString(),
                s.At.ToString(Iso),
                s.By,
                s.Note.GetValueOr(string.Empty))).ToList(),
            job.ContentSelection.Match(c => (ContentSelectionDto?)c.ToDto(), () => null),
            job.Weather.Match(
                w => (WeatherReportDto?)new WeatherReportDto(
                    w.Conditions,
                    w.TemperatureC,
                    w.WindDescription.Match<string?>(v => v, () => null),
                    w.PrecipitationProbabilityPercent,
                    w.Alerts),
                () => null),
            job.Fuel.Match(
                f => (FuelReportDto?)new FuelReportDto(
                    f.Summaries.Select(s => new FuelSummaryDto(
                        s.FuelType, s.MinPrice, s.AveragePrice, s.StationCount, s.LatestModifiedUtc)).ToList()),
                () => null),
            job.GoodRunRoads.Count > 0 ? job.GoodRunRoads : null,
            job.Coverage.Match(c => (CoverageDto?)CoverageMapping.ToDto(c), () => null),
            job.PronunciationDictionaryId.Match<string?>(v => v, () => null),
            job.PronunciationVersionId.Match<string?>(v => v, () => null),
            job.GlobalPronunciationDictionaryId.Match<string?>(v => v, () => null),
            job.GlobalPronunciationVersionId.Match<string?>(v => v, () => null),
            MapPreReview(job.PreReview),
            job.ETag,
            // Slot-owned moment the curation sweep stops waiting: derived from ScheduledForUtc
            // minus the remaining lead, and always before FallbackDeadlineUtc. Empty means no
            // curation window was enabled or the deadline is unknown.
            job.CurationDeadlineUtc.Match(d => d.ToString(Iso), () => string.Empty),
            MapSupersededTake(job, supersededTakeAudioUrl));
    }
    private static bool HasTrafficData(Job job) =>
        job.IncidentsFetchedAt.HasValue || job.Incidents.Count > 0 || job.AllIncidents.Count > 0
        || job.Corridors.Count > 0 || job.FlowRoads.Count > 0 || job.MonitoredRoutes.Count > 0;


    private static TrafficDataDto MapTrafficData(Job job)
    {
        return new TrafficDataDto(
            job.IncidentsFetchedAt.Match(d => d.ToString(Iso), () => string.Empty),
            ComputeBbox(job),
            MapIncidentDtos(job.AllIncidents.Count > 0 ? job.AllIncidents : job.Incidents),
            job.Corridors.Select(c => c.ToCorridorDto()).ToList(),
            // Named arguments deliberately — same trap as AudioFinalizer's JobFlowRoad build:
            // FlowRoadDto's 8th positional slot is BaseScore (double?), NOT FunctionalClass (int).
            // Passing the class positionally converted implicitly and compiled clean while writing
            // the functional class into BaseScore, dropping BoostAmount entirely, and defaulting
            // FunctionalClass to 3 — so the review rundown showed a bare "(+)" with no amount even
            // once the job persisted the boost correctly.
            job.FlowRoads.Select(f => new FlowRoadDto(
                f.RoadName,
                f.JamFactor,
                Math.Round(f.LengthMeters / 1000.0, 1),
                f.IsWeighted,
                f.IsSelected,
                Math.Round(f.Score, 1),
                f.PriorityBoosted,
                BaseScore: f.BaseScore,
                BoostAmount: f.BoostAmount,
                FunctionalClass: f.FunctionalClass)).ToList(),
            job.MonitoredRoutes.Select(r => new MonitoredRouteDto(
                r.Name, r.DriveTimeMinutes, r.NormalDriveTimeMinutes, r.DelayMinutes, Math.Round(r.DistanceKm, 1))).ToList());
    }

    private static double[] ComputeBbox(Job job)
    {
        if (!job.Latitude.HasValue || !job.Longitude.HasValue || !job.RadiusKm.HasValue)
            return [];

        double lat = job.Latitude.Value;
        double lng = job.Longitude.Value;
        double r = job.RadiusKm.Value;

        // degrees per km at this latitude
        double latDeg = r / 111.32;
        double lngDeg = r / (111.32 * Math.Cos(lat * Math.PI / 180));

        return [lng - lngDeg, lat - latDeg, lng + lngDeg, lat + latDeg]; // [west, south, east, north]
    }

    // Selected-corridor incidents only — the subset that backs the bulletin (regeneration seed).
    private static List<JobIncidentDto> MapIncidentDtos(Job job) => MapIncidentDtos(job.Incidents);

    private static List<JobIncidentDto> MapIncidentDtos(IReadOnlyList<JobIncident> incidents)
    {
        return incidents.Select(ToDto).ToList();
    }

    /// <summary>The one incident-record wire mapping, shared with the Event Ledger's payload view.</summary>
    public static JobIncidentDto ToDto(this JobIncident i)
    {
        return new JobIncidentDto(
            i.Key,
            i.Priority,
            i.Headline,
            i.Detail.GetValueOr(string.Empty),
            i.FunctionalClass,
            i.LengthMeters,
            i.Latitude.Match(v => (double?)v, () => null),
            i.Longitude.Match(v => (double?)v, () => null),
            Shape: i.Shape?.Select(p => new CoveragePointDto(p.Lat, p.Lng)).ToList(),
            Direction: i.Direction.Match(v => (string?)v, () => null),
            LaneDetail: i.LaneDetail.Match(v => (string?)v, () => null),
            Comment: i.Comment.Match(v => (string?)v, () => null),
            RoadClosed: i.RoadClosed,
            EntryTimeUtc: i.EntryTimeUtc,
            Type: i.Type,
            StartTimeUtc: i.StartTimeUtc,
            EndTimeUtc: i.EndTimeUtc,
            Sources: i.Sources,
            LanesClosed: i.LanesClosed,
            LanesTotal: i.LanesTotal,
            OtherAssets: i.OtherAssets is { Count: > 0 } assets ? assets : null,
            CanonicalClass: i.CanonicalClass,
            ProseFields: i.ProseFields,
            SourceRecordIdentities: i.SourceRecordIdentities);
    }

    private static ScriptDataDto MapScriptData(Job job)
    {
        IReadOnlyList<ScriptSentenceDto>? sentences = job.ScriptSentences.Count > 0
            ? job.ScriptSentences.Select(s => new ScriptSentenceDto(
                s.Text, s.CorridorKeys, s.EvidenceIds ?? [], s.RouteKeys ?? [])).ToList()
            : null;

        string scriptText = job.ScriptText.GetValueOr(string.Empty);
        string spanText = job.TaggedScriptText.Match(
            tagged => DeliveryTags.WordsPreserved(scriptText, tagged) ? tagged : scriptText,
            () => scriptText);
        var roadNames = job.Corridors
            .Select(c => c.RoadName)
            .Concat(job.FlowRoads.Select(r => r.RoadName))
            .Concat(job.GoodRunRoads);
        IReadOnlyList<ScriptRoadPhraseDto>? roadPhraseSpans =
            ScriptProvenanceMapper.MapRoadPhraseSpans(spanText, roadNames) is { Count: > 0 } spans
                ? spans
                : null;

        return new ScriptDataDto(
            job.AttemptCount,
            scriptText,
            job.LastEstimatedDurationSec.GetValueOr(0),
            sentences,
            job.TaggedScriptText.Match<string?>(v => v, () => null),
            job.DeliveryStyleId.Match<string?>(v => v, () => null),
            job.DeliveryStyleVersion.Match<int?>(v => v, () => null),
            roadPhraseSpans);
    }

    /// <summary>The one pre-review wire mapping, shared by the job's own PreReview and the
    ///     superseded take's.</summary>
    private static AudioPreReviewDto? MapPreReview(Option<AudioPreReviewOutcome> preReview) =>
        preReview.Match(
            p => (AudioPreReviewDto?)new AudioPreReviewDto(p.MatchRatio, p.SuspectWords, p.NumberMismatches),
            () => null);

    /// <summary>
    ///     The bulletin a fallback replaced, projected read-only. <paramref name="audioUrl" /> is
    ///     resolved separately from the job's own audio (the job's now points at the cart or the
    ///     re-aired bulletin), and is empty when the take's blob could not be resolved.
    /// </summary>
    private static SupersededTakeDto? MapSupersededTake(Job job, string audioUrl)
    {
        return job.SupersededTake.Match<SupersededTakeDto?>(t => new SupersededTakeDto(
                t.CapturedAt.ToString(Iso),
                t.Reason,
                t.ScriptText,
                t.TaggedScriptText.Match<string?>(v => v, () => null),
                audioUrl,
                t.MeasuredDurationSec.Match(v => (double?)v, () => null),
                t.EstimatedDurationSec.Match(v => (double?)v, () => null),
                t.ScriptSentences.Select(s => new ScriptSentenceDto(
                    s.Text, s.CorridorKeys, s.EvidenceIds ?? [], s.RouteKeys ?? [])).ToList(),
                t.WordTimings.Match<IReadOnlyList<WordTimingDto>?>(
                    timings => timings.Select(w => new WordTimingDto(w.Word, w.StartSeconds, w.EndSeconds)).ToList(),
                    () => null),
                MapPreReview(t.PreReview),
                t.DeliveryStyleId.Match<string?>(v => v, () => null),
                t.DeliveryStyleVersion.Match<int?>(v => v, () => null)),
            () => null);
    }

    private static ScriptDto MapScript(Job job)
    {
        return new ScriptDto(MapScriptData(job));
    }

    private static AudioDataDto MapAudioData(Job job, string audioUrl)
    {
        return new AudioDataDto(
            "wav",
            44100,
            job.MeasuredDurationSec.GetValueOr(0),
            audioUrl);
    }

    private static AudioDto MapAudio(Job job, string audioUrl)
    {
        return new AudioDto(
            MapAudioData(job, audioUrl),
            job.MeasuredDurationSec.GetValueOr(0),
            job.MeasuredDurationSec.GetValueOr(0) - job.TargetDurationSeconds,
            job.WordTimings.Match<IReadOnlyList<WordTimingDto>?>(
                timings => timings.Select(t => new WordTimingDto(t.Word, t.StartSeconds, t.EndSeconds)).ToList(),
                () => null));
    }



    private static ApprovalDecisionDto MapApproval(Job job)
    {
        var approvalEntry = job.StatusHistory
            .LastOrDefault(s => s.Status is JobStatus.Approved);
        return new ApprovalDecisionDto(
            approvalEntry?.At.ToString(Iso) ?? job.LastUpdatedAt.ToString(Iso),
            approvalEntry?.By ?? "system");
    }

    /// <summary>
    ///     Where the aired audio came from when it was not this slot's own bulletin. Shared by
    ///     the FallbackUsed and Published shapes so a covered slot reads the same either side
    ///     of delivery.
    /// </summary>
    private static FallbackDataDto MapFallback(Job job) =>
        new(job.FallbackSourceJobId.GetValueOr(string.Empty),
            job.FallbackAppliedAt.Match(d => d.ToString(Iso), () => string.Empty),
            job.FallbackReason.GetValueOr(string.Empty),
            job.FallbackStreak);

    private static RejectionDataDto MapRejection(Job job)
    {
        var latestRejection = job.RejectionHistory.LastOrDefault();
        return new RejectionDataDto(
            latestRejection?.Reason ?? "unknown",
            latestRejection?.At.ToString(Iso) ?? job.LastUpdatedAt.ToString(Iso),
            latestRejection?.By ?? "system");
    }

    private static IReadOnlyList<RejectionHistoryEntryDto> MapRejectionHistory(Job job)
    {
        return job.RejectionHistory.Select(r => new RejectionHistoryEntryDto(
            r.Reason,
            r.Notes,
            r.RejectedScript.Match(v => (string?)v, () => null),
            r.At.ToString(Iso),
            r.By)).ToList();
    }

    /// <summary>
    ///     Maps the failed-at status to the activity that was in flight.
    ///     Pipeline statuses are set at phase START (HereFetched is stamped
    ///     with "Fetching traffic..."), so "failed at status X" means the
    ///     work AFTER that milestone died. The DTO contract speaks activity
    ///     stages (FETCHING_TRAFFIC, GENERATING_SCRIPT, ...), not statuses.
    /// </summary>
    private static string ToFailureStage(this JobStatus failedAt)
    {
        return failedAt switch
        {
            JobStatus.Requested => "REQUESTING",
            JobStatus.HereFetched => "FETCHING_TRAFFIC",
            JobStatus.IncidentsSelected or JobStatus.AwaitingCuration => "GENERATING_SCRIPT",
            JobStatus.ScriptReady => "SYNTHESIZING_AUDIO",
            JobStatus.WaitingApproval => "PUBLISHING",
            JobStatus.Approved => "PUBLISHING",
            JobStatus.Rejected => "REGENERATING",
            JobStatus.Failed => "FAILED",
            _ => "UNKNOWN"
        };
    }

    private static FailureDataDto MapFailure(Job job)
    {
        return new FailureDataDto(
            job.FailedAtStatus.Match(s => s.ToFailureStage(), () => "UNKNOWN"),
            job.ErrorMessage.GetValueOr("Unknown error"),
            job.LastUpdatedAt.ToString(Iso),
            job.AttemptCount,
            job.LastEstimatedDurationSec.Match(v => (double?)v, () => null),
            job.TargetDurationSeconds,
            job.AttemptHistory.Select(a => new AttemptResultDto(
                a.AttemptNumber,
                a.EstimatedDurationSec.Match(v => (double?)v, () => null),
                a.ActualDurationSec.Match(v => (double?)v, () => null),
                a.Succeeded,
                a.FailureReason.Match(v => (string?)v, () => null),
                a.At.ToString(Iso))).ToList());
    }
    private record BaseProperties(
        string JobId,
        string Status,
        string Station,
        string Market,
        double TargetDurationSec,
        string CreatedAt,
        string LastUpdatedAt,
        JobInputDto Input,
        IReadOnlyList<LogEntryDto> Logs,
        IReadOnlyList<SourceFetchDiagnosticDto> SourceDiagnostics,
        IReadOnlyList<StatusHistoryItemDto> StatusHistory,
        ContentSelectionDto? Content,
        WeatherReportDto? Weather,
        FuelReportDto? Fuel,
        IReadOnlyList<string>? GoodRuns,
        CoverageDto? Coverage,
        string? PronunciationDictionaryId,
        string? PronunciationVersionId,
        string? GlobalPronunciationDictionaryId,
        string? GlobalPronunciationVersionId,
        AudioPreReviewDto? PreReview,
        string? Etag,
        string CurationDeadlineUtc,
        SupersededTakeDto? SupersededTake);
}
