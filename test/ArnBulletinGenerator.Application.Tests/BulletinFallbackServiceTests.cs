using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class BulletinFallbackServiceTests
{
    private static readonly DateTime Now = new(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly IScheduleRepository _scheduleRepo = Substitute.For<IScheduleRepository>();
    private readonly BroadcastOptions _options = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IFillerAudioCacheService _fillerAudioCache = Substitute.For<IFillerAudioCacheService>();
    private readonly IFillerStatementsRepository _statementsRepo = Substitute.For<IFillerStatementsRepository>();
    private readonly IAudioPipelineService _audioPipeline = Substitute.For<IAudioPipelineService>();
    private readonly IDurationEstimator _durationEstimator = Substitute.For<IDurationEstimator>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly PerVoiceFillerCartResolver _perVoiceFillerCart;
    private readonly BulletinFallbackService _sut;

    public BulletinFallbackServiceTests()
    {
        _timeProvider.SetUtcNow(Now);
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>()).Returns(Result.Success());
        // Timezone only feeds the day-window; the same-day lookup is mocked with Arg.Any<DateTime>().
        _scheduleRepo.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));

        _perVoiceFillerCart = new PerVoiceFillerCartResolver(
            _fillerAudioCache, Options.Create(_options),
            Substitute.For<ILogger<PerVoiceFillerCartResolver>>());
        _statementsRepo.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _audioPipeline.StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>()).Returns([1, 2, 3]);
        _audioPipeline.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>()).Returns([1, 2, 3, 4]);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(16.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var padder = new FallbackAudioPadder(
            _statementsRepo, _fillerAudioCache, _audioPipeline, _durationEstimator, _broadcastOutput,
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<ILogger<FallbackAudioPadder>>(), _timeProvider);
        _sut = new BulletinFallbackService(
            _scheduleRepo, _jobRepo, Options.Create(_options), _perVoiceFillerCart, padder, _timeProvider,
            Substitute.For<ILogger<BulletinFallbackService>>());
    }

    private Job TargetJob() => Job.Create("target", "3AW", "mel", 30, Now).Value;

    private static Job PublishedSource(string jobId = "fb-src")
    {
        var source = Job.Create(jobId, "3AW", "mel", 30, Now).Value;
        source.AudioUrl = Option<string>.Some("https://audio.example.com/fallback.mp3");
        source.ScriptText = Option<string>.Some("fallback script");
        source.TransitionTo(JobStatus.AudioReady, utcNow: Now);
        source.TransitionTo(JobStatus.WaitingApproval, utcNow: Now);
        source.Approve(Now);
        source.Publish(Now);
        return source;
    }

    [Fact]
    public async Task SameDayBulletinExists_AppliesSameDayFallback()
    {
        var source = PublishedSource();
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.SameDayBulletin);
        outcome.Applied.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/fallback.mp3");
        job.FallbackReason.GetValueOr("").ShouldBe(FallbackReasons.ContentLimited);
        await _jobRepo.Received().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplayContradictedByOwnFreshFetch_RefusesReuse_CoversWithFillerInstead()
    {
        // RISK-37b: the Greensborough Bypass closure re-aired 15 times, all Published, ~3 hours
        // after this exact contradiction was sitting in each job's own fetched evidence.
        _options.FillerCartAudioUrl = "https://audio.example.com/filler.wav";
        var source = PublishedSource();
        source.ScriptText = Option<string>.Some(
            "The Greensborough Bypass is still closed southbound at the M80 ramp.");
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));

        var job = TargetJob();
        job.IncidentsFetchedAt = Option<DateTime>.Some(Now);
        job.SetIncidents([
            new JobIncident("H1", 2, "construction on Greensborough Bypass", "Greensborough Bypass", 2, 400,
                Option<string>.Some("Between Lower Plenty Road and Watsonia Road - Lane blocked"),
                RoadClosed: false)
        ]);

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/filler.wav");
        await _jobRepo.Received().AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.StartsWith("Same-day replay refused")),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplayConfirmedByOwnFreshFetch_StillReuses()
    {
        var source = PublishedSource();
        source.ScriptText = Option<string>.Some("The Greensborough Bypass is closed southbound.");
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));

        var job = TargetJob();
        job.IncidentsFetchedAt = Option<DateTime>.Some(Now);
        job.SetIncidents([
            new JobIncident("H1", 1, "roadClosure on Greensborough Bypass", "Greensborough Bypass", 1, 400,
                Option<string>.Some("Closed"), RoadClosed: true)
        ]);

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.SameDayBulletin);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
    }

    [Fact]
    public async Task ReplayWithNoFreshFetchOfItsOwn_IsNotAdjudicated_ReusesAsBefore()
    {
        // A job that never fetched (provider outage) has no evidence to contradict a replay with;
        // refusing here would kill every legitimate outage replay.
        var source = PublishedSource();
        source.ScriptText = Option<string>.Some("The Greensborough Bypass is closed southbound.");
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.SameDayBulletin);
    }

    // ── Covers copy from the chain's ROOT original, never a covered neighbour ─────────────

    private static Job CoveredNeighbour(Job root, string jobId, int streak)
    {
        var covered = Job.Create(jobId, "3AW", "mel", 30, Now).Value;
        covered.ApplyFallback(root, Now, fallbackStreak: streak);
        covered.Publish(Now);
        return covered;
    }

    [Fact]
    public async Task CoverOfACover_CopiesFromTheRootOriginal_AndCountsTheChain()
    {
        // Observed live 2026-08-17 (dev): one degenerate ancestor — empty script, 17.0s audio —
        // replicated through every later cover, and every link reported streak 1.
        var root = PublishedSource("root-original");
        root.ScriptText = Option<string>.Some("The M1 is slow through Beenleigh");
        var neighbour = CoveredNeighbour(root, "covered-neighbour", 2);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(neighbour)));
        _jobRepo.GetJobAsync("root-original", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(root));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.SameDayBulletin);
        job.ScriptText.Value.ShouldBe("The M1 is slow through Beenleigh");
        job.AudioUrl.Value.ShouldBe(root.AudioUrl.Value);
        job.FallbackSourceJobId.Value.ShouldBe("root-original");
        job.FallbackStreak.ShouldBe(3);
    }

    [Fact]
    public async Task BrokenCoverChain_NeverCopiesSecondHandFields_CoversWithFillerInstead()
    {
        // A walk that cannot reach the true original ends on a covered link, whose fields are
        // second-hand by definition — the exact defect root-resolution exists to fix.
        _options.FillerCartAudioUrl = "https://audio.example.com/filler.wav";
        var root = PublishedSource("root-original");
        var neighbour = CoveredNeighbour(root, "covered-neighbour", 1);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(neighbour)));
        _jobRepo.GetJobAsync("root-original", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("gone"));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/filler.wav");
    }

    [Fact]
    public async Task ChainRootedInAScriptlessCover_FallsThroughToTheFillerCart()
    {
        // A chain rooted in a filler-cart cover has no bulletin to re-air.
        _options.FillerCartAudioUrl = "https://audio.example.com/filler.wav";
        var rootless = Job.Create("cart-cover", "3AW", "mel", 30, Now).Value;
        rootless.ApplyFillerFallback("https://audio.example.com/filler.wav", Now);
        rootless.Publish(Now);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(rootless)));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/filler.wav");
    }

    [Fact]
    public async Task NoSameDay_FillerConfigured_AppliesFiller()
    {
        _options.FillerCartAudioUrl = "https://audio.example.com/filler.wav";

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/filler.wav");
        job.FallbackReason.GetValueOr("").ShouldBe(FallbackReasons.ContentLimited);
        await _jobRepo.Received().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoSameDay_PerVoiceCartResolves_AppliesPerVoiceCartOverEvergreen()
    {
        _options.FillerCartScript = "You're listening to Your Traffic Network.";
        _options.FillerCartAudioUrl = "https://audio.example.com/evergreen.wav";
        var job = TargetJob();
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "elevenlabs", "voice-1", "Voice One", "eleven_v3",
            Option<VoiceSettings>.Some(VoiceSettings.Default)));
        _fillerAudioCache.ResolveCartAsync(
                _options.FillerCartScript, "voice-1", VoiceSettings.Default, "eleven_v3",
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedCartClip>.Success(new ResolvedCartClip(
                "https://audio.example.com/filler-cache/__cart__/voice-key.wav", [1, 2, 3], 15.98, [])));

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/filler-cache/__cart__/voice-key.wav");
        job.FallbackReason.GetValueOr("").ShouldBe(FallbackReasons.ContentLimited);
        await _jobRepo.Received().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoSameDay_PerVoiceCartNone_AppliesEvergreenCart()
    {
        // FillerCartScript empty → resolver returns None → evergreen cart must be used.
        _options.FillerCartScript = "";
        _options.FillerCartAudioUrl = "https://audio.example.com/evergreen.wav";

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/evergreen.wav");
        await _jobRepo.Received().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoSameDay_NoFiller_ReturnsNoneAndDoesNotSave()
    {
        _options.FillerCartAudioUrl = "";

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        outcome.Applied.ShouldBeFalse();
        job.Status.ShouldNotBe(JobStatus.FallbackUsed);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameDaySourceIsAudioless_ReturnsNone_AndDoesNotFallThroughToFiller()
    {
        // A configured filler must NOT rescue an audioless same-day source — the original chain
        // skips entirely in that case rather than substituting the cart.
        _options.FillerCartAudioUrl = "https://audio.example.com/filler.wav";
        var audioless = Job.Create("fb-src", "3AW", "mel", 30, Now).Value; // no AudioUrl
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(audioless)));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        job.Status.ShouldNotBe(JobStatus.FallbackUsed);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameDaySourceIsSameJob_ReturnsNone()
    {
        var self = PublishedSource("target"); // same id as the target
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(self)));

        var job = TargetJob();
        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyPublished_ReturnsNone_DoesNotFallback()
    {
        var job = TargetJob();
        job.TransitionTo(JobStatus.AudioReady, utcNow: Now);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: Now);
        job.Approve(Now);
        job.Publish(Now);

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        outcome.Applied.ShouldBeFalse();
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyApproved_ReturnsNone_DoesNotFallback()
    {
        var job = TargetJob();
        job.TransitionTo(JobStatus.AudioReady, utcNow: Now);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: Now);
        job.Approve(Now);

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        outcome.Applied.ShouldBeFalse();
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyFallbackUsed_ReturnsNone_DoesNotFurtherFallback()
    {
        var job = TargetJob();
        var src = PublishedSource("fb-src-for-terminal-guard");
        job.ApplyFallback(src, Now, reason: FallbackReasons.ContentLimited);
        job.Status.ShouldBe(JobStatus.FallbackUsed); // precondition

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.None);
        outcome.Applied.ShouldBeFalse();
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    // ── Finding 32: the fallback must cover (or at least be measured against) the slot ──

    private const string CartScript = "You're listening to Your Traffic Network.";

    private static Job JobWithVoiceConfig()
    {
        var job = Job.Create("target", "3AW", "mel", 30, Now).Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "elevenlabs", "voice-1", "Voice One", "eleven_v3",
            Option<VoiceSettings>.Some(VoiceSettings.Default)));
        return job;
    }

    private void StubPerVoiceCart(double durationSeconds, string url = "https://audio.example.com/cart.wav")
    {
        _options.FillerCartScript = CartScript;
        _fillerAudioCache.ResolveCartAsync(
                CartScript, "voice-1", VoiceSettings.Default, "eleven_v3", Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedCartClip>.Success(new ResolvedCartClip(url, [1, 2, 3], durationSeconds, [])));
    }

    [Fact]
    public async Task PerVoiceCart_UnderFillsSlot_SurfacesMeasuredDurationAndWarns()
    {
        // The measured live shape: a 15.98s cart in a 30s slot. The defect was SILENT — the job
        // carried no measured duration and nothing warned, so an operator approved ~14s of dead
        // air. Both must now be visible.
        StubPerVoiceCart(15.98);
        var job = JobWithVoiceConfig();

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/cart.wav");
        job.MeasuredDurationSec.Value.ShouldBe(15.98);
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("16.0s of the 30s slot", StringComparison.Ordinal)
                                && m.Contains("dead air", StringComparison.Ordinal)),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerVoiceCart_MateriallyShort_WithFillerStatements_PadsTowardTheSlot()
    {
        StubPerVoiceCart(15.98);
        var job = JobWithVoiceConfig();
        _statementsRepo.GetStatementsAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([
                new FillerStatement("f1", "Allow extra time on the city loop this afternoon"),
                new FillerStatement("f2", "Traffic is heavy on the West Gate Bridge")
            ]));
        _fillerAudioCache.ResolveAsync(Arg.Any<FillerStatement>(), "voice-1", VoiceSettings.Default,
                "eleven_v3", Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([9, 8, 7], 7.0, [])));
        // Stitched natural read = 28.0s (within the 1.1 slow-down cap of the 30s slot) -> the
        // fine-tune closes it exactly; the fine-tuned file measures 30.0s.
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(28.0, 30.0);

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        // The padded clip replaces the plain cart URL, and its file duration is surfaced.
        job.AudioUrl.Value.ShouldBe("filler_pad_target.wav");
        job.MeasuredDurationSec.Value.ShouldBe(30.0);
        await _broadcastOutput.Received(1).SaveAsync(Arg.Any<byte[]>(), "filler_pad_target", Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("padded to", StringComparison.Ordinal)),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerVoiceCart_PaddingCannotResolveAudio_KeepsThePlainCart()
    {
        // Reliability contract: padding is best-effort and the fallback must NEVER fail closed.
        // Statements exist but their audio cannot be synthesised — the plain cart still applies,
        // and the under-fill is still surfaced.
        StubPerVoiceCart(15.98);
        var job = JobWithVoiceConfig();
        _statementsRepo.GetStatementsAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([
                new FillerStatement("f1", "Allow extra time on the city loop this afternoon")
            ]));
        _fillerAudioCache.ResolveAsync(Arg.Any<FillerStatement>(), "voice-1", VoiceSettings.Default,
                "eleven_v3", Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Failure("tts down"));

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/cart.wav");
        job.MeasuredDurationSec.Value.ShouldBe(15.98);
        await _broadcastOutput.DidNotReceive().SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerVoiceCart_FillsTheSlot_DoesNotAttemptPadding()
    {
        // A cart that is not materially short (gap within the 1s threshold) is applied as-is —
        // no statement resolution, no upload, no dead-air warning.
        StubPerVoiceCart(29.6);
        var job = JobWithVoiceConfig();

        var outcome = await _sut.TryApplyFallbackAsync(job, FallbackReasons.ContentLimited, CancellationToken.None);

        outcome.Kind.ShouldBe(FallbackKind.FillerCart);
        job.AudioUrl.Value.ShouldBe("https://audio.example.com/cart.wav");
        job.MeasuredDurationSec.Value.ShouldBe(29.6);
        await _fillerAudioCache.DidNotReceive().ResolveAsync(
            Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("dead air", StringComparison.Ordinal)),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }
}
