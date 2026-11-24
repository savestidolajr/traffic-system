/**
 * Deterministic colour palette for distinguishing voices in the rotation UI
 * (timeline segments, legend swatches, cycle chips). Colours are assigned by a
 * voice's index within a schedule's referenced set, so a given voice keeps the
 * same colour across the timeline, legend and card glyph.
 */
export interface VoiceColor {
    /** Filled segment / chip background with readable foreground. */
    readonly bar: string
    /** Small swatch / dot background (legend, glyph). */
    readonly dot: string
}

const PALETTE: readonly VoiceColor[] = [
    { bar: 'bg-sky-600 text-white', dot: 'bg-sky-600' },
    { bar: 'bg-violet-600 text-white', dot: 'bg-violet-600' },
    { bar: 'bg-emerald-600 text-white', dot: 'bg-emerald-600' },
    { bar: 'bg-amber-600 text-white', dot: 'bg-amber-600' },
    { bar: 'bg-rose-600 text-white', dot: 'bg-rose-600' },
    { bar: 'bg-cyan-700 text-white', dot: 'bg-cyan-700' },
    { bar: 'bg-fuchsia-600 text-white', dot: 'bg-fuchsia-600' },
    { bar: 'bg-teal-600 text-white', dot: 'bg-teal-600' },
]

/** Palette entry for a 0-based index, wrapping when the set exceeds the palette. */
export function voiceColor(index: number): VoiceColor {
    const len = PALETTE.length
    return PALETTE[((index % len) + len) % len]
}

/**
 * Builds a stable voiceId → palette-index map over an ordered list of ids
 * (blanks and duplicates ignored), so colours stay consistent across surfaces.
 */
export function buildVoiceColorMap(
    ids: readonly string[]
): ReadonlyMap<string, number> {
    const map = new Map<string, number>()
    let next = 0
    for (const id of ids) {
        if (id && !map.has(id)) map.set(id, next++)
    }
    return map
}
