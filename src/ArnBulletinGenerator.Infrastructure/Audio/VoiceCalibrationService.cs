using System.Collections.Concurrent;

using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Calibrates voice-specific speech rates by synthesizing a reference phrase
///     and measuring actual duration vs. syllable count.
/// </summary>
public sealed class VoiceCalibrationService(
    IVoiceCalibrationRepository repository,
    IAudioSynthesizer synthesizer,
    IDurationEstimator durationEstimator,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    ILogger<VoiceCalibrationService> logger) : IVoiceCalibrationService
{
    // Reference phrase with known syllable count for calibration.
    // This phrase is designed to:
    // 1. Have a mix of short and long words
    // 2. Include common punctuation patterns (6 commas)
    // 3. Be long enough for accurate measurement (~60 syllables)
    // 4. Include Australian place names matching production scripts
    private const string ReferencePhrase =
        "Good morning, Melbourne, here's your traffic update. " +
        "Near Tullamarine, expect delays on the freeway into the city. " +
        "The Monash is slow near Warrigal Road, so allow extra time. " +
        "Stay safe, drive carefully, and we'll keep you updated.";

    // EMA smoothing factor: 30% weight to new measurement, 70% to history.
    private const double EmaAlpha = 0.3;

    // Plausibility bounds shared by BOTH calibration writers. Real-bulletin writes have always been
    // gated; the reference-phrase writer was not, which is what let an impossible rate reach the row.
    private const double MinSyllablesPerSecond = 2.0;
    private const double MaxSyllablesPerSecond = 12.0;
    private const double MinSyllablesPerWord = 0.8;
    private const double MaxSyllablesPerWord = 3.0;
    private const double MinWordsPerMinute = 90.0;
    private const double MaxWordsPerMinute = 240.0;

    // Dynamically computed syllable count for the reference phrase using the vowel-group heuristic.
    private static readonly int ReferenceSyllableCount = SyllableCounter.CountSyllables(ReferencePhrase);

    // Calibrations older than this are considered stale and trigger automatic recalibration.
    // A calibration request is expensive and must be shared by callers using the same
    // voice/settings/model. The worker never observes a caller token: cancellation only releases
    // that caller's wait, so one cancelled request cannot cancel another caller's calibration.
    private readonly ConcurrentDictionary<string, Lazy<Task<Result<VoiceCalibration>>>> _calibrationFlights = new(
        StringComparer.Ordinal);
    private static readonly TimeSpan CalibrationReadCacheTtl = TimeSpan.FromMinutes(1);
    private readonly object _readCacheGate = new();
    private readonly Dictionary<string, (Option<VoiceCalibration> Value, DateTimeOffset ExpiresAt)> _readCache = new(
        StringComparer.Ordinal);
    private static readonly TimeSpan CalibrationStalenessThreshold = TimeSpan.FromHours(24);
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;

    public async Task<CalibrationMetrics> GetCalibrationMetricsAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        bool triggerCalibrationIfMissing = true,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default)
    {
        var defaults = new CalibrationMetrics(
            _broadcastOptions.WordsPerMinute,
            _broadcastOptions.SyllablesPerSecond,
            CalibrationDefaults.SyllablesPerWord);

        if (string.IsNullOrEmpty(voiceId))
            return defaults;

        var calibration = await ResolveCalibrationAsync(voiceId, ResolveSettings(settings), triggerCalibrationIfMissing,
            cancellationToken, modelOverride);
        if (calibration is null)
            return defaults;

        return new CalibrationMetrics(
            ResolveWordsPerMinute(calibration),
            calibration.SyllablesPerSecond > 0 ? calibration.SyllablesPerSecond : _broadcastOptions.SyllablesPerSecond,
            calibration.SyllablesPerWord > 0 ? calibration.SyllablesPerWord : CalibrationDefaults.SyllablesPerWord);
    }

    public Task<Result<VoiceCalibration>> CalibrateVoiceAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default)
    {
        var effectiveSettings = ResolveSettings(settings);
        string modelId = modelOverride.GetValueOr("unknown");
        modelId = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId.Trim();
        string key = $"{voiceId}|{effectiveSettings.GetConfigurationKey(voiceId)}|{modelId}";
        var flight = _calibrationFlights.GetOrAdd(
            key,
            _ => new Lazy<Task<Result<VoiceCalibration>>>(
                () => CalibrateVoiceCoreAsync(voiceId, effectiveSettings, modelId),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<Result<VoiceCalibration>> sharedTask = flight.Value;
        _ = sharedTask.ContinueWith(
            _ => _calibrationFlights.TryRemove(
                new KeyValuePair<string, Lazy<Task<Result<VoiceCalibration>>>>(key, flight)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return AwaitCalibrationFlightAsync(key, flight, cancellationToken);
    }

    private async Task<Result<VoiceCalibration>> AwaitCalibrationFlightAsync(
        string key,
        Lazy<Task<Result<VoiceCalibration>>> flight,
        CancellationToken cancellationToken)
    {
        try
        {
            // WaitAsync cancels only this waiter; the shared worker keeps running for other callers.
            return await flight.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (flight.IsValueCreated && flight.Value.IsCompleted)
                _calibrationFlights.TryRemove(new KeyValuePair<string, Lazy<Task<Result<VoiceCalibration>>>>(key, flight));
        }
    }

    private async Task<Result<VoiceCalibration>> CalibrateVoiceCoreAsync(
        string voiceId,
        VoiceSettings effectiveSettings,
        string modelId)
    {

        try
        {
            // The reference phrase MUST render on the same model the station actually airs on.
            // Without this override it fell back to the configured default, so a profile pinned to
            // eleven_multilingual_v2 had its rate measured on eleven_v3 — a 24 wpm systematic error
            // on the same voice (measured: 135.6 wpm on v3 vs 159.4 on multilingual_v2 vs 170.0 on
            // turbo_v2_5 for EF2rFRNHqbfaMctIvS2k reading the same 635-char bulletin).
            var synthesisResult = await synthesizer.SynthesizeAsync(
                ReferencePhrase,
                Option<VoiceSettings>.Some(effectiveSettings),
                Option<string>.Some(voiceId),
                Option<string>.Some(modelId),
                cancellationToken: CancellationToken.None);

            if (synthesisResult.IsFailure)
                // Preserve the typed disposition (InvalidInput for a 4xx provider rejection,
                // e.g. an unknown voice id) so the calibrate HTTP edge can return 400 instead of
                // a misleading 500 leaking the provider error.
                return ResultFailure.Propagate<VoiceCalibration>(synthesisResult);

            double actualDuration = durationEstimator.GetDuration(synthesisResult.Value.Audio);
            if (actualDuration <= 0)
                return Result<VoiceCalibration>.Failure("Invalid audio duration");

            // Calculate syllables per second using TOTAL duration.
            // This rate naturally includes TTS pauses, so estimation won't double-count them.
            int syllablesCount = ReferenceSyllableCount;
            double measuredRate = syllablesCount / actualDuration;
            int referenceWordCount = WordCounter.Count(ReferencePhrase);
            double measuredSyllablesPerWord = referenceWordCount > 0
                ? syllablesCount / (double)referenceWordCount
                : CalibrationDefaults.SyllablesPerWord;
            double measuredWordsPerMinute = referenceWordCount / actualDuration * 60.0;

            // Same sanity gate RecordActualDurationAsync applies to real bulletin reads. This path had
            // NONE, which made it the only writer able to put an impossible rate into the row — and
            // the only candidate for the dev spike that moved Dolly Male Young to 186 wpm (every
            // attempt-level write in that window measured 136-147 and would have needed ~298 wpm to
            // do it). A truncated or failed reference render must not poison slot fitting.
            if (measuredRate is < MinSyllablesPerSecond or > MaxSyllablesPerSecond
                || measuredSyllablesPerWord is < MinSyllablesPerWord or > MaxSyllablesPerWord
                || measuredWordsPerMinute is < MinWordsPerMinute or > MaxWordsPerMinute)
            {
                logger.LogWarning(
                    "Calibration for voice {VoiceId} REJECTED as out of range — {Wpm:F1} wpm, {Rate:F2} syll/s, {Spw:F2} syll/word from a {Duration:F2}s reference render.",
                    voiceId, measuredWordsPerMinute, measuredRate, measuredSyllablesPerWord, actualDuration);
                return Result<VoiceCalibration>.Failure(
                    $"Reference calibration out of range ({measuredWordsPerMinute:F0} wpm).");
            }

            var calibration = await GetOrCreateCalibrationAsync(voiceId, effectiveSettings, syllablesCount,
                actualDuration, modelId, CancellationToken.None);

            // The synthetic reference phrase reads materially faster than real bulletin copy (158.6 vs
            // 128-147 wpm measured on dev), so blending it into a WARM row mixes two populations into
            // one number that describes neither. It exists to bootstrap a cold row; once real reads are
            // arriving via RecordActualDurationAsync they are the better evidence, and the reference
            // render only refreshes the staleness clock.
            if (calibration.RunCount > 0)
            {
                logger.LogInformation(
                    "Calibration for voice {VoiceId} already has {Runs} real measurements — reference phrase not blended in (bootstrap only).",
                    voiceId, calibration.RunCount);
                calibration.CalibratedAt = timeProvider.GetUtcNow().UtcDateTime;
            }
            else
            {
                calibration.RecordMeasurement(measuredRate, measuredSyllablesPerWord,
                    timeProvider.GetUtcNow().UtcDateTime, EmaAlpha, actualDuration, syllablesCount);
            }

            var saveResult = await repository.SaveCalibrationAsync(calibration, CancellationToken.None);
            if (saveResult.IsFailure)
            {
                InvalidateReadCache(voiceId, effectiveSettings, modelId);
                return Result<VoiceCalibration>.Failure(saveResult.Error);
            }

            InvalidateReadCache(voiceId, effectiveSettings, modelId);
            logger.LogInformation(
                "Calibration refined for voice {VoiceId}: {Rate:F2} syllables/sec average over {Runs} runs",
                voiceId, calibration.SyllablesPerSecond, calibration.RunCount);

            return Result<VoiceCalibration>.Success(calibration);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Calibration failed for voice {VoiceId}", voiceId);
            return Result<VoiceCalibration>.Failure($"Calibration error: {ex.Message}");
        }
    }
    public async Task<bool> HasValidCalibrationAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        CancellationToken cancellationToken = default)
    {
        var effectiveSettings = ResolveSettings(settings);
        var calibrationResult = await ReadCalibrationAsync(
            voiceId, effectiveSettings, "unknown", cancellationToken);
        return calibrationResult.IsSuccess
               && calibrationResult.Value.HasValue
               && !IsStale(calibrationResult.Value.Value);
    }

    public async Task RecordActualDurationAsync(
        string voiceId,
        VoiceSettings settings,
        string text,
        double actualDuration,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default)
    {
        if (string.IsNullOrWhiteSpace(text) || actualDuration <= 0) return;

        try
        {
            int syllablesCount = SyllableCounter.CountSyllables(text);
            int wordCount = WordCounter.Count(text);
            if (syllablesCount <= 0 || wordCount <= 0)
                return;

            double measuredRate = syllablesCount / actualDuration;
            double measuredSyllablesPerWord = syllablesCount / (double)wordCount;
            double measuredWordsPerMinute = wordCount / actualDuration * 60.0;

            // Sanity check - skip outliers (same bounds the reference-phrase writer now applies)
            if (measuredRate < MinSyllablesPerSecond || measuredRate > MaxSyllablesPerSecond)
                return;

            if (measuredSyllablesPerWord < MinSyllablesPerWord || measuredSyllablesPerWord > MaxSyllablesPerWord)
                return;

            if (measuredWordsPerMinute < MinWordsPerMinute || measuredWordsPerMinute > MaxWordsPerMinute) return;

            string modelId = modelOverride.GetValueOr("unknown");
            modelId = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId.Trim();
            var calibration = await GetOrCreateCalibrationAsync(
                voiceId, settings, modelId: modelId, cancellationToken: cancellationToken);
            calibration.RecordMeasurement(
                measuredRate, measuredSyllablesPerWord, utcNow: timeProvider.GetUtcNow().UtcDateTime);

            var saveResult = await repository.SaveCalibrationAsync(calibration, cancellationToken);
            if (saveResult.IsSuccess)
                InvalidateReadCache(voiceId, settings, modelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record actual duration for {VoiceId}", voiceId);
        }
    }


    // ── Private helpers ──────────────────────────────────────────────────

    private bool IsStale(VoiceCalibration calibration)
    {
        return timeProvider.GetUtcNow().UtcDateTime - calibration.CalibratedAt > CalibrationStalenessThreshold;
    }

    /// <summary>
    ///     Resolves calibration data — recalibrating automatically if stale or missing.
    ///     Returns null when no calibration could be obtained.
    /// </summary>
    private async Task<Result<Option<VoiceCalibration>>> ReadCalibrationAsync(
        string voiceId,
        VoiceSettings settings,
        string modelId,
        CancellationToken cancellationToken)
    {
        modelId = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId.Trim();
        var key = new VoiceCalibrationLookup(voiceId, settings, modelId).Key;
        var now = timeProvider.GetUtcNow();
        lock (_readCacheGate)
        {
            if (_readCache.TryGetValue(key, out var cached))
            {
                if (cached.ExpiresAt > now)
                    return Result<Option<VoiceCalibration>>.Success(cached.Value);

                _readCache.Remove(key);
            }
        }

        Task<Result<Option<VoiceCalibration>>>? readTask =
            modelId.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                ? repository.GetCalibrationAsync(voiceId, settings, cancellationToken)
                : repository.GetCalibrationAsync(voiceId, settings, cancellationToken, modelId);
        if (readTask is null && !modelId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            readTask = repository.GetCalibrationAsync(voiceId, settings, cancellationToken);
        if (readTask is null)
            return Result<Option<VoiceCalibration>>.Failure("Calibration repository returned no result.");

        var result = await readTask;
        if (result is null)
            return Result<Option<VoiceCalibration>>.Failure("Calibration repository returned no result.");

        if (result.IsSuccess)
        {
            lock (_readCacheGate)
                _readCache[key] = (result.Value, now + CalibrationReadCacheTtl);
        }

        return result;
    }

    private void InvalidateReadCache(string voiceId, VoiceSettings settings, string modelId)
    {
        var key = new VoiceCalibrationLookup(voiceId, settings, modelId).Key;
        lock (_readCacheGate)
            _readCache.Remove(key);
    }

    private async Task<VoiceCalibration?> ResolveCalibrationAsync(
        string voiceId,
        VoiceSettings effectiveSettings,
        bool triggerCalibrationIfMissing,
        CancellationToken cancellationToken,
        Option<string> modelOverride = default)
    {
        var modelId = modelOverride.GetValueOr("unknown");
        var calibrationResult = await ReadCalibrationAsync(
            voiceId, effectiveSettings, modelId, cancellationToken);

        if (calibrationResult.IsSuccess && calibrationResult.Value.HasValue)
        {
            var calibration = calibrationResult.Value.Value;

            if (!IsStale(calibration))
                return calibration;

            logger.LogWarning(
                "Calibration for voice {VoiceId} is stale (calibrated {Elapsed:F1}h ago). Triggering recalibration.",
                voiceId, (timeProvider.GetUtcNow().UtcDateTime - calibration.CalibratedAt).TotalHours);

            var refreshResult = await CalibrateVoiceAsync(
                voiceId, Option<VoiceSettings>.Some(effectiveSettings), cancellationToken, modelOverride);
            if (refreshResult.IsSuccess)
                return refreshResult.Value;

            logger.LogWarning(
                "Calibration refresh failed for voice {VoiceId}: {Error}. Using stale data.",
                voiceId, refreshResult.Error);
            return calibration;
        }

        if (!triggerCalibrationIfMissing)
            return null;

        var result = await CalibrateVoiceAsync(
            voiceId, Option<VoiceSettings>.Some(effectiveSettings), cancellationToken, modelOverride);
        if (result.IsSuccess)
            return result.Value;

        logger.LogWarning(
            "Calibration failed for voice {VoiceId}: {Error}. Using defaults.",
            voiceId, result.Error);

        return null;
    }

    private static VoiceSettings ResolveSettings(Option<VoiceSettings> settings)
    {
        return settings.GetValueOr(VoiceSettings.Default);
    }

    private double ResolveWordsPerMinute(VoiceCalibration calibration)
    {
        if (calibration.WordsPerMinute > 0)
            return calibration.WordsPerMinute;

        if (calibration.SyllablesPerSecond > 0 && calibration.SyllablesPerWord > 0)
            return calibration.SyllablesPerSecond * 60.0 / calibration.SyllablesPerWord;

        return _broadcastOptions.WordsPerMinute;
    }

    private async Task<VoiceCalibration> GetOrCreateCalibrationAsync(
        string voiceId,
        VoiceSettings settings,
        int? syllablesCount = null,
        double? referenceDuration = null,
        string modelId = "unknown",
        CancellationToken cancellationToken = default)
    {
        var calibrationResult = await ReadCalibrationAsync(
            voiceId, settings, modelId, cancellationToken);

        if (calibrationResult.IsSuccess && calibrationResult.Value.HasValue)
            return calibrationResult.Value.Value;

        return new VoiceCalibration(
            settings.GetCalibrationConfigurationKey(voiceId, modelId), voiceId, settings, modelId)
        {
            Provider = ProviderNames.ElevenLabs,
            RunCount = 0,
            WordsPerMinute = _broadcastOptions.WordsPerMinute,
            SyllablesPerMinute = _broadcastOptions.SyllablesPerSecond * 60.0,
            SyllablesPerWord = CalibrationDefaults.SyllablesPerWord,
            ReferenceSyllableCount = syllablesCount ?? 0,
            ReferenceDurationSeconds = referenceDuration ?? 0,
            CalibratedAt = timeProvider.GetUtcNow().UtcDateTime,
            ValidFor = TimeSpan.FromDays(30)
        };
    }
}
