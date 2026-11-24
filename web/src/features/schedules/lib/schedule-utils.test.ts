import { describe, expect, it, vi, afterEach } from 'vitest'
import { formatNextRun } from './schedule-utils'
import { formatTimeUntil } from '@/lib/formatting'

/**
 * The on-air board's "On air next" and the schedules table both render this. The bug these
 * cover: formatNextRun delegated future instants to formatRelativeTime, which clamps the
 * future to "just now" — so a run 33 minutes away announced itself as already happening.
 */
describe('formatNextRun', () => {
    afterEach(() => vi.useRealTimers())

    const at = (msFromNow: number) =>
        new Date(Date.now() + msFromNow).toISOString()

    it('counts down to a future run rather than reading "just now"', () => {
        const { relative, urgent } = formatNextRun(at(33 * 60_000), true)

        expect(relative).toBe('in 33m')
        expect(relative).not.toContain('just now')
        expect(urgent).toBe(false)
    })

    it('spells hours and zero-padded minutes for a distant run', () => {
        expect(formatNextRun(at(125 * 60_000), true).relative).toBe('in 2h 05m')
    })

    it('flags an imminent run urgent and never renders 0m', () => {
        const { relative, urgent } = formatNextRun(at(20_000), true)

        expect(relative).toBe('in 1m')
        expect(urgent).toBe(true)
    })

    it('reports a past run as overdue, not as a countdown', () => {
        const { relative, urgent } = formatNextRun(at(-5 * 60_000), true)

        expect(relative).toBe('Overdue')
        expect(urgent).toBe(true)
    })

    it('reads Paused when disabled, whatever the next run says', () => {
        expect(formatNextRun(at(60_000), false).relative).toBe('Paused')
    })

    it('degrades to a dash on a missing or malformed instant', () => {
        expect(formatNextRun(null, true).relative).toBe('-')
        expect(formatNextRun('not-a-date', true).relative).toBe('-')
    })
})

describe('formatTimeUntil', () => {
    it('accepts a Date and an ISO string identically', () => {
        const iso = new Date(Date.now() + 45 * 60_000).toISOString()

        expect(formatTimeUntil(iso)).toBe('45m')
        expect(formatTimeUntil(new Date(iso))).toBe('45m')
    })
})
