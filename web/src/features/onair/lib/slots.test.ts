// =============================================================================
// Slot Computation Tests
// =============================================================================

import { describe, it, expect } from 'vitest'
import { computeStationLanes, findAirSlot } from './slots'
import {
    m,
    BASE_NOW,
    EARLY_NOW,
    LATE_NOW,
    makeJob,
    makeSchedule,
    dailySlot,
} from '@/test/factories'
import type {
    MarketSchedule,
    ScheduleCalendar,
} from '@/features/schedules/types'

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('computeStationLanes', () => {
    // -----------------------------------------------------------------------
    // State machine
    // -----------------------------------------------------------------------

    it('maps APPROVED/PUBLISHED to covered', () => {
        const now = BASE_NOW
        // Daily 07:00 UTC slot, two minutes ahead of `now`
        const schedule = dailySlot()
        const job = makeJob({
            status: 'APPROVED',
            jobId: 'job-covered',
            station: 'TEST',
            createdAt: m(now, -13), // 06:45 — the poller's 15-min lead on 07:00
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        expect(lanes).toHaveLength(1)
        const slot = lanes[0].slots[0]
        expect(slot.state).toBe('covered')
        expect(slot.job?.jobId).toBe('job-covered')
    })

    it('maps WAITING_APPROVAL to needsReview', () => {
        const now = BASE_NOW
        const schedule = dailySlot()
        const job = makeJob({
            status: 'WAITING_APPROVAL',
            jobId: 'job-review',
            station: 'TEST',
            createdAt: m(now, -13), // 06:45 — 15-min lead on the 07:00 slot
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        expect(lanes[0].slots[0].state).toBe('needsReview')
    })

    it('maps pipeline statuses (REQUESTED..AUDIO_READY) to generating', () => {
        const now = BASE_NOW
        const schedule = dailySlot()
        const pipelineStatuses = [
            'REQUESTED',
            'HERE_FETCHED',
            'INCIDENTS_SELECTED',
            'SCRIPT_READY',
            'SYNTHESIZING_AUDIO',
            'AUDIO_READY',
        ] as const
        for (const status of pipelineStatuses) {
            const job = makeJob({
                status,
                jobId: `job-${status}`,
                station: 'TEST',
                createdAt: m(now, -13), // 06:45 — 15-min lead on the 07:00 slot
            })
            const lanes = computeStationLanes([schedule], [job], now, {
                past: 0,
                future: 1,
            })
            expect(lanes[0].slots[0].state).toBe('generating')
        }
    })

    it('maps REJECTED/FAILED/FALLBACK_USED to atRisk', () => {
        const now = BASE_NOW
        const schedule = dailySlot()
        const riskStatuses = ['REJECTED', 'FAILED', 'FALLBACK_USED'] as const
        for (const status of riskStatuses) {
            const job = makeJob({
                status,
                jobId: `job-${status}`,
                station: 'TEST',
                createdAt: m(now, -13), // 06:45 — 15-min lead on the 07:00 slot
            })
            const lanes = computeStationLanes([schedule], [job], now, {
                past: 0,
                future: 1,
            })
            expect(lanes[0].slots[0].state).toBe('atRisk')
        }
    })

    it('maps a terminal MISSED job to missed even while its slot is still ahead', () => {
        const now = BASE_NOW // 06:58Z
        const schedule = dailySlot() // 07:00Z daily
        // The slot is 2 min in the FUTURE: if any timing or status-set logic
        // shadowed the terminal status, this would read scheduled or covered.
        const job = makeJob({
            status: 'MISSED',
            jobId: 'job-missed',
            station: 'TEST',
            createdAt: m(now, -13), // 06:45 — the poller's 15-min lead on 07:00
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        expect(lanes[0].slots[0].state).toBe('missed')
    })

    it('maps WAITING_APPROVAL past its slot to late', () => {
        const now = LATE_NOW // 07:00Z
        const schedule = makeSchedule({
            cronExpression: '45 6 * * *', // 06:45Z daily
            timezone: 'UTC',
            station: 'TEST',
        })
        // Generated at 06:30 for the 06:45 slot; that slot is now 15 min past,
        // well beyond the 5-min grace, so the bulletin is overdue but still
        // pending approval.
        const job = makeJob({
            status: 'WAITING_APPROVAL',
            jobId: 'job-late',
            station: 'TEST',
            createdAt: m(now, -30), // 06:30 — 15-min lead on the 06:45 slot
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 1,
            future: 0,
        })
        expect(lanes[0].slots).toHaveLength(1)
        expect(lanes[0].slots[0].state).toBe('late')
    })

    it('maps pipeline statuses past their slot to late', () => {
        const now = LATE_NOW // 07:00Z
        const schedule = makeSchedule({
            cronExpression: '45 6 * * *', // 06:45Z daily
            timezone: 'UTC',
            station: 'TEST',
        })
        const job = makeJob({
            status: 'AUDIO_READY',
            jobId: 'job-late-pipeline',
            station: 'TEST',
            createdAt: m(now, -30), // 06:30 — 15-min lead on the 06:45 slot
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 1,
            future: 0,
        })
        expect(lanes[0].slots[0].state).toBe('late')
    })

    it('maps empty past slot beyond 5min grace to missed', () => {
        const now = LATE_NOW // 07:00Z
        // Fires at :15 and :45, so the most recent past occurrence is 06:45,
        // a clear 15 minutes before `now`.
        //
        // This used to use `* * * * *` and passed for the wrong reason:
        // enumerateSlots only requested `past * 3 + future` occurrences from a
        // 6h lookback, so an every-minute cron truncated two hours short and
        // the "most recent past slot" it returned was actually ~6h old. Now
        // that the whole lookback window is enumerated, an every-minute cron's
        // most recent past slot is `now` itself, which is correctly inside the
        // grace. The cron has to be sparse for this assertion to mean anything.
        const schedule = makeSchedule({
            cronExpression: '15,45 * * * *',
            station: 'TEST',
            timezone: 'UTC',
        })
        const lanes = computeStationLanes([schedule], [], now, {
            past: 1,
            future: 0,
        })
        expect(lanes[0].slots).toHaveLength(1)
        expect(lanes[0].slots[0].at.toISOString()).toBe(
            '2026-06-02T06:45:00.000Z'
        )
        expect(lanes[0].slots[0].state).toBe('missed')
        expect(lanes[0].slots[0].job).toBeUndefined()
    })

    it('maps empty future slot to scheduled', () => {
        const now = EARLY_NOW
        const schedule = makeSchedule({
            cronExpression: '* * * * *', // every minute
            station: 'TEST',
        })
        const lanes = computeStationLanes([schedule], [], now, {
            past: 0,
            future: 2,
        })
        expect(lanes[0].slots.length).toBeGreaterThan(0)
        for (const slot of lanes[0].slots) {
            expect(slot.state).toBe('scheduled')
            expect(slot.job).toBeUndefined()
        }
    })

    // -----------------------------------------------------------------------
    // Matching edges
    // -----------------------------------------------------------------------

    // A bulletin belongs to the slot one generation lead AFTER it was created,
    // because that is what the poller does. Matching on creation time alone put
    // it 15 min to the left of its own slot, so on any cadence under 30 minutes
    // it was attributed to the slot it FOLLOWED.
    it('matches a bulletin to the slot its generation lead points at', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            cronExpression: '15 7 * * *', // 07:15 UTC daily
            timezone: 'UTC',
            station: 'TEST',
        })
        const job = makeJob({
            status: 'APPROVED',
            jobId: 'job-lead',
            station: 'TEST',
            createdAt: m(now, 0), // 07:00 — 15-min lead on the 07:15 slot
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        const matched = lanes[0].slots.find((s) => s.job?.jobId === 'job-lead')
        expect(matched).toBeDefined()
        expect(matched?.state).toBe('covered')
    })

    it('keeps a bulletin with no lead ad-hoc instead of claiming a slot', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            cronExpression: '15 7 * * *', // 07:15 UTC daily
            timezone: 'UTC',
            station: 'TEST',
        })
        // A manual trigger fires when it is requested, with no lead at all: it
        // must not be pulled onto whichever slot sits a lead time ahead of it.
        const job = makeJob({
            status: 'APPROVED',
            jobId: 'job-manual',
            station: 'TEST',
            createdAt: m(now, 15), // 07:15 — the slot itself
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        expect(
            lanes[0].slots.find((s) => s.job?.jobId === 'job-manual')
        ).toBeUndefined()
        expect(lanes[0].adHoc.some((j) => j.jobId === 'job-manual')).toBe(true)
    })

    it('picks nearest slot when job is equidistant-ish', () => {
        const now = LATE_NOW
        // Slots at 06:50 and 07:10 UTC, job at 07:00: both 10 min away;
        // the chronologically first slot wins the tie (strict < comparison).
        const schedule = makeSchedule({
            cronExpression: '50 6 * * *\n10 7 * * *',
            timezone: 'UTC',
            station: 'TEST',
        })
        const job = makeJob({
            status: 'APPROVED',
            jobId: 'job-equal',
            station: 'TEST',
            createdAt: m(now, 0),
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 1,
            future: 1,
        })
        // Job should be matched to the chronologically first slot with minimal distance
        const matched = lanes[0].slots.find((s) => s.job?.jobId === 'job-equal')
        expect(matched).toBeDefined()
    })

    // -----------------------------------------------------------------------
    // Disabled schedule
    // -----------------------------------------------------------------------

    it('excludes disabled schedules from slot generation', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            enabled: false,
            cronExpression: '0 0 * * *',
            station: 'TEST',
        })
        const lanes = computeStationLanes([schedule], [], now, {
            past: 0,
            future: 2,
        })
        expect(lanes).toHaveLength(1)
        expect(lanes[0].enabled).toBe(false)
        expect(lanes[0].slots).toHaveLength(0)
    })

    it('does not place old jobs into adHoc for a disabled schedule', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            enabled: false,
            cronExpression: '0 0 * * *',
            station: 'TEST',
        })
        // Job created 3 days ago — stale, would have landed in adHoc
        // before the fix because matchJobsToSlots was still called
        // against an empty slot array, pushing every unmatched job
        // into the ad-hoc bucket regardless of age or status.
        const oldJob = makeJob({
            station: 'TEST',
            status: 'APPROVED',
            createdAt: m(now, -4320),
            jobId: 'job-stale',
        })
        const lanes = computeStationLanes([schedule], [oldJob], now, {
            past: 0,
            future: 2,
        })
        expect(lanes).toHaveLength(1)
        expect(lanes[0].enabled).toBe(false)
        // Core regression: before the fix, adHoc had length 1 containing
        // the stale APPROVED job, making it appear as current activity.
        expect(lanes[0].adHoc).toHaveLength(0)
        expect(lanes[0].slots).toHaveLength(0)
    })

    // -----------------------------------------------------------------------
    // 6-field cron normalization
    // -----------------------------------------------------------------------

    it('strips seconds field from 6-field cron expressions', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            cronExpression: '0 0 7 * * 1-5', // 6-field: sec=0, min=0, hour=7
            station: 'TEST',
        })
        const lanes = computeStationLanes([schedule], [], now, {
            past: 0,
            future: 1,
        })
        expect(lanes[0].slots).toHaveLength(1)
        // Should resolve to 07:00 in Australia/Sydney timezone
        const slotHrs = lanes[0].slots[0].at.getUTCHours()
        // 07:00 AEST = 21:00 UTC previous day (AEST = UTC+10)
        // June 2, 07:00 AEST = June 1, 21:00 UTC
        expect(slotHrs).toBe(21)
    })

    // -----------------------------------------------------------------------
    // Station filtering: jobs for other stations don't appear
    // -----------------------------------------------------------------------

    it('ignores jobs for other stations', () => {
        const now = LATE_NOW
        const schedule = makeSchedule({
            cronExpression: '0 0 * * *',
            station: '2GB 873 Sydney News/Talk',
        })
        const job = makeJob({
            status: 'APPROVED',
            station: '3AW 693 Melbourne Current',
            jobId: 'job-other',
            createdAt: m(now, 0),
        })
        const lanes = computeStationLanes([schedule], [job], now, {
            past: 0,
            future: 1,
        })
        expect(lanes[0].slots[0].job).toBeUndefined()
        const otherJobInAdHoc = lanes[0].adHoc.some(
            (j) => j.jobId === 'job-other'
        )
        expect(otherJobInAdHoc).toBe(false)
    })
})

describe('calendar-aware slot enumeration', () => {
    const baseCron = '0 7 * * *'
    const patternCron = '0 9 * * *'

    function calendarSchedule(
        calendar: ScheduleCalendar,
        overrides: Partial<MarketSchedule> = {}
    ) {
        return makeSchedule({
            station: 'TEST',
            timezone: 'UTC',
            cronExpression: baseCron,
            calendar,
            ...overrides,
        })
    }

    it('omits the base slot on a suppressed station-local date', () => {
        const schedule = calendarSchedule({
            patterns: [],
            assignments: [
                {
                    startDate: '2026-06-02',
                    endDate: '2026-06-02',
                    patternId: null,
                    suppress: true,
                    note: null,
                },
            ],
        })

        const lanes = computeStationLanes(
            [schedule],
            [],
            new Date('2026-06-02T06:58:00Z'),
            { past: 0, future: 1 }
        )

        expect(lanes[0].slots[0]?.at.toISOString()).toBe(
            '2026-06-03T07:00:00.000Z'
        )
    })

    it('uses the assigned pattern cron on a pattern day', () => {
        const schedule = calendarSchedule({
            patterns: [
                {
                    id: 'holiday',
                    name: 'Holiday clock',
                    cronExpression: patternCron,
                },
            ],
            assignments: [
                {
                    startDate: '2026-06-02',
                    endDate: '2026-06-02',
                    patternId: 'holiday',
                    suppress: false,
                    note: null,
                },
            ],
        })

        const lanes = computeStationLanes(
            [schedule],
            [],
            new Date('2026-06-02T06:58:00Z'),
            { past: 0, future: 1 }
        )

        expect(lanes[0].slots[0]?.at.toISOString()).toBe(
            '2026-06-02T09:00:00.000Z'
        )
    })

    it('keeps the base cron on an ordinary day', () => {
        const schedule = calendarSchedule({
            patterns: [
                {
                    id: 'holiday',
                    name: 'Holiday clock',
                    cronExpression: patternCron,
                },
            ],
            assignments: [
                {
                    startDate: '2026-06-03',
                    endDate: '2026-06-03',
                    patternId: 'holiday',
                    suppress: false,
                    note: null,
                },
            ],
        })

        const lanes = computeStationLanes(
            [schedule],
            [],
            new Date('2026-06-02T06:58:00Z'),
            { past: 0, future: 1 }
        )

        expect(lanes[0].slots[0]?.at.toISOString()).toBe(
            '2026-06-02T07:00:00.000Z'
        )
    })

    it('resolves assignment boundaries in the station timezone for the board and review deadline', () => {
        const schedule = calendarSchedule(
            {
                patterns: [
                    {
                        id: 'holiday',
                        name: 'Holiday clock',
                        cronExpression: patternCron,
                    },
                ],
                assignments: [
                    {
                        startDate: '2026-06-02',
                        endDate: '2026-06-02',
                        patternId: 'holiday',
                        suppress: false,
                        note: null,
                    },
                ],
            },
            { timezone: 'Australia/Sydney' }
        )
        const now = new Date('2026-06-01T13:58:00Z') // June 1, 23:58 Sydney
        const expected = '2026-06-01T23:00:00.000Z' // June 2, 09:00 Sydney

        const lanes = computeStationLanes([schedule], [], now, {
            past: 0,
            future: 1,
        })
        expect(lanes[0].slots[0]?.at.toISOString()).toBe(expected)

        const job = makeJob({
            station: 'TEST',
            status: 'APPROVED',
            createdAt: '2026-06-01T22:45:00Z',
        })
        expect(findAirSlot([schedule], job, now)?.toISOString()).toBe(expected)
    })
})
