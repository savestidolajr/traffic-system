import { describe, it, expect } from 'vitest'
import {
    availabilityWindowError,
    isEligibleNow,
    summariseAvailability,
    summariseDays,
    type StationLocalNow,
} from './filler-availability'
import type { FillerAvailabilityEntry } from '../api/filler-statements-hooks'

const NOW: StationLocalNow = {
    date: '2026-08-06',
    time: '08:30',
    dayName: 'Thursday',
}

const window = (
    over: Partial<FillerAvailabilityEntry>
): FillerAvailabilityEntry => ({
    startDate: null,
    endDate: null,
    days: null,
    startLocal: null,
    endLocal: null,
    ...over,
})

describe('isEligibleNow', () => {
    it('null availability is always eligible', () => {
        expect(isEligibleNow(null, NOW)).toBe(true)
    })

    it('an empty window is always eligible', () => {
        expect(isEligibleNow(window({}), NOW)).toBe(true)
    })

    it('inclusive date bounds exclude outside dates and keep the boundary dates', () => {
        expect(
            isEligibleNow(
                window({ startDate: '2026-08-01', endDate: '2026-11-30' }),
                NOW
            )
        ).toBe(true)
        expect(isEligibleNow(window({ startDate: '2026-08-07' }), NOW)).toBe(
            false
        )
        expect(isEligibleNow(window({ endDate: '2026-08-05' }), NOW)).toBe(
            false
        )
        // Boundaries are inclusive — same-day start and end stay eligible.
        expect(
            isEligibleNow(
                window({ startDate: '2026-08-06', endDate: '2026-08-06' }),
                NOW
            )
        ).toBe(true)
    })

    it('the day-of-week set is matched against the local date (Thursday here)', () => {
        expect(isEligibleNow(window({ days: ['Thursday'] }), NOW)).toBe(true)
        expect(isEligibleNow(window({ days: ['Monday', 'Friday'] }), NOW)).toBe(
            false
        )
        // Empty days = every day.
        expect(isEligibleNow(window({ days: [] }), NOW)).toBe(true)
    })

    it('half-open same-day window is active at start, inactive at end', () => {
        expect(
            isEligibleNow(
                window({ startLocal: '08:00', endLocal: '09:00' }),
                NOW
            )
        ).toBe(true)
        // [start, end) — the start instant itself is inside the window, matching the
        // backend's one shared TimeWindowOverlap.Contains primitive.
        expect(
            isEligibleNow(
                window({ startLocal: '08:30', endLocal: '09:00' }),
                NOW
            )
        ).toBe(true)
        expect(
            isEligibleNow(
                window({ startLocal: '07:00', endLocal: '08:30' }),
                NOW
            )
        ).toBe(false)
    })

    it('a midnight-wrapping window is active before start OR after end', () => {
        const wrap = window({ startLocal: '22:00', endLocal: '06:00' })
        // 08:30 Thursday is inside neither wrapped range.
        expect(isEligibleNow(wrap, NOW)).toBe(false)
        expect(
            isEligibleNow(wrap, {
                date: '2026-08-06',
                time: '23:00',
                dayName: 'Thursday',
            })
        ).toBe(true)
        expect(
            isEligibleNow(wrap, {
                date: '2026-08-07',
                time: '02:00',
                dayName: 'Friday',
            })
        ).toBe(true)
        // 06:00 is the half-open end — not active.
        expect(
            isEligibleNow(wrap, {
                date: '2026-08-07',
                time: '06:00',
                dayName: 'Friday',
            })
        ).toBe(false)
    })

    it('day-of-week is evaluated on the air date even inside a wrapped window', () => {
        // 02:00 Friday is inside the Thursday-22:00 window; Friday must be in the set.
        const wrap = window({
            startLocal: '22:00',
            endLocal: '06:00',
            days: ['Thursday'],
        })
        expect(
            isEligibleNow(wrap, {
                date: '2026-08-07',
                time: '02:00',
                dayName: 'Friday',
            })
        ).toBe(false)
    })
})

describe('availabilityWindowError', () => {
    it('accepts a valid window', () => {
        expect(
            availabilityWindowError(
                window({
                    startDate: '2026-08-01',
                    startLocal: '06:00',
                    endLocal: '09:00',
                })
            )
        ).toBeNull()
    })

    it('rejects end date before start date', () => {
        expect(
            availabilityWindowError(
                window({ startDate: '2026-08-10', endDate: '2026-08-01' })
            )
        ).toContain('before')
    })

    it('rejects a half-set time window', () => {
        expect(
            availabilityWindowError(window({ startLocal: '06:00' }))
        ).toContain('both')
        expect(
            availabilityWindowError(window({ endLocal: '09:00' }))
        ).toContain('both')
    })

    it('rejects a zero-length time window', () => {
        expect(
            availabilityWindowError(
                window({ startLocal: '06:00', endLocal: '06:00' })
            )
        ).toContain('differ')
    })
})

describe('summariseDays', () => {
    it('renders empty and full sets', () => {
        expect(summariseDays(null)).toBe('Every day')
        expect(summariseDays([])).toBe('Every day')
        expect(
            summariseDays([
                'Monday',
                'Tuesday',
                'Wednesday',
                'Thursday',
                'Friday',
                'Saturday',
                'Sunday',
            ])
        ).toBe('Every day')
    })

    it('renders the named blocks', () => {
        expect(
            summariseDays([
                'Monday',
                'Tuesday',
                'Wednesday',
                'Thursday',
                'Friday',
            ])
        ).toBe('Weekdays')
        expect(summariseDays(['Saturday', 'Sunday'])).toBe('Weekends')
    })

    it('renders a sparse set in display order', () => {
        expect(summariseDays(['Friday', 'Monday', 'Wednesday'])).toBe(
            'Mon, Wed, Fri'
        )
    })
})

describe('summariseAvailability', () => {
    it('is empty when no window is set', () => {
        expect(summariseAvailability(null)).toBe('')
    })

    it('builds the compact one-line summary', () => {
        expect(
            summariseAvailability(
                window({
                    startDate: '2026-08-01',
                    endDate: '2026-11-30',
                    days: [
                        'Monday',
                        'Tuesday',
                        'Wednesday',
                        'Thursday',
                        'Friday',
                    ],
                    startLocal: '06:00',
                    endLocal: '09:00',
                })
            )
        ).toBe('Weekdays 06:00-09:00, from 1 Aug, until 30 Nov')
    })

    it('drops the unconstrained parts', () => {
        expect(
            summariseAvailability(
                window({ startLocal: '22:00', endLocal: '06:00' })
            )
        ).toBe('22:00-06:00')
    })
})
