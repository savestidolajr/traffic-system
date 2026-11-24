import { describe, expect, it } from 'vitest'
import {
    occurrenceTimesForDate,
    projectYear,
    resolveAssignment,
    resolveDay,
    summariseYear,
    weekdaySlotCounts,
} from './schedule-calendar'
import type {
    ScheduleAssignment,
    ScheduleCalendar,
    SchedulePattern,
} from '../types'

const SYDNEY = 'Australia/Sydney'

// ─── resolveAssignment ───────────────────────────────────────────────────────

describe('resolveAssignment', () => {
    it('returns null when the date is outside every assignment window', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-12-25',
            endDate: '2027-12-25',
            patternId: 'holiday',
            suppress: false,
            note: null,
        }
        const calendar: ScheduleCalendar = {
            patterns: [],
            assignments: [assignment],
        }

        expect(resolveAssignment(calendar, '2027-12-24')).toBeNull()
    })

    it('returns the containing assignment when there is one match', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-12-24',
            endDate: '2027-12-26',
            patternId: 'holiday',
            suppress: false,
            note: 'Christmas clock',
        }
        const calendar: ScheduleCalendar = {
            patterns: [],
            assignments: [assignment],
        }

        expect(resolveAssignment(calendar, '2027-12-25')).toEqual(assignment)
    })

    it('lets a single-date Christmas override beat a month-long December clock', () => {
        const decemberClock: ScheduleAssignment = {
            startDate: '2027-12-01',
            endDate: '2027-12-31',
            patternId: 'december',
            suppress: false,
            note: 'December reduced clock',
        }
        const christmasDay: ScheduleAssignment = {
            startDate: '2027-12-25',
            endDate: '2027-12-25',
            patternId: 'christmas',
            suppress: false,
            note: 'Christmas Day clock',
        }
        const calendar: ScheduleCalendar = {
            patterns: [],
            assignments: [decemberClock, christmasDay],
        }

        expect(resolveAssignment(calendar, '2027-12-25')).toBe(christmasDay)
    })

    it('lets the later array entry win when matching windows have the same span', () => {
        const first: ScheduleAssignment = {
            startDate: '2027-07-01',
            endDate: '2027-07-07',
            patternId: 'first',
            suppress: false,
            note: null,
        }
        const later: ScheduleAssignment = {
            startDate: '2027-07-01',
            endDate: '2027-07-07',
            patternId: 'later',
            suppress: false,
            note: null,
        }
        const calendar: ScheduleCalendar = {
            patterns: [],
            assignments: [first, later],
        }

        expect(resolveAssignment(calendar, '2027-07-04')).toBe(later)
    })

    it('treats assignment boundaries as inclusive and adjacent dates as outside', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-08-10',
            endDate: '2027-08-12',
            patternId: 'special',
            suppress: false,
            note: null,
        }
        const calendar: ScheduleCalendar = {
            patterns: [],
            assignments: [assignment],
        }

        expect(resolveAssignment(calendar, '2027-08-09')).toBeNull()
        expect(resolveAssignment(calendar, '2027-08-10')).toBe(assignment)
        expect(resolveAssignment(calendar, '2027-08-12')).toBe(assignment)
        expect(resolveAssignment(calendar, '2027-08-13')).toBeNull()
    })
})

// ─── resolveDay ───────────────────────────────────────────────────────────────

describe('resolveDay', () => {
    it('uses the schedule base expression when there is no calendar', () => {
        const schedule = {
            cronExpression: '0 7 * * 1-5',
            calendar: null,
        }

        expect(resolveDay(schedule, '2027-03-01')).toMatchObject({
            kind: 'base',
            cronExpression: '0 7 * * 1-5',
            patternId: null,
            patternName: null,
            assignment: null,
        })
    })

    it('resolves a suppressing assignment to an empty suppressed day', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-04-25',
            endDate: '2027-04-25',
            patternId: null,
            suppress: true,
            note: 'No bulletin',
        }
        const schedule = {
            cronExpression: '0 7 * * *',
            calendar: {
                patterns: [],
                assignments: [assignment],
            },
        }

        expect(resolveDay(schedule, '2027-04-25')).toMatchObject({
            kind: 'suppressed',
            cronExpression: '',
            patternId: null,
            patternName: null,
            assignment,
        })
    })

    it('resolves a pattern assignment with the pattern expression and identity', () => {
        const pattern: SchedulePattern = {
            id: 'public-holiday',
            name: 'Public holiday clock',
            cronExpression: '0 10 * * *',
        }
        const assignment: ScheduleAssignment = {
            startDate: '2027-06-14',
            endDate: '2027-06-14',
            patternId: pattern.id,
            suppress: false,
            note: 'King Birthday',
        }
        const schedule = {
            cronExpression: '0 7 * * 1-5',
            calendar: {
                patterns: [pattern],
                assignments: [assignment],
            },
        }

        expect(resolveDay(schedule, '2027-06-14')).toMatchObject({
            kind: 'pattern',
            cronExpression: pattern.cronExpression,
            patternId: pattern.id,
            patternName: pattern.name,
            assignment,
        })
    })

    it('falls back to the base clock for a dangling pattern id', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-06-14',
            endDate: '2027-06-14',
            patternId: 'deleted-pattern',
            suppress: false,
            note: null,
        }
        const schedule = {
            cronExpression: '0 7 * * 1-5',
            calendar: {
                patterns: [],
                assignments: [assignment],
            },
        }

        expect(resolveDay(schedule, '2027-06-14')).toMatchObject({
            kind: 'base',
            cronExpression: '0 7 * * 1-5',
            patternId: null,
            patternName: null,
            assignment,
        })
        expect(resolveDay(schedule, '2027-06-14').kind).not.toBe('suppressed')
    })

    it('matches a pattern id case-insensitively, as the backend does', () => {
        // `ScheduleCalendar.ResolveDay` looks the pattern up with OrdinalIgnoreCase and
        // `Create` rejects ids that collide case-insensitively. A case-sensitive match here
        // would preview "normal clock" for a day the backend actually runs on the pattern.
        const schedule = {
            cronExpression: '0 7 * * 1-5',
            calendar: {
                patterns: [
                    {
                        id: 'Public-Holiday',
                        name: 'Public holiday',
                        cronExpression: '0 9-15 * * 0,1,2,3,4,5,6',
                    },
                ],
                assignments: [
                    {
                        startDate: '2027-06-14',
                        endDate: '2027-06-14',
                        patternId: 'public-holiday',
                        suppress: false,
                        note: "King's Birthday",
                    },
                ],
            },
        }

        expect(resolveDay(schedule, '2027-06-14')).toMatchObject({
            kind: 'pattern',
            cronExpression: '0 9-15 * * 0,1,2,3,4,5,6',
            patternId: 'Public-Holiday',
            patternName: 'Public holiday',
        })
    })
})

// ─── occurrenceTimesForDate ──────────────────────────────────────────────────

describe('occurrenceTimesForDate', () => {
    it('returns weekday clock times and none on Sunday', () => {
        expect(occurrenceTimesForDate('0 6 * * 1-5', '2027-03-01')).toEqual([
            '06:00',
        ])
        expect(occurrenceTimesForDate('0 6 * * 1-5', '2027-03-07')).toEqual([])
    })

    it('resolves each line of a compound expression for its matching weekday', () => {
        const expression = '0 8 * * 1-5\n30 9 * * 6'

        expect(occurrenceTimesForDate(expression, '2027-03-01')).toEqual([
            '08:00',
        ])
        expect(occurrenceTimesForDate(expression, '2027-03-06')).toEqual([
            '09:30',
        ])
        expect(occurrenceTimesForDate(expression, '2027-03-07')).toEqual([])
    })

    it('returns wall-clock times, unchanged across a DST transition', () => {
        // The times a cron NAMES do not move when the clocks do: an 06:00 weekday
        // read is 06:00 local on both sides of the transition. Modelling the 23/25
        // hour day here is what forced a per-minute Intl walk and made the Year tab
        // take seconds; the backend's Cronos evaluation owns that nuance.
        const expression = '0 6 * * *'

        expect(occurrenceTimesForDate(expression, '2027-10-03')).toEqual([
            '06:00',
        ])
        expect(occurrenceTimesForDate(expression, '2027-04-04')).toEqual([
            '06:00',
        ])
    })

    it('returns no occurrences for an empty expression', () => {
        expect(occurrenceTimesForDate('', '2027-03-01')).toEqual([])
    })

    it('costs no measurable time on a real 60-line station cron', () => {
        // Regression on the reason this was rewritten: enumerating candidate minutes
        // through Intl cost 1,166 ms for ONE day of this shape of expression.
        const lines: string[] = []
        for (let hour = 5; hour < 20; hour++)
            for (const minute of [0, 15, 30, 45])
                lines.push(`${minute} ${hour} * * 1-5`)
        const expression = lines.join('\n')

        const started = performance.now()
        const times = occurrenceTimesForDate(expression, '2027-03-01')
        const elapsed = performance.now() - started

        expect(times).toHaveLength(60)
        expect(elapsed).toBeLessThan(50)
    })
})

// ─── weekdaySlotCounts ────────────────────────────────────────────────────────

describe('weekdaySlotCounts', () => {
    it('indexes Sunday at zero', () => {
        expect(weekdaySlotCounts('0 6,18 * * 1-5')).toEqual([
            0, 2, 2, 2, 2, 2, 0,
        ])
    })

    it('counts every distinct minute a compound expression names', () => {
        expect(weekdaySlotCounts('0,30 6 * * 6\n0 7 * * 6')).toEqual([
            0, 0, 0, 0, 0, 0, 3,
        ])
    })
})

// ─── projectYear / summariseYear ──────────────────────────────────────────────

describe('projectYear and summariseYear', () => {
    it('projects 366 days in a leap year and 365 in a non-leap year', () => {
        const schedule = {
            cronExpression: '0 12 * * *',
            timezone: SYDNEY,
            calendar: null,
        }

        expect(projectYear(schedule, 2028)).toHaveLength(366)
        expect(projectYear(schedule, 2027)).toHaveLength(365)
    })

    it('shows suppressed days with zero slots and retains their non-zero base slots', () => {
        const assignment: ScheduleAssignment = {
            startDate: '2027-01-15',
            endDate: '2027-01-15',
            patternId: null,
            suppress: true,
            note: 'Station holiday',
        }
        const schedule = {
            cronExpression: '0 12 * * *',
            timezone: SYDNEY,
            calendar: {
                patterns: [],
                assignments: [assignment],
            },
        }

        const days = projectYear(schedule, 2027)
        const suppressed = days.find((day) => day.iso === '2027-01-15')
        const summary = summariseYear(days)

        expect(suppressed).toMatchObject({
            kind: 'suppressed',
            slots: 0,
            baseSlots: 1,
        })
        expect(summary.suppressedDays).toBe(1)
    })

    it('summarises ten overridden dates and reflects a higher weekday pattern clock', () => {
        const pattern: SchedulePattern = {
            id: 'extended-weekday',
            name: 'Extended weekday clock',
            cronExpression: '0 9,10 * * 1-5',
        }
        const assignment: ScheduleAssignment = {
            startDate: '2027-01-04',
            endDate: '2027-01-13',
            patternId: pattern.id,
            suppress: false,
            note: 'Summer run',
        }
        const schedule = {
            cronExpression: '0 9 * * *',
            timezone: SYDNEY,
            calendar: {
                patterns: [pattern],
                assignments: [assignment],
            },
        }

        const summary = summariseYear(projectYear(schedule, 2027))

        expect(summary.overriddenDays).toBe(10)
        expect(summary.totalSlots).toBeGreaterThan(summary.baseTotalSlots)
    })
})
