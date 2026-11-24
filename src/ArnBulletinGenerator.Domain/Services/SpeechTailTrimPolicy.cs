using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Decides where a synthesised take should END — after the last spoken sound and before
///     anything the model appended to it.
///     <para>
///         eleven_v3 intermittently renders a trailing inhalation after the final word. Measured on
///         dev job a0947b1e (2026-08-10): the speech decayed to -57 dBFS, then energy CLIMBED back
///         to -32 dBFS over the last 175 ms with the zero-crossing rate rising to ~3.9 kHz — the
///         signature of turbulent airflow, not a fricative. Six fresh renders of the same script
///         reproduced it once, so it is intermittent rather than universal.
///     </para>
///     <para>
///         Nothing downstream removes it. <c>WavDurationProcessor.TrimTrailingSilence</c> uses an
///         amplitude threshold of 48 (-56.7 dBFS) and the breath peaks at 2943 (-20.9 dBFS), sixty
///         times above it, so the backward scan stops on the first block and trims nothing. The
///         exact-fit step then cuts straight through the burst with no fade.
///     </para>
/// </summary>
public static class SpeechTailTrimPolicy
{
    /// <summary>
    ///     How far past the last spoken character a plosive release may still be running. A final
    ///     /t/ releases as a broadband burst AFTER the aligner says the character ended — measured
    ///     at 110 ms and -24 dBFS on the same take — so cutting on the alignment boundary itself
    ///     swallows the consonant, and a fixed offset lands mid-burst and clicks. The cut point is
    ///     searched for inside this window instead.
    /// </summary>
    public const double ReleaseLookaheadSeconds = 0.15;

    /// <summary>
    ///     Linear fade applied to the final moments of the trimmed take, so the cut lands on zero
    ///     instead of mid-waveform. Short enough to be inaudible against speech, long enough to
    ///     avoid the click an abrupt splice produces.
    /// </summary>
    public const double FadeOutSeconds = 0.005;

    /// <summary>Resolution of the quiet-point search inside the release window.</summary>
    public const double SearchWindowSeconds = 0.005;

    /// <summary>
    ///     End time of the last character that carries speech — the last LETTER OR DIGIT, never
    ///     trailing punctuation.
    ///     <para>
    ///         The distinction is the whole trick. ElevenLabs assigns every trailing sound to the
    ///         final punctuation mark: on the measured take each real character spanned 53-80 ms
    ///         while the closing '.' spanned 320 ms, so the alignment's own last end time IS the end
    ///         of the file and tells you nothing. The last alphanumeric end is the honest boundary.
    ///     </para>
    ///     None when there is no alignment or it carries no alphanumeric character.
    /// </summary>
    public static Option<double> LastSpokenCharacterEnd(Option<AudioAlignment> alignment)
    {
        if (alignment.IsNone)
            return Option<double>.None;

        var a = alignment.Value;
        if (a.Characters.Count == 0 || a.EndSeconds.Count == 0)
            return Option<double>.None;

        int n = Math.Min(a.Characters.Count, a.EndSeconds.Count);
        for (int i = n - 1; i >= 0; i--)
        {
            string c = a.Characters[i];
            if (c.Length > 0 && char.IsLetterOrDigit(c[0]) && a.EndSeconds[i] > 0)
                return Option<double>.Some(a.EndSeconds[i]);
        }

        return Option<double>.None;
    }

    /// <summary>
    ///     Index of the quietest window in <paramref name="windowEnergies" /> — the natural gap
    ///     between a consonant release and whatever follows it. Ties resolve to the EARLIEST window
    ///     so the take is cut as tight as the audio allows. -1 when there is nothing to search.
    /// </summary>
    public static int QuietestWindow(IReadOnlyList<double> windowEnergies)
    {
        int best = -1;
        double bestDb = double.MaxValue;
        for (int i = 0; i < windowEnergies.Count; i++)
        {
            if (windowEnergies[i] < bestDb)
            {
                bestDb = windowEnergies[i];
                best = i;
            }
        }

        return best;
    }
}
