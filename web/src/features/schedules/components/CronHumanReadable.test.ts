import { describe, expect, it } from 'vitest'
import cronstrue from 'cronstrue'
import { abbreviate } from '@/features/schedules/lib/cron-format'

const opts = { use24HourTimeFormat: true } as const

/**
 * abbreviate() rewrites cronstrue's English with regexes ("At 0, 15, 30, and
 * 45 minutes past the hour" → "Every 15 min"). These tests pin the exact
 * cronstrue phrasing so a cronstrue upgrade that changes its wording breaks
 * loudly here instead of silently un-abbreviating every card row.
 */
describe('cronstrue output format pin', () => {
    it('phrases evenly-stepped minute lists the way abbreviate() expects', () => {
        expect(cronstrue.toString('0,15,30,45 6-17 * * 1,2,3,4,5', opts)).toBe(
            'At 0, 15, 30, and 45 minutes past the hour, between 06:00 and 17:59, only on Monday, Tuesday, Wednesday, Thursday, and Friday'
        )
        expect(cronstrue.toString('0,30 5-22 * * 1,2,3,4,5', opts)).toBe(
            'At 0 and 30 minutes past the hour, between 05:00 and 22:59, only on Monday, Tuesday, Wednesday, Thursday, and Friday'
        )
    })

    it('phrases specific times and hourly intervals the way abbreviate() expects', () => {
        expect(cronstrue.toString('0 6,12,18 * * 1,2,3,4,5', opts)).toBe(
            'At 06:00, 12:00 and 18:00, only on Monday, Tuesday, Wednesday, Thursday, and Friday'
        )
        expect(cronstrue.toString('30 8,14 * * 0,6', opts)).toBe(
            'At 08:30 and 14:30, only on Sunday and Saturday'
        )
        expect(cronstrue.toString('0 9-17 * * 1,2,3,4,5', opts)).toBe(
            'Every hour, between 09:00 and 17:00, only on Monday, Tuesday, Wednesday, Thursday, and Friday'
        )
        expect(cronstrue.toString('0 18 * * 1,2,3,4,5', opts)).toBe(
            'At 18:00, only on Monday, Tuesday, Wednesday, Thursday, and Friday'
        )
    })
})

describe('abbreviate', () => {
    it('collapses evenly-stepped minute lists into "Every N min"', () => {
        expect(
            abbreviate(
                cronstrue.toString('0,15,30,45 6-17 * * 1,2,3,4,5', opts)
            )
        ).toBe(
            'Every 15 min, between 06:00 and 17:59, only on Mon, Tue, Wed, Thu, Fri'
        )
    })

    it('shortens two-minute lists without inventing an interval', () => {
        // "0 and 30" has no comma list, so the interval collapse cannot
        // apply — the phrase is shortened instead
        expect(
            abbreviate(cronstrue.toString('0,30 5-22 * * 1,2,3,4,5', opts))
        ).toBe(
            '0 and 30 min past, between 05:00 and 22:59, only on Mon, Tue, Wed, Thu, Fri'
        )
    })

    it('leaves unevenly-spaced minute lists verbatim (just shortened)', () => {
        expect(abbreviate('At 0, 10, 45 minutes past the hour')).toBe(
            '0, 10, 45 min past'
        )
    })

    it('shortens day names and specific-time phrases', () => {
        expect(abbreviate(cronstrue.toString('30 8,14 * * 0,6', opts))).toBe(
            '08:30 and 14:30, only on Sun and Sat'
        )
        expect(
            abbreviate(cronstrue.toString('0 6,12,18 * * 1,2,3,4,5', opts))
        ).toBe('06:00, 12:00 and 18:00, only on Mon, Tue, Wed, Thu, Fri')
    })
})
