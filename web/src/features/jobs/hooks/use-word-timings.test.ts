import { describe, it, expect } from 'vitest'
import { buildWordTimings } from './use-word-timings'
import type { AudioWordTiming } from '../types'

const measured = (
    ...entries: Array<[string, number, number]>
): AudioWordTiming[] =>
    entries.map(([word, startSec, endSec]) => ({ word, startSec, endSec }))

describe('buildWordTimings', () => {
    it('uses measured timings when token counts align', () => {
        const r = buildWordTimings(
            'Good morning Melbourne',
            13,
            measured(
                ['Good', 0.1, 0.4],
                ['morning', 0.45, 0.9],
                ['Melbourne', 1.0, 1.6]
            )
        )
        expect(r).toEqual([
            { word: 'Good', startMs: 100 },
            { word: 'morning', startMs: 450 },
            { word: 'Melbourne', startMs: 1000 },
        ])
    })

    it('falls back to the heuristic on token count mismatch (never misaligns)', () => {
        const r = buildWordTimings(
            'Good morning Melbourne drivers',
            10,
            measured(['Good', 0.1, 0.4], ['morning', 0.45, 0.9])
        )
        expect(r).toHaveLength(4)
        // Heuristic spacing, not the measured values
        expect(r![0].startMs).toBe(0)
        expect(r![1].startMs).toBeGreaterThan(0)
    })

    it('drops cue tokens from both sides before aligning', () => {
        const r = buildWordTimings(
            '[upbeat] Good morning',
            10,
            measured(
                ['[upbeat]', 0, 0.05],
                ['Good', 0.1, 0.4],
                ['morning', 0.45, 0.9]
            )
        )
        expect(r).toEqual([
            { word: 'Good', startMs: 100 },
            { word: 'morning', startMs: 450 },
        ])
    })

    it('keeps monotonic heuristic timings without measured data', () => {
        const r = buildWordTimings('one two three four', 8)
        expect(r).toHaveLength(4)
        for (let i = 1; i < r!.length; i++) {
            expect(r![i].startMs).toBeGreaterThan(r![i - 1].startMs)
        }
    })

    it('realigns a token the TTS split across two timing entries (M80)', () => {
        const r = buildWordTimings(
            'to the M80 Ring',
            20,
            measured(
                ['to', 0.1, 0.3],
                ['the', 0.35, 0.5],
                ['M', 0.6, 0.75],
                ['80', 0.8, 1.05],
                ['Ring', 1.1, 1.5]
            )
        )
        expect(r).toEqual([
            { word: 'to', startMs: 100 },
            { word: 'the', startMs: 350 },
            // Merged onto the single script token, keeping the "M" fragment start.
            { word: 'M80', startMs: 600 },
            // Downstream words stay aligned instead of shifting by one.
            { word: 'Ring', startMs: 1100 },
        ])
    })

    it('splits a measured word spanning several tokens by interpolation', () => {
        const r = buildWordTimings(
            'M 80 Ring',
            20,
            measured(['M80', 0.6, 1.0], ['Ring', 1.1, 1.5])
        )
        expect(r).toEqual([
            { word: 'M', startMs: 600 },
            { word: '80', startMs: 800 },
            { word: 'Ring', startMs: 1100 },
        ])
    })

    it('falls back to the heuristic when tokens cannot be reconciled by merge', () => {
        const r = buildWordTimings(
            'alpha bravo',
            10,
            measured(['alpha', 0.5, 0.8], ['charlie', 0.9, 1.2])
        )
        expect(r).toHaveLength(2)
        // Heuristic starts the first token at 0, not the measured 500ms.
        expect(r![0].startMs).toBe(0)
    })

    it('returns undefined for empty script or zero duration without timings', () => {
        expect(buildWordTimings('', 10)).toBeUndefined()
        expect(buildWordTimings('words here', 0)).toBeUndefined()
    })
})
