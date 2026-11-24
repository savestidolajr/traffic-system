using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     A market scenario to evaluate: fixed coverage + target duration. <paramref name="WeatherRequired" />
///     and <paramref name="FuelRequired" /> make that source a station fixture (must air whenever its
///     data was fetched), and <paramref name="FillOrder" /> overrides the station fill priority, whose
///     Weather-versus-Fuel order is also the on-air read order. Together they let one scenario exercise
///     the always-include and section-order contracts.
/// </summary>
internal readonly record struct BulletinScenario(
    string Market,
    string City,
    double Lat,
    double Lng,
    double RadiusKm,
    double TargetSeconds,
    int MaxCorridors = 5,
    bool Traffic = true,
    bool Weather = false,
    bool Fuel = false,
    bool WeatherRequired = false,
    bool FuelRequired = false,
    IReadOnlyList<FillSource>? FillOrder = null);

/// <summary>
///     Runs one scenario end-to-end through the production generation path — replayed HERE traffic →
///     <see cref="IScriptGenerator" /> → scorers. The traffic input is deterministic (corpus replay).
///     When <c>measureDuration</c> is set the script is synthesized via ElevenLabs so duration is a real
///     measurement; when a <see cref="WhisperTranscriber" /> is also supplied the same audio is transcribed
///     and scored for pronunciation, with the data's suburbs added as target terms.
/// </summary>
internal static class BulletinEval
{
    private const string DefaultVoiceId = "CwhRBWXzGAHq8TQ4Fs17"; // Roger — local-dev default voice.

    // Demo/eval only: 5 sample per-market filler statements standing in for admin-authored content
    // (production seeds these via the FillerStatements admin CRUD). Real, evergreen, nationally-true
    // road facts — concrete information a listener can act on, never soft "take care" advice.
    // Eligibility/cost is derived automatically from word count (FillerStatementSelector); admins
    // author text only.
    private static readonly IReadOnlyList<FillerStatement> SampleFillerStatements =
    [
        new("f1", "School zones are back to 40 on school days — watch for the flashing signs morning and afternoon."),
        new("f2", "Learner and P1 drivers can't use a phone at all behind the wheel, even hands-free."),
        new("f3", "Roadworks speed limits apply around the clock, whether or not crews are on site."),
        new("f4", "Children under seven must be in an approved child seat or booster."),
        new("f5", "Keep left unless overtaking on roads signed 80 or above."),
    ];

    public static async Task<ScenarioScore> EvaluateAsync(
        IServiceProvider provider,
        BulletinScenario scenario,
        CancellationToken cancellationToken,
        bool measureDuration = false,
        WhisperTranscriber? transcriber = null,
        bool fullPipeline = false)
    {
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IScriptGenerator>();

        var context = await BuildContextAsync(scope.ServiceProvider, scenario, cancellationToken);

        // Mirror production (BroadcastOrchestrator.GenerateAndSynthesizeAsync): an empty/whitespace
        // generation result is a FAILURE, not a valid script — retry up to 2 more times before
        // giving up, rather than shipping dead air as the scored/reported result (observed live:
        // a transient empty draft shipped as the final script with no retry; also observed
        // recurring under the scenario-matrix's rapid-fire API load, so one retry wasn't always enough).
        string script = string.Empty;
        for (int attempt = 0; attempt < 3 && script.Length == 0; attempt++)
        {
            var attemptResult = await generator.GenerateScriptAsync(context, cancellationToken);
            script = attemptResult.IsSuccess && !string.IsNullOrWhiteSpace(attemptResult.Value.Text)
                ? attemptResult.Value.Text
                : string.Empty;
        }

        // Full-pipeline mode: run the production review→refine→compliance loop so the review pack
        // judges the SHIPPED script, not the raw draft. Shared with the regression replay so the two
        // paths can't drift (see RegressionEval).
        if (fullPipeline)
            script = await ReviewRefineAndComplyAsync(
                scope.ServiceProvider, context, script, scenario.TargetSeconds, cancellationToken);

        return await ScoreAsync(
            scope.ServiceProvider, scenario, context, script, cancellationToken,
            measureDuration, transcriber);
    }

    /// <summary>
    ///     Assembles the full generation context for a scenario — replayed/live HERE fetches,
    ///     content gating, good-run fill, monitored routes, and the sample filler statements —
    ///     exactly as <see cref="EvaluateAsync" /> does. Extracted so alternative pipelines (the
    ///     experiment harness) run against the IDENTICAL context the production path sees: the
    ///     A/B variable stays the pipeline shape, never the data.
    /// </summary>
    internal static async Task<BroadcastWithTraffic> BuildContextAsync(
        IServiceProvider services, BulletinScenario scenario, CancellationToken cancellationToken)
    {
        var traffic = services.GetRequiredService<ITrafficProvider>();
        var routing = services.GetRequiredService<IRoutingProvider>();
        var monitoredRouteOptions = services.GetRequiredService<IOptions<MonitoredRoutesOptions>>().Value;
        var weatherProvider = services.GetRequiredService<IWeatherProvider>();
        var fuelProvider = services.GetRequiredService<IFuelPriceProvider>();

        var area = CoverageArea.Create(scenario.Lat, scenario.Lng, scenario.RadiusKm);
        if (area.IsFailure)
            throw new InvalidOperationException($"Invalid coverage for {scenario.Market}: {area.Error}");

        // Content gating mirrors BroadcastOrchestrator: fetch only the sources the scenario enables,
        // so the eval covers every Traffic/Weather/Fuel permutation. The generator then reads
        // context.Content (traffic vs noTraffic) and context.Weather/Fuel.HasValue.
        List<TrafficCorridor> corridors = [];
        IReadOnlyList<FlowRoad> flowPool = [];
        IReadOnlyList<string> goodRuns = [];
        IReadOnlyList<MonitoredRouteStatus> monitoredRoutes = [];
        if (scenario.Traffic)
        {
            var corridorsResult = await traffic.GetCorridorsAsync(area.Value, scenario.MaxCorridors, cancellationToken);
            corridors = corridorsResult.IsSuccess ? corridorsResult.Value.ToList() : [];

            // Good-run fill: the flowing-road pool is the body of a sparse/all-clear bulletin.
            var flowResult = await traffic.GetFreeFlowingRoadsAsync(area.Value, 18, cancellationToken);
            flowPool = flowResult.IsSuccess ? flowResult.Value : [];
            flowPool = GoodRunPolicy.ExcludeCorridorRoads(flowPool, corridors);
            int slotCapacity = Math.Clamp((int)(scenario.TargetSeconds / 5.0), 6, 15);
            // Voiceable count, not the raw count: a busy market whose content collapses to one
            // closure after tiering still needs flowing-road fill before generic filler (Sydney
            // fresh-eval: 12 raw corridors read as "slot full", good-runs suppressed, 18s of dead
            // air filled with 3 generic road-safety facts).
            goodRuns = GoodRunPolicy.SelectForGeneration(
                flowPool, CorridorTiering.CountVoiceable(corridors), slotCapacity, 8).Roads;

            // Monitored routes are keyed by market (production: MarketSchedule.MarketId). The eval's
            // City is the natural human key that matches the appsettings MonitoredRoutes starter set;
            // the internal slug (scenario.Market) never matches a city-keyed config.
            monitoredRoutes = await MonitoredRouteReporter.GetReportsAsync(
                routing, scenario.City, monitoredRouteOptions, cancellationToken);
        }

        // Weather / fuel only when enabled (live HERE only; empty under replay).
        var weather = Option<WeatherReport>.None;
        if (scenario.Weather)
        {
            var weatherResult = await weatherProvider.GetWeatherAsync(area.Value, cancellationToken);
            if (weatherResult.IsSuccess)
                weather = Option<WeatherReport>.Some(weatherResult.Value);
        }

        var fuel = Option<FuelPriceReport>.None;
        if (scenario.Fuel)
        {
            var fuelResult = await fuelProvider.GetFuelPricesAsync(area.Value, cancellationToken);
            if (fuelResult.IsSuccess && fuelResult.Value.HasPrices)
                fuel = Option<FuelPriceReport>.Some(fuelResult.Value);
        }

        var request = new BroadcastRequest
        {
            TargetDurationSeconds = scenario.TargetSeconds,
            JobId = $"eval-{scenario.Market}",
            Location = scenario.City,
            Coverage = area.Value,
            ScriptOnly = true,
            MarketId = Option<string>.Some(scenario.Market),
            Content = new BulletinContentSelection(
                scenario.Traffic, scenario.Weather, scenario.Fuel,
                scenario.WeatherRequired, scenario.FuelRequired)
        };
        return request.WithCorridors(corridors) with
        {
            City = Option<string>.Some(scenario.City),
            MonitoredRoutes = monitoredRoutes,
            GoodRunRoads = goodRuns,
            FlowPool = flowPool,
            Weather = weather,
            Fuel = fuel,
            FillerStatements = SampleFillerStatements,
            FillOrder = scenario.FillOrder ?? request.FillOrder
        };
    }

    /// <summary>
    ///     Scores a finished script against the scenario's context with the full deterministic
    ///     suite. Shared by <see cref="EvaluateAsync" /> and the experiment harness so every
    ///     pipeline arm is judged by the IDENTICAL gates.
    /// </summary>
    internal static async Task<ScenarioScore> ScoreAsync(
        IServiceProvider services,
        BulletinScenario scenario,
        BroadcastWithTraffic context,
        string script,
        CancellationToken cancellationToken,
        bool measureDuration = false,
        WhisperTranscriber? transcriber = null)
    {
        var safety = services.GetRequiredService<IContentSafetyFilter>();
        var corridors = context.Corridors;

        // Synthesize once; reuse the audio for both measured duration and pronunciation.
        ElevenLabsAudioResult? audio = (measureDuration || transcriber is not null) && script.Length > 0
            ? await SynthesizeAsync(services, script, cancellationToken)
            : null;

        PronunciationResult? pronunciation = null;
        if (audio is not null && transcriber is not null)
        {
            string transcript = await transcriber.TranscribeAsync(audio.AudioData, cancellationToken);
            pronunciation = PronunciationScorer.Evaluate(script, transcript, KnownPlaces(corridors, scenario.City));
        }

        return new ScenarioScore(
            scenario.Market,
            corridors.Count,
            // Voiceable corridors only: the fabrication vocabulary must be what the writer SAW.
            // Scoring against the raw list passed iter2's three invented Melbourne CBD roads —
            // they existed in tier-suppressed minor corridors the writer was never given. The
            // MinorTraffic escalation has to be mirrored too: with it set the summarizer renders the
            // minor tier under a Major headline, and scoring without it reported every legitimately
            // voiced minor road as invented (live eval, Sydney: Beecroft Road, Epping Tunnel, M2).
            FabricationScorer.Evaluate(script,
                CorridorTiering.SelectVoiceable(corridors, includeMinorTier:
                    context.FillOrder.Contains(FillSource.MinorTraffic)),
                context.MonitoredRoutes, context.GoodRunRoads),
            DurationScorer.Evaluate(script, scenario.TargetSeconds, audio?.DurationSeconds),
            StyleScorer.Evaluate(script),
            await SafetyScorer.EvaluateAsync(safety, script),
            script,
            pronunciation,
            ScriptSanityScorer.Evaluate(script),
            TargetShapeScorer.Evaluate(script, corridors, scenario.TargetSeconds,
                await ResolveWordsPerSecondAsync(services, cancellationToken)),
            BulletinDataComposer.ComposeAllData(context, includeFillerStatements: true));
    }

    /// <summary>
    ///     The production review→refine→compliance pass (mirrors
    ///     <c>BroadcastOrchestrator.ReviewAndRefineScriptAsync</c> + <c>EnsureWordTargetComplianceAsync</c>):
    ///     iterate reviewer→refiner up to the retry ceiling, then expand an under-length final script
    ///     toward the slot. Shared by the scenario eval and the regression replay so they can't drift.
    /// </summary>
    internal static async Task<string> ReviewRefineAndComplyAsync(
        IServiceProvider services, BroadcastWithTraffic context, string script,
        double targetSeconds, CancellationToken cancellationToken)
    {
        if (script.Length == 0)
            return script;

        var reviewer = services.GetRequiredService<IScriptReviewer>();
        var refiner = services.GetRequiredService<IScriptRefiner>();
        var options = services.GetRequiredService<IOptions<BroadcastOptions>>().Value;

        for (int attempt = 1; attempt <= options.MaxReviewRefineRetries; attempt++)
        {
            var review = await reviewer.ReviewScriptAsync(
                script, (int)targetSeconds, null, null, context, cancellationToken, null);
            if (review.IsFailure)
                break;
            var rollCall = RollCallDetector.Detect(script, context);
            var dupAnchor = DuplicateAnchorDetector.Detect(script, context);
            var metaLeak = MetaLeakDetector.Detect(script);
            var typeStack = IncidentTypeStackDetector.Detect(script);
            var repeatedPrep = RepeatedPrepositionDetector.Detect(script);
            var impactStack = ImpactStackDetector.Detect(script);
            var fakeLocation = FakeLocationDetector.Detect(script);
            var sentenceClone = SentenceCloneDetector.Detect(script, context);
            var unselectedSource = UnselectedSourceDetector.Detect(script, context);
            var requiredSource = RequiredSourceDetector.Detect(script, context);
            var sectionOrder = SectionOrderDetector.Detect(script, context);
            if (review.Value.IsApproved && !rollCall.HasRollCall && !dupAnchor.HasDuplicateAnchor
                && !metaLeak.HasMetaLeak && !typeStack.HasTypeStack && !repeatedPrep.HasRepeatedPreposition
                && !impactStack.HasImpactStack && !fakeLocation.HasFakeLocation && !sentenceClone.HasClone
                && !unselectedSource.HasFabrication && !requiredSource.HasMissingSource
                && !sectionOrder.HasOutOfOrderSection)
                break;
            // Mirror production (ScriptReviewLoop, 2026-08-21): reviewer issues route to
            // sentence-scoped patches; a sentence whose patch fails is DROPPED. Unresolvable
            // issues defer to the next review attempt — there is no whole-script rewrite anymore.
            var split = ScriptSentenceSplitter.Split(script).ToList();
            foreach (var issue in review.Value.Issues.Where(i => i.Order is > 0)
                         .OrderByDescending(i => i.Order!.Value))
            {
                int order = issue.Order!.Value;
                if (order > split.Count)
                    continue;
                var sentence = new DraftedSentence(order, split[order - 1], []);
                var patch = await refiner.PatchSentenceAsync(
                    context, sentence, $"Fix this issue: {issue.Reason}", [],
                    WordCounter.Count(sentence.Text), cancellationToken);
                if (patch.IsSuccess && !string.IsNullOrWhiteSpace(patch.Value?.Text))
                    split[order - 1] = patch.Value!.Text;
                else
                    split.RemoveAt(order - 1);
            }
            script = string.Join(" ", split.Select(s => s.Trim()));
        }

        int targetWords = Math.Max(10,
            WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(options.WordsPerMinute, targetSeconds));
        if (SlotFitPlanner.NeedsExpansion(WordCounter.Count(script), targetWords))
        {
            // Mirror production (WordTargetEnforcer): grow one sentence at a time with the
            // sentence refiner, skipping locked (weather/fuel) lines; the patch's own word
            // tolerance bounds each growth.
            var sentences = ScriptSentenceSplitter.Split(script).ToList();
            int expandedWords = WordCounter.Count(script);
            for (int i = 0; i < sentences.Count && SlotFitPlanner.NeedsExpansion(expandedWords, targetWords); i++)
            {
                if (ContentSourceSignals.IsWeatherLine(sentences[i]) || ContentSourceSignals.IsFuelLine(sentences[i]))
                    continue;
                int sentenceWords = WordCounter.Count(sentences[i]);
                int gap = Math.Max(2, (int)Math.Ceiling((targetWords - expandedWords) / (double)(sentences.Count - i)));
                var patch = await refiner.PatchSentenceAsync(
                    context, new DraftedSentence(i + 1, sentences[i], []),
                    SlotFitPlanner.ExpansionFeedback(targetWords), [], sentenceWords + gap, cancellationToken);
                if (patch.IsSuccess && !string.IsNullOrWhiteSpace(patch.Value?.Text))
                {
                    int patchedWords = WordCounter.Count(patch.Value!.Text);
                    if (patchedWords > sentenceWords)
                    {
                        sentences[i] = patch.Value!.Text;
                        expandedWords += patchedWords - sentenceWords;
                    }
                }
            }
            script = string.Join(" ", sentences.Select(s => s.Trim()));
        }

        // Mirror production (FinalizeExpandedScript): re-run the full detector suite AFTER
        // expansion too — a fresh, ungated refine call (either the main loop's last attempt or
        // this expansion pass) can silently reintroduce a defect the loop already cleared
        // (observed live: a clean Melbourne script regained a type-stack clause via this exact
        // expansion path). Type-stack, roll-call (Rule B), duplicate-anchor (the safe
        // bare-self-reference shape), repeated-preposition, impact-stack, and fake-location all
        // have a safe mechanical fix; the rest are logged for visibility.
        if (IncidentTypeStackDetector.Detect(script).HasTypeStack)
            script = IncidentTypeStackDetector.Repair(script);
        if (RollCallDetector.Detect(script, context).HasRollCall)
            script = RollCallDetector.Repair(script, context);
        if (DuplicateAnchorDetector.Detect(script, context).HasDuplicateAnchor)
            script = DuplicateAnchorDetector.Repair(script, context);
        if (RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition)
            script = RepeatedPrepositionDetector.Repair(script);
        if (ImpactStackDetector.Detect(script).HasImpactStack)
            script = ImpactStackDetector.Repair(script);
        if (FakeLocationDetector.Detect(script).HasFakeLocation)
            script = FakeLocationDetector.Repair(script);
        if (MetaLeakDetector.Detect(script).HasMetaLeak)
            script = MetaLeakDetector.Repair(script);
        if (SentenceCloneDetector.Detect(script, context).HasClone)
            script = SentenceCloneDetector.Repair(script, context);
        if (UnselectedSourceDetector.Detect(script, context).HasFabrication)
            script = UnselectedSourceDetector.Repair(script, context);
        if (SectionOrderDetector.Detect(script, context).HasOutOfOrderSection)
            script = SectionOrderDetector.Repair(script, context);

        return script;
    }

    private static async Task<ElevenLabsAudioResult?> SynthesizeAsync(
        IServiceProvider scope, string script, CancellationToken cancellationToken)
    {
        var client = scope.GetRequiredService<ElevenLabsClient>();
        var settings = scope.GetRequiredService<IOptions<ElevenLabsOptions>>().Value.VoiceSettings;
        string? configuredVoiceId = scope.GetRequiredService<IConfiguration>()["AiSettings:ElevenLabs:VoiceId"];
        string voiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? DefaultVoiceId : configuredVoiceId;

        var result = await client.GenerateAudioAsync(
            script, settings.Speed, settings.Stability, settings.SimilarityBoost,
            settings.Style, settings.UseSpeakerBoost, voiceId, modelOverride: null, ct: cancellationToken);

        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    ///     Words-per-second anchor for the length dimension, read from the benchmark voice's measured
    ///     calibration (per voice + settings) so the band tracks the chosen voice — not a flat constant.
    ///     No synthesis is triggered (script-only stays quota-free); falls back to the configured default
    ///     rate when no calibration exists or storage is unreachable.
    /// </summary>
    internal static async Task<double> ResolveWordsPerSecondAsync(
        IServiceProvider scope, CancellationToken cancellationToken)
    {
        try
        {
            var calibration = scope.GetRequiredService<IVoiceCalibrationService>();
            string? configuredVoiceId = scope.GetRequiredService<IConfiguration>()["AiSettings:ElevenLabs:VoiceId"];
            string voiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? DefaultVoiceId : configuredVoiceId;
            var vs = scope.GetRequiredService<IOptions<ElevenLabsOptions>>().Value.VoiceSettings;
            var settings = VoiceSettings.Create(vs.Stability, vs.SimilarityBoost, vs.Style, vs.UseSpeakerBoost, vs.Speed);

            var metrics = await calibration.GetCalibrationMetricsAsync(
                voiceId,
                settings.IsSuccess ? Option<VoiceSettings>.Some(settings.Value) : default,
                triggerCalibrationIfMissing: false,
                cancellationToken);

            return metrics.WordsPerMinute > 0 ? metrics.WordsPerMinute / 60.0 : 0;
        }
        catch
        {
            return 0; // Scorer falls back to its default rate.
        }
    }

    /// <summary>Place names the model was given (the city and each corridor's suburb) — pronunciation targets.</summary>
    private static IReadOnlyList<string> KnownPlaces(IReadOnlyList<TrafficCorridor> corridors, string city)
    {
        var places = new List<string> { city };
        foreach (var corridor in corridors)
        {
            string suburb = corridor.Suburb.GetValueOr(string.Empty);
            if (!string.IsNullOrWhiteSpace(suburb))
                places.Add(suburb);
        }

        return places;
    }
}
