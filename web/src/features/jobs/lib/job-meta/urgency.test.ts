import { describe, it, expect } from 'vitest'
import {
    formatOverdue,
    OVERDUE_CAP_SECS,
    isAirSlotPassed,
    resolveBindingDeadline,
} from './urgency'

describe('formatOverdue', () => {
    it('formats under an hour as mm:ss', () => {
        expect(formatOverdue(0)).toBe('0:00')
        expect(formatOverdue(65)).toBe('1:05')
        expect(formatOverdue(3599)).toBe('59:59')
    })

    it('formats an hour or more (but under the cap) as Xh Ym / Xh', () => {
        expect(formatOverdue(3600)).toBe('1h')
        expect(formatOverdue(51060)).toBe('14h 11m')
        expect(formatOverdue(OVERDUE_CAP_SECS - 1)).toBe('23h 59m')
    })

    it('caps anything at or beyond 24h so it never falls back to raw mm:ss', () => {
        expect(formatOverdue(OVERDUE_CAP_SECS)).toBe('24h+')
        // The bug this guards against: a >24h overdue job (e.g. 2469:32
        // minutes:seconds ~= 148172s) must render the capped label, not a
        // raw negative or uncapped minutes:seconds string.
        expect(formatOverdue(148172)).toBe('24h+')
        expect(formatOverdue(OVERDUE_CAP_SECS * 10)).toBe('24h+')
    })

    it('ignores sign — always formats the magnitude', () => {
        expect(formatOverdue(-65)).toBe('1:05')
        expect(formatOverdue(-148172)).toBe('24h+')
    })
})

describe('isAirSlotPassed', () => {
    const NOW = Date.parse('2026-08-24T04:00:00Z')
    const slot = (to: string) => ({ input: { timeWindow: { from: '', to } } })

    it('is true once the job’s own slot is behind the grace window', () => {
        expect(isAirSlotPassed(slot('2026-08-11T11:20:00Z'), NOW)).toBe(true)
    })

    it('is false while the slot is still ahead', () => {
        expect(isAirSlotPassed(slot('2026-08-24T04:05:00Z'), NOW)).toBe(false)
    })

    it('never hides an action on missing or unparseable data', () => {
        expect(isAirSlotPassed({}, NOW)).toBe(false)
        expect(
            isAirSlotPassed(
                { input: { timeWindow: { from: '', to: '' } } },
                NOW
            )
        ).toBe(false)
        expect(isAirSlotPassed(slot('not a date'), NOW)).toBe(false)
    })

    it('answers for terminal statuses, which resolveBindingDeadline cannot', () => {
        // The trap this helper exists for: resolveBindingDeadline is WAITING_APPROVAL-only, so
        // `?.slotMissed === true` silently reads FALSE — "slot fine" — on a covered job whose
        // slot went a fortnight ago, which is exactly the state every stranded FALLBACK_USED
        // job on dev and test was in.
        const covered = {
            status: 'FALLBACK_USED' as const,
            lastUpdatedAt: '2026-08-11T11:20:00Z',
        }
        expect(
            resolveBindingDeadline(
                covered,
                new Date('2026-08-11T11:20:00Z'),
                NOW
            )
        ).toBeNull()
        expect(isAirSlotPassed(slot('2026-08-11T11:20:00Z'), NOW)).toBe(true)
    })
})
