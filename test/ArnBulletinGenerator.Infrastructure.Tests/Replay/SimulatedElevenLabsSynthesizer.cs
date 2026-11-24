using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     ElevenLabs stand-in whose durations are FITTED FROM REAL DEV SYNTHESIS HISTORY
///     (<c>synthesisHistory</c> table export, committed at <c>test/corpus/fallback-replay/synthesis-history.json</c>).
///     Per (voiceId, modelId) it least-squares fits <c>durationSeconds * speed = a * syllables + b</c>
///     over the recorded reads (850 real ElevenLabs calls; leave-one-out MAE 0.9–1.7s per voice —
///     at/below the measured 2.55s identical-input spread of real ElevenLabs, i.e. the model is as
///     accurate as physics allows for a deterministic simulator). Unseen voices fall back to the
///     pooled global fit.
///     <para>
///     Emits a real WAV (low-amplitude sine) of exactly the predicted duration so the production
///     <c>AudioPipelineService</c> post-synthesis machinery (duration measurement, compress/pad,
///     slot-fill gate) operates mechanically unchanged. Deterministic by default; set
///     <c>ARN_REPLAY_NOISE_SIGMA</c> (seconds at speed 1.0) for seeded Monte Carlo jitter that
///     mimics ElevenLabs' inherent duration variance.
///     </para>
///     Mirrors the real provider's "VoiceId is required" failure so misconfigured-voice scenarios
///     reproduce faithfully.
/// </summary>
internal sealed class SimulatedElevenLabsSynthesizer : IAudioSynthesizer
{
    private const int SampleRate = 16_000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private readonly ILogger<SimulatedElevenLabsSynthesizer> _logger;
    private readonly Dictionary<(string Voice, string Model), List<(double Speed, double SecPerSyl, double Intercept)>> _models;
    private readonly (double SecPerSyl, double Intercept) _global;
    private readonly double _noiseSigma;

    public SimulatedElevenLabsSynthesizer(string historyJsonPath, ILogger<SimulatedElevenLabsSynthesizer> logger)
    {
        _logger = logger;
        var records = JsonSerializer.Deserialize<List<SynthesisRecord>>(File.ReadAllText(historyJsonPath),
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidOperationException($"Empty synthesis history seed: {historyJsonPath}");

        _global = Fit(records) ?? (0.246, 0.0); // pooled corpus slope as last resort

        // Empirical rates PER SPEED BUCKET: the ElevenLabs `speed` setting's duration effect is
        // measurably SUPERLINEAR (9Kes same-day reads: 0.43 s/word @1.09 vs 0.31 s/word @1.2 — a
        // 1.33× duration swing for a 1.10× speed change), so a single speed-normalized fit
        // systematically under-predicts slow reads. Nearest recorded bucket wins; only the residual
        // speed gap is scaled inverse-linearly.
        _models = records
            .GroupBy(r => (r.VoiceId, r.ModelId))
            .Where(g => g.Count() >= 8)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => Math.Round(r.Speed, 2))
                    .Where(sg => sg.Count() >= 8)
                    .Select(sg => Fit([.. sg]) is { } fit ? (sg.Key, fit.SecPerSyl, fit.Intercept) : default)
                    .Where(t => t != default)
                    .OrderBy(t => t.Key)
                    .ToList());

        _noiseSigma = double.TryParse(
            Environment.GetEnvironmentVariable("ARN_REPLAY_NOISE_SIGMA"), out double s)
            ? s
            : 0.0;

        _logger.LogInformation(
            "Simulated ElevenLabs ready — {GroupCount} voice/model groups ({BucketCount} speed buckets) from {RecordCount} real reads, noiseSigma={Sigma}s",
            _models.Count, _models.Sum(m => m.Value.Count), records.Count, _noiseSigma);
    }

    public Task<Result<SynthesizedAudio>> SynthesizeAsync(
        string text,
        Option<VoiceSettings> settings = default,
        Option<string> voiceIdOverride = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string voiceId = voiceIdOverride.GetValueOr(string.Empty);
        if (string.IsNullOrWhiteSpace(voiceId))
            return Task.FromResult(Result<SynthesizedAudio>.Failure(
                "VoiceId is required. Each schedule or job must specify a voice."));

        string modelId = modelOverride.GetValueOr(string.Empty);
        double speed = settings.Match(v => v.Speed <= 0 ? 1.0 : v.Speed, () => 1.0);

        // Tags are ElevenLabs delivery directions, not spoken words — never counted.
        string plain = DeliveryTags.Strip(text);
        int syllables = SyllableCounter.CountSyllables(plain);

        double duration = Math.Max(0.5, Predict(voiceId, modelId, speed, syllables));

        if (_noiseSigma > 0)
            duration = Math.Max(0.5, duration + Gaussian(text, voiceId) * _noiseSigma / speed);

        _logger.LogDebug(
            "[SimTTS] voice={Voice} model={Model} speed={Speed:F2} syllables={Syl} -> {Duration:F2}s",
            voiceId, modelId, speed, syllables, duration);

        return Task.FromResult(Result<SynthesizedAudio>.Success(new SynthesizedAudio(BuildWav(duration))));
    }

    private double Predict(string voiceId, string modelId, double speed, int syllables)
    {
        var buckets = ResolveBuckets(voiceId, modelId);
        if (buckets is not { Count: > 0 })
            return (_global.SecPerSyl * syllables + _global.Intercept) / speed;

        // Nearest recorded speed bucket; residual gap scaled inverse-linearly.
        var bucket = buckets.MinBy(b => Math.Abs(b.Speed - speed));
        double atBucket = bucket.SecPerSyl * syllables + bucket.Intercept;
        return atBucket * bucket.Speed / speed;
    }

    private List<(double Speed, double SecPerSyl, double Intercept)>? ResolveBuckets(string voiceId, string modelId)
    {
        if (_models.TryGetValue((voiceId, modelId), out var exact) && exact.Count > 0)
            return exact;

        // Same voice, any model — better than the global pool. Prefer the group with most buckets.
        return _models.Where(kv => kv.Key.Voice == voiceId && kv.Value.Count > 0)
            .OrderByDescending(kv => kv.Value.Count)
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    /// <summary>OLS fit of durationSeconds against syllable count (within one speed bucket).</summary>
    private static (double SecPerSyl, double Intercept)? Fit(IReadOnlyList<SynthesisRecord> records)
    {
        var pts = records
            .Where(r => r.SyllableCount > 0 && r.AchievedDuration > 0 && r.Speed > 0)
            .Select(r => (X: (double)r.SyllableCount, Y: r.AchievedDuration))
            .ToList();
        if (pts.Count < 2)
            return null;

        double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
        double sxx = pts.Sum(p => (p.X - mx) * (p.X - mx));
        if (sxx <= 0)
            return null;
        double slope = pts.Sum(p => (p.X - mx) * (p.Y - my)) / sxx;
        return (slope, my - slope * mx);
    }

    /// <summary>Deterministic per-(text,voice) standard normal via Box–Muller on a stable hash.</summary>
    private static double Gaussian(string text, string voiceId)
    {
        int h = HashCode.Combine(text, voiceId);
        var rng = new Random(h);
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static byte[] BuildWav(double durationSeconds)
    {
        int sampleCount = (int)(SampleRate * durationSeconds);
        var pcm = new byte[sampleCount * Channels * (BitsPerSample / 8)];
        const double frequency = 220.0;
        const short amplitude = 900;
        for (int i = 0; i < sampleCount; i++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / SampleRate) * amplitude);
            int offset = i * 2;
            pcm[offset] = (byte)(sample & 0xff);
            pcm[offset + 1] = (byte)((sample >> 8) & 0xff);
        }

        return WavHeaderBuilder.WrapInWavHeader(pcm, SampleRate, Channels, BitsPerSample);
    }
}
