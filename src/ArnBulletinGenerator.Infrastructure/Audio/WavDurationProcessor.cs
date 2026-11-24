using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using SoundTouch;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Post-processes synthesized WAV audio to hit a target duration exactly, via
///     pitch-preserving WSOLA time-stretch, silence padding/trimming, and sample-accurate
///     trim-or-pad fitting.
/// </summary>
public sealed class WavDurationProcessor(ILogger<WavDurationProcessor> logger) : IAudioProcessor
{
    // Pitch-preserving time-stretch stays transparent within a moderate tempo range; clamp to
    // guard against pathological factors. The pipeline bounds the request far tighter than this.
    private const double MinTempo = 0.5;
    private const double MaxTempo = 2.0;

    public byte[] EnsureDuration(byte[] audioData, double targetSeconds)
    {
        double currentDuration = GetDuration(audioData);
        logger.LogDebug(
            "[WavProcessor:EnsureDuration] Current: {Current:F3}s | Target: {Target:F3}s | Delta: {Delta:F3}s",
            currentDuration, targetSeconds, targetSeconds - currentDuration);

        if (currentDuration <= 0 || currentDuration >= targetSeconds)
        {
            logger.LogDebug(
                "[WavProcessor:EnsureDuration] No-op — current ({Current:F3}s) is {Reason}",
                currentDuration, currentDuration <= 0 ? "invalid (<=0)" : $">= target ({targetSeconds:F3}s)");
            return audioData;
        }

        var wavInfo = WavInfoParser.GetWavInfo(audioData);
        if (wavInfo is null) return audioData;

        double rawNeededBytes = (targetSeconds - currentDuration) * wavInfo.ByteRate;
        int neededBytes = AudioMathHelper.AlignToBlockBoundary(
            (int)rawNeededBytes,
            wavInfo.Channels,
            wavInfo.BitsPerSample);

        logger.LogDebug(
            "[WavProcessor:EnsureDuration] Padding {RawBytes:F1} raw bytes (aligned to {Aligned}) | ByteRate: {ByteRate} | Silence: {SilenceSec:F6}s",
            rawNeededBytes, neededBytes, wavInfo.ByteRate, (double)neededBytes / wavInfo.ByteRate);

        byte[] newWavData = new byte[audioData.Length + neededBytes];
        Buffer.BlockCopy(audioData, 0, newWavData, 0, audioData.Length);

        using var outMs = new MemoryStream(newWavData);
        using var writer = new BinaryWriter(outMs);
        outMs.Position = 4;
        writer.Write(newWavData.Length - 8);
        outMs.Position = wavInfo.DataPosition - 4;
        writer.Write(wavInfo.DataSize + neededBytes);

        double resultDuration = GetDuration(newWavData);
        logger.LogInformation(
            "[WavProcessor:EnsureDuration] Padded: {Before:F3}s → {After:F3}s (target: {Target:F3}s, residual: {Residual:F6}s)",
            currentDuration, resultDuration, targetSeconds, resultDuration - targetSeconds);

        return newWavData;
    }

    public double GetDuration(byte[] audioData)
    {
        var wavInfo = WavInfoParser.GetWavInfo(audioData);
        double duration = wavInfo?.DurationSeconds ?? 0;
        logger.LogDebug(
            "[WavProcessor:GetDuration] {Duration:F6}s | DataSize: {DataSize} bytes | ByteRate: {ByteRate} | SampleRate: {SampleRate}",
            duration, wavInfo?.DataSize ?? 0, wavInfo?.ByteRate ?? 0, wavInfo?.SampleRate ?? 0);
        return duration;
    }

    public byte[] AdjustSpeed(byte[] audioData, double targetSeconds)
    {
        double currentDuration = GetDuration(audioData);
        logger.LogDebug(
            "[WavProcessor:AdjustSpeed] Current: {Current:F6}s | Target: {Target:F6}s | Tempo: {Tempo:F6}",
            currentDuration, targetSeconds,
            currentDuration > 0 && targetSeconds > 0 ? currentDuration / targetSeconds : 0);

        if (currentDuration <= 0 || targetSeconds <= 0 || currentDuration == targetSeconds)
        {
            logger.LogDebug(
                "[WavProcessor:AdjustSpeed] No-op — current ({Current:F6}s) {Reason}",
                currentDuration, currentDuration <= 0 ? "is invalid (<=0)" : "== target (exact match)");
            return audioData;
        }

        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null) return audioData;
        if (info.BitsPerSample != 16)
        {
            logger.LogWarning(
                "[WavProcessor:AdjustSpeed] Unsupported bit depth {Bits} — returning audio unchanged",
                info.BitsPerSample);
            return audioData;
        }

        return TimeStretchToTarget(audioData, info, currentDuration, targetSeconds);
    }

    /// <summary>
    ///     Trims low-amplitude trailing audio from PCM WAV data.
    ///     Intended to remove generated tail silence without altering voiced sections.
    /// </summary>
    public byte[] TrimTrailingSilence(
        byte[] audioData,
        short amplitudeThreshold = 48,
        double maxTrimSeconds = 3.0)
    {
        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null || info.DataSize <= 0 || info.BitsPerSample != 16)
            return audioData;

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0)
            return audioData;

        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        if (dataEnd > audioData.Length)
            return audioData;

        int maxTrimBytes = AudioMathHelper.AlignToBlockBoundary((int)(maxTrimSeconds * info.ByteRate), info.Channels,
            info.BitsPerSample);
        int minScanIndex = Math.Max(dataStart, dataEnd - Math.Max(maxTrimBytes, blockAlign));

        int trimEnd = dataEnd;
        for (int index = dataEnd - blockAlign; index >= minScanIndex; index -= blockAlign)
        {
            if (!IsSilentBlock(audioData, index, blockAlign, amplitudeThreshold))
            {
                trimEnd = index + blockAlign;
                break;
            }

            trimEnd = index;
        }

        if (trimEnd >= dataEnd || trimEnd <= dataStart)
            return audioData;

        int targetDataSize = trimEnd - dataStart;
        int bytesToTrim = info.DataSize - targetDataSize;
        if (bytesToTrim <= 0)
            return audioData;

        int postDataLength = audioData.Length - dataEnd;
        int newLength = audioData.Length - bytesToTrim;
        byte[] trimmed = new byte[newLength];
        Buffer.BlockCopy(audioData, 0, trimmed, 0, dataStart + targetDataSize);

        // Preserve any post-data chunks (LIST, INFO, etc.) that follow the audio data.
        if (postDataLength > 0)
            Buffer.BlockCopy(audioData, dataEnd, trimmed, dataStart + targetDataSize, postDataLength);

        using var ms = new MemoryStream(trimmed);
        using var writer = new BinaryWriter(ms);
        ms.Position = 4;
        writer.Write(newLength - 8);
        ms.Position = info.DataPosition - 4;
        writer.Write(targetDataSize);

        logger.LogInformation(
            "[WavProcessor:TrimTail] Trimmed trailing audio: {Before:F3}s -> {After:F3}s (removed {TrimmedMs:F0}ms, threshold={Threshold})",
            GetDuration(audioData), GetDuration(trimmed), bytesToTrim * 1000.0 / info.ByteRate, amplitudeThreshold);

        return trimmed;
    }

    /// <summary>
    ///     Cuts everything after the last spoken sound, using the TTS aligner's own boundary rather
    ///     than an amplitude threshold, and fades the new end to zero. Removes the trailing inhale
    ///     eleven_v3 intermittently appends — see <see cref="SpeechTailTrimPolicy" /> for the
    ///     measurements and for why the alignment's LAST end time cannot be used directly.
    ///     <para>
    ///         <paramref name="lastSpokenSeconds" /> is a FLOOR, not the cut: a final plosive keeps
    ///         releasing past it, so the cut is the quietest window within
    ///         <see cref="SpeechTailTrimPolicy.ReleaseLookaheadSeconds" /> after it. Returns the
    ///         audio unchanged when the floor sits at or past the end (nothing to remove) or the WAV
    ///         cannot be inspected.
    ///     </para>
    /// </summary>
    public byte[] TrimAfterSpeech(byte[] audioData, double lastSpokenSeconds)
    {
        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null || info.DataSize <= 0 || info.BitsPerSample != 16 || lastSpokenSeconds <= 0)
            return audioData;

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0 || info.ByteRate <= 0)
            return audioData;

        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        if (dataEnd > audioData.Length)
            return audioData;

        int floorOffset = AudioMathHelper.AlignToBlockBoundary(
            (int)(lastSpokenSeconds * info.ByteRate), info.Channels, info.BitsPerSample);
        int searchStart = dataStart + floorOffset;
        if (searchStart >= dataEnd)
            return audioData;   // the aligner's boundary is already the end — nothing appended

        int windowBytes = Math.Max(blockAlign, AudioMathHelper.AlignToBlockBoundary(
            (int)(SpeechTailTrimPolicy.SearchWindowSeconds * info.ByteRate), info.Channels, info.BitsPerSample));
        int searchEnd = Math.Min(dataEnd, searchStart + AudioMathHelper.AlignToBlockBoundary(
            (int)(SpeechTailTrimPolicy.ReleaseLookaheadSeconds * info.ByteRate), info.Channels, info.BitsPerSample));

        var energies = new List<double>();
        for (int index = searchStart; index + windowBytes <= searchEnd; index += windowBytes)
            energies.Add(WindowRms(audioData, index, windowBytes));
        if (energies.Count == 0)
            return audioData;

        int quietest = SpeechTailTrimPolicy.QuietestWindow(energies);
        if (quietest < 0)
            return audioData;

        // Cut at the FAR edge of the quietest window: its near edge may still hold the tail of the
        // release, and the window is only a few milliseconds wide either way.
        int trimEnd = searchStart + ((quietest + 1) * windowBytes);
        if (trimEnd >= dataEnd || trimEnd <= dataStart)
            return audioData;

        int targetDataSize = trimEnd - dataStart;
        int postDataLength = audioData.Length - dataEnd;
        int newLength = dataStart + targetDataSize + postDataLength;
        byte[] trimmed = new byte[newLength];
        Buffer.BlockCopy(audioData, 0, trimmed, 0, dataStart + targetDataSize);
        if (postDataLength > 0)
            Buffer.BlockCopy(audioData, dataEnd, trimmed, dataStart + targetDataSize, postDataLength);

        ApplyFadeOut(trimmed, dataStart, targetDataSize, info);

        using (var ms = new MemoryStream(trimmed))
        using (var writer = new BinaryWriter(ms))
        {
            ms.Position = 4;
            writer.Write(newLength - 8);
            ms.Position = info.DataPosition - 4;
            writer.Write(targetDataSize);
        }

        logger.LogInformation(
            "[WavProcessor:TrimAfterSpeech] {Before:F3}s -> {After:F3}s (removed {RemovedMs:F0}ms after the last "
            + "spoken character at {Floor:F3}s)",
            GetDuration(audioData), GetDuration(trimmed),
            (info.DataSize - targetDataSize) * 1000.0 / info.ByteRate, lastSpokenSeconds);

        return trimmed;
    }

    private static double WindowRms(byte[] audioData, int index, int lengthBytes)
    {
        double sumSquares = 0;
        int samples = 0;
        for (int i = index; i + 1 < index + lengthBytes; i += 2)
        {
            short s = BitConverter.ToInt16(audioData, i);
            sumSquares += (double)s * s;
            samples++;
        }

        return samples == 0 ? 0 : Math.Sqrt(sumSquares / samples);
    }

    private static void ApplyFadeOut(byte[] wav, int dataStart, int dataSize, WavInfo info)
    {
        int fadeBytes = AudioMathHelper.AlignToBlockBoundary(
            (int)(SpeechTailTrimPolicy.FadeOutSeconds * info.ByteRate), info.Channels, info.BitsPerSample);
        fadeBytes = Math.Min(fadeBytes, dataSize);
        if (fadeBytes <= 0)
            return;

        int fadeStart = dataStart + dataSize - fadeBytes;
        int totalSamples = fadeBytes / 2;
        for (int i = 0; i < totalSamples; i++)
        {
            int at = fadeStart + (i * 2);
            double gain = 1.0 - ((i + 1) / (double)totalSamples);
            short faded = (short)Math.Clamp(
                (int)Math.Round(BitConverter.ToInt16(wav, at) * gain), short.MinValue, short.MaxValue);
            wav[at] = (byte)(faded & 0xFF);
            wav[at + 1] = (byte)((faded >> 8) & 0xFF);
        }
    }

    /// <summary>
    ///     Trims low-amplitude leading audio from PCM WAV data, reporting how many seconds were
    ///     removed so callers can re-anchor word timings. Keeps a small onset cushion
    ///     (<paramref name="keepSeconds" />) before the first voiced block so speech never starts
    ///     abruptly at sample zero. Introduced for filler-clip stitching: ElevenLabs clips carry
    ///     0.1–0.4s of leading silence which, added to the intended inter-clip pause, produced an
    ///     awkward ~0.7s joint on air (2026-07-20, test job 1700e9c3 — every natural sentence
    ///     boundary in the same read measured 0.22–0.44s).
    /// </summary>
    public byte[] TrimLeadingSilence(
        byte[] audioData,
        out double trimmedSeconds,
        short amplitudeThreshold = 48,
        double maxTrimSeconds = 1.5,
        double keepSeconds = 0.02)
    {
        trimmedSeconds = 0;
        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null || info.DataSize <= 0 || info.BitsPerSample != 16)
            return audioData;

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0)
            return audioData;

        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        if (dataEnd > audioData.Length)
            return audioData;

        int maxTrimBytes = AudioMathHelper.AlignToBlockBoundary((int)(maxTrimSeconds * info.ByteRate), info.Channels,
            info.BitsPerSample);
        int maxScanIndex = Math.Min(dataEnd, dataStart + Math.Max(maxTrimBytes, blockAlign));

        int firstVoiced = dataStart;
        bool foundVoiced = false;
        for (int index = dataStart; index + blockAlign <= maxScanIndex; index += blockAlign)
        {
            if (!IsSilentBlock(audioData, index, blockAlign, amplitudeThreshold))
            {
                firstVoiced = index;
                foundVoiced = true;
                break;
            }
        }

        if (!foundVoiced)
            return audioData; // no voiced block within the scan window — leave untouched

        int keepBytes = AudioMathHelper.AlignToBlockBoundary((int)(keepSeconds * info.ByteRate), info.Channels,
            info.BitsPerSample);
        int trimStart = Math.Max(dataStart, firstVoiced - keepBytes);
        int bytesToTrim = trimStart - dataStart;
        if (bytesToTrim <= 0)
            return audioData;

        int targetDataSize = info.DataSize - bytesToTrim;
        int postDataLength = audioData.Length - dataEnd;
        int newLength = audioData.Length - bytesToTrim;
        byte[] trimmed = new byte[newLength];
        // Headers up to the data chunk are unchanged; voiced data shifts left over the cut.
        Buffer.BlockCopy(audioData, 0, trimmed, 0, dataStart);
        Buffer.BlockCopy(audioData, trimStart, trimmed, dataStart, targetDataSize);

        // Preserve any post-data chunks (LIST, INFO, etc.) that follow the audio data.
        if (postDataLength > 0)
            Buffer.BlockCopy(audioData, dataEnd, trimmed, dataStart + targetDataSize, postDataLength);

        using var ms = new MemoryStream(trimmed);
        using var writer = new BinaryWriter(ms);
        ms.Position = 4;
        writer.Write(newLength - 8);
        ms.Position = info.DataPosition - 4;
        writer.Write(targetDataSize);

        trimmedSeconds = bytesToTrim / (double)info.ByteRate;
        logger.LogInformation(
            "[WavProcessor:TrimLead] Trimmed leading audio: {Before:F3}s -> {After:F3}s (removed {TrimmedMs:F0}ms, threshold={Threshold})",
            GetDuration(audioData), GetDuration(trimmed), trimmedSeconds * 1000.0, amplitudeThreshold);

        return trimmed;
    }

    /// <summary>
    ///     Returns the position (seconds) of the end of the last voiced block — the spoken span,
    ///     ignoring trailing silence. Unlike <see cref="TrimTrailingSilence" /> this is an UNCAPPED
    ///     pure measurement (no maxTrim limit) and never mutates the audio, so it stays correct even
    ///     when a large silence pad has been appended to fill a slot (where TrimTrailingSilence would
    ///     stop after 3s and leave most of the pad counted as speech). Falls back to the full
    ///     duration when the WAV cannot be inspected; returns 0 for entirely-silent data.
    /// </summary>
    public double GetSpokenDuration(byte[] audioData, short amplitudeThreshold = 48)
    {
        var info = WavInfoParser.GetWavInfo(audioData);
        if (info is null || info.DataSize <= 0 || info.BitsPerSample != 16 || info.ByteRate <= 0)
            return GetDuration(audioData);

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0)
            return GetDuration(audioData);

        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        if (dataEnd > audioData.Length)
            return GetDuration(audioData);

        for (int index = dataEnd - blockAlign; index >= dataStart; index -= blockAlign)
        {
            if (!IsSilentBlock(audioData, index, blockAlign, amplitudeThreshold))
                return (double)(index + blockAlign - dataStart) / info.ByteRate;
        }

        return 0.0;
    }

    /// <summary>
    ///     Pitch-preserving time-stretch (WSOLA via SoundTouch) so playback duration matches
    ///     <paramref name="targetSeconds" />. The sample rate is unchanged — only the number of
    ///     samples changes — so the voice keeps its pitch (unlike a sample-rate vari-speed retime).
    ///     Output is exact-fit (trim / zero-pad) to the target sample count.
    /// </summary>
    private byte[] TimeStretchToTarget(byte[] audioData, WavInfo info, double currentDuration, double targetSeconds)
    {
        double tempo = Math.Clamp(currentDuration / targetSeconds, MinTempo, MaxTempo);

        int dataStart = (int)info.DataPosition;
        int dataBytes = Math.Min(info.DataSize, audioData.Length - dataStart);
        int totalSamples = dataBytes / 2; // interleaved int16 samples across all channels
        int framesIn = info.Channels > 0 ? totalSamples / info.Channels : totalSamples;

        var processor = new SoundTouchProcessor
        {
            SampleRate = info.SampleRate,
            Channels = info.Channels,
            Tempo = tempo,
        };
        // Speech-tuned WSOLA parameters (SoundTouch's defaults are tuned for music).
        processor.SetSetting(SettingId.SequenceDurationMs, 40);
        processor.SetSetting(SettingId.SeekWindowDurationMs, 15);
        processor.SetSetting(SettingId.OverlapDurationMs, 8);

        var output = new List<float>(capacity: ((int)(framesIn / tempo) * info.Channels) + 4096);
        float[] receiveBuffer = new float[4096 * info.Channels];

        // Stream in put/drain cycles so the processor's internal FIFO stays small, and convert
        // bytes → float per chunk (never materialising the whole take as float[]) — measured
        // 2026-07-17 via WavDurationProcessorTests.AdjustSpeed_ThirtySecondStereoTake_..., a
        // 30s 44.1kHz stereo take's full AdjustSpeed call allocated 39.3MB with the deleted
        // full-buffer approach vs 28.9MB chunked (~10.3MB saved, matching the eliminated
        // float[totalSamples] buffer exactly) — byte-identical output either way.
        const int putChunkFrames = 4096;
        float[] putBuffer = new float[putChunkFrames * info.Channels];
        int frameOffset = 0;
        int received;
        while (frameOffset < framesIn)
        {
            int chunk = Math.Min(putChunkFrames, framesIn - frameOffset);
            int samplesInChunk = chunk * info.Channels;
            int byteOffset = dataStart + (frameOffset * info.Channels * 2);
            for (int i = 0; i < samplesInChunk; i++)
                putBuffer[i] = BitConverter.ToInt16(audioData, byteOffset + (i * 2)) / 32768f;
            processor.PutSamples(putBuffer.AsSpan(0, samplesInChunk), chunk);
            frameOffset += chunk;
            while ((received = processor.ReceiveSamples(receiveBuffer.AsSpan(), 4096)) > 0)
                output.AddRange(receiveBuffer.AsSpan(0, received * info.Channels));
        }
        processor.Flush();
        while ((received = processor.ReceiveSamples(receiveBuffer.AsSpan(), 4096)) > 0)
            output.AddRange(receiveBuffer.AsSpan(0, received * info.Channels));

        // Exact-fit to the target sample count (WSOLA residual is sub-frame to a few ms).
        int targetFrames = (int)Math.Round(targetSeconds * info.SampleRate);
        int targetSamples = targetFrames * info.Channels;
        byte[] pcm = new byte[targetSamples * 2]; // zero-initialised = trailing-silence pad
        int copy = Math.Min(output.Count, targetSamples);
        for (int i = 0; i < copy; i++)
        {
            int v = (int)Math.Round(Math.Clamp(output[i], -1f, 1f) * 32767f);
            short s = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[(i * 2) + 1] = (byte)((s >> 8) & 0xFF);
        }

        // The exact-fit above TRUNCATES whenever WSOLA produced more than the slot holds, which cuts
        // straight through the waveform and clicks — and it discards any fade an earlier stage
        // applied, because that fade is no longer at the end. Verified live on dev job acdfbe7f
        // (2026-08-10): the file ended on -1576 mid-cycle. Fade here, where the final length is
        // actually decided.
        //
        // Fade the REAL audio's tail (ending at `copy`), not the slot's. The old guard skipped the
        // fade entirely when WSOLA came up SHORT, on the reasoning that a zero-padded result already
        // ends in silence. That holds only when the pad is long enough to be silence. Test job
        // 23b9bb1c (2026-08-24) landed 22 samples — 0.5ms — of pad: the file stepped from -283 to
        // digital zero in one sample, ending on rising energy at -27.7 dBFS with the final word
        // "by" guillotined. ARN heard a read that stopped mid-word. The pad is not a fade, and a
        // sub-millisecond one is just a click with extra steps; the tail of the audio is what has
        // to reach zero. This path is also the ONLY tail treatment when ElevenLabs returns no
        // alignment, because TrimAfterSpeech (which fades) is skipped on that branch.
        if (copy > 0)
            ApplyFadeOut(pcm, 0, copy * 2, info);

        byte[] result = WavHeaderBuilder.WrapInWavHeader(pcm, info.SampleRate, info.Channels, info.BitsPerSample);
        logger.LogInformation(
            "[WavProcessor:AdjustSpeed] Pitch-preserving retime: {Before:F3}s → {After:F3}s (target {Target:F3}s, tempo {Tempo:F4})",
            currentDuration, GetDuration(result), targetSeconds, tempo);
        return result;
    }

    /// <summary>
    ///     Guarantees the WAV data is EXACTLY <paramref name="targetSeconds" /> long by
    ///     operating at the sample level — trimming excess samples or padding silence.
    ///     Rebuilds from a clean 44-byte header for deterministic output.
    /// </summary>
    public byte[] EnsureExactDuration(byte[] wavData, double targetSeconds)
    {
        var info = WavInfoParser.GetWavInfo(wavData);
        if (info is null || info.SampleRate <= 0)
            return wavData;

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0)
            return wavData;

        int targetDataBytes = AudioMathHelper.AlignToBlockBoundary(
            (int)(targetSeconds * info.SampleRate) * blockAlign,
            blockAlign);

        if (targetDataBytes == info.DataSize)
        {
            logger.LogDebug(
                "[WavProcessor:EnsureExact] Already exact — {Bytes} data bytes for {Target:F6}s",
                info.DataSize, targetSeconds);
            return wavData;
        }

        // Guard: refuse to pad more than 0.5s of silence or trim more than 0.5s of speech.
        // Large deltas should be surfaced to the orchestrator for retry, not masked.
        int maxDeltaBytes = (int)(0.5 * info.SampleRate) * blockAlign;
        int deltaBytes = Math.Abs(targetDataBytes - info.DataSize);
        if (deltaBytes > maxDeltaBytes)
        {
            logger.LogWarning(
                "[WavProcessor:EnsureExact] Refusing to {Action} {DeltaSec:F2}s (max 0.5s) — returning audio unchanged",
                targetDataBytes > info.DataSize ? "pad" : "trim",
                (double)deltaBytes / (info.SampleRate * blockAlign));
            return wavData;
        }

        if (targetDataBytes < info.DataSize && info.BitsPerSample == 16)
        {
            int dataStart = (int)info.DataPosition;
            int dataEnd = Math.Min(dataStart + info.DataSize, wavData.Length);
            int discardedStart = dataStart + targetDataBytes;
            bool discardedRangeContainsSpeech = false;
            for (int index = discardedStart; index + blockAlign <= dataEnd; index += blockAlign)
            {
                if (!IsSilentBlock(wavData, index, blockAlign, amplitudeThreshold: 48))
                {
                    discardedRangeContainsSpeech = true;
                    break;
                }
            }

            // Prod job 8d37ced0/c2894ffb (2026-08-18) shipped an exactly-22.000s Cairns bulletin
            // whose final word "Manoora" was guillotined here, evidenced by the file ending on a
            // rising non-zero sample with no fade. Preserve voiced excess by compressing the whole
            // read instead of discarding its tail.
            if (discardedRangeContainsSpeech)
            {
                logger.LogInformation(
                    "[WavProcessor:EnsureExact] Excess contains voiced speech — pitch-preserving retime instead of tail trim "
                    + "(discarded range: {Discarded:F3}s, target: {Target:F3}s)",
                    (info.DataSize - targetDataBytes) / (double)info.ByteRate, targetSeconds);
                return TimeStretchToTarget(wavData, info, info.DurationSeconds, targetSeconds);
            }
        }

        return ResizeDataBytes(wavData, info, targetDataBytes, targetSeconds);
    }

    /// <summary>
    ///     Truncates (or pads) PCM data to EXACTLY <paramref name="targetSeconds" />, with NO guard
    ///     on delta magnitude — unlike <see cref="EnsureExactDuration" /> (which refuses corrections
    ///     beyond 0.5s specifically to avoid masking a large near-miss synthesis error), this is for
    ///     callers who have ALREADY measured exactly how much trailing content is padding/silence to
    ///     discard on purpose (e.g. <c>AudioPipelineService.TrimToSpokenDuration</c> stripping a
    ///     full slot-length silence pad — appended by the underrun path toward a target it couldn't
    ///     reach — before filler gets stitched onto the real spoken content).
    /// </summary>
    public byte[] TrimToDuration(byte[] wavData, double targetSeconds)
    {
        var info = WavInfoParser.GetWavInfo(wavData);
        if (info is null || info.SampleRate <= 0)
            return wavData;

        int blockAlign = info.Channels * info.BitsPerSample / 8;
        if (blockAlign <= 0)
            return wavData;

        int targetDataBytes = AudioMathHelper.AlignToBlockBoundary(
            (int)(targetSeconds * info.SampleRate) * blockAlign,
            blockAlign);

        return targetDataBytes == info.DataSize
            ? wavData
            : ResizeDataBytes(wavData, info, targetDataBytes, targetSeconds);
    }

    private byte[] ResizeDataBytes(byte[] wavData, WavInfo info, int targetDataBytes, double targetSeconds)
    {
        int dataStart = (int)info.DataPosition;
        int bytesToCopy = Math.Min(targetDataBytes, info.DataSize);
        byte[] pcmData = new byte[targetDataBytes]; // zero-initialized = silence padding
        Buffer.BlockCopy(wavData, dataStart, pcmData, 0, bytesToCopy);

        byte[] result = WavHeaderBuilder.WrapInWavHeader(pcmData, info.SampleRate, info.Channels, info.BitsPerSample);
        double resultDuration = GetDuration(result);

        logger.LogInformation(
            "[WavProcessor:Resize] {Action}: {Before:F6}s → {After:F6}s (target: {Target:F6}s, residual: {Residual:F9}s)",
            targetDataBytes > info.DataSize ? "Padded" : "Trimmed",
            info.DurationSeconds, resultDuration, targetSeconds, resultDuration - targetSeconds);

        return result;
    }

    private static bool IsSilentBlock(byte[] audioData, int index, int blockAlign, short amplitudeThreshold)
    {
        for (int offset = 0; offset + 1 < blockAlign; offset += 2)
        {
            short sample = BitConverter.ToInt16(audioData, index + offset);
            if (Math.Abs(sample) > amplitudeThreshold)
                return false;
        }

        return true;
    }
}
