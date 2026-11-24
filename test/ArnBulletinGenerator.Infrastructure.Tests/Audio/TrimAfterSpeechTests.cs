using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

/// <summary>
///     Guards the trailing-inhale trim. Shapes are synthesised to the profile MEASURED on dev job
///     a0947b1e (2026-08-10): speech, a brief silence, then a rising broadband burst that the
///     amplitude-threshold trim cannot see because it peaks 60x above the silence threshold.
/// </summary>
public class TrimAfterSpeechTests
{
    private const int SampleRate = 44100;
    private static WavDurationProcessor Sut() => new(NullLogger<WavDurationProcessor>.Instance);

    /// <summary>Builds mono 16-bit WAV from a list of (seconds, amplitude) segments.</summary>
    private static byte[] Wav(params (double Seconds, short Amplitude)[] segments)
    {
        var pcm = new List<byte>();
        int phase = 0;
        foreach (var (secs, amp) in segments)
        {
            int n = (int)(secs * SampleRate);
            for (int i = 0; i < n; i++)
            {
                // Alternate sign every sample so amplitude is the RMS driver and the segment has
                // broadband character — the trim only ever looks at energy.
                short s = (short)((phase++ % 2 == 0) ? amp : -amp);
                pcm.Add((byte)(s & 0xFF));
                pcm.Add((byte)((s >> 8) & 0xFF));
            }
        }

        return WavHeaderBuilder.WrapInWavHeader([.. pcm], SampleRate, 1, 16);
    }

    private static double Rms(byte[] wav, double fromSeconds, double toSeconds)
    {
        var info = WavInfoParser.GetWavInfo(wav)!;
        int start = (int)info.DataPosition + ((int)(fromSeconds * SampleRate) * 2);
        int end = Math.Min((int)info.DataPosition + info.DataSize,
            (int)info.DataPosition + ((int)(toSeconds * SampleRate) * 2));
        double sum = 0;
        int n = 0;
        for (int i = start; i + 1 < end; i += 2)
        {
            short s = BitConverter.ToInt16(wav, i);
            sum += (double)s * s;
            n++;
        }

        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }

    [Fact]
    public void RemovesTheTrailingBreath_AndKeepsThePlosiveRelease()
    {
        // 1.0s speech | 0.11s plosive release | 0.025s gap | 0.175s breath.
        // The aligner puts the last character's end at 1.0s — the CLOSURE, before the release.
        byte[] wav = Wav((1.0, 8000), (0.11, 5000), (0.025, 60), (0.175, 900));
        double before = Sut().GetDuration(wav);

        byte[] trimmed = Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 1.0);
        double after = Sut().GetDuration(trimmed);

        after.ShouldBeLessThan(before);
        // The release survives: energy at 1.05s (mid-burst) is still there.
        Rms(trimmed, 1.05, 1.10).ShouldBeGreaterThan(1000);
        // The breath is gone: the file ends before it started (1.135s).
        after.ShouldBeLessThan(1.16);
        after.ShouldBeGreaterThanOrEqualTo(1.11);
    }

    [Fact]
    public void CutsAtTheQuietPoint_NotAtTheAlignmentBoundary()
    {
        // Cutting on the boundary itself would swallow the release; this is the specific failure
        // the release-lookahead exists to prevent.
        byte[] wav = Wav((1.0, 8000), (0.11, 5000), (0.025, 60), (0.175, 900));
        byte[] trimmed = Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 1.0);

        Sut().GetDuration(trimmed).ShouldBeGreaterThan(1.10);
    }

    [Fact]
    public void EndsOnASoftZero_SoTheCutDoesNotClick()
    {
        byte[] wav = Wav((1.0, 8000), (0.11, 5000), (0.025, 60), (0.175, 900));
        byte[] trimmed = Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 1.0);

        var info = WavInfoParser.GetWavInfo(trimmed)!;
        int lastSampleAt = (int)info.DataPosition + info.DataSize - 2;
        Math.Abs(BitConverter.ToInt16(trimmed, lastSampleAt)).ShouldBeLessThan((short)200);
    }

    [Fact]
    public void CleanTake_LosesOnlyTheLookaheadWindow_NotSpeech()
    {
        // No breath: speech runs to the boundary and stops. The trim may only shave inside the
        // release window, never into the speech before it.
        byte[] wav = Wav((1.0, 8000), (0.02, 40));
        byte[] trimmed = Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 1.0);

        Sut().GetDuration(trimmed).ShouldBeGreaterThanOrEqualTo(1.0);
        Rms(trimmed, 0.5, 0.95).ShouldBeGreaterThan(7000);   // speech untouched
    }

    [Fact]
    public void AdjustSpeed_TruncatingToTheSlot_StillEndsOnASoftZero()
    {
        // The exact-fit inside the WSOLA retime truncates whenever the stretch produced more than
        // the slot holds. That cut lands mid-waveform AND discards any fade an earlier stage
        // applied, because the fade is no longer at the end. Found live on dev job acdfbe7f
        // (2026-08-10): the shipped file ended on -1576 with 10027 peak in its last 5ms.
        byte[] loudThroughout = Wav((2.0, 12000));
        const double target = 1.6;   // forces compression, so the exact-fit truncates rather than pads

        byte[] fitted = Sut().AdjustSpeed(loudThroughout, target);

        var info = WavInfoParser.GetWavInfo(fitted)!;
        int lastSampleAt = (int)info.DataPosition + info.DataSize - 2;
        Math.Abs(BitConverter.ToInt16(fitted, lastSampleAt)).ShouldBeLessThan((short)500);
        // …and it really did truncate — the body is still full-level, only the very end is faded.
        Rms(fitted, 0.5, 1.0).ShouldBeGreaterThan(8000);
    }

    [Fact]
    public void BoundaryAtOrPastTheEnd_ReturnsTheAudioUnchanged()
    {
        byte[] wav = Wav((1.0, 8000));
        Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 1.0).ShouldBeSameAs(wav);
        Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 5.0).ShouldBeSameAs(wav);
        Sut().TrimAfterSpeech(wav, lastSpokenSeconds: 0).ShouldBeSameAs(wav);
    }

    // ── The policy half ──

    [Fact]
    public void LastSpokenCharacterEnd_IgnoresTrailingPunctuation()
    {
        // The measured defect: eleven_v3 assigns the breath to the final '.', which then spans 320ms
        // against a 66ms median. Taking the alignment's LAST end time yields the end of the file and
        // trims nothing — the last LETTER is the honest boundary.
        var alignment = new AudioAlignment(
            ["S", "t", "o", "p", ".", " "],
            [0.00, 0.08, 0.16, 0.24, 0.32, 0.64],
            [0.08, 0.16, 0.24, 0.32, 0.64, 0.70]);

        SpeechTailTrimPolicy.LastSpokenCharacterEnd(Option<AudioAlignment>.Some(alignment))
            .GetValueOr(-1).ShouldBe(0.32);
    }

    [Fact]
    public void LastSpokenCharacterEnd_NoUsableAlignment_IsNone()
    {
        SpeechTailTrimPolicy.LastSpokenCharacterEnd(Option<AudioAlignment>.None).HasValue.ShouldBeFalse();
        SpeechTailTrimPolicy.LastSpokenCharacterEnd(
            Option<AudioAlignment>.Some(new AudioAlignment([], [], []))).HasValue.ShouldBeFalse();
        // Punctuation only — nothing spoken to anchor on.
        SpeechTailTrimPolicy.LastSpokenCharacterEnd(
                Option<AudioAlignment>.Some(new AudioAlignment([".", "!"], [0, 0.1], [0.1, 0.2])))
            .HasValue.ShouldBeFalse();
    }

    [Fact]
    public void QuietestWindow_PrefersTheEarliestOnATie()
    {
        // Cut as tight as the audio allows when two windows are equally quiet.
        SpeechTailTrimPolicy.QuietestWindow([9.0, 1.0, 5.0, 1.0]).ShouldBe(1);
        SpeechTailTrimPolicy.QuietestWindow([]).ShouldBe(-1);
    }
}
