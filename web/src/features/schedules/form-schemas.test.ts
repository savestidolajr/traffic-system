import { describe, it, expect } from 'vitest'
import { ScheduleFormSchema, voiceWindowsOverlap } from './form-schemas'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'
import type { ScheduleFormData } from './types'

function baseForm(overrides: Partial<ScheduleFormData> = {}): ScheduleFormData {
    return {
        marketId: 'm',
        name: 'N',
        station: 'S',
        timezone: 'Australia/Sydney',
        enabled: true,
        playoutEnabled: true,
        latitude: -33.8,
        longitude: 151.2,
        radiusKm: 10,
        targetDurationSeconds: 30,
        notifyBeforePlayoutMinutes: 15,
        escalationBeforePlayoutMinutes: 10,
        voiceProfileId: 'vp-1',
        deliveryStyleId: '',
        pronunciationDictionaryId: '',
        bookendId: '',
        bookendRotationMode: 'single',
        bookendRotationIds: [],
        bookendWindows: [],
        bookendTokens: [],
        intervalMinutes: 15,
        daysOfWeek: [1],
        scheduleMode: 'specific',
        startTime: '06:00',
        endTime: '09:00',
        shapeType: 'circle',
        specificTimes: ['08:00'],
        calendar: null,
        content: { ...DEFAULT_CONTENT_SELECTION },
        fillOrder: ['weather', 'fuel', 'filler'],
        voiceRotationMode: 'single',
        voiceRotationIds: [],
        voiceWindows: [],
        ...overrides,
    }
}

describe('ScheduleFormSchema voice rotation', () => {
    it('accepts single mode', () => {
        expect(ScheduleFormSchema.safeParse(baseForm()).success).toBe(true)
    })

    it('accepts round-robin with a set', () => {
        const data = baseForm({
            voiceRotationMode: 'roundRobin',
            voiceRotationIds: ['vp-1', 'vp-2'],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects round-robin with no voices', () => {
        const data = baseForm({
            voiceRotationMode: 'roundRobin',
            voiceRotationIds: [],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects round-robin with a duplicate voice', () => {
        const data = baseForm({
            voiceRotationMode: 'roundRobin',
            voiceRotationIds: ['vp-1', 'vp-1'],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('accepts time-of-day with non-overlapping windows', () => {
        const data = baseForm({
            voiceRotationMode: 'timeOfDay',
            voiceWindows: [
                { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                { start: '10:00', end: '18:00', voiceProfileId: 'vp-2' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects time-of-day with no windows', () => {
        const data = baseForm({
            voiceRotationMode: 'timeOfDay',
            voiceWindows: [],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects time-of-day with overlapping windows', () => {
        const data = baseForm({
            voiceRotationMode: 'timeOfDay',
            voiceWindows: [
                { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                { start: '09:00', end: '12:00', voiceProfileId: 'vp-2' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects a window with no voice', () => {
        const data = baseForm({
            voiceRotationMode: 'timeOfDay',
            voiceWindows: [
                { start: '06:00', end: '10:00', voiceProfileId: '' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects a zero-length window', () => {
        const data = baseForm({
            voiceRotationMode: 'timeOfDay',
            voiceWindows: [
                { start: '08:00', end: '08:00', voiceProfileId: 'vp-1' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })
})

describe('ScheduleFormSchema source-specific coverage', () => {
    it('accepts independent circle overrides for weather and fuel', () => {
        const result = ScheduleFormSchema.safeParse(
            baseForm({
                weatherCoverage: {
                    latitude: -33.9,
                    longitude: 151.1,
                    radiusKm: 1,
                    shapeType: 'circle',
                },
                fuelCoverage: {
                    latitude: -33.95,
                    longitude: 151.05,
                    radiusKm: 8,
                    shapeType: 'circle',
                },
            })
        )

        expect(result.success).toBe(true)
    })

    it('rejects a non-circle source-specific override', () => {
        const result = ScheduleFormSchema.safeParse(
            baseForm({
                fuelCoverage: {
                    latitude: -33.9,
                    longitude: 151.1,
                    radiusKm: 8,
                    shapeType: 'bbox',
                    west: 151,
                    south: -34,
                    east: 151.2,
                    north: -33.8,
                },
            })
        )

        expect(result.success).toBe(false)
    })
})

describe('voiceWindowsOverlap', () => {
    it('detects an overlap', () => {
        expect(
            voiceWindowsOverlap([
                { start: '06:00', end: '10:00' },
                { start: '09:00', end: '12:00' },
            ])
        ).toBe(true)
    })

    it('treats adjacent windows as non-overlapping', () => {
        expect(
            voiceWindowsOverlap([
                { start: '06:00', end: '10:00' },
                { start: '10:00', end: '14:00' },
            ])
        ).toBe(false)
    })

    it('detects a midnight-wrap overlap', () => {
        expect(
            voiceWindowsOverlap([
                { start: '22:00', end: '06:00' },
                { start: '05:00', end: '07:00' },
            ])
        ).toBe(true)
    })
})

describe('ScheduleFormSchema bookend rotation', () => {
    it('accepts single mode with no bookends', () => {
        expect(ScheduleFormSchema.safeParse(baseForm()).success).toBe(true)
    })

    it('accepts a selected bookend pair', () => {
        const data = baseForm({ bookendId: 'bk-1' })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('accepts round-robin with a set', () => {
        const data = baseForm({
            bookendRotationMode: 'roundRobin',
            bookendRotationIds: ['bk-1', 'bk-2'],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects round-robin with no bookends', () => {
        const data = baseForm({
            bookendRotationMode: 'roundRobin',
            bookendRotationIds: [],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects round-robin with a duplicate bookend', () => {
        const data = baseForm({
            bookendRotationMode: 'roundRobin',
            bookendRotationIds: ['bk-1', 'bk-1'],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('accepts time-of-day with non-overlapping windows', () => {
        const data = baseForm({
            bookendRotationMode: 'timeOfDay',
            bookendWindows: [
                { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                { start: '10:00', end: '18:00', bookendId: 'bk-2' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects time-of-day with no windows', () => {
        const data = baseForm({
            bookendRotationMode: 'timeOfDay',
            bookendWindows: [],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects time-of-day with overlapping windows', () => {
        const data = baseForm({
            bookendRotationMode: 'timeOfDay',
            bookendWindows: [
                { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                { start: '09:00', end: '12:00', bookendId: 'bk-2' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects a window with no bookend', () => {
        const data = baseForm({
            bookendRotationMode: 'timeOfDay',
            bookendWindows: [{ start: '06:00', end: '10:00', bookendId: '' }],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })
})

describe('ScheduleFormSchema bookend tokens', () => {
    it('accepts valid override rows', () => {
        const data = baseForm({
            bookendTokens: [
                { key: 'city', value: 'Gold Coast' },
                { key: 'suburb', value: 'Southport' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('accepts blank rows (dropped by the payload builder)', () => {
        const data = baseForm({ bookendTokens: [{ key: '', value: '' }] })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects a duplicate token key case-insensitively', () => {
        const data = baseForm({
            bookendTokens: [
                { key: 'City', value: 'Gold Coast' },
                { key: 'city', value: 'Brisbane' },
            ],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects an invalid token key', () => {
        const data = baseForm({
            bookendTokens: [{ key: '1bad-key!', value: 'x' }],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects a non-empty key with an empty value', () => {
        const data = baseForm({
            bookendTokens: [{ key: 'city', value: '' }],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects an empty key with a non-empty value', () => {
        const data = baseForm({
            bookendTokens: [{ key: '', value: 'Gold Coast' }],
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })
})

// Regression for bug-bash-2026-08-05 finding 51: the client schema accepted
// values the backend rejects (duration 0-4s, names over 120 chars), so an
// admin passed inline validation and only hit the raw 400 at save. The client
// bounds must match the server's (5-600s, 120 chars, 2-minute cron floor).
describe('ScheduleFormSchema server parity', () => {
    it('accepts the server duration floor of 5s', () => {
        const data = baseForm({ targetDurationSeconds: 5 })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects durations below the server floor', () => {
        for (const seconds of [0, 1, 4]) {
            const data = baseForm({ targetDurationSeconds: seconds })
            expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
        }
    })

    it('accepts the server duration ceiling of 600s', () => {
        const data = baseForm({ targetDurationSeconds: 600 })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects durations above the server ceiling', () => {
        const data = baseForm({ targetDurationSeconds: 601 })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('rejects a name longer than the server cap of 120 chars', () => {
        const data = baseForm({ name: 'n'.repeat(121) })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('accepts a name exactly at the server cap of 120 chars', () => {
        const data = baseForm({ name: 'n'.repeat(120) })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects a station longer than the server cap of 120 chars', () => {
        const data = baseForm({ station: 's'.repeat(121) })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })

    it('accepts the server interval floor of 2 minutes', () => {
        const data = baseForm({
            scheduleMode: 'interval',
            intervalMinutes: 2,
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    // Superseded the former client minimum of 1: MarketSchedule validates these
    // alert offsets as non-negative values, so zero is a valid backend contract.
    it('accepts a zero escalation offset allowed by the server', () => {
        const data = baseForm({
            notifyBeforePlayoutMinutes: 1,
            escalationBeforePlayoutMinutes: 0,
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects an interval below the server floor', () => {
        const data = baseForm({
            scheduleMode: 'interval',
            intervalMinutes: 1,
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(false)
    })
})

// "Always include" implies selected, mirroring the backend's 400 on a PUT
// that flags a source it does not select. The UI clears the flag on deselect,
// so this guards imported/legacy payloads rather than the normal edit path.
describe('ScheduleFormSchema always include', () => {
    it('accepts a required source that is selected', () => {
        const data = baseForm({
            content: {
                traffic: true,
                weather: true,
                fuel: true,
                weatherRequired: true,
                fuelRequired: true,
            },
        })
        expect(ScheduleFormSchema.safeParse(data).success).toBe(true)
    })

    it('rejects weatherRequired without weather selected', () => {
        const result = ScheduleFormSchema.safeParse(
            baseForm({
                content: {
                    traffic: true,
                    weather: false,
                    fuel: false,
                    weatherRequired: true,
                    fuelRequired: false,
                },
            })
        )
        expect(result.success).toBe(false)
        if (!result.success) {
            const issue = result.error.issues.find(
                (i) => i.path.join('.') === 'content.weatherRequired'
            )
            expect(issue?.message).toBe(
                'Always include requires the source to be selected'
            )
        }
    })

    it('rejects fuelRequired without fuel selected', () => {
        const result = ScheduleFormSchema.safeParse(
            baseForm({
                content: {
                    traffic: true,
                    weather: false,
                    fuel: false,
                    weatherRequired: false,
                    fuelRequired: true,
                },
            })
        )
        expect(result.success).toBe(false)
        if (!result.success) {
            const issue = result.error.issues.find(
                (i) => i.path.join('.') === 'content.fuelRequired'
            )
            expect(issue?.message).toBe(
                'Always include requires the source to be selected'
            )
        }
    })
})
