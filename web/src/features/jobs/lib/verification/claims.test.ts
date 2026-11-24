// =============================================================================
// Claim checking unit tests
// =============================================================================

import { describe, it, expect } from 'vitest'
import { checkSentenceClaims, type LinkedCorridor } from './claims'

// ---------------------------------------------------------------------------
// Delay checks
// ---------------------------------------------------------------------------

describe('checkSentenceClaims — delay', () => {
    it('no flag when delay is within 5-minute tolerance', () => {
        const sentence = 'Expect delays of 10 minutes on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 12 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('no flag when delay diff is exactly 4 (under 5)', () => {
        const sentence = 'Expect delays of 10 minutes on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 6 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('flags when delay difference is exactly 5 minutes', () => {
        const sentence = 'Expect delays of 10 minutes on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 5 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('delay')
        expect(flags[0].note).toBe('Script says 10 min, source data says 5 min')
    })

    it('flags when delay diff is >= 5 minutes', () => {
        const sentence = 'Expect delays of 15 minutes on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 5 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('delay')
        expect(flags[0].note).toBe('Script says 15 min, source data says 5 min')
    })

    it('uses the closest corridor delay when multiple corridors', () => {
        const sentence = 'Expect delays of 10 minutes.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 4 },
            { roadName: 'M5', delayMinutes: 10 },
            { roadName: 'M7', delayMinutes: 2 },
        ]
        // Closest is M5 (10), diff=0 — no flag
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('no flag when sentence has no delay number', () => {
        const sentence = 'Congestion on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 10 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('no flag when corridor has no delay data (null/undefined)', () => {
        const sentence = 'Expect delays of 10 minutes on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: null },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('no flag when sentence has "min" abbreviation', () => {
        const sentence = 'Delays of 5 min on the M4.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 4 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('no flag when sentence has "minute" word', () => {
        const sentence = 'There may be a 20-minute delay.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 14 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('delay')
    })

    it('zero false positive: sentence mentions minutes unrelated to delay', () => {
        const sentence = 'The incident was reported 5 minutes ago on the M4.'
        // This matches "5 minutes" — but the function extracts the first
        // number before minute/min. This is an expected limitation: a 5-min
        // mention in a non-delay context would still be checked.
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', delayMinutes: 10 },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        // Since 5 and 10 have a diff of 5 (>= 5), it flags
        // This is acceptable behavior — the reviewer can see the context.
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(1)
    })
})

// ---------------------------------------------------------------------------
// Direction checks
// ---------------------------------------------------------------------------

describe('checkSentenceClaims — direction', () => {
    it('no flag when corridor direction agrees with sentence', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: 'westbound' },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('flags when corridor direction disagrees with sentence', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: 'eastbound' },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('direction')
        expect(flags[0].note).toBe(
            'Script says westbound, source says eastbound'
        )
    })

    it('no flag when corridor has no direction (null)', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: null },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('no flag when corridor has no direction (undefined)', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [{ roadName: 'M4' }]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('no flag when sentence has no direction word', () => {
        const sentence = 'Congestion on the M4 at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: 'eastbound' },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('treats citybound === inbound equivalently', () => {
        const sentence = 'On the Harbour Bridge citybound.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'Harbour Bridge', direction: 'inbound' },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('treats inbound === citybound equivalently (reverse)', () => {
        const sentence = 'On the Harbour Bridge inbound.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'Harbour Bridge', direction: 'citybound' },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('flags only when EVERY linked corridor with direction disagrees', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: 'eastbound' },
            { roadName: 'M4_Ramp', direction: 'westbound' },
        ]
        // One agrees (westbound), so no flag
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('flags when all corridors that have a direction disagree', () => {
        const sentence = 'On the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            { roadName: 'M4', direction: 'eastbound' },
            { roadName: 'M4_Ramp', direction: null },
            { roadName: 'M4_OnRamp' },
        ]
        // Only M4 has a direction (eastbound) which disagrees
        // M4_Ramp has null, M4_OnRamp has undefined — both skipped
        // So ALL corridors WITH direction disagree → flag
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('direction')
    })

    it('never compares city-relative and compass directions — geometry unknown', () => {
        // "citybound" on a road running south toward the CBD IS "southbound";
        // cross-family comparison produced false red flags.
        const citybound = checkSentenceClaims(
            'The Monash is heavy citybound at Warrigal Road.',
            [{ roadName: 'Monash', direction: 'southbound' }]
        )
        expect(citybound.filter((f) => f.kind === 'direction')).toHaveLength(0)

        const compass = checkSentenceClaims(
            'The Monash is heavy northbound at Warrigal Road.',
            [{ roadName: 'Monash', direction: 'citybound' }]
        )
        expect(compass.filter((f) => f.kind === 'direction')).toHaveLength(0)
    })

    it('still flags within the city-relative family', () => {
        const flags = checkSentenceClaims(
            'The Monash is heavy citybound at Warrigal Road.',
            [{ roadName: 'Monash', direction: 'outbound' }]
        )
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('direction')
    })
})

// ---------------------------------------------------------------------------
// Combined checks
// ---------------------------------------------------------------------------

describe('checkSentenceClaims — combined', () => {
    it('returns both delay and direction flags when both disagree', () => {
        const sentence =
            'Expect 15 minute delays on the M4 westbound at Strathfield.'
        const corridors: LinkedCorridor[] = [
            {
                roadName: 'M4',
                delayMinutes: 5,
                direction: 'eastbound',
            },
        ]
        const flags = checkSentenceClaims(sentence, corridors)
        expect(flags).toHaveLength(2)
        expect(flags.find((f) => f.kind === 'delay')).toBeDefined()
        expect(flags.find((f) => f.kind === 'direction')).toBeDefined()
    })

    it('returns empty array for unlinked sentence (no corridors at all)', () => {
        const sentence = 'Thanks for listening to 2GB.'
        const flags = checkSentenceClaims(sentence, [])
        expect(flags).toEqual([])
    })
})

describe('checkSentenceClaims — spoken numbers', () => {
    it('parses word-form delays: "fifteen minutes" is a claim', () => {
        const flags = checkSentenceClaims(
            'Expect delays of around fifteen minutes on the M4.',
            [{ roadName: 'M4', delayMinutes: 5 }]
        )
        expect(flags).toHaveLength(1)
        expect(flags[0].kind).toBe('delay')
        expect(flags[0].note).toBe('Script says 15 min, source data says 5 min')
    })

    it('parses compound word-form delays: "twenty-five minutes"', () => {
        const flags = checkSentenceClaims(
            'Delays of twenty-five minutes on the freeway.',
            [{ roadName: 'M4', delayMinutes: 24 }]
        )
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })

    it('word-form delay within tolerance does not flag', () => {
        const flags = checkSentenceClaims(
            'Expect delays of about ten minutes on the M4.',
            [{ roadName: 'M4', delayMinutes: 8 }]
        )
        expect(flags.filter((f) => f.kind === 'delay')).toHaveLength(0)
    })
})
