import { describe, expect, it } from 'vitest'
import type { EmergencyEvent } from '../types'
import {
    emergencyCategoryLabel,
    EMERGENCY_TIER_LABEL,
    mustAirCount,
    sortEmergencyEvents,
    suppressionSummary,
} from './emergency'

const event = (
    id: string,
    overrides: Partial<EmergencyEvent> = {}
): EmergencyEvent => ({
    id,
    sourceFeedId: 'qld-escad',
    agency: 'QFD',
    state: 'qld',
    title: 'Incident',
    warningLevel: 'None',
    eventType: 'other',
    eventCategory: 'other',
    licence: 'CC BY 4.0',
    airEligible: false,
    ...overrides,
})

describe('emergency helpers', () => {
    it('labels every warning tier for operators', () => {
        expect(EMERGENCY_TIER_LABEL).toEqual({
            None: 'Monitoring',
            Advice: 'Advice',
            WatchAndAct: 'Watch and Act',
            EmergencyWarning: 'Emergency Warning',
        })
    })

    it('orders by tier, eligibility, distance, then stable id', () => {
        const events = [
            event('none-far', { distanceKm: 1 }),
            event('warning', {
                warningLevel: 'EmergencyWarning',
                airEligible: true,
                distanceKm: 40,
            }),
            event('advice-ineligible', {
                warningLevel: 'Advice',
                distanceKm: 1,
            }),
            event('advice-eligible-far', {
                warningLevel: 'Advice',
                airEligible: true,
                distanceKm: 20,
            }),
            event('advice-eligible-near', {
                warningLevel: 'Advice',
                airEligible: true,
                distanceKm: 2,
            }),
            event('none-near', { distanceKm: 0.5 }),
        ]

        expect(sortEmergencyEvents(events).map((item) => item.id)).toEqual([
            'warning',
            'advice-eligible-near',
            'advice-eligible-far',
            'advice-ineligible',
            'none-near',
            'none-far',
        ])
    })

    it('puts records without distance after located records and ties by id', () => {
        const events = [
            event('z', { distanceKm: null }),
            event('b', { distanceKm: 2 }),
            event('a', { distanceKm: 2 }),
        ]
        expect(sortEmergencyEvents(events).map((item) => item.id)).toEqual([
            'a',
            'b',
            'z',
        ])
    })

    it('uses the specified category vocabulary', () => {
        expect(emergencyCategoryLabel('fire')).toBe('Fire')
        expect(emergencyCategoryLabel('earth')).toBe('Earthquake')
        expect(emergencyCategoryLabel('planned')).toBe('Planned burn')
        expect(emergencyCategoryLabel('unknown-value')).toBe('Unclassified')
    })

    it('summarises only the categories suppressed by airability', () => {
        expect(
            suppressionSummary([
                event('fire', {
                    eventCategory: 'fire',
                    airEligible: true,
                }),
                event('medical', { eventCategory: 'medical' }),
                event('burn', { eventCategory: 'planned' }),
                event('other', { eventCategory: 'other' }),
            ])
        ).toBe('3 not airable (medical, planned burns, unclassified)')
        expect(
            suppressionSummary([event('eligible', { airEligible: true })])
        ).toBe(null)
    })

    it('counts every emergency warning tier', () => {
        expect(
            mustAirCount([
                event('none'),
                event('warning-a', { warningLevel: 'EmergencyWarning' }),
                event('warning-b', {
                    warningLevel: 'EmergencyWarning',
                    airEligible: true,
                }),
            ])
        ).toBe(2)
    })
})
