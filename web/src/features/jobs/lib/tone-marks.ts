/**
 * ElevenLabs v3 inline audio tags, authored LITERALLY in the script text.
 *
 * A tag steers roughly the next 4-5 words of speech and then the read reverts
 * (ElevenLabs, "Expressive mode / Limitations"). Because tags are ordinary
 * text the operator can see exactly where each one sits and place another
 * wherever the tone needs to hold — no sentence-level indirection.
 *
 * The backend synthesises the tagged read verbatim as long as stripping the
 * tags leaves the approved words (`DeliveryTags.WordsPreserved`, a token
 * comparison), so anything shown here is exactly what airs.
 *
 * Precedence (decided 2026-08-02): any operator tone SUPPRESSES the delivery
 * director for the whole bulletin. One author per read.
 */

/**
 * Tags with a MEASURED effect on a station voice (2026-08-05: 50 devices, 364
 * generations, each run 4x against an 8-run unchanged control with Welch t-tests —
 * see docs/reference/delivery-device-map.md). Not the documented list: most of that
 * does nothing here.
 *
 * ElevenLabs splits tags into "Directions" (`[sad]`, `[excited]`, `[thoughtful]`,
 * `[professional]`…) and "Non-verbal" (`[sighs]`, `[clears throat]`, pauses).
 * Every Direction tag was inaudible on this voice at every stability setting,
 * on a premade performative voice, and on a voice freshly designed for range -
 * because v3 derives delivery from what the WORDS mean, and a road closure is
 * affectively flat. Only Non-verbal tags fire on flat text, because they insert
 * an acoustic event rather than colour the prosody.
 *
 * Measurement confirmed exactly that split and quantified it: a device is audible
 * only when it inserts non-speech, shifts pitch, or changes duration. The breath
 * family is therefore listed here rather than withheld — an operator confirmed all
 * four by ear, and whether a sigh belongs on air is an editorial call they are
 * better placed to make than this file. Two placement rules the chips cannot show:
 * a tag on the OPENING line measured as doing nothing (put it from the second
 * sentence on), and the strings are an exact lookup — `[sighs]` fires, `[sigh]`
 * does not. Anything typed is still passed through, so the rest of the vocabulary
 * stays reachable when a bulletin warrants it.
 */
export const TONE_PRESETS: readonly string[] = [
    // Pacing — safest and most used; repeat at clause breaks to carry a line.
    '[short pause]',
    '[long pause]',
    // Breath and body — the only tags that reliably land, because they insert a sound.
    '[exhales]',
    '[exhales sharply]',
    '[sighs]',
    '[clears throat]',
    '[frustrated sigh]',
    // Manner — weaker, and whisper needs to sit mid-bulletin to register at all.
    '[whispering]',
    '[excitedly]',
    '[brightly]',
    '[delighted]',
]

/** One v3 audio tag — the same shape `DeliveryTags.TagPattern` strips. */
export const TONE_TAG_PATTERN = /\[[^[\]]{0,80}\]/g

/**
 * True when the TTS model INTERPRETS audio tags — parity with the backend's
 * `DeliveryTags.SupportsTags`, deliberately a substring test on "v3" and not an
 * equality check on `eleven_v3`, so a v3 variant is never wrongly treated as
 * tagless. Every other model SPEAKS a tag: `[excitedly]` airs as the word
 * "excitedly". Absent/unknown model returns false — the safe default is no tags,
 * matching the backend, which strips them at the synthesis boundary regardless.
 */
export function modelSupportsTags(model: string | undefined | null): boolean {
    return typeof model === 'string' && model.toLowerCase().includes('v3')
}

/**
 * Removes inline audio tags, collapsing the whitespace they leave behind —
 * parity with the backend's `DeliveryTags.Strip`, so word counts and the
 * approved-words payload agree with what the pipeline measures.
 */
export function stripTags(text: string): string {
    if (!text.includes('[')) return text
    return text
        .replace(TONE_TAG_PATTERN, ' ')
        .replace(/\s{2,}/g, ' ')
        .trim()
}

/**
 * Replaces every tag with a SAME-LENGTH run of spaces. The result has the
 * identical length and character offsets as the tagged text, so pronunciation
 * and verdict marks computed on it align with the rendered script without any
 * offset mapping — and the words inside tags become spaces, so the matcher can
 * never mistake a tag label for a real word.
 */
export function padTags(text: string): string {
    if (!text.includes('[')) return text
    return text.replace(TONE_TAG_PATTERN, (tag) => ' '.repeat(tag.length))
}

/**
 * `DeliveryTags.TagPattern` on the backend is `\[[^\[\]]{0,80}\]`. A tag that
 * does not match is never stripped, so it corrupts every word and duration
 * count AND reaches the voice as speech. Enforce the same shape here.
 */
const MAX_TAG_LENGTH = 80
const TAG = /^\[[^[\]]{1,80}\]$/

export type TagResult =
    | { readonly ok: true; readonly tag: string }
    | { readonly ok: false; readonly error: string }

/**
 * Accepts either bare wording ("solemn") or literal tags the operator typed
 * themselves ("[solemn] [slowly]"), and returns exactly what will be sent.
 */
export function normaliseTag(raw: string): TagResult {
    const text = raw.trim()
    if (!text) return { ok: false, error: 'Type a tone.' }

    if (text.includes('[') || text.includes(']')) {
        const tags = text.match(/\[[^[\]]*\]/g) ?? []
        if (tags.join(' ') !== text.replace(/\s+/g, ' '))
            return {
                ok: false,
                error: 'Write tags as [like this], separated by spaces.',
            }
        if (tags.some((t) => !TAG.test(t)))
            return {
                ok: false,
                error: `Each tag must be 1–${MAX_TAG_LENGTH} characters.`,
            }
        return { ok: true, tag: tags.join(' ') }
    }

    if (text.length > MAX_TAG_LENGTH)
        return {
            ok: false,
            error: `Keep it under ${MAX_TAG_LENGTH} characters.`,
        }
    return { ok: true, tag: `[${text.toLowerCase()}]` }
}

/** Strip brackets for display; the tag itself is always stored bracketed. */
export function toneLabel(tag: string): string {
    return tag.replace(/[[\]]/g, ' ').trim().replace(/\s+/g, ' ')
}

/** Letter/digit runs — the same tokenisation the backend's word guard uses. */
const WORD_TOKEN = /[\p{L}\p{Nd}]+/gu

/**
 * True when `tagged` preserves the spoken words of `plain` exactly — only
 * added tags, capitalisation, and punctuation are permitted. Mirrors
 * `DeliveryTags.WordsPreserved`, so a STALE tagged read (e.g. a verbatim
 * re-voice that updated the words but not the stored tags) is detected and
 * can be set aside instead of rendering words that no longer aired.
 */
export function sameWords(plain: string, tagged: string): boolean {
    const tokens = (s: string) =>
        stripTags(s).toLowerCase().match(WORD_TOKEN) ?? []
    const a = tokens(plain)
    const b = tokens(tagged)
    return a.length === b.length && a.every((w, i) => w === b[i])
}

/**
 * The exact text that aired: the stored tagged read when its spoken words
 * still match the canonical script, otherwise the script itself.
 *
 * This is also the ONE coordinate space every script mark lives in. Offsets
 * resolved against the untagged script are meaningless in the rendered read —
 * each tag before them shifts the text right by its own length — so producers
 * and renderers of marks MUST agree on this text. Shared rather than derived
 * twice for exactly that reason.
 */
export function resolveAiredRead(
    text: string,
    taggedText: string | null | undefined
): string {
    return taggedText && sameWords(text, taggedText) ? taggedText : text
}
