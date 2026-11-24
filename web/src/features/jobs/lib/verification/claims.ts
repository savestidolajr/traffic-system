// =============================================================================
// Claim checking — deterministic comparison between sentence text and
// linked-corridor data, flagging contradictions the reviewer must resolve.
// =============================================================================

import { normalizeSpokenNumbers } from './spoken-numbers'

export interface LinkedCorridor {
    readonly roadName: string
    readonly delayMinutes?: number | null
    readonly direction?: string | null
}

export interface ClaimFlag {
    readonly kind: 'delay' | 'direction'
    readonly note: string
}

// ---------------------------------------------------------------------------
// Direction normalisation
// ---------------------------------------------------------------------------

function normalizeDirection(dir: string): string {
    const lower = dir.toLowerCase().trim()
    return lower === 'citybound' ? 'inbound' : lower
}

const DIRECTION_WORDS = [
    'citybound',
    'inbound',
    'outbound',
    'eastbound',
    'westbound',
    'northbound',
    'southbound',
] as const

// Direction families: compass headings and city-relative directions are NOT
// comparable without road geometry — "citybound" on a road that runs south
// toward the CBD IS "southbound", and ATN style actively translates compass
// to city-relative. Comparing across families produced false red flags.
const CITY_RELATIVE: Record<string, true> = {
    inbound: true,
    outbound: true,
}

// ---------------------------------------------------------------------------
// Claim checks
// ---------------------------------------------------------------------------

/**
 * Check a sentence against its linked corridors for factual claims that
 * contradict source data. Both sides must state a value to flag — zero
 * false positives on silence.
 *
 * @returns Flags for every claim that disagrees with the source data.
 */
export function checkSentenceClaims(
    sentenceText: string,
    corridors: readonly LinkedCorridor[]
): ClaimFlag[] {
    const flags: ClaimFlag[] = []
    const lower = sentenceText.toLowerCase()

    // -- Delay check --------------------------------------------------------
    // Scripts voice quantities as words ("around fifteen minutes"), so
    // normalize spoken numbers to digits before extracting the claim.
    const delayMatch = normalizeSpokenNumbers(sentenceText).match(
        /(\d+)\s*-?\s*(?:minutes?|mins?)\b/i
    )
    if (delayMatch) {
        const sentenceDelay = parseInt(delayMatch[1], 10)

        // Collect numeric delays from linked corridors: routed delayMinutes,
        // else null (no flow-delay computation to avoid false positives)
        const corridorDelays: number[] = []
        for (const c of corridors) {
            if (c.delayMinutes != null) {
                corridorDelays.push(c.delayMinutes)
            }
        }

        if (corridorDelays.length > 0) {
            // Closest corridor delay to the sentence claim
            let minDiff = Infinity
            let bestDelay = corridorDelays[0]
            for (const d of corridorDelays) {
                const diff = Math.abs(d - sentenceDelay)
                if (diff < minDiff) {
                    minDiff = diff
                    bestDelay = d
                }
            }

            if (minDiff >= 5) {
                flags.push({
                    kind: 'delay',
                    note: `Script says ${sentenceDelay} min, source data says ${bestDelay} min`,
                })
            }
        }
    }

    // -- Direction check ----------------------------------------------------
    const sentenceDirection = DIRECTION_WORDS.find((w) => lower.includes(w))

    if (sentenceDirection) {
        const normalizedSentence = normalizeDirection(sentenceDirection)
        const sentenceFamily = CITY_RELATIVE[normalizedSentence] === true

        // Collect normalised directions from corridors that have one — but
        // only those in the SAME family as the sentence's direction word
        // (missing direction or cross-family = skip, not a claim).
        const corridorDirs: string[] = []
        const rawDirs: string[] = []
        for (const c of corridors) {
            if (c.direction == null) continue
            const normalized = normalizeDirection(c.direction)
            if ((CITY_RELATIVE[normalized] === true) !== sentenceFamily)
                continue
            corridorDirs.push(normalized)
            rawDirs.push(c.direction)
        }

        // Flag only when EVERY comparable corridor direction disagrees
        if (
            corridorDirs.length > 0 &&
            corridorDirs.every((d) => d !== normalizedSentence)
        ) {
            flags.push({
                kind: 'direction',
                note: `Script says ${sentenceDirection}, source says ${rawDirs[0]}`,
            })
        }
    }

    return flags
}
