using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

/// <summary>
///     Exhaustive Job persistence round-trip suite: proves Save→Load ≡ identity for the WHOLE
///     <see cref="Job" /> aggregate through the real <see cref="TableStorageJobRepository" />
///     mapping path (FitTableProperty/ExpandTableProperty compression, enum parsing,
///     Option↔nullable conversion, partition/index lookup) — not just the individual fields
///     <see cref="TableStorageJobRepositoryTests" /> and <see cref="JobSnapshotPersistenceTests" />
///     spot-check. A ratchet (<see cref="Job_PersistedSurface_IsFullyCoveredByThisSuite" />) fails
///     the build the moment a new Job property is added without a matching mapper entry and a
///     builder assignment here, so this seam can never silently regress again.
/// </summary>
public class JobPersistenceRoundTripTests
{
    private readonly ILogger<TableStorageJobRepository> _logger = Substitute.For<ILogger<TableStorageJobRepository>>();
    private readonly TableStorageJobRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public JobPersistenceRoundTripTests()
    {
        _sut = new TableStorageJobRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            TimeProvider.System,
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Round-trip vehicle ───────────────────────────────────────────

    private void SeedGet(JobEntity entity)
    {
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = entity.RowKey,
            Market = entity.Market
        };
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(indexEntity));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    /// <summary>
    ///     Saves <paramref name="job" /> through the real <see cref="TableStorageJobRepository.SaveJobAsync" />
    ///     path (capturing the entity the mapper actually upserts), stamps it with a storage-assigned
    ///     ETag/Timestamp the way Azure Table Storage would on write, then reloads it through the real
    ///     <see cref="TableStorageJobRepository.GetJobAsync" /> path — the exact round trip a job survives
    ///     in production (index lookup → partition-scoped get → MapToDomain).
    /// </summary>
    private async Task<(JobEntity Captured, Job Reloaded)> SaveAndReloadAsync(Job job)
    {
        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var saveResult = await _sut.SaveJobAsync(job);
        saveResult.IsSuccess.ShouldBeTrue(saveResult.Error);
        captured.ShouldNotBeNull();
        captured!.ETag = new ETag("srv-etag-1");
        captured.Timestamp = DateTimeOffset.UtcNow;

        SeedGet(captured);
        var getResult = await _sut.GetJobAsync(job.JobId);
        getResult.IsSuccess.ShouldBeTrue(getResult.Error);
        return (captured, getResult.Value);
    }

    // ── JSON diff ────────────────────────────────────────────────────

    private static JsonObject SerializeForComparison(Job job)
    {
        var node = JsonSerializer.SerializeToNode(job, JsonDefaults.Storage) as JsonObject;
        node.ShouldNotBeNull();
        // ETag is storage-assigned on write and ConcurrencyFingerprint is computed at load time
        // from the loaded entity's material columns — both legitimately diverge between the
        // pre-save and post-reload Job and are not part of the job's actual persisted data.
        node!.Remove("ETag");
        node.Remove("ConcurrencyFingerprint");
        return node;
    }

    /// <summary>Deep-diffs two JSON trees, returning one readable entry per differing leaf/shape, instead of a single giant string inequality.</summary>
    private static List<string> DiffJson(JsonNode? original, JsonNode? reloaded, string path = "$")
    {
        var diffs = new List<string>();

        if (original is null && reloaded is null) return diffs;
        if (original is null || reloaded is null)
        {
            diffs.Add($"{path}: original={original?.ToJsonString() ?? "null"} reloaded={reloaded?.ToJsonString() ?? "null"}");
            return diffs;
        }

        if (original is JsonObject originalObj && reloaded is JsonObject reloadedObj)
        {
            var keys = originalObj.Select(kv => kv.Key)
                .Union(reloadedObj.Select(kv => kv.Key))
                .OrderBy(k => k, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var hasOriginal = originalObj.TryGetPropertyValue(key, out var originalVal);
                var hasReloaded = reloadedObj.TryGetPropertyValue(key, out var reloadedVal);
                if (!hasReloaded)
                {
                    diffs.Add($"{path}.{key}: missing from reloaded job (original={originalVal?.ToJsonString() ?? "null"})");
                    continue;
                }
                if (!hasOriginal)
                {
                    diffs.Add($"{path}.{key}: unexpected on reloaded job (reloaded={reloadedVal?.ToJsonString() ?? "null"})");
                    continue;
                }
                diffs.AddRange(DiffJson(originalVal, reloadedVal, $"{path}.{key}"));
            }
            return diffs;
        }

        if (original is JsonArray originalArr && reloaded is JsonArray reloadedArr)
        {
            if (originalArr.Count != reloadedArr.Count)
            {
                diffs.Add($"{path}: array length original={originalArr.Count} reloaded={reloadedArr.Count}");
                return diffs;
            }
            for (int i = 0; i < originalArr.Count; i++)
                diffs.AddRange(DiffJson(originalArr[i], reloadedArr[i], $"{path}[{i}]"));
            return diffs;
        }

        var originalText = original.ToJsonString();
        var reloadedText = reloaded.ToJsonString();
        if (originalText != reloadedText)
            diffs.Add($"{path}: original={originalText} reloaded={reloadedText}");
        return diffs;
    }

    private static void AssertRoundTrips(Job original, Job reloaded)
    {
        var diffs = DiffJson(SerializeForComparison(original), SerializeForComparison(reloaded));
        diffs.ShouldBeEmpty($"Job round-trip mismatch(es) (Save→Load is not an identity):\n{string.Join("\n", diffs)}");
    }

    // ── Full-aggregate builder ───────────────────────────────────────

    /// <summary>
    ///     Builds a Job with every property <see cref="TableStorageJobRepository.MapToEntity" />/
    ///     <see cref="TableStorageJobRepository.MapToDomain" /> persists set to a distinctive,
    ///     non-default value, so a round trip through the real repository path exercises every
    ///     column mapping at once. <see cref="Job.ETag" /> and <see cref="Job.ConcurrencyFingerprint" />
    ///     are deliberately left unset — they are storage-assigned/load-computed, not authored.
    /// </summary>
    private static Job CreateFullyPopulatedJob()
    {
        var createdAt = new DateTime(2026, 3, 10, 6, 0, 0, DateTimeKind.Utc);
        var statusHistory = new List<StatusHistoryEntry>
        {
            new(JobStatus.Requested, createdAt, "system", Option<string>.Some("initial request")),
            new(JobStatus.HereFetched, createdAt.AddMinutes(1), "system"),
            new(JobStatus.ScriptReady, createdAt.AddMinutes(3), "generator", Option<string>.Some("script drafted")),
            new(JobStatus.WaitingApproval, createdAt.AddMinutes(5), "system"),
            new(JobStatus.Approved, createdAt.AddMinutes(8), "alice@arn.com.au", Option<string>.Some("looks good"))
        };
        var coverage = CoverageArea.Create(-27.4698, 153.0251, 42.5).Value;

        var job = Job.Reconstitute(
            jobId: "rt-full-9f24",
            station: "97.3FM",
            market: "Brisbane",
            targetDurationSeconds: 62.5,
            statusHistory: statusHistory,
            latitude: Option<double>.Some(-27.4698),
            longitude: Option<double>.Some(153.0251),
            radiusKm: Option<double>.Some(42.5),
            timezoneId: Option<string>.Some("Australia/Brisbane"),
            coverage: Option<CoverageArea>.Some(coverage),
            createdAt: createdAt,
            status: JobStatus.Approved,
            attemptCount: 3,
            fallbackStreak: 2);

        job.LastUpdatedAt = createdAt.AddMinutes(8);
        job.MeasuredDurationSec = Option<double>.Some(61.8);
        job.ScriptText = Option<string>.Some("Good morning Brisbane, here's your traffic on the 97.3.");
        job.TaggedScriptText = Option<string>.Some("<break time=\"0.3s\"/>Good morning Brisbane...");
        job.DeliveryStyleId = Option<string>.Some("energetic-drive");
        job.DeliveryStyleVersion = Option<int>.Some(4);
        job.BookendId = Option<string>.Some("bk-brisbane-drive");
        job.BookendVersion = Option<int>.Some(3);
        job.PronunciationDictionaryId = Option<string>.Some("brisbane-roads-v2");
        job.PronunciationVersionId = Option<string>.Some("pv-77");
        job.GlobalPronunciationDictionaryId = Option<string>.Some("platform-wide-v1");
        job.GlobalPronunciationVersionId = Option<string>.Some("pv-global-3");
        job.AudioUrl = Option<string>.Some("https://storage.example.com/audio/rt-full-9f24.mp3");
        job.AudioSha256 = "F" + new string('A', 63);
        job.ErrorMessage = Option<string>.Some("transient synth retry warning");
        job.ScheduledForUtc = Option<DateTime>.Some(new DateTime(2026, 3, 10, 6, 15, 0, DateTimeKind.Utc));
        job.CurationDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 3, 10, 6, 7, 30, DateTimeKind.Utc));
        job.FallbackDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 3, 10, 6, 20, 0, DateTimeKind.Utc));
        job.MissedDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 3, 10, 6, 30, 0, DateTimeKind.Utc));
        job.FallbackSourceJobId = Option<string>.Some("rt-full-source-77");
        job.FallbackAppliedAt = Option<DateTime>.Some(new DateTime(2026, 3, 10, 6, 18, 0, DateTimeKind.Utc));
        job.FallbackReason = Option<string>.Some("synthesis timeout");
        job.FailedAtStatus = Option<JobStatus>.Some(JobStatus.SynthesizingAudio);
        job.LastEstimatedDurationSec = Option<double>.Some(63.2);

        job.SetAttemptHistory(new[]
        {
            new AttemptResult(1, 3.1, false, createdAt.AddMinutes(2), Option<double>.Some(58.0), Option<double>.None,
                Option<string>.Some("too short")),
            new AttemptResult(2, 3.3, false, createdAt.AddMinutes(4), Option<double>.Some(60.5), Option<double>.Some(59.9),
                Option<string>.Some("timing drift")),
            new AttemptResult(3, 3.25, true, createdAt.AddMinutes(6), Option<double>.Some(62.0), Option<double>.Some(61.8),
                Option<string>.None)
        });

        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            Provider: "ElevenLabs",
            VoiceId: "voice-782f",
            VoiceName: "Harper",
            Model: "eleven_v3",
            Settings: Option<VoiceSettings>.Some(VoiceSettings.Create(0.62, 0.81, 0.15, true, 1.05).Value),
            PaceBoostWpm: 6.5));
        job.VoiceProfileId = Option<string>.Some("vp-harper-drive");
        job.VoiceProfileVersion = Option<int>.Some(2);

        job.WordTimings = Option<IReadOnlyList<WordTiming>>.Some(new List<WordTiming>
        {
            new("Good", 0.0, 0.32),
            new("morning", 0.32, 0.71),
            new("Brisbane", 0.71, 1.18)
        });

        job.PreReview = Option<AudioPreReviewOutcome>.Some(new AudioPreReviewOutcome(
            MatchRatio: 0.93,
            SuspectWords: new[] { "Woolloongabba", "Kessels" },
            NumberMismatches: new[] { "42 minutes vs 45 minutes" }));

        job.SetSupersededTake(Option<SupersededTake>.Some(new SupersededTake(
            CapturedAt: createdAt.AddMinutes(18),
            Reason: "missed-window",
            ScriptText: "The Ipswich Motorway is banked up inbound near Rocklea.",
            TaggedScriptText: Option<string>.Some("<break time=\"0.2s\"/>The Ipswich Motorway is banked up..."),
            AudioUrl: Option<string>.Some("broadcast_20260310_060412_rt-full-9f24.wav"),
            AudioSha256: "E" + new string('B', 63),
            MeasuredDurationSec: Option<double>.Some(59.4),
            EstimatedDurationSec: Option<double>.Some(60.1),
            ScriptSentences:
            [
                new JobScriptSentence("The Ipswich Motorway is banked up inbound near Rocklea.",
                    ["Ipswich Motorway"])
            ],
            WordTimings: Option<IReadOnlyList<WordTiming>>.Some(
                Enumerable.Range(0, 60).Select(i => new WordTiming($"word{i}", i * 0.4, i * 0.4 + 0.35)).ToList()),
            PreReview: Option<AudioPreReviewOutcome>.Some(new AudioPreReviewOutcome(
                0.88, ["Woolloongabba"], [])),
            DeliveryStyleId: Option<string>.Some("energetic-drive"),
            DeliveryStyleVersion: Option<int>.Some(4),
            BookendId: Option<string>.Some("bk-brisbane-drive"),
            BookendVersion: Option<int>.Some(3))));

        job.PendingRevoiceObservation = Option<RevoiceObservation>.Some(new RevoiceObservation(
            CapturedAtUtc: createdAt.AddMinutes(21),
            By: "producer.jane@arn.com.au",
            OriginalScript: "The Ipswich Motorway is experiencing delays inbound near Rocklea.",
            EditedScript: "The Ipswich Motorway is banked up inbound near Rocklea."));

        job.ContentSelection = Option<BulletinContentSelection>.Some(new BulletinContentSelection(true, true, true));

        job.Weather = Option<WeatherReport>.Some(new WeatherReport(
            Conditions: "Clear, hot",
            TemperatureC: 31.4,
            WindDescription: Option<string>.Some("Light north-easterly"),
            PrecipitationProbabilityPercent: 5,
            Alerts: new[] { "Heat warning" },
            Outlook: Option<string>.Some("Cooling later in the week"),
            IsForecast: true));

        job.Fuel = Option<FuelPriceReport>.Some(new FuelPriceReport(new[]
        {
            new FuelPriceSummary("Unleaded 91", 1.679, 1.732, 58, DateTimeOffset.Parse("2026-03-10T05:50:00Z"), "Fortitude Valley"),
            new FuelPriceSummary("Diesel", 1.799, 1.844, 40, DateTimeOffset.Parse("2026-03-10T05:45:00Z"), "Milton")
        }));

        job.GoodRunRoads = new[] { "Gateway Motorway", "Riverside Expressway" };

        job.SetCorridors(new[]
        {
            new JobCorridor("Ipswich Motorway", 2, 76.5, "congestion", "Heavy congestion inbound", 3, 4.2, 1, true,
                FunctionalClass: 1, Direction: "inbound", Suburb: "Rocklea", DriveTimeMinutes: 22,
                NormalDriveTimeMinutes: 14, DelayMinutes: 8, IncidentKeys: new[] { "inc-1", "inc-2" }, PriorityBoosted: true),
            new JobCorridor("Pacific Motorway", 4, 22.0, "crash", "Minor crash outbound", 1, 1.1, 2, false)
        });

        job.SetFlowRoads(new[]
        {
            new JobFlowRoad("Gateway Motorway", 0.4, 5200, true, true, Score: 88.5, PriorityBoosted: true, FunctionalClass: 1),
            new JobFlowRoad("Centenary Highway", 0.9, 3100, false, false)
        });

        job.SetScriptSentences(new[]
        {
            new JobScriptSentence("The Ipswich Motorway is banked up inbound near Rocklea.", new[] { "Ipswich Motorway" }),
            new JobScriptSentence("Gateway Motorway is running clear both ways.", new[] { "Gateway Motorway" })
        });

        job.SetIncidents(new[]
        {
            new JobIncident("inc-1", 1, "Crash on Ipswich Motorway", "Rocklea", FunctionalClass: 1, LengthMeters: 450,
                Detail: Option<string>.Some("Two lanes blocked"), Latitude: Option<double>.Some(-27.53),
                Longitude: Option<double>.Some(153.0),
                Shape: new[] { new CoveragePoint(-27.53, 153.0), new CoveragePoint(-27.531, 153.001) },
                Direction: Option<string>.Some("inbound"), LaneDetail: Option<string>.Some("Left 2 lanes closed"),
                Comment: Option<string>.Some("Tow truck en route"), RoadClosed: false, EntryTimeUtc: createdAt.AddMinutes(-10),
                SourceRecordIdentities: [new SourceRecordIdentity("here", "inc-1-current", "inc-1-original")])
        });

        job.SetAllIncidents(new[]
        {
            new JobIncident("inc-1", 1, "Crash on Ipswich Motorway", "Rocklea"),
            new JobIncident("inc-2", 2, "Roadworks on Pacific Motorway", "Eight Mile Plains", RoadClosed: true)
        });

        job.IncidentsFetchedAt = Option<DateTime>.Some(createdAt.AddMinutes(-2));
        job.SourceDiagnostics = new[]
        {
            new SourceFetchDiagnostic(
                "here", SourceFetchOutcome.Ok, null,
                DateTimeOffset.Parse("2026-03-10T05:58:00Z"), null),
            new SourceFetchDiagnostic(
                "qldtraffic", SourceFetchOutcome.ServedStale, "HTTP 429 (rate limited)",
                DateTimeOffset.Parse("2026-03-10T05:51:00Z"), 420),
            new SourceFetchDiagnostic(
                "trafficsa", SourceFetchOutcome.Failed, "HTTP 403 (forbidden)", null, null)
        };

        job.SetLogs(new[]
        {
            new JobLog(createdAt, JobLogLevel.Info, "Job requested"),
            new JobLog(createdAt.AddMinutes(1), JobLogLevel.Warn, "HERE returned partial data"),
            new JobLog(createdAt.AddMinutes(6), JobLogLevel.Error, "Synth retry required")
        });

        job.SetRejectionHistory(new[]
        {
            new RejectionEntry("Wrong pronunciation", "Fix 'Woolloongabba'", createdAt.AddMinutes(3), "bob@arn.com.au",
                Option<string>.Some("Old rejected script text"))
        });

        job.PreviousScriptContext = Option<string>.Some("Earlier bulletin mentioned the M1 southbound clearing.");
        job.PreviousJobId = Option<string>.Some("rt-prev-55");
        job.IncidentChangeSummary = Option<IncidentChangeSummary>.Some(new IncidentChangeSummary(
            createdAt.AddMinutes(-1), NewCount: 2, UpdatedCount: 1, ClearedCount: 3,
            SummaryText: Option<string>.Some("2 new, 1 updated, 3 cleared")));
        job.NarrativeIncidentChangeSummary = Option<IncidentChangeSummary>.Some(new IncidentChangeSummary(
            createdAt.AddMinutes(-1), NewCount: 1, UpdatedCount: 0, ClearedCount: 5,
            SummaryText: Option<string>.Some("Narrative: M1 cleared")));

        job.MonitoredRoutes = new[]
        {
            new MonitoredRouteStatus("Gateway to Airport", new CoveragePoint(-27.42, 153.03), new CoveragePoint(-27.39, 153.12),
                24, 16, 18.4)
        };

        job.ApprovedBy = "alice@arn.com.au";
        job.ApprovedByObjectId = "11111111-2222-3333-4444-555555555555";
        job.ApprovedAt = new DateTimeOffset(createdAt.AddMinutes(8));
        job.PublishedBy = "carol@arn.com.au";
        job.PublishedByObjectId = "66666666-7777-8888-9999-000000000000";
        job.PublishedAt = new DateTimeOffset(createdAt.AddMinutes(10));
        job.DeliveryIdempotencyKey = "delivery-rt-idem";
        job.DeliveryReviewedEtag = "reviewed-etag-rt";
        job.DeliveryKind = Option<DeliveryKind>.Some(DeliveryKind.Fallback);
        job.DeliveryRequestedBy = "operator.delivery@arn.com.au";
        job.DeliveryRequestedByObjectId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        job.DeliveryFallbackSourceJobId = "rt-fallback-source";
        job.DeliveryFallbackAudioUrl = "https://storage.example.com/audio/rt-fallback.wav";
        job.DeliveryFallbackReason = "approved take exceeded the playout deadline";
        job.DeliveryFallbackNotes = "Fallback cart selected by the delivery worker.";
        job.DeliveryFallbackDurationSeconds = 58.6;
        job.DeliveryAttemptCount = 2;
        job.DeliveryLastAttemptAt = new DateTimeOffset(createdAt.AddMinutes(19));
        job.DeliveryLastError = "previous playout attempt timed out";


        job.GeneratorInput = new GeneratorInputSnapshot
        {
            TargetDurationSeconds = 62.5,
            Location = "Brisbane CBD",
            City = Option<string>.Some("Brisbane"),
            MarketId = Option<string>.Some("brisbane"),
            Content = new BulletinContentSelection(true, true, true),
            Corridors = new List<TrafficCorridor>
            {
                new("Ipswich Motorway", 2, 1, 4200, 76.5, "congestion", "Heavy congestion inbound",
                    new List<TrafficIncident> { new("inc-1", "Crash on Ipswich Motorway", 1, "Rocklea", "accident") },
                    Suburb: Option<string>.Some("Rocklea"), Direction: Option<string>.Some("inbound"))
            },
            MonitoredRoutes = new List<MonitoredRouteStatus>
            {
                new("Gateway to Airport", new CoveragePoint(-27.42, 153.03), new CoveragePoint(-27.39, 153.12), 24, 16, 18.4)
            },
            GoodRunRoads = new List<string> { "Gateway Motorway" },
            FlowPool = new List<FlowRoad> { new("Gateway Motorway", 0.4, 5200, true, 1, 88.5, true) },
            Weather = Option<WeatherReport>.Some(new WeatherReport("Clear, hot", 31.4,
                Option<string>.Some("Light north-easterly"), 5, new[] { "Heat warning" })),
            Fuel = Option<FuelPriceReport>.Some(new FuelPriceReport(new[]
            {
                new FuelPriceSummary("Unleaded 91", 1.679, 1.732, 58)
            }))
        };

        return job;
    }

    [Fact]
    public async Task FullyPopulatedJob_RoundTrips_ThroughRealRepositoryPath()
    {
        var job = CreateFullyPopulatedJob();

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.ShouldNotBeNull();
        captured!.SourceDiagnosticsJson!.ShouldContain("\"Outcome\":\"ServedStale\"");
        captured!.SourceDiagnosticsJson!.ShouldContain("\"Detail\":\"HTTP 429 (rate limited)\"");
        reloaded.SourceDiagnostics.Count.ShouldBe(3);
        reloaded.SourceDiagnostics[1].Outcome.ShouldBe(SourceFetchOutcome.ServedStale);
        reloaded.SourceDiagnostics[1].StaleSeconds.ShouldBe(420);
        reloaded.SourceDiagnostics[1].DataAsOfUtc.ShouldNotBeNull();
        AssertRoundTrips(job, reloaded);
    }

    [Fact]
    public async Task SupersededTake_RoundTrips_ThroughTheSizeFittedColumn()
    {
        // The take's script IS the only surviving record of a bulletin a fallback destroyed, and it
        // rides a FitTableProperty column — so the read path MUST expand before deserializing. A
        // missing ExpandTableProperty hands a "gz:"-prefixed string to the deserializer and the
        // column silently reads back as None, which is exactly the data loss this field exists to
        // stop. Word timings are included because they are what pushes the payload toward the cap.
        var job = CreateFullyPopulatedJob();
        var original = job.SupersededTake.Value;

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.SupersededTakeJson.ShouldNotBeNull();
        var take = reloaded.SupersededTake.Value;
        take.ScriptText.ShouldBe(original.ScriptText);
        take.Reason.ShouldBe("missed-window");
        take.CapturedAt.ShouldBe(original.CapturedAt);
        take.AudioUrl.Value.ShouldBe("broadcast_20260310_060412_rt-full-9f24.wav");
        take.ScriptSentences.Count.ShouldBe(original.ScriptSentences.Count);
        take.WordTimings.Value.Count.ShouldBe(60);
        take.PreReview.Value.MatchRatio.ShouldBe(0.88);
        take.MeasuredDurationSec.Value.ShouldBe(59.4);
        take.EstimatedDurationSec.Value.ShouldBe(60.1);
    }

    [Fact]
    public async Task PendingRevoiceObservation_RoundTrips_ThroughTheSizeFittedColumn()
    {
        // The observation carries two full scripts and rides the same FitTableProperty column
        // contract as the retained take, so the read path MUST expand before deserializing.
        var job = CreateFullyPopulatedJob();
        var original = job.PendingRevoiceObservation.Value;

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.RevoiceObservationJson.ShouldNotBeNull();
        var observation = reloaded.PendingRevoiceObservation.Value;
        observation.OriginalScript.ShouldBe(original.OriginalScript);
        observation.EditedScript.ShouldBe(original.EditedScript);
        observation.By.ShouldBe("producer.jane@arn.com.au");
        observation.CapturedAtUtc.ShouldBe(original.CapturedAtUtc);
    }

    [Fact]
    public async Task CurationDeadlineUtc_RoundTrips_ThroughRealRepositoryReload()
    {
        var job = CreateFullyPopulatedJob();
        var expected = job.CurationDeadlineUtc.Value;

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.CurationDeadlineUtc.ShouldBe(new DateTimeOffset(expected));
        reloaded.CurationDeadlineUtc.HasValue.ShouldBeTrue();
        reloaded.CurationDeadlineUtc.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task LegacySingularOfficialEvidence_LoadsIntoPluralReceiptList()
    {
        var job = CreateFullyPopulatedJob();
        var (captured, _) = await SaveAndReloadAsync(job);

        var corridors = JsonNode.Parse(captured.CorridorsJson!)!.AsArray();
        var first = corridors[0]!.AsObject();
        first.Remove("OfficialEvidences");
        first["OfficialEvidence"] = JsonSerializer.SerializeToNode(
            new OfficialEvidence("livetraffic-nsw", "Crash on Ipswich Motorway", true),
            JsonDefaults.Storage);
        captured.CorridorsJson = corridors.ToJsonString(JsonDefaults.Storage);

        SeedGet(captured);
        var result = await _sut.GetJobAsync(job.JobId);

        result.IsSuccess.ShouldBeTrue(result.Error);
        var evidence = result.Value.Corridors[0].OfficialEvidences.ShouldHaveSingleItem();
        evidence.SourceId.ShouldBe("livetraffic-nsw");
        evidence.Headline.ShouldBe("Crash on Ipswich Motorway");
    }

    [Fact]
    public async Task LegacyRowWithoutSourceDiagnostics_LoadsWithEmptyDiagnostics()
    {
        var job = CreateFullyPopulatedJob();
        var (captured, _) = await SaveAndReloadAsync(job);

        captured.SourceDiagnosticsJson = null;
        SeedGet(captured);
        var result = await _sut.GetJobAsync(job.JobId);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.SourceDiagnostics.ShouldBeEmpty();
    }

    // ── The ratchet ───────────────────────────────────────────────────

    [Fact]
    public void Job_PersistedSurface_IsFullyCoveredByThisSuite()
    {
        // Every property MapToEntity/MapToDomain actually persist, and that
        // CreateFullyPopulatedJob() above populates.
        var persisted = new HashSet<string>
        {
            "JobId", "Station", "Market", "TargetDurationSeconds", "CreatedAt", "LastUpdatedAt", "Status",
            "MeasuredDurationSec", "ScriptText", "GeneratorInput", "TaggedScriptText", "DeliveryStyleId",
            "DeliveryStyleVersion", "VoiceProfileId", "VoiceProfileVersion", "BookendId", "BookendVersion",
            "PronunciationDictionaryId", "PronunciationVersionId",
            "GlobalPronunciationDictionaryId", "GlobalPronunciationVersionId", "AudioUrl",
            "AudioSha256", "ErrorMessage", "ScheduledForUtc", "CurationDeadlineUtc", "FallbackDeadlineUtc", "MissedDeadlineUtc",
            "FallbackSourceJobId", "FallbackAppliedAt", "FallbackReason", "FallbackStreak", "FailedAtStatus",
            "AttemptCount",
            "LastEstimatedDurationSec", "AttemptHistory", "VoiceConfig", "WordTimings", "PreReview",
            "ContentSelection", "Weather", "Fuel", "GoodRunRoads", "Corridors", "FlowRoads",
            "ScriptSentences", "Incidents", "AllIncidents", "IncidentsFetchedAt", "SourceDiagnostics",
            "Logs", "StatusHistory", "RejectionHistory", "Latitude", "Longitude", "RadiusKm", "Coverage", "TimezoneId",
            "PreviousScriptContext", "PreviousJobId", "IncidentChangeSummary", "NarrativeIncidentChangeSummary",
            "MonitoredRoutes", "ApprovedBy", "ApprovedByObjectId", "ApprovedAt", "PublishedBy",
            "DeliveryIdempotencyKey", "DeliveryReviewedEtag", "DeliveryKind", "DeliveryRequestedBy",
            "DeliveryRequestedByObjectId", "DeliveryFallbackSourceJobId", "DeliveryFallbackAudioUrl",
            "DeliveryFallbackReason", "DeliveryFallbackNotes", "DeliveryFallbackDurationSeconds",
            "DeliveryAttemptCount", "DeliveryLastAttemptAt", "DeliveryLastError",
            "PublishedByObjectId", "PublishedAt", "SupersededTake", "PendingRevoiceObservation"
        };

        // Deliberately NOT persisted by MapToEntity/MapToDomain, and therefore excluded from
        // both CreateFullyPopulatedJob() and the round-trip equality check:
        var excluded = new Dictionary<string, string>
        {
            ["ETag"] = "storage-assigned optimistic-concurrency token, not authored job data",
            ["ConcurrencyFingerprint"] = "computed by ComputeFingerprint(entity) at load time, not persisted itself"
        };

        var expected = persisted.Union(excluded.Keys).ToHashSet();
        var actual = typeof(Job)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var newlyAdded = actual.Except(expected).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var noLongerExists = expected.Except(actual).OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (newlyAdded.Count > 0)
            Assert.Fail(string.Join("\n", newlyAdded.Select(name =>
                $"New Job property '{name}': populate it in CreateFullyPopulatedJob, map it in " +
                "TableStorageJobRepository.MapToEntity AND MapToDomain, then add it here.")));

        noLongerExists.ShouldBeEmpty(
            $"Job no longer has these properties this suite still lists — remove them from the " +
            $"expected/excluded sets above: {string.Join(", ", noLongerExists)}");
    }

    // ── Edge cases ────────────────────────────────────────────────────

    [Fact]
    public async Task OversizedCompressibleColumn_IsStoredGzipped_AndStillRoundTripsExactly()
    {
        // An incident-heavy market (observed live: adelaide 31-33K chars on EVERY job) can push a
        // single JSON column past Azure Table Storage's 32K-char per-property limit. It must
        // gzip-compress ("gz:" sentinel) rather than drop, and the FULL job (not just this one
        // column) must still round-trip exactly.
        var job = CreateFullyPopulatedJob();
        var hugePool = Enumerable.Range(0, 600)
            .Select(i => new JobIncident($"gen-inc-{i}", 1,
                $"Roadworks plus a broken-down vehicle causing lane closures and heavy delays at point {i} 🚧",
                $"Road {i}"))
            .ToArray();
        job.SetAllIncidents(hugePool);
        var serializedAllIncidents = JsonSerializer.Serialize(hugePool, JsonDefaults.Storage);
        serializedAllIncidents.Length.ShouldBeGreaterThan(30_000);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.AllIncidentsJson.ShouldNotBeNull();
        captured.AllIncidentsJson!.ShouldStartWith("gz:");
        captured.AllIncidentsJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        AssertRoundTrips(job, reloaded);
    }

    [Fact]
    public async Task UnicodeEmojiNewlinesAndPipes_RoundTripExactly()
    {
        // Script/road/log text carries live operator and TTS-provider content: emoji, non-ASCII
        // place names, embedded newlines, and pipe characters (a plausible Table Storage/CSV
        // delimiter collision) must all survive gzip-or-not, Option-wrap-or-not, unmangled.
        var job = CreateFullyPopulatedJob();
        job.ScriptText = Option<string>.Some(
            "G'day! ⚠️ Traffic's heavy on the M1 near Yatala.\nExpect delays | plan ahead. Café closures too — ½ the carpark's shut.");
        job.TaggedScriptText = Option<string>.Some("<break/>G'day! ⚠️ Traffic's heavy…\nLine two | pipe.");
        job.GoodRunRoads = new[] { "Kūrilpa Bridge", "St Lucia | Riverside walk", "Ōtira Rd\nnorthbound" };
        job.SetLogs(new[]
        {
            new JobLog(job.CreatedAt, JobLogLevel.Info, "Fetched incidents for 東京 test-market 🗾 | ok"),
            new JobLog(job.CreatedAt.AddMinutes(1), JobLogLevel.Warn, "Line one\nLine two\tTabbed | piped")
        });
        job.SetIncidents(new[]
        {
            new JobIncident("uni-1", 1, "Crash — Woolloongabba (½ lane) 🚗💥", "Kūrilpa Bridge",
                Comment: Option<string>.Some("Operator note: café closed | detour via Ōtira Rd\nsee map"))
        });
        job.SetAllIncidents(job.Incidents.ToArray());

        var (_, reloaded) = await SaveAndReloadAsync(job);

        AssertRoundTrips(job, reloaded);
        reloaded.ScriptText.Value.ShouldContain("⚠️");
        reloaded.ScriptText.Value.ShouldContain("\n");
        reloaded.ScriptText.Value.ShouldContain("|");
        reloaded.GoodRunRoads.ShouldContain("Kūrilpa Bridge");
        reloaded.Logs[0].Message.ShouldContain("東京");
        reloaded.Logs[0].Message.ShouldContain("🗾");
    }

    [Fact]
    public async Task MinimalFreshJob_RoundTrips()
    {
        // A brand-new Job.Create() with nothing else ever set (the very first save in a job's
        // life) must round-trip cleanly too — every optional field is at its true default, not
        // just "empty but populated" like the full builder above.
        var job = Job.Create("rt-minimal-1", "2GB", "Sydney", 90, DateTime.UtcNow).Value;

        var (_, reloaded) = await SaveAndReloadAsync(job);

        AssertRoundTrips(job, reloaded);
    }

    [Fact]
    public async Task IncompressiblyOversizedColumn_IsDroppedWithWarning_CharacterizationOfCurrentBehavior()
    {
        // CHARACTERIZATION, not a round-trip pass: this documents CURRENT, INTENTIONAL behavior
        // rather than a bug. Per TableStorageJobRepository.FitTableProperty (see its comment
        // citing the 2026-07-16 adelaide incident): when even the GZIP-compressed form of a
        // column still exceeds the 30,000-char Table Storage cap, the column is dropped (with a
        // warning) so the REST of the job still persists, rather than aborting the whole write.
        // AllIncidents is therefore expected to legitimately diverge (empty on reload) here —
        // asserting that divergence explicitly instead of silently loosening AssertRoundTrips.
        var job = CreateFullyPopulatedJob();
        var incompressible = Enumerable.Range(0, 1500)
            .Select(i => new JobIncident($"gen-inc-{i}", 1,
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}",
                $"{Guid.NewGuid():N}"))
            .ToArray();
        job.SetAllIncidents(incompressible);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.AllIncidentsJson.ShouldBeNull();
        reloaded.AllIncidents.ShouldBeEmpty();

        // Everything else on the job — including the small, essential Incidents column and every
        // other property CreateFullyPopulatedJob sets — is unaffected by the one dropped column.
        var originalMinusAllIncidents = SerializeForComparison(job);
        originalMinusAllIncidents.Remove("AllIncidents");
        var reloadedMinusAllIncidents = SerializeForComparison(reloaded);
        reloadedMinusAllIncidents.Remove("AllIncidents");
        var diffs = DiffJson(originalMinusAllIncidents, reloadedMinusAllIncidents);
        diffs.ShouldBeEmpty($"Unrelated properties should be unaffected by the dropped column:\n{string.Join("\n", diffs)}");
    }

    [Fact]
    public async Task CreatedAtAndLastUpdatedAt_RoundTrip_AsExactUtcInstant()
    {
        var job = CreateFullyPopulatedJob();
        job.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        job.LastUpdatedAt.Kind.ShouldBe(DateTimeKind.Utc);

        var (_, reloaded) = await SaveAndReloadAsync(job);

        reloaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        reloaded.CreatedAt.ShouldBe(job.CreatedAt);
        reloaded.LastUpdatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        reloaded.LastUpdatedAt.ShouldBe(job.LastUpdatedAt);
    }

    // ── VC-1 / VC-2: LogsJson and RejectionHistoryJson share the storage-ceiling contract ──

    /// <summary>
    ///     Performs a second, terminal state write on an already-loaded job (which carries a
    ///     storage ETag, so the write goes through the optimistic-concurrency
    ///     UpdateEntityAsync path rather than the initial upsert) and returns the entity the
    ///     mapper actually wrote. Proves the row — including its oversized history columns —
    ///     is still writable and can reach a terminal state (VC-1: past the ceiling every
    ///     state write used to 500, so the job could never be published/rejected).
    /// </summary>
    private async Task<JobEntity> SaveToTerminalStateAsync(Job reloaded)
    {
        var publishResult = reloaded.Publish(DateTime.UtcNow, "tester");
        publishResult.IsSuccess.ShouldBeTrue(publishResult.Error);

        JobEntity? updated = null;
        _tableClient.UpdateEntityAsync(
                Arg.Do<JobEntity>(e => updated = e),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var saveResult = await _sut.SaveJobAsync(reloaded);
        saveResult.IsSuccess.ShouldBeTrue(saveResult.Error);
        updated.ShouldNotBeNull();
        updated!.Status.ShouldBe(JobStatus.Published.ToString());
        return updated;
    }

    [Fact]
    public async Task OversizedLogsJson_IsGzipped_RoundTripsExactly_AndStillAcceptsTerminalStateWrite()
    {
        // LogsJson is written at every pipeline step, so it is the column most likely to hit
        // the 30,000-char storage threshold. Compressible log text (the normal case) must
        // gzip-compress ("gz:" sentinel) losslessly — every entry survives the round trip —
        // and the job must still accept a subsequent terminal state write.
        var job = CreateFullyPopulatedJob();
        var hugeLogs = Enumerable.Range(0, 300)
            .Select(i => new JobLog(job.CreatedAt.AddMinutes(i), JobLogLevel.Info,
                $"Pipeline step {i}: fetched traffic showing heavy congestion and lane closures on the Pacific Motorway at point {i}"))
            .ToArray();
        job.SetLogs(hugeLogs);
        JsonSerializer.Serialize(hugeLogs, JsonDefaults.Storage).Length.ShouldBeGreaterThan(30_000);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.LogsJson.ShouldNotBeNull();
        captured.LogsJson!.ShouldStartWith("gz:");
        captured.LogsJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        AssertRoundTrips(job, reloaded);

        var updated = await SaveToTerminalStateAsync(reloaded);
        updated.LogsJson!.ShouldStartWith("gz:");
        updated.LogsJson.Length.ShouldBeLessThanOrEqualTo(30_000);
    }

    [Fact]
    public async Task OversizedRejectionHistory_IsGzipped_RoundTripsExactly_AndStillAcceptsTerminalStateWrite()
    {
        // VC-2: rejection rounds carry the full rejected script plus free-text notes. Multiple
        // rounds on one bulletin (reject → regenerate → reject…) accumulate on the same row;
        // compressible round content (the normal case) must gzip losslessly and the job must
        // still accept a subsequent terminal state write.
        var job = CreateFullyPopulatedJob();
        var rounds = Enumerable.Range(0, 70)
            .Select(i => new RejectionEntry(
                $"Reason {i}",
                $"Operator feedback round {i}: the bulletin missed the crash near point {i} and the lane closures on the motorway — please regenerate with the correct incidents.",
                job.CreatedAt.AddMinutes(i),
                $"operator{i}@arn.com.au",
                Option<string>.Some(
                    $"Good morning listeners, here is the traffic update: the motorway is banked up at point {i} with heavy delays of about {i * 3} minutes inbound towards the city this morning, and roadworks are blocking a lane at point {i}.")))
            .ToArray();
        job.SetRejectionHistory(rounds);
        JsonSerializer.Serialize(rounds, JsonDefaults.Storage).Length.ShouldBeGreaterThan(30_000);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.RejectionHistoryJson.ShouldNotBeNull();
        captured.RejectionHistoryJson!.ShouldStartWith("gz:");
        captured.RejectionHistoryJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        AssertRoundTrips(job, reloaded);

        var updated = await SaveToTerminalStateAsync(reloaded);
        updated.RejectionHistoryJson!.ShouldStartWith("gz:");
        updated.RejectionHistoryJson.Length.ShouldBeLessThanOrEqualTo(30_000);
    }

    [Fact]
    public async Task OversizedLogsJson_Incompressible_TrimsOldestEntries_PreservesNewest_AndStillAcceptsWrites()
    {
        // Incompressible log payloads (GUID-heavy entries) defeat gzip, so the column must
        // degrade to the trim rung: the OLDEST entries are dropped, the most recent (and
        // terminal-state) entries survive verbatim, and the row stays writable — never a 500.
        var job = CreateFullyPopulatedJob();
        var hugeLogs = Enumerable.Range(0, 700)
            .Select(i => new JobLog(job.CreatedAt.AddMinutes(i), JobLogLevel.Info,
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N} step {i}"))
            .ToArray();
        job.SetLogs(hugeLogs);
        JsonSerializer.Serialize(hugeLogs, JsonDefaults.Storage).Length.ShouldBeGreaterThan(30_000);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.LogsJson.ShouldNotBeNull();
        captured.LogsJson!.ShouldNotStartWith("gz:");
        captured.LogsJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        reloaded.Logs.Count.ShouldBeLessThan(hugeLogs.Length);
        reloaded.Logs.Count.ShouldBeGreaterThan(0);
        // Trimming keeps the NEWEST entries: the last one survives byte-for-byte.
        reloaded.Logs[^1].ShouldBe(hugeLogs[^1]);
        // The OLDEST entries were dropped first.
        reloaded.Logs[0].At.ShouldBeGreaterThan(hugeLogs[0].At);

        var updated = await SaveToTerminalStateAsync(reloaded);
        updated.LogsJson!.Length.ShouldBeLessThanOrEqualTo(30_000);
    }

    [Fact]
    public async Task OversizedRejectionHistory_Incompressible_TrimsOldestRounds_PreservesNewest_AndStillAcceptsWrites()
    {
        // VC-2 with incompressible round content (random-id notes/scripts): gzip cannot fit
        // the column, so the OLDEST rejection rounds are trimmed first, the most recent round
        // survives verbatim, and the row stays writable — never a 500.
        var job = CreateFullyPopulatedJob();
        var rounds = Enumerable.Range(0, 700)
            .Select(i => new RejectionEntry(
                $"Reason {i}",
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N} notes",
                job.CreatedAt.AddMinutes(i),
                $"operator{i}@arn.com.au",
                Option<string>.Some($"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}")))
            .ToArray();
        job.SetRejectionHistory(rounds);
        JsonSerializer.Serialize(rounds, JsonDefaults.Storage).Length.ShouldBeGreaterThan(30_000);

        var (captured, reloaded) = await SaveAndReloadAsync(job);

        captured.RejectionHistoryJson.ShouldNotBeNull();
        captured.RejectionHistoryJson!.ShouldNotStartWith("gz:");
        captured.RejectionHistoryJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        reloaded.RejectionHistory.Count.ShouldBeLessThan(rounds.Length);
        reloaded.RejectionHistory.Count.ShouldBeGreaterThan(0);
        // Trimming keeps the NEWEST rejection round intact — notes and rejected script.
        reloaded.RejectionHistory[^1].ShouldBe(rounds[^1]);
        reloaded.RejectionHistory[^1].Notes.ShouldBe(rounds[^1].Notes);
        reloaded.RejectionHistory[^1].RejectedScript.ShouldBe(rounds[^1].RejectedScript);
        // The OLDEST rounds were dropped first.
        reloaded.RejectionHistory[0].At.ShouldBeGreaterThan(rounds[0].At);

        var updated = await SaveToTerminalStateAsync(reloaded);
        updated.RejectionHistoryJson!.Length.ShouldBeLessThanOrEqualTo(30_000);
    }
}
