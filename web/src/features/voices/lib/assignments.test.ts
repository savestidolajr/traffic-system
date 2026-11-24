import { describe, expect, it } from 'vitest'
import { collectVoiceAssignments, scheduleVoiceIds } from './assignments'
import type { MarketSchedule } from '@/features/schedules/types'

/**
 * Regression for bug-bash-2026-08-05 finding 8: the voices gallery reported
 * every profile "Unassigned" because it trusted the wire DTO's
 * `assignedStations` (always [] — nothing populates it after Create). The
 * counts must instead come from the schedules list, including voice-rotation
 * members. If this derivation breaks, the gallery silently claims live voices
 * are unused again.
 */

function schedule(
    marketId: string,
    voice: {
        voiceProfileId?: string | null
        rotation?: MarketSchedule['voiceRotation']
    } = {}
): MarketSchedule {
    return {
        marketId,
        name: null,
        station: null,
        timezone: null,
        cronExpression: null,
        enabled: true,
        targetDurationSeconds: 30,
        notifyBeforePlayoutMinutes: 15,
        escalationBeforePlayoutMinutes: 10,
        voiceProfileId: voice.voiceProfileId ?? null,
        deliveryStyleId: null,
        pronunciationDictionaryId: null,
        bookendId: null,
        nextRunAt: null,
        currentJobId: null,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        coverage: {
            latitude: -33.8,
            longitude: 151.2,
            radiusKm: 10,
            shapeType: 'circle',
        },
        content: null,
        voiceRotation: voice.rotation ?? null,
        bookendRotation: null,
        bookendTokens: null,
        fillOrder: null,
    }
}

describe('scheduleVoiceIds', () => {
    it('collects the single voice', () => {
        const ids = scheduleVoiceIds(
            schedule('sydney', { voiceProfileId: 'vp-a' })
        )
        expect(ids).toEqual(['vp-a'])
    })

    it('collects round-robin members', () => {
        const ids = scheduleVoiceIds(
            schedule('melbourne', {
                rotation: {
                    mode: 'roundRobin',
                    voiceProfileIds: ['vp-a', 'vp-b'],
                    windows: [],
                },
            })
        )
        expect(ids.sort()).toEqual(['vp-a', 'vp-b'])
    })

    it('collects time-of-day window voices', () => {
        const ids = scheduleVoiceIds(
            schedule('perth', {
                rotation: {
                    mode: 'timeOfDay',
                    voiceProfileIds: [],
                    windows: [
                        {
                            start: '06:00',
                            end: '12:00',
                            voiceProfileId: 'vp-a',
                        },
                        {
                            start: '12:00',
                            end: '18:00',
                            voiceProfileId: 'vp-b',
                        },
                    ],
                },
            })
        )
        expect(ids.sort()).toEqual(['vp-a', 'vp-b'])
    })

    it('returns nothing for a schedule with no voice', () => {
        expect(scheduleVoiceIds(schedule('perth'))).toEqual([])
    })
})

describe('collectVoiceAssignments', () => {
    it('attributes a profile to every station airing with it', () => {
        const map = collectVoiceAssignments([
            schedule('sydney', { voiceProfileId: 'vp-a' }),
            schedule('melbourne', { voiceProfileId: 'vp-a' }),
            schedule('perth', { voiceProfileId: 'vp-b' }),
        ])
        expect(map.get('vp-a')?.sort()).toEqual(['melbourne', 'sydney'])
        expect(map.get('vp-b')).toEqual(['perth'])
        // A profile no schedule references stays unassigned
        expect(map.get('vp-z')).toBeUndefined()
    })

    it('labels stations by station name, falling back to name then market id', () => {
        const map = collectVoiceAssignments([
            {
                ...schedule('adelaide', { voiceProfileId: 'vp-a' }),
                station: 'KIIS 102.3',
                name: 'KIIS 102.3 Adelaide',
            },
            {
                ...schedule('brisbane', { voiceProfileId: 'vp-a' }),
                station: null,
                name: 'KIIS 97.3 Brisbane',
            },
            {
                ...schedule('darwin', { voiceProfileId: 'vp-a' }),
                station: null,
                name: null,
            },
        ])
        expect(map.get('vp-a')?.sort()).toEqual([
            'KIIS 102.3',
            'KIIS 97.3 Brisbane',
            'darwin',
        ])
    })

    it('counts rotation members as assigned stations', () => {
        const map = collectVoiceAssignments([
            schedule('cairns', {
                rotation: {
                    mode: 'roundRobin',
                    voiceProfileIds: ['vp-a', 'vp-b'],
                    windows: [],
                },
            }),
        ])
        expect(map.get('vp-a')).toEqual(['cairns'])
        expect(map.get('vp-b')).toEqual(['cairns'])
    })

    it('does not double-count one station on one profile', () => {
        const map = collectVoiceAssignments([
            schedule('goldcoast', { voiceProfileId: 'vp-a' }),
        ])
        expect(map.get('vp-a')).toEqual(['goldcoast'])
    })
})
