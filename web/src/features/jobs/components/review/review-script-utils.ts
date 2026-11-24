/** Beyond this multiple of the aim band's ceiling, no amount of the audio
 *  pipeline's post-synthesis speed-up compression can fit the read into the
 *  slot -- the overrun stops being an editorial trim and becomes physically
 *  infeasible (see SpeechDurationOptions.MaxSpeedUpFactor /
 *  AbsolutePaceBoostSpeedUpCeiling server-side). */
const IMPOSSIBLE_OVER_MULTIPLIER = 1.6

/** True once a script is so far beyond the aim band's ceiling that even the
 *  maximum speed-up the audio pipeline allows can't compress it into the slot. */
export function isScriptTooLongToFit(
    wordCount: number,
    maxWordCount: number
): boolean {
    return wordCount > maxWordCount * IMPOSSIBLE_OVER_MULTIPLIER
}
