import { describe, it, expect } from 'vitest'
import {
    buildCronFromCalendar,
    parseCronToCalendar,
    detectCronLoss,
    formatTimeLabel,
    TIME_SLOTS,
    type CronCalendarInput,
} from './cron-calendar'
import { expectIntervalParse } from '@/test/utils'
import { intervalInput, specificInput } from '@/test/factories'

const ALL_DAYS = [0, 1, 2, 3, 4, 5, 6]
const WEEKEND = [0, 6]

// ─── TIME_SLOTS ───────────────────────────────────────────────────────────────

describe('TIME_SLOTS', () => {
    it('has 19 slots from 5am to 11pm', () => {
        expect(TIME_SLOTS.length).toBe(19)
        expect(TIME_SLOTS[0].value).toBe('05:00')
        expect(TIME_SLOTS[18].value).toBe('23:00')
    })
})

// ─── formatTimeLabel ──────────────────────────────────────────────────────────

describe('formatTimeLabel', () => {
    it.each([
        ['06:00', '06:00'],
        ['12:00', '12:00'],
        ['00:00', '00:00'],
        ['13:00', '13:00'],
        ['23:00', '23:00'],
        ['06:30', '06:30'],
        ['14:45', '14:45'],
    ])('formats %s as %s', (time, expected) => {
        expect(formatTimeLabel(time)).toBe(expected)
    })
})

// ─── buildCronFromCalendar — interval mode ────────────────────────────────────

describe('buildCronFromCalendar – interval mode', () => {
    it('generates correct cron for 6am–6pm every 15 min on weekdays', () => {
        const result = buildCronFromCalendar(intervalInput())
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe(
            '0,15,30,45 6-17 * * 1,2,3,4,5\n0 18 * * 1,2,3,4,5'
        )
    })

    it('generates correct cron for 6am–11pm every 15 min', () => {
        const result = buildCronFromCalendar(
            intervalInput({ endTime: '23:00' })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe(
            '0,15,30,45 6-22 * * 1,2,3,4,5\n0 23 * * 1,2,3,4,5'
        )
    })

    it('generates correct cron for 5am–11pm every 30 min', () => {
        const result = buildCronFromCalendar(
            intervalInput({
                startTime: '05:00',
                endTime: '23:00',
                intervalMinutes: 30,
            })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe(
            '0,30 5-22 * * 1,2,3,4,5\n0 23 * * 1,2,3,4,5'
        )
    })

    it('generates correct cron for 9am–5pm every 60 min', () => {
        const result = buildCronFromCalendar(
            intervalInput({
                startTime: '09:00',
                endTime: '17:00',
                intervalMinutes: 60,
            })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('0 9-17 * * 1,2,3,4,5')
    })

    it('generates correct cron for 8am–10pm every 120 min on all days', () => {
        const result = buildCronFromCalendar(
            intervalInput({
                startTime: '08:00',
                endTime: '22:00',
                intervalMinutes: 120,
                daysOfWeek: ALL_DAYS,
            })
        )
        expect(result.error).toBeUndefined()
        // A full week is written `*`: same slots, and it round-trips with what the
        // backend stores instead of drifting to the enumerated form on every save.
        expect(result.cronExpression).toBe('0 8-22 * * *')
    })

    it('generates correct cron for 6am–9pm every 180 min on weekends', () => {
        const result = buildCronFromCalendar(
            intervalInput({
                startTime: '06:00',
                endTime: '21:00',
                intervalMinutes: 180,
                daysOfWeek: WEEKEND,
            })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('0 6-21 * * 0,6')
    })

    it('uses * when no days specified', () => {
        const result = buildCronFromCalendar(intervalInput({ daysOfWeek: [] }))
        expect(result.cronExpression).toBe('0,15,30,45 6-17 * * *\n0 18 * * *')
    })

    it('returns error when start >= end', () => {
        const result = buildCronFromCalendar(
            intervalInput({ startTime: '18:00', endTime: '06:00' })
        )
        expect(result.cronExpression).toBeNull()
        expect(result.error).toBeDefined()
    })

    it('returns error when start == end', () => {
        const result = buildCronFromCalendar(
            intervalInput({ startTime: '10:00', endTime: '10:00' })
        )
        expect(result.cronExpression).toBeNull()
        expect(result.error).toBeDefined()
    })

    it('returns error when times are not on the hour', () => {
        const result = buildCronFromCalendar(
            intervalInput({ startTime: '06:15', endTime: '18:00' })
        )
        expect(result.cronExpression).toBeNull()
        expect(result.error).toBeDefined()
    })

    it('returns error for invalid time format', () => {
        const result = buildCronFromCalendar(
            intervalInput({ startTime: 'invalid', endTime: '18:00' })
        )
        expect(result.cronExpression).toBeNull()
        expect(result.error).toBeDefined()
    })

    it('works with single-hour range (adjacent hours)', () => {
        const result = buildCronFromCalendar(
            intervalInput({
                startTime: '10:00',
                endTime: '11:00',
                intervalMinutes: 15,
            })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe(
            '0,15,30,45 10-10 * * 1,2,3,4,5\n0 11 * * 1,2,3,4,5'
        )
    })
})

// ─── buildCronFromCalendar — specific times mode ──────────────────────────────

describe('buildCronFromCalendar – specific times mode', () => {
    it('generates cron for a single on-the-hour time', () => {
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: ['08:00'] })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('0 8 * * 1,2,3,4,5')
    })

    it('generates cron for multiple on-the-hour times', () => {
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: ['06:00', '12:00', '18:00'] })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('0 6,12,18 * * 1,2,3,4,5')
    })

    it('generates cron for all TIME_SLOTS on the hour', () => {
        const allHours = TIME_SLOTS.map((s) => s.value) // '05:00' to '23:00'
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: allHours })
        )
        expect(result.error).toBeUndefined()
        // All hours 5-23 on minute 0
        expect(result.cronExpression).toBe(
            '0 5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23 * * 1,2,3,4,5'
        )
    })

    it('handles a single custom time with non-zero minutes', () => {
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: ['08:30'] })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('30 8 * * 1,2,3,4,5')
    })

    it('uses newlines for times with different minutes', () => {
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: ['08:00', '10:30'] })
        )
        expect(result.error).toBeUndefined()
        // Two minute groups: :00 group and :30 group
        expect(result.cronExpression).toContain('\n')
        const lines = result.cronExpression!.split('\n')
        expect(lines).toHaveLength(2)
        // One line for minute 0 hours, one for minute 30 hours
        expect(lines).toContain('0 8 * * 1,2,3,4,5')
        expect(lines).toContain('30 10 * * 1,2,3,4,5')
    })

    it('groups multiple hours with the same minute', () => {
        const result = buildCronFromCalendar(
            specificInput({
                specificTimes: ['08:00', '10:30', '12:00', '14:30'],
            })
        )
        expect(result.error).toBeUndefined()
        const lines = result.cronExpression!.split('\n')
        expect(lines).toHaveLength(2)
        expect(lines).toContain('0 8,12 * * 1,2,3,4,5')
        expect(lines).toContain('30 10,14 * * 1,2,3,4,5')
    })

    it('returns error when no times are specified', () => {
        const result = buildCronFromCalendar(
            specificInput({ specificTimes: [] })
        )
        expect(result.cronExpression).toBeNull()
        expect(result.error).toBeDefined()
    })

    it('handles weekend days', () => {
        const result = buildCronFromCalendar(
            specificInput({
                daysOfWeek: WEEKEND,
                specificTimes: ['07:00', '19:00'],
            })
        )
        expect(result.error).toBeUndefined()
        expect(result.cronExpression).toBe('0 7,19 * * 0,6')
    })

    it('handles times with :15 and :45 minutes', () => {
        const result = buildCronFromCalendar(
            specificInput({
                specificTimes: ['06:15', '12:15', '08:45', '14:45'],
            })
        )
        expect(result.error).toBeUndefined()
        const lines = result.cronExpression!.split('\n')
        expect(lines).toHaveLength(2)
        expect(lines).toContain('15 6,12 * * 1,2,3,4,5')
        expect(lines).toContain('45 8,14 * * 1,2,3,4,5')
    })
})

// ─── parseCronToCalendar ──────────────────────────────────────────────────────

describe('parseCronToCalendar', () => {
    it('returns defaults for null', () => {
        const result = parseCronToCalendar(null)
        expectIntervalParse(result, {
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 15,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('returns defaults for empty string', () => {
        const result = parseCronToCalendar('')
        expect(result.scheduleMode).toBe('interval')
    })

    it('parses interval cron with comma minutes: 0,15,30,45 6-18 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar('0,15,30,45 6-18 * * 1,2,3,4,5')
        expectIntervalParse(result, {
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 15,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('parses interval cron: 0,30 5-23 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar('0,30 5-23 * * 1,2,3,4,5')
        expectIntervalParse(result, {
            startTime: '05:00',
            endTime: '23:00',
            intervalMinutes: 30,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('parses interval cron: 0 9-17 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar('0 9-17 * * 1,2,3,4,5')
        expectIntervalParse(result, {
            startTime: '09:00',
            endTime: '17:00',
            intervalMinutes: 60,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('parses interval cron with slash minutes: */30 6-18 * * *', () => {
        const result = parseCronToCalendar('*/30 6-18 * * *')
        expectIntervalParse(result, {
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 30,
            daysOfWeek: [0, 1, 2, 3, 4, 5, 6],
        })
    })

    it('parses 6-field NCRONTAB with hour range: 0 30 6-9 * * 1-5 → 6:30…9:30', () => {
        const result = parseCronToCalendar('0 30 6-9 * * 1-5')
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual([
            '06:30',
            '07:30',
            '08:30',
            '09:30',
        ])
        expect(result.daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('parses 6-field NCRONTAB hourly interval: 0 0 6-18 * * 1-5', () => {
        const result = parseCronToCalendar('0 0 6-18 * * 1-5')
        expectIntervalParse(result, {
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 60,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('expands day-of-week ranges: 0 7,8 * * 1-5', () => {
        const result = parseCronToCalendar('0 7,8 * * 1-5')
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual(['07:00', '08:00'])
        expect(result.daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('expands hour ranges with a fixed non-zero minute: 30 6-9 * * 1-5', () => {
        const result = parseCronToCalendar('30 6-9 * * 1-5')
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual([
            '06:30',
            '07:30',
            '08:30',
            '09:30',
        ])
    })

    it('normalizes NCRONTAB Sunday 7 to 0: 0 9 * * 7', () => {
        const result = parseCronToCalendar('0 9 * * 7')
        expect(result.daysOfWeek).toEqual([0])
    })

    it('parses specific-times cron (multiple hours): 0 6,12,18 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar('0 6,12,18 * * 1,2,3,4,5')
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual(['06:00', '12:00', '18:00'])
        expect(result.daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('parses specific-times cron with non-zero minute: 30 8,14 * * 0,6', () => {
        const result = parseCronToCalendar('30 8,14 * * 0,6')
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual(['08:30', '14:30'])
        expect(result.daysOfWeek).toEqual([0, 6])
    })

    it('parses single specific hour: 0 8 * * 1,2,3,4,5', () => {
        // Single hour without comma — this should still be parseable
        const result = parseCronToCalendar('0 8 * * 1,2,3,4,5')
        // With a single hour and minute 0, it could be interval or specific.
        // The current code treats single hour without comma as interval.
        // Whatever mode it parses to, a round-trip should preserve the semantics.
        expect(result.daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('parses compound interval cron: 0,15,30,45 6-17 * * 1,2,3,4,5\\n0 18 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar(
            '0,15,30,45 6-17 * * 1,2,3,4,5\n0 18 * * 1,2,3,4,5'
        )
        expectIntervalParse(result, {
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 15,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('parses compound interval cron: 0,30 5-22 * * 1,2,3,4,5\\n0 23 * * 1,2,3,4,5', () => {
        const result = parseCronToCalendar(
            '0,30 5-22 * * 1,2,3,4,5\n0 23 * * 1,2,3,4,5'
        )
        expectIntervalParse(result, {
            startTime: '05:00',
            endTime: '23:00',
            intervalMinutes: 30,
            daysOfWeek: [1, 2, 3, 4, 5],
        })
    })

    it('parses multi-line cron', () => {
        const cron = '0 8,12 * * 1,2,3,4,5\n30 10,14 * * 1,2,3,4,5'
        const result = parseCronToCalendar(cron)
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes.sort()).toEqual(
            ['08:00', '10:30', '12:00', '14:30'].sort()
        )
        expect(result.daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('collapses a time that recurs across weekday and weekend lines', () => {
        // Real dev shape (Sydney KIIS 90s): 07:59 is scheduled on the Mon–Fri
        // line and again on the Sat and Sun lines. The calendar model is a set
        // of times, so it must land once — repeats become duplicate React keys
        // in the studio and break chip removal.
        const result = parseCronToCalendar(
            '59 7 * * 1-5\n59 7 * * 6\n59 7 * * 0\n29 6 * * 1-5\n59 8 * * 1-5\n59 8 * * 6'
        )
        expect(result.scheduleMode).toBe('specific')
        expect(result.specificTimes).toEqual(['06:29', '07:59', '08:59'])
        expect(result.daysOfWeek).toEqual([0, 1, 2, 3, 4, 5, 6])
    })

    it('reads a * day field as every day, not as the weekday default', () => {
        // Regression: `*` was expanded to Mon-Fri, so the year calendar showed a 7-day
        // station with ~104 empty weekends and a weekday-only slot total, and a weekly
        // save would have narrowed the stored clock to `1,2,3,4,5`. The Mon-Fri default
        // is for a schedule with no cron at all (see the null case below).
        const result = parseCronToCalendar('0,15,30,45 6-18 * * *')
        expect(result.daysOfWeek).toEqual([0, 1, 2, 3, 4, 5, 6])
    })

    it('still defaults to weekdays when there is no cron yet', () => {
        expect(parseCronToCalendar(null).daysOfWeek).toEqual([1, 2, 3, 4, 5])
    })

    it('parses all-days: 0,30 8-20 * * 0,1,2,3,4,5,6', () => {
        const result = parseCronToCalendar('0,30 8-20 * * 0,1,2,3,4,5,6')
        expectIntervalParse(result, {
            startTime: '08:00',
            endTime: '20:00',
            intervalMinutes: 30,
            daysOfWeek: [0, 1, 2, 3, 4, 5, 6],
        })
    })
})

// ─── Round-trip tests: build → parse → build  ────────────────────────────────

describe('round-trip: buildCronFromCalendar → parseCronToCalendar → buildCronFromCalendar', () => {
    /** Assert that building, then parsing, then rebuilding produces the same cron. */
    function expectRoundTrip(input: CronCalendarInput) {
        const result1 = buildCronFromCalendar(input)
        expect(result1.error).toBeUndefined()
        expect(result1.cronExpression).toBeTruthy()

        const parsed = parseCronToCalendar(result1.cronExpression!)

        // Rebuild with the exact parsed values
        const result2 = buildCronFromCalendar(parsed)
        expect(result2.error).toBeUndefined()
        expect(result2.cronExpression).toBe(result1.cronExpression)
    }

    it('interval: 6am–6pm every 15 min weekdays', () => {
        expectRoundTrip(intervalInput())
    })

    it('interval: 6am–11pm every 15 min', () => {
        expectRoundTrip(intervalInput({ endTime: '23:00' }))
    })

    it('interval: 5am–11pm every 30 min', () => {
        expectRoundTrip(
            intervalInput({
                startTime: '05:00',
                endTime: '23:00',
                intervalMinutes: 30,
            })
        )
    })

    it('interval: 8am–10pm every 120 min all days', () => {
        expectRoundTrip(
            intervalInput({
                startTime: '08:00',
                endTime: '22:00',
                intervalMinutes: 120,
                daysOfWeek: ALL_DAYS,
            })
        )
    })

    it('interval: 6am–9pm every 180 min weekends', () => {
        expectRoundTrip(
            intervalInput({
                startTime: '06:00',
                endTime: '21:00',
                intervalMinutes: 180,
                daysOfWeek: WEEKEND,
            })
        )
    })

    it('specific: single on-the-hour time', () => {
        expectRoundTrip(specificInput({ specificTimes: ['08:00'] }))
    })

    it('specific: multiple on-the-hour times', () => {
        expectRoundTrip(
            specificInput({ specificTimes: ['06:00', '12:00', '18:00'] })
        )
    })

    it('specific: mixed minutes (triggers multi-line cron)', () => {
        expectRoundTrip(
            specificInput({
                specificTimes: ['08:00', '10:30', '12:00', '14:30'],
            })
        )
    })

    it('specific: weekend schedule', () => {
        expectRoundTrip(
            specificInput({
                daysOfWeek: WEEKEND,
                specificTimes: ['07:00', '19:00'],
            })
        )
    })

    it('specific: all TIME_SLOTS', () => {
        expectRoundTrip(
            specificInput({
                specificTimes: TIME_SLOTS.map((s) => s.value),
            })
        )
    })

    it('specific: times at :15 and :45', () => {
        expectRoundTrip(
            specificInput({
                specificTimes: ['06:15', '12:15', '08:45', '14:45'],
            })
        )
    })

    it('interval: 9am–5pm 60 min weekdays', () => {
        expectRoundTrip(
            intervalInput({
                startTime: '09:00',
                endTime: '17:00',
                intervalMinutes: 60,
            })
        )
    })

    // ── Property-style sweeps over representative calendars ──

    const DAY_SUBSETS: readonly (readonly number[])[] = [
        [1, 2, 3, 4, 5], // weekdays
        [0, 6], // weekends
        [1, 3, 5], // Mon/Wed/Fri
        [2], // single day
        ALL_DAYS,
    ]

    describe('specific-times mode round-trips for every weekday subset', () => {
        it.each(DAY_SUBSETS.map((d) => [d.join(','), d] as const))(
            'single time on days %s',
            (_label, daysOfWeek) => {
                expectRoundTrip(
                    specificInput({
                        daysOfWeek: [...daysOfWeek],
                        specificTimes: ['07:30'],
                    })
                )
            }
        )

        it.each(DAY_SUBSETS.map((d) => [d.join(','), d] as const))(
            'multiple on-the-hour times on days %s',
            (_label, daysOfWeek) => {
                expectRoundTrip(
                    specificInput({
                        daysOfWeek: [...daysOfWeek],
                        specificTimes: ['06:00', '09:00', '17:00'],
                    })
                )
            }
        )
    })

    describe('interval mode round-trips across steps and windows', () => {
        // Sub-hourly steps produce the two-line compound cron; 60 produces
        // the single-line hourly form. Both must survive the round trip.
        const STEPS = [5, 10, 15, 20, 30, 60] as const
        const WINDOWS = [
            ['06:00', '18:00'],
            ['05:00', '23:00'],
            ['09:00', '10:00'], // adjacent hours
        ] as const

        it.each(
            STEPS.flatMap((step) =>
                WINDOWS.map(
                    ([startTime, endTime]) =>
                        [step, startTime, endTime] as const
                )
            )
        )('every %d min, %s–%s on weekdays', (step, startTime, endTime) => {
            expectRoundTrip(
                intervalInput({
                    startTime,
                    endTime,
                    intervalMinutes: step,
                })
            )
        })

        it.each(DAY_SUBSETS.map((d) => [d.join(','), d] as const))(
            'sub-hourly compound form on days %s',
            (_label, daysOfWeek) => {
                expectRoundTrip(
                    intervalInput({
                        daysOfWeek: [...daysOfWeek],
                        intervalMinutes: 15,
                    })
                )
            }
        )
    })
})

// ─── detectCronLoss ───────────────────────────────────────────────────────────

describe('detectCronLoss', () => {
    // Dev-stored NCRONTAB (6 fields, seconds first) for a day-specific
    // compound: distinct weekday times on 1-5, different times on Sat (6) and
    // Sun (0). Same shape as adelaide's 45-line / brisbane's 44-line /
    // perth's 43-line expressions on dev.
    const DAY_SPECIFIC_COMPOUND = [
        '0 59 5 * * 1-5',
        '0 59 6 * * 1-5',
        '0 59 8 * * 1-5',
        '0 29 9 * * 1-5',
        '0 45 9 * * 1-5',
        '0 58 6 * * 6',
        '0 58 8 * * 6',
        '0 30 9 * * 6',
        '0 46 9 * * 0',
    ].join('\n')

    it('reports loss for a day-specific compound cron (weekday vs weekend times)', () => {
        const report = detectCronLoss(DAY_SPECIFIC_COMPOUND)

        expect(report.lossy).toBe(true)
        // The summary states what the stored pattern runs per day and what the
        // flattened rewrite would run, plus the irreversible consequence.
        expect(report.summary).toContain('weekdays 5 times')
        expect(report.summary).toContain('Saturdays 3 times')
        expect(report.summary).toContain('Sundays 1 time')
        expect(report.summary).toContain('every selected day 9 times')
        expect(report.summary).toContain('cannot be undone')
    })

    it('reports no loss for a simple expression (a few times, all days)', () => {
        const report = detectCronLoss('0 7,8,9 * * 0,1,2,3,4,5,6')

        expect(report.lossy).toBe(false)
        expect(report.summary).toBe('')
    })

    it('reports no loss for a multi-minute expression with a single day group', () => {
        const report = detectCronLoss('0 7,8 * * 1-5\n30 9 * * 1-5')

        expect(report.lossy).toBe(false)
        expect(report.summary).toBe('')
    })

    it('reports no loss for the interval compound form', () => {
        const report = detectCronLoss('0,15,30,45 6-17 * * 1-5\n0 18 * * 1-5')

        expect(report.lossy).toBe(false)
        expect(report.summary).toBe('')
    })

    it('reports no loss for an empty or null cron', () => {
        expect(detectCronLoss(null).lossy).toBe(false)
        expect(detectCronLoss('').lossy).toBe(false)
    })

    it('reports no loss when the same times repeat across per-day lines', () => {
        // One time split across one line per day is representable: the editor
        // merges it into a single day list without changing run slots.
        const report = detectCronLoss('0 7 * * 1\n0 7 * * 2\n0 7 * * 3')

        expect(report.lossy).toBe(false)
        expect(report.summary).toBe('')
    })
})
