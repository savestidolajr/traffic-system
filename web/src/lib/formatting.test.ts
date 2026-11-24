// =============================================================================
// Formatting Utility Tests
// =============================================================================

import { describe, it, expect } from 'vitest'
import {
    formatClockTimeFromDate,
    formatDuration,
    formatRelativeTime,
    formatRunTime,
    formatShortTime,
    formatTookSec,
    parseTimestamp,
    truncateId,
} from './formatting'

describe('formatDuration', () => {
    it('formats mm:ss', () => {
        expect(formatDuration(125)).toBe('2:05')
    })

    it('drops the sign by default', () => {
        expect(formatDuration(-125)).toBe('2:05')
    })

    it('prefixes the sign when requested', () => {
        expect(formatDuration(-125, { sign: true })).toBe('-2:05')
    })

    it('shows hours only when enabled and >= 1h', () => {
        expect(formatDuration(3725)).toBe('62:05')
        expect(formatDuration(3725, { showHours: true })).toBe('1:02:05')
    })

    it('returns a dash for non-finite input instead of NaN:NaN', () => {
        // NaN reaches here via Math.round() on an unparsed timestamp or a
        // missing duration field (SystemAuditTab, PipelineStepper, the audio
        // player) — it must degrade like every malformed value, not air
        // "NaN:NaN" / "Infinity:NaN".
        expect(formatDuration(NaN)).toBe('-')
        expect(formatDuration(Number.POSITIVE_INFINITY)).toBe('-')
        expect(formatDuration(undefined as unknown as number)).toBe('-')
    })
})

describe('formatTookSec', () => {
    it('uses compact seconds under a minute', () => {
        expect(formatTookSec(42)).toBe('42s')
    })

    it('delegates to formatDuration from 60s', () => {
        expect(formatTookSec(60)).toBe('1:00')
    })
})

describe('formatClockTimeFromDate', () => {
    it('formats lowercase broadcast clock time', () => {
        const date = new Date(2026, 5, 12, 9, 5) // local 9:05
        expect(formatClockTimeFromDate(date)).toBe('9:05 am')
    })

    it('returns a dash for an invalid date instead of throwing', () => {
        // DecisionDock derives its deadline as
        // new Date(parseTimestamp(job.lastUpdatedAt) + …); a malformed backend
        // timestamp must not crash the whole review cockpit. date-fns format()
        // throws RangeError on Invalid Date, and every sibling formatter
        // degrades via safeFormat — this one must too.
        expect(formatClockTimeFromDate(new Date(NaN))).toBe('-')
    })
})

describe('formatShortTime', () => {
    it('formats 24-hour HH:mm', () => {
        const date = new Date(2026, 5, 12, 6, 30)
        expect(formatShortTime(date)).toMatch(/^06:30$/)
    })

    it('appends a timezone name when requested', () => {
        const date = new Date(2026, 5, 12, 6, 30)
        expect(formatShortTime(date, { tzName: true })).toMatch(/^06:30 \S+/)
    })
})

describe('parseTimestamp', () => {
    it('parses ISO strings to epoch ms', () => {
        expect(parseTimestamp('2026-06-12T00:00:00.000Z')).toBe(
            Date.UTC(2026, 5, 12)
        )
    })

    it('returns NaN for garbage (callers must guard)', () => {
        expect(Number.isNaN(parseTimestamp('not-a-date'))).toBe(true)
    })
})

describe('formatRelativeTime', () => {
    it('returns empty string on invalid input instead of throwing', () => {
        expect(formatRelativeTime('garbage')).toBe('')
    })
})

describe('formatRunTime', () => {
    it('returns a dash on an invalid timezone instead of throwing', () => {
        expect(formatRunTime(new Date(), 'Not/AZone')).toBe('-')
    })
})

describe('truncateId', () => {
    it('truncates long ids with an ellipsis', () => {
        expect(truncateId('abcdef0123456789')).toBe('abcdef...6789')
    })

    it('returns short ids unchanged', () => {
        expect(truncateId('short')).toBe('short')
    })
})
