import { describe, it, expect } from 'vitest'
import { incidentFactLine, incidentSourceTag } from './incident-labels'

describe('incidentFactLine', () => {
    it('returns null when all fields are absent', () => {
        expect(incidentFactLine({})).toBeNull()
    })

    it('returns null when all fields are null or undefined', () => {
        expect(
            incidentFactLine({
                direction: null,
                laneDetail: null,
                comment: null,
            })
        ).toBeNull()
    })

    it('returns null when fields are whitespace-only strings', () => {
        expect(
            incidentFactLine({
                direction: '   ',
                laneDetail: '  ',
                comment: ' \t ',
            })
        ).toBeNull()
    })

    it('returns direction when only direction is set', () => {
        expect(incidentFactLine({ direction: 'Southbound' })).toBe('Southbound')
    })

    it('returns laneDetail when only laneDetail is set', () => {
        expect(incidentFactLine({ laneDetail: 'Two lanes blocked' })).toBe(
            'Two lanes blocked'
        )
    })

    it('returns comment as fallback when direction and laneDetail are absent', () => {
        expect(incidentFactLine({ comment: 'Sewer connection' })).toBe(
            'Sewer connection'
        )
    })

    it('joins direction and laneDetail when both are present', () => {
        expect(
            incidentFactLine({
                direction: 'Northbound',
                laneDetail: 'Right lane closed',
            })
        ).toBe('Northbound · Right lane closed')
    })

    it('uses laneDetail over comment when both are present (comment is dropped)', () => {
        expect(
            incidentFactLine({
                direction: 'Eastbound',
                laneDetail: 'Lane closed',
                comment: 'Road works',
            })
        ).toBe('Eastbound · Lane closed')
    })

    it('returns null when direction is absent and laneDetail/comment are empty strings', () => {
        expect(
            incidentFactLine({
                direction: null,
                laneDetail: '',
                comment: '',
            })
        ).toBeNull()
    })
})

describe('incidentFactLine window and detail composition', () => {
    it('leads with the detail and appends the active window', () => {
        expect(
            incidentFactLine({
                detail: 'At Foster Road - Road construction',
                direction: 'Citybound',
                endTimeUtc: '2099-08-22T06:31:00Z',
            })
        ).toBe(
            `At Foster Road - Road construction · Citybound · until ${new Date('2099-08-22T06:31:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}`
        )
    })

    it('shows a from-date only while the start is still in the future', () => {
        expect(
            incidentFactLine({ startTimeUtc: '2000-01-01T00:00:00Z' })
        ).toBeNull()
        expect(
            incidentFactLine({ startTimeUtc: '2099-01-01T00:00:00Z' })
        ).toMatch(/^from /)
    })
})

describe('incidentSourceTag', () => {
    const names: Record<string, string> = {
        here: 'HERE',
        'livetraffic-nsw': 'Live Traffic NSW',
    }
    const display = (id: string) => names[id] ?? id

    it('is silent for plain HERE-only rows', () => {
        expect(incidentSourceTag(['here'], display)).toBeNull()
        expect(incidentSourceTag(null, display)).toBeNull()
        expect(incidentSourceTag([], display)).toBeNull()
    })

    it('names every contributing feed when an authority contributed', () => {
        expect(incidentSourceTag(['livetraffic-nsw'], display)).toBe(
            'Live Traffic NSW'
        )
        expect(incidentSourceTag(['here', 'livetraffic-nsw'], display)).toBe(
            'HERE + Live Traffic NSW'
        )
    })
})
