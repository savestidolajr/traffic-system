import { describe, it, expect } from 'vitest'
import { getNextCronOccurrences } from './cron-occurrences'

/**
 * Resolve date parts in a given timezone — mirrors the production code's approach
 * so assertions stay deterministic and don't hardcode absolute dates.
 */
function tzParts(date: Date, timezone: string) {
    const fmt = new Intl.DateTimeFormat('en-US', {
        timeZone: timezone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        weekday: 'short',
        hour12: false,
    })
    const raw: Record<string, string> = {}
    for (const p of fmt.formatToParts(date)) {
        if (p.type !== 'literal') raw[p.type] = p.value
    }
    return {
        year: parseInt(raw.year!, 10),
        month: parseInt(raw.month!, 10),
        day: parseInt(raw.day!, 10),
        hour: parseInt(raw.hour!, 10),
        minute: parseInt(raw.minute!, 10),
        weekday: raw.weekday!,
    }
}

const TZ = 'Australia/Sydney'
const FROM = new Date('2026-06-01T14:00:00Z') // a Monday in Sydney (June 1 2026 is a Monday AEST)

// ---------------------------------------------------------------------------
// Every-minute cron
// ---------------------------------------------------------------------------
describe('every-minute cron', () => {
    it('returns count dates each 60s apart, all after from', () => {
        const results = getNextCronOccurrences('* * * * *', TZ, 3, FROM)
        expect(results).toHaveLength(3)
        for (let i = 0; i < results.length; i++) {
            expect(results[i].getTime()).toBeGreaterThan(FROM.getTime())
        }
        expect(results[1].getTime() - results[0].getTime()).toBe(60_000)
        expect(results[2].getTime() - results[1].getTime()).toBe(60_000)
    })
})

// ---------------------------------------------------------------------------
// 6-field NCRONTAB (seconds first) — the backend's stored format
// ---------------------------------------------------------------------------
describe('6-field NCRONTAB cron', () => {
    it('drops the seconds field: 0 30 6-9 * * 1-5 runs at :30 past 6–9', () => {
        const results = getNextCronOccurrences('0 30 6-9 * * 1-5', TZ, 4, FROM)
        expect(results).toHaveLength(4)
        for (const d of results) {
            const p = tzParts(d, TZ)
            expect(p.minute).toBe(30)
            expect(p.hour).toBeGreaterThanOrEqual(6)
            expect(p.hour).toBeLessThanOrEqual(9)
            expect(['Mon', 'Tue', 'Wed', 'Thu', 'Fri']).toContain(p.weekday)
        }
    })
})

// ---------------------------------------------------------------------------
// Daily fixed time
// ---------------------------------------------------------------------------
describe('daily fixed time', () => {
    it('returns 3 occurrences at 06:00 Sydney time on consecutive days', () => {
        const results = getNextCronOccurrences('0 6 * * *', TZ, 3, FROM)
        expect(results).toHaveLength(3)

        const days = new Set<number>()
        for (const d of results) {
            const p = tzParts(d, TZ)
            // hour 6, minute 0 in Sydney
            expect(p.hour).toBe(6)
            expect(p.minute).toBe(0)
            days.add(p.day)
        }
        // three distinct days
        expect(days.size).toBe(3)
    })
})

// ---------------------------------------------------------------------------
// Weekday-only (the previously-broken case)
// ---------------------------------------------------------------------------
describe('weekday-only cron', () => {
    it('returns occurrences only on Monday–Friday', () => {
        // Request 5 occurrences, which should be 5 consecutive weekdays
        const results = getNextCronOccurrences('0 6 * * 1-5', TZ, 5, FROM)
        expect(results).toHaveLength(5)

        const weekdayMap: Record<string, number> = {
            sun: 0,
            mon: 1,
            tue: 2,
            wed: 3,
            thu: 4,
            fri: 5,
            sat: 6,
        }

        for (const d of results) {
            const p = tzParts(d, TZ)
            const dow = weekdayMap[p.weekday.toLowerCase()]
            expect(dow).not.toBe(-1) // must be a known weekday value
            // 0=Sun, 6=Sat — neither should appear for 1-5
            expect(dow).not.toBe(0)
            expect(dow).not.toBe(6)
        }
    })
})

// ---------------------------------------------------------------------------
// Day-of-month
// ---------------------------------------------------------------------------
describe('day-of-month cron', () => {
    it('returns occurrences on day-of-month === 1', () => {
        const results = getNextCronOccurrences('0 0 1 * *', TZ, 3, FROM)
        expect(results).toHaveLength(3)

        for (const d of results) {
            const p = tzParts(d, TZ)
            expect(p.day).toBe(1)
            expect(p.hour).toBe(0)
            expect(p.minute).toBe(0)
        }
    })
})

// ---------------------------------------------------------------------------
// Empty / invalid cron
// ---------------------------------------------------------------------------
describe('invalid cron expressions', () => {
    it('returns [] for empty string', () => {
        expect(getNextCronOccurrences('', TZ, 5, FROM)).toEqual([])
    })

    it('returns [] for garbage expression', () => {
        expect(getNextCronOccurrences('garbage', TZ, 5, FROM)).toEqual([])
    })

    it('returns [] fast for a never-satisfiable day (Feb 31), instead of scanning forever', () => {
        // Regression: this used to walk the calendar unboundedly (recursive
        // month scan) hunting a day that never exists — the same recursion the
        // e2e frozen-clock Date shim pushed into a review-page stack overflow.
        expect(getNextCronOccurrences('0 8 31 2 *', TZ, 3, FROM)).toEqual([])
    })

    it('still enumerates the rare-but-real Feb 29', () => {
        const results = getNextCronOccurrences('0 8 29 2 *', TZ, 1, FROM)
        expect(results).toHaveLength(1)
        const parts = new Intl.DateTimeFormat('en-AU', {
            timeZone: TZ,
            month: 'numeric',
            day: 'numeric',
        }).formatToParts(results[0])
        const get = (t: string) => parts.find((p) => p.type === t)?.value ?? ''
        expect(get('month')).toBe('2')
        expect(get('day')).toBe('29')
    })
})

// ---------------------------------------------------------------------------
// Multi-line OR'd cron
// ---------------------------------------------------------------------------
describe('multi-line cron', () => {
    it('alternates between 06:00 and 18:00', () => {
        const multi = '0 6 * * *\n0 18 * * *'
        const results = getNextCronOccurrences(multi, TZ, 4, FROM)
        expect(results).toHaveLength(4)

        const expectedHours = [6, 18, 6, 18]

        for (let i = 0; i < results.length; i++) {
            const p = tzParts(results[i], TZ)
            expect(p.hour).toBe(expectedHours[i])
            expect(p.minute).toBe(0)
        }
    })
})

describe('getNextCronOccurrences · DST fold', () => {
    /**
     * The cursor used to advance with `setMinutes(getMinutes() + 1)`, which is
     * wall-clock arithmetic in the PROCESS's zone rather than the `timezone`
     * argument. On the Australian April fallback the repeated 02:00-02:59 hour made
     * "+1 minute" land back inside the fold, so the cursor stopped advancing in real
     * time and enumeration spun until its step cap: an hourly Sydney clock reported
     * 1,500 occurrences on that one day instead of 25. This suite runs under
     * `TZ=Australia/Sydney`, so it reproduces the original conditions exactly.
     */
    it('advances through the fallback fold instead of spinning inside it', () => {
        const hits = getNextCronOccurrences(
            '0 * * * *',
            'Australia/Sydney',
            2000,
            new Date('2027-04-03T12:59:00Z'),
            new Date('2027-04-04T13:59:00Z')
        )

        // 2027-04-04 local runs 00:00 to 23:00 with 02:00 occurring twice.
        expect(hits).toHaveLength(25)
        // Strictly increasing instants: the fold cannot stall the cursor.
        for (let i = 1; i < hits.length; i++) {
            expect(hits[i]!.getTime()).toBeGreaterThan(hits[i - 1]!.getTime())
        }
    })
})
