/**
 * The ElevenLabs v3 audio-tag palette the delivery director may use, grouped by
 * MEASURED effect. Operators see human labels, never the raw `[tag]` strings.
 *
 * The grouping is evidence-based, not editorial. Each tag below was run 4x against an
 * 8-run unchanged control on a real station voice and compared per metric with a Welch
 * t-test (2026-08-05, 364 generations, `docs/reference/delivery-device-map.md`). The
 * finding that shapes this file: a tag is only audible when it INSERTS NON-SPEECH,
 * SHIFTS PITCH, or CHANGES DURATION. Abstract emotion words move pitch span and
 * velocity only, which is inaudible — so most of the old palette did nothing at all.
 *
 * Two rules operators cannot see from the chips, both enforced in the writer prompt:
 *  - Tag strings are an exact lookup, not semantics. `[sighs]` works, `[sigh]` and
 *    `[sighing]` do not. `[brightly]` works, `[bright]` does not. A wrong form fails
 *    silently, which is why "Others" input is a last resort.
 *  - Placement matters more than choice. A tag at the very start of the script is the
 *    weakest position; the same tag before the second sentence is far stronger.
 */
export type AudioTag =
    // Measured audible — inserts non-speech
    | '[sighs]'
    | '[exhales]'
    | '[exhales sharply]'
    | '[clears throat]'
    | '[frustrated sigh]'
    | '[snorts]'
    | '[happy gasp]'
    // Measured audible — shifts pitch or lifts
    | '[whispering]'
    | '[excitedly]'
    | '[delighted]'
    // No measured effect. Retained so existing styles keep readable labels.
    | '[urgent]'
    | '[serious]'
    | '[calm]'
    | '[warmly]'
    | '[reassuring]'
    | '[sympathetic]'
    | '[matter-of-fact]'
    | '[excited]'
    | '[emphatic]'
    | '[upbeat]'

export interface Cue {
    readonly tag: AudioTag
    readonly label: string
}

export interface CueGroup {
    readonly name: string
    /** One-line, plain-language hint shown under the group's custom input. */
    readonly hint: string
    readonly cues: readonly Cue[]
}

/** Grouped, human-labelled palette. Order is the operator-facing order. */
export const CUE_GROUPS: readonly CueGroup[] = [
    {
        name: 'Breath and body',
        hint: 'Audible on air. These insert a real breath or sound, which is why they land.',
        cues: [
            { tag: '[sighs]', label: 'Sigh' },
            { tag: '[exhales]', label: 'Exhale' },
            { tag: '[exhales sharply]', label: 'Sharp exhale' },
            { tag: '[clears throat]', label: 'Clear throat' },
            { tag: '[frustrated sigh]', label: 'Frustrated sigh' },
            { tag: '[snorts]', label: 'Snort' },
            { tag: '[happy gasp]', label: 'Gasp' },
        ],
    },
    {
        name: 'Voice',
        hint: 'Audible on air. Whisper needs to sit mid-bulletin, not on the opening line.',
        cues: [
            { tag: '[whispering]', label: 'Whisper' },
            { tag: '[excitedly]', label: 'Excitedly' },
            { tag: '[delighted]', label: 'Delighted' },
        ],
    },
    {
        name: 'Unverified',
        hint: 'No measurable effect on the voices tested. Kept for existing styles; prefer the groups above.',
        cues: [
            { tag: '[urgent]', label: 'Urgent' },
            { tag: '[serious]', label: 'Serious' },
            { tag: '[calm]', label: 'Calm' },
            { tag: '[warmly]', label: 'Warm' },
            { tag: '[reassuring]', label: 'Reassuring' },
            { tag: '[sympathetic]', label: 'Sympathetic' },
            { tag: '[matter-of-fact]', label: 'Matter-of-fact' },
            { tag: '[excited]', label: 'Excited' },
            { tag: '[emphatic]', label: 'Emphatic' },
            { tag: '[upbeat]', label: 'Upbeat' },
        ],
    },
]

export const ALL_CUE_TAGS: readonly AudioTag[] = CUE_GROUPS.flatMap((g) =>
    g.cues.map((c) => c.tag)
)

const CURATED_SET = new Set<string>(ALL_CUE_TAGS)

export function isCuratedTag(tag: string): boolean {
    return CURATED_SET.has(tag)
}

// Static, string-keyed lookup table -> Record (not Map): keys are fixed literals.
const LABEL_BY_TAG: Record<string, string> = Object.fromEntries(
    CUE_GROUPS.flatMap((g) => g.cues.map((c) => [c.tag, c.label] as const))
)

/**
 * Human label for a stored tag. Curated tags use their title-case label;
 * custom tags fall back to the de-bracketed text, capitalised so a stored
 * lowercase cue ("cheerful") reads the same as a curated one ("Calm").
 */
export function cueLabel(tag: string): string {
    const label = LABEL_BY_TAG[tag] ?? tag.replace(/^\[|\]$/g, '')
    return label.charAt(0).toUpperCase() + label.slice(1)
}

/**
 * Normalise free operator input into a stored audio tag, or null if it is not a
 * usable 1-2 word cue. Lower-cases, strips/wraps brackets, and rejects junk so a
 * custom cue stays consistent with how the director and ElevenLabs expect tags
 * (e.g. `Deadpan` -> `[deadpan]`, `very calm` -> `[very calm]`).
 */
export function normalizeTag(raw: string): string | null {
    const inner = raw
        .trim()
        .toLowerCase()
        .replace(/^\[+|\]+$/g, '')
        .trim()
    if (!inner || inner.length > 24) return null
    // One or two words; letters with optional internal hyphens/apostrophes.
    if (!/^[a-z][a-z'-]*( [a-z][a-z'-]*)?$/.test(inner)) return null
    return `[${inner}]`
}

/**
 * Normalise + de-duplicate a stored tag list, keeping custom tags and dropping
 * only malformed ones. Replaces the old palette-only filter so an operator's own
 * cues survive an edit.
 */
export function sanitizeTags(raw: readonly string[]): string[] {
    const out: string[] = []
    const seen = new Set<string>()
    for (const tag of raw) {
        const normalized = normalizeTag(tag)
        if (normalized && !seen.has(normalized)) {
            seen.add(normalized)
            out.push(normalized)
        }
    }
    return out
}
