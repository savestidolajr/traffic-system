using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using SoundTouch;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

[Trait("Category", "Slow")]
public class WavDurationProcessorTests(ITestOutputHelper output)
{
    private readonly WavDurationProcessor _processor = new(Substitute.For<ILogger<WavDurationProcessor>>());

    private static byte[] CreateWavWithSamples(short[] samples, int sampleRate = 44100, short channels = 1)
    {
        byte[] pcm = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
        return WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate, channels, 16);
    }

    // --- GetDuration ---

    [Fact]
    public void GetDuration_ValidWav_ReturnsCorrectDuration()
    {
        // 44100 mono samples at 44100 Hz = exactly 1 second
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);

        _processor.GetDuration(wav).ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void GetDuration_EmptyArray_ReturnsZero()
    {
        _processor.GetDuration([]).ShouldBe(0);
    }

    [Fact]
    public void GetDuration_InvalidData_ReturnsZero()
    {
        _processor.GetDuration([1, 2, 3, 4]).ShouldBe(0);
    }

    // --- TrimTrailingSilence ---

    [Fact]
    public void TrimTrailingSilence_AllSilentTail_TrimsToLastLoudSample()
    {
        // 1000 loud samples followed by 500 silent samples
        short[] samples = new short[1500];
        for (int i = 0; i < 1000; i++) samples[i] = 1000;
        // remaining 500 are 0 (silent)

        byte[] wav = CreateWavWithSamples(samples);
        byte[] trimmed = _processor.TrimTrailingSilence(wav);

        _processor.GetDuration(trimmed).ShouldBeLessThan(_processor.GetDuration(wav));
    }

    [Fact]
    public void TrimTrailingSilence_NoSilentTail_ReturnsIdentical()
    {
        // All samples are loud
        short[] samples = new short[1000];
        for (int i = 0; i < samples.Length; i++) samples[i] = 500;

        byte[] wav = CreateWavWithSamples(samples);
        byte[] result = _processor.TrimTrailingSilence(wav);

        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void TrimTrailingSilence_EmptyData_ReturnsOriginal()
    {
        byte[] result = _processor.TrimTrailingSilence([]);
        result.ShouldBeEmpty();
    }

    // --- TrimLeadingSilence ---

    [Fact]
    public void TrimLeadingSilence_SilentLead_TrimsAndReportsSeconds()
    {
        // 22050 silent samples (0.5s at 44.1kHz) followed by 1000 loud samples
        short[] samples = new short[23050];
        for (int i = 22050; i < samples.Length; i++) samples[i] = 1000;

        byte[] wav = CreateWavWithSamples(samples);
        byte[] trimmed = _processor.TrimLeadingSilence(wav, out double trimmedSeconds);

        // 0.5s lead minus the 0.02s onset cushion
        trimmedSeconds.ShouldBe(0.48, 0.01);
        _processor.GetDuration(trimmed).ShouldBe(_processor.GetDuration(wav) - trimmedSeconds, 0.001);
    }

    [Fact]
    public void TrimLeadingSilence_KeepsOnsetCushion_SpeechDoesNotStartAtSampleZero()
    {
        short[] samples = new short[23050];
        for (int i = 22050; i < samples.Length; i++) samples[i] = 1000;

        byte[] wav = CreateWavWithSamples(samples);
        byte[] trimmed = _processor.TrimLeadingSilence(wav, out _);

        // The first ~0.02s of the trimmed clip must still be the silent cushion.
        var info = WavInfoParser.GetWavInfo(trimmed)!;
        int firstSample = BitConverter.ToInt16(trimmed, (int)info.DataPosition);
        firstSample.ShouldBe((short)0);
    }

    [Fact]
    public void TrimLeadingSilence_NoSilentLead_ReturnsIdentical()
    {
        short[] samples = new short[1000];
        for (int i = 0; i < samples.Length; i++) samples[i] = 500;

        byte[] wav = CreateWavWithSamples(samples);
        byte[] result = _processor.TrimLeadingSilence(wav, out double trimmedSeconds);

        trimmedSeconds.ShouldBe(0);
        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void TrimLeadingSilence_AllSilent_ReturnsOriginal()
    {
        // Entirely silent within the scan window — nothing voiced to anchor on; leave untouched.
        short[] samples = new short[44100];

        byte[] wav = CreateWavWithSamples(samples);
        byte[] result = _processor.TrimLeadingSilence(wav, out double trimmedSeconds);

        trimmedSeconds.ShouldBe(0);
        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void TrimLeadingSilence_LeadLongerThanCap_TrimsOnlyWithinCap()
    {
        // 2.0s of silence then speech, with a 1.5s default cap: the voiced block sits beyond
        // the scan window, so the clip is left untouched (never trim blind past the cap).
        int lead = (int)(2.0 * 44100);
        short[] samples = new short[lead + 1000];
        for (int i = lead; i < samples.Length; i++) samples[i] = 1000;

        byte[] wav = CreateWavWithSamples(samples);
        byte[] result = _processor.TrimLeadingSilence(wav, out double trimmedSeconds);

        trimmedSeconds.ShouldBe(0);
        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void TrimLeadingSilence_EmptyData_ReturnsOriginal()
    {
        byte[] result = _processor.TrimLeadingSilence([], out double trimmedSeconds);
        trimmedSeconds.ShouldBe(0);
        result.ShouldBeEmpty();
    }

    // --- EnsureDuration ---

    [Fact]
    public void EnsureDuration_ShorterThanTarget_PadsSilence()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] padded = _processor.EnsureDuration(wav, 2.0);

        _processor.GetDuration(padded).ShouldBeGreaterThanOrEqualTo(2.0);
    }

    [Fact]
    public void EnsureDuration_LongerThanTarget_ReturnsOriginal()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 88200); // 2s
        byte[] result = _processor.EnsureDuration(wav, 1.0);

        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void EnsureDuration_InvalidWav_ReturnsOriginal()
    {
        byte[] data = new byte[] { 1, 2, 3 };
        _processor.EnsureDuration(data, 5.0).ShouldBeSameAs(data);
    }

    // --- AdjustSpeed ---

    [Fact]
    public void AdjustSpeed_SlowerTarget_ChangesPlaybackDuration()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] adjusted = _processor.AdjustSpeed(wav, 2.0);

        _processor.GetDuration(adjusted).ShouldBe(2.0, 0.05);
    }

    [Fact]
    public void AdjustSpeed_FasterTarget_ChangesPlaybackDuration()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 22050, numSamples: 22050); // 1s at 22050
        byte[] adjusted = _processor.AdjustSpeed(wav, 0.5);

        _processor.GetDuration(adjusted).ShouldBe(0.5, 0.05);
    }

    [Fact]
    public void AdjustSpeed_SameTarget_ReturnsOriginal()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] result = _processor.AdjustSpeed(wav, 1.0);

        result.Length.ShouldBe(wav.Length);
    }

    /// <summary>
    ///     ARN, 2026-08-24: test job 23b9bb1c aired a read that stopped mid-word — "...powered by"
    ///     was heard as "...powered". The retime landed 22 samples (0.5ms) of zero pad after live
    ///     speech, so the file stepped from -283 to digital zero in one sample, ending on RISING
    ///     energy at -27.7 dBFS. The fade was skipped precisely because the result was short, on the
    ///     reasoning that a zero-padded result already ends in silence. A sub-millisecond pad is not
    ///     silence. Whatever the pad, the audio's own tail has to reach zero.
    /// </summary>
    [Theory]
    // A same-duration target is excluded deliberately: AdjustSpeed returns the input untouched, so
    // it makes no cut and owes no fade — the source's own tail is TrimAfterSpeech's responsibility.
    [InlineData(0.9)]     // compressing — the on-air case (24.7s read into a 22s slot)
    [InlineData(1.1)]     // stretching
    public void AdjustSpeed_ReadEndingOnLoudSpeech_LandsOnZeroRatherThanStepping(double targetSeconds)
    {
        // Full-scale-ish alternating tone for the whole second: there is no natural decay anywhere,
        // so any zero in the output is the pipeline's doing, not the source's.
        var samples = new short[44100];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(i % 2 == 0 ? 12000 : -12000);
        byte[] wav = CreateWavWithSamples(samples);

        byte[] adjusted = _processor.AdjustSpeed(wav, targetSeconds);

        short[] outSamples = ReadSamples(adjusted);
        int last = Array.FindLastIndex(outSamples, s => s != 0);
        last.ShouldBeGreaterThan(0, "the retime produced no audio at all");

        // The last sample carrying signal must be a fade remnant, not a full-amplitude cut. A 5ms
        // linear fade at 44.1kHz drives its final sample to a small fraction of the peak.
        Math.Abs((int)outSamples[last]).ShouldBeLessThan(1200,
            $"file ends on live speech ({outSamples[last]}) — the tail was guillotined, not faded");

        // And the approach to it must be monotone-ish rather than a cliff: the fade ramps down, so
        // energy in the final 2ms sits well below the body of the read.
        int fadeWindow = (int)(0.002 * 44100);
        int peakInTail = 0;
        for (int i = Math.Max(0, last - fadeWindow); i <= last; i++)
            peakInTail = Math.Max(peakInTail, Math.Abs((int)outSamples[i]));
        peakInTail.ShouldBeLessThan(12000,
            "the final 2ms is at full body level — no fade was applied");
    }

    private static short[] ReadSamples(byte[] wav)
    {
        var info = WavInfoParser.GetWavInfo(wav);
        info.ShouldNotBeNull();
        int start = (int)info.DataPosition;
        int count = info.DataSize / 2;
        var samples = new short[count];
        for (int i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(wav, start + (i * 2));

        return samples;
    }

    [Fact]
    public void AdjustSpeed_InvalidWav_ReturnsOriginal()
    {
        byte[] data = new byte[] { 1, 2, 3 };
        _processor.AdjustSpeed(data, 1.0).ShouldBeSameAs(data);
    }

    [Fact]
    public void AdjustSpeed_Slowing_PreservesSampleRate()
    {
        // Pitch-preserving retime keeps the sample rate and adds samples — unlike the old
        // vari-speed method, which slowed playback by lowering the sample rate.
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] adjusted = _processor.AdjustSpeed(wav, 1.4);

        var info = WavInfoParser.GetWavInfo(adjusted);
        info.ShouldNotBeNull();
        info!.SampleRate.ShouldBe(44100);
        _processor.GetDuration(adjusted).ShouldBe(1.4, 0.05);
    }

    [Fact]
    public void AdjustSpeed_SpeedingUp_PreservesSampleRate()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] adjusted = _processor.AdjustSpeed(wav, 0.7);

        var info = WavInfoParser.GetWavInfo(adjusted);
        info.ShouldNotBeNull();
        info!.SampleRate.ShouldBe(44100);
        _processor.GetDuration(adjusted).ShouldBe(0.7, 0.05);
    }

    [Fact]
    public void AdjustSpeed_Slowing_AddsSamplesAtSameRate()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        var before = WavInfoParser.GetWavInfo(wav)!;
        byte[] adjusted = _processor.AdjustSpeed(wav, 1.5);
        var after = WavInfoParser.GetWavInfo(adjusted)!;

        after.SampleRate.ShouldBe(before.SampleRate);
        after.DataSize.ShouldBeGreaterThan(before.DataSize);
    }

    [Fact]
    public void AdjustSpeed_ThirtySecondStereoTake_AvoidsFullInputBufferAllocation()
    {
        // Deferred audit item 4: TimeStretchToTarget used to materialise the whole take as
        // float[totalSamples] (~10MB for a 30s 44.1kHz stereo buffer) before feeding SoundTouch
        // in 4096-frame chunks. It now converts bytes→float per chunk into a reused buffer.
        // AdjustSpeedOldFullBufferAlgorithm below reproduces the deleted algorithm verbatim,
        // wrapped in the SAME surrounding steps (GetDuration/WavInfoParser/exact-fit/WrapInWavHeader)
        // as the real AdjustSpeed, so the two measurements cover identical scope and the delta is
        // attributable only to the buffer-allocation change (log-call allocation is a few hundred
        // bytes either way and is negligible at this MB scale).
        const int sampleRate = 44100;
        const short channels = 2;
        const int frames = 30 * sampleRate; // 30s stereo take
        byte[] wav = WavTestHelper.CreateMinimalWav(channels: channels, sampleRate: sampleRate, numSamples: frames);
        const double targetSeconds = 33.0; // tempo 30/33 ≈ 0.909, within the [0.5, 2.0] clamp

        long baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] baselineResult = AdjustSpeedOldFullBufferAlgorithm(wav, targetSeconds);
        long baselineAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;

        long chunkedBefore = GC.GetAllocatedBytesForCurrentThread();
        byte[] adjusted = _processor.AdjustSpeed(wav, targetSeconds);
        long chunkedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - chunkedBefore;

        long totalSamples = (long)frames * channels;
        long eliminatedBufferBytes = totalSamples * sizeof(float);

        long savedBytes = baselineAllocatedBytes - chunkedAllocatedBytes;
        output.WriteLine($"Eliminated float[totalSamples] buffer alone would be: {eliminatedBufferBytes / 1024.0:F0} KB");
        output.WriteLine($"Baseline (deleted full-buffer algorithm), full AdjustSpeed scope: {baselineAllocatedBytes / 1024.0:F0} KB");
        output.WriteLine($"Current (chunked) implementation, full AdjustSpeed scope:        {chunkedAllocatedBytes / 1024.0:F0} KB");
        output.WriteLine($"Allocation saved:                                                {savedBytes / 1024.0:F0} KB");

        _processor.GetDuration(baselineResult).ShouldBe(targetSeconds, 0.05);
        _processor.GetDuration(adjusted).ShouldBe(targetSeconds, 0.05);
        // The chunked version must allocate materially less than the reproduced pre-change
        // baseline for the identical input/target, and the saving must track the size of the
        // eliminated whole-take float[] buffer (allow 20% slack for GC/JIT measurement noise) —
        // proving the removed allocation, not just "some" unrelated improvement.
        chunkedAllocatedBytes.ShouldBeLessThan(baselineAllocatedBytes);
        savedBytes.ShouldBeGreaterThan((long)(eliminatedBufferBytes * 0.8));
    }

    /// <summary>
    ///     Reproduces the deleted <c>AdjustSpeed</c> → <c>TimeStretchToTarget</c> algorithm (full
    ///     <c>float[totalSamples]</c> allocated up front, then fed to SoundTouch in 4096-frame
    ///     chunks) so the before/after allocation comparison is measured against the real prior
    ///     behaviour end-to-end, not guessed or measured over a mismatched scope.
    /// </summary>
    private byte[] AdjustSpeedOldFullBufferAlgorithm(byte[] audioData, double targetSeconds)
    {
        double currentDuration = _processor.GetDuration(audioData);
        if (currentDuration <= 0 || targetSeconds <= 0 || currentDuration == targetSeconds) return audioData;

        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null || info.BitsPerSample != 16) return audioData;

        double tempo = Math.Clamp(currentDuration / targetSeconds, 0.5, 2.0);

        int dataStart = (int)info.DataPosition;
        int dataBytes = Math.Min(info.DataSize, audioData.Length - dataStart);
        int totalSamples = dataBytes / 2;
        float[] input = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
            input[i] = BitConverter.ToInt16(audioData, dataStart + (i * 2)) / 32768f;

        var processor = new SoundTouchProcessor
        {
            SampleRate = info.SampleRate,
            Channels = info.Channels,
            Tempo = tempo,
        };
        processor.SetSetting(SettingId.SequenceDurationMs, 40);
        processor.SetSetting(SettingId.SeekWindowDurationMs, 15);
        processor.SetSetting(SettingId.OverlapDurationMs, 8);

        int framesIn = info.Channels > 0 ? totalSamples / info.Channels : totalSamples;
        var output = new List<float>(capacity: ((int)(framesIn / tempo) * info.Channels) + 4096);
        float[] buffer = new float[4096 * info.Channels];

        const int putChunkFrames = 4096;
        int frameOffset = 0;
        int received;
        while (frameOffset < framesIn)
        {
            int chunk = Math.Min(putChunkFrames, framesIn - frameOffset);
            processor.PutSamples(input.AsSpan(frameOffset * info.Channels), chunk);
            frameOffset += chunk;
            while ((received = processor.ReceiveSamples(buffer.AsSpan(), 4096)) > 0)
                output.AddRange(buffer.AsSpan(0, received * info.Channels));
        }
        processor.Flush();
        while ((received = processor.ReceiveSamples(buffer.AsSpan(), 4096)) > 0)
            output.AddRange(buffer.AsSpan(0, received * info.Channels));

        int targetFrames = (int)Math.Round(targetSeconds * info.SampleRate);
        int targetSamples = targetFrames * info.Channels;
        byte[] pcm = new byte[targetSamples * 2];
        int copy = Math.Min(output.Count, targetSamples);
        for (int i = 0; i < copy; i++)
        {
            int v = (int)Math.Round(Math.Clamp(output[i], -1f, 1f) * 32767f);
            short s = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[(i * 2) + 1] = (byte)((s >> 8) & 0xFF);
        }

        return WavHeaderBuilder.WrapInWavHeader(pcm, info.SampleRate, info.Channels, info.BitsPerSample);
    }

    // --- EnsureExactDuration ---

    [Fact]
    public void EnsureExactDuration_SlightlyShort_PadsToExact()
    {
        // 44000 samples at 44100 Hz ≈ 0.9977s, target 1.0s — delta ~0.1s, well within 0.5s guard
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44000);
        byte[] result = _processor.EnsureExactDuration(wav, 1.0);

        _processor.GetDuration(result).ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void EnsureExactDuration_SlightlyLong_TrimsToExact()
    {
        // 44200 samples at 44100 Hz ≈ 1.0023s, target 1.0s — delta ~0.05s, within 0.5s guard
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44200);
        byte[] result = _processor.EnsureExactDuration(wav, 1.0);

        _processor.GetDuration(result).ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void EnsureExactDuration_ExcessIsVoicedSpeech_CompressesInsteadOfDiscardingTheTail()
    {
        byte[] wav = WavTestHelper.CreateWavWithVoicedTail(2.12, 0.12);

        byte[] result = _processor.EnsureExactDuration(wav, 2.0);

        _processor.GetDuration(result).ShouldBe(2.0, 0.001);
        WavTestHelper.PeakAbsSampleInFinalSeconds(result, 0.15).ShouldBeGreaterThan(48);
    }

    [Fact]
    public void EnsureExactDuration_ExcessIsSilence_StillTrimsBluntly()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: (int)(2.12 * 44100));

        byte[] result = _processor.EnsureExactDuration(wav, 2.0);

        _processor.GetDuration(result).ShouldBe(2.0, 0.001);
        WavTestHelper.PeakAbsSampleInFinalSeconds(result, 0.15).ShouldBe(0);
    }

    [Fact]
    public void EnsureExactDuration_LargeDelta_RefusesAndReturnsOriginal()
    {
        // 44100 samples = 1s, target 3s — delta 2s, exceeds 0.5s guard
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        byte[] result = _processor.EnsureExactDuration(wav, 3.0);

        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void EnsureExactDuration_AlreadyExact_ReturnsOriginal()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        byte[] result = _processor.EnsureExactDuration(wav, 1.0);

        result.Length.ShouldBe(wav.Length);
    }

    // --- TrimToDuration ---

    [Fact]
    public void TrimToDuration_LargeDelta_DoesNotRefuseUnlikeEnsureExactDuration()
    {
        // Same 2s delta that EnsureExactDuration_LargeDelta_RefusesAndReturnsOriginal refuses —
        // TrimToDuration has NO delta guard, so it pads all the way to target instead.
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        byte[] result = _processor.TrimToDuration(wav, 3.0);

        _processor.GetDuration(result).ShouldBe(3.0, 0.001);
    }

    [Fact]
    public void TrimToDuration_TrimsLargeSilenceTail_MatchingSpokenContent()
    {
        // 1000 loud samples followed by 4x as many (4000) silent samples — mimics the real
        // underrun bug: a slot-length silence pad appended after the spoken content, several
        // times longer than the speech itself.
        short[] samples = new short[5000];
        for (int i = 0; i < 1000; i++) samples[i] = 1000;
        // remaining 4000 are 0 (silent)

        byte[] wav = CreateWavWithSamples(samples);
        double loudDurationSeconds = 1000.0 / 44100.0;
        byte[] trimmed = _processor.TrimToDuration(wav, loudDurationSeconds);

        _processor.GetDuration(trimmed).ShouldBe(loudDurationSeconds, 0.001);
        _processor.GetDuration(trimmed).ShouldBeLessThan(_processor.GetDuration(wav) / 2);
    }

    [Fact]
    public void TrimToDuration_AlreadyExact_ReturnsOriginal()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        byte[] result = _processor.TrimToDuration(wav, 1.0);

        result.Length.ShouldBe(wav.Length);
    }

    [Fact]
    public void TrimToDuration_InvalidWav_ReturnsOriginal()
    {
        byte[] data = new byte[] { 1, 2, 3 };
        _processor.TrimToDuration(data, 5.0).ShouldBeSameAs(data);
    }

    [Fact]
    public void TrimToDuration_ShorterThanTarget_PadsSilence()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100); // 1s
        byte[] padded = _processor.TrimToDuration(wav, 2.0);

        _processor.GetDuration(padded).ShouldBeGreaterThanOrEqualTo(2.0);
    }

    // --- GetSpokenDuration ---

    [Fact]
    public void GetSpokenDuration_TrailingSilencePad_ReturnsSpokenSpanShorterThanFullDuration()
    {
        // 1000 loud samples followed by a long trailing silence pad (50000 samples) — mimics a
        // synthesized read padded out to fill a slot. The spoken span must end at the loud
        // block, well short of the full padded duration (the whole point vs GetDuration).
        short[] samples = new short[51000];
        for (int i = 0; i < 1000; i++) samples[i] = 1000;
        // remaining 50000 default to 0 (silent)

        byte[] wav = CreateWavWithSamples(samples);
        double loudDurationSeconds = 1000.0 / 44100.0;

        double spoken = _processor.GetSpokenDuration(wav);

        spoken.ShouldBe(loudDurationSeconds, 0.001);
        spoken.ShouldBeLessThan(_processor.GetDuration(wav));
    }

    [Fact]
    public void GetSpokenDuration_EntirelySilentData_ReturnsZero()
    {
        short[] samples = new short[1000]; // all zero — entirely silent

        byte[] wav = CreateWavWithSamples(samples);

        _processor.GetSpokenDuration(wav).ShouldBe(0.0);
    }

    [Fact]
    public void GetSpokenDuration_NonSixteenBitWav_FallsBackToFullDuration()
    {
        // 8-bit PCM is unsupported for the amplitude scan and must fall back to GetDuration —
        // the full duration, silence included — rather than mis-reading the sample bytes.
        byte[] pcm = new byte[500];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = 200;
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate: 44100, channels: 1, bitsPerSample: 8);

        _processor.GetSpokenDuration(wav).ShouldBe(_processor.GetDuration(wav));
    }

    [Fact]
    public void GetSpokenDuration_AmplitudeThreshold_ControlsWhetherQuietTailCountsAsVoiced()
    {
        // Loud block, then a quiet-but-not-silent tail (amplitude 30), then true silence.
        short[] samples = new short[1400];
        for (int i = 0; i < 1000; i++) samples[i] = 1000; // loud
        for (int i = 1000; i < 1200; i++) samples[i] = 30; // quiet tail
        // remaining 200 default to 0 (true silence)

        byte[] wav = CreateWavWithSamples(samples);

        // Threshold 48 (default): the quiet tail (30) reads as silent too, so the scan trims
        // straight back to the end of the loud block.
        _processor.GetSpokenDuration(wav, amplitudeThreshold: 48)
            .ShouldBe(1000.0 / 44100.0, 0.001);

        // Threshold 20: the quiet tail (30) now exceeds the threshold and counts as voiced, so
        // only the true silence past it gets trimmed.
        _processor.GetSpokenDuration(wav, amplitudeThreshold: 20)
            .ShouldBe(1200.0 / 44100.0, 0.001);
    }
}
