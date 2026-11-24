// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { cleanup, renderHook } from '@testing-library/react'
import { formatDuration } from '@/lib/formatting'
import { formatOverdue, APPROVAL_WINDOW_MINUTES } from '../lib/job-meta/urgency'

/**
 * The nav badge and the review cockpit's decision clock render the SAME sentence from two
 * different formatters: `formatOverdue` when a bulletin is late, `formatDuration` when it is
 * still due. `formatDuration` defaults `showHours` to false, so the "still due" half rendered
 * anything past an hour as unbounded minutes — a bulletin due in two hours read "120:00 left",
 * which an operator naturally reads as 120 hours. The overdue half was always correct, which is
 * exactly why the inconsistency survived: you only see it by comparing the two branches.
 */

const NOW = new Date('2026-07-28T00:00:00Z').getTime()

// A job WAITING_APPROVAL whose deadline sits `minutesOut` minutes in the future. The deadline is
// lastUpdatedAt + APPROVAL_WINDOW_MINUTES, so we work backwards from the real window.
function waitingJob(minutesOut: number) {
    const lastUpdatedAt = new Date(
        NOW + (minutesOut - APPROVAL_WINDOW_MINUTES) * 60_000
    ).toISOString()
    return { jobId: 'j1', status: 'WAITING_APPROVAL', lastUpdatedAt }
}

const activeJobs = vi.hoisted(() => ({ current: [] as unknown[] }))

vi.mock('../api/job-hooks', () => ({
    useLiveJobs: () => ({ data: { jobs: activeJobs.current } }),
}))
vi.mock('@/hooks/use-second-tick', () => ({ useNow: () => NOW }))
// The badge now counts down the EARLIER of the slot cutoff and the approval
// dwell, resolved from schedules. These cases are about the dwell half, so the
// slot map is empty and `bindingSecondsRemaining` falls back to the SLA — the
// same fallback the real resolver uses for a job whose station has no slot.
vi.mock('./use-binding-deadlines', async (importOriginal) => ({
    ...(await importOriginal<typeof import('./use-binding-deadlines')>()),
    useBindingDeadlines: () => new Map<string, Date>(),
}))

const { useWaitingBadge } = await import('./use-waiting-badge')

afterEach(() => {
    cleanup()
    activeJobs.current = []
})

describe('useWaitingBadge countdown', () => {
    it('renders a two-hour deadline with an hours component, not "120:00"', () => {
        activeJobs.current = [waitingJob(120)]

        const { result } = renderHook(() => useWaitingBadge())

        expect(result.current.countdownLabel).toBe('2:00:00 left')
        expect(result.current.countdownLabel).not.toContain('120:00')
        expect(result.current.badgeAriaLabel).toContain('2:00:00')
    })

    it('still renders a sub-hour deadline as mm:ss', () => {
        activeJobs.current = [waitingJob(25)]

        const { result } = renderHook(() => useWaitingBadge())

        expect(result.current.countdownLabel).toBe('25:00 left')
    })

    it('keeps the overdue branch unchanged', () => {
        activeJobs.current = [waitingJob(-120)]

        const { result } = renderHook(() => useWaitingBadge())

        expect(result.current.countdownLabel).toBe('2h overdue')
    })

    it('regression: the unguarded formatter default is what produced 120:00', () => {
        // Pin the underlying behaviour so nobody "simplifies" the call sites back.
        expect(formatDuration(7200)).toBe('120:00')
        expect(formatDuration(7200, { showHours: true })).toBe('2:00:00')
        expect(formatOverdue(-7200)).toBe('2h')
    })
})
