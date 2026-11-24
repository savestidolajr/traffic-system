import { describe, it, expect } from 'vitest'
import { fitVerdictFor } from './voice-fit'

/**
 * The audition verdict must use exact band cutoffs — an out-of-band read that
 * is off by a tenth of a second is still out of band, and a read on the line
 * must not be sent back for a rewrite it does not need.
 */
describe('fitVerdictFor', () => {
    it('fits a read at the exact slot target', () => {
        const verdict = fitVerdictFor(30, 30)
        expect(verdict.fits).toBe(true)
        expect(verdict.lowSec).toBeCloseTo(27, 10)
        expect(verdict.highSec).toBeCloseTo(33, 10)
    })

    it('fits at the lower boundary of the ±10% band', () => {
        expect(fitVerdictFor(27, 30).fits).toBe(true)
    })

    it('fits at the upper boundary of the ±10% band', () => {
        expect(fitVerdictFor(33, 30).fits).toBe(true)
    })

    it('does not fit just below the lower boundary', () => {
        expect(fitVerdictFor(26.999, 30).fits).toBe(false)
    })

    it('does not fit just above the upper boundary', () => {
        expect(fitVerdictFor(33.001, 30).fits).toBe(false)
    })

    it('keeps fractional durations unrounded so cutoffs stay exact', () => {
        // A 90s slot: 81s and 99s are the band edges; anything past them fails.
        expect(fitVerdictFor(81.0001, 90).fits).toBe(true)
        expect(fitVerdictFor(98.9999, 90).fits).toBe(true)
        expect(fitVerdictFor(80.9999, 90).fits).toBe(false)
        expect(fitVerdictFor(99.0001, 90).fits).toBe(false)
    })
})
