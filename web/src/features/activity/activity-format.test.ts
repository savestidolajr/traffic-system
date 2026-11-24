// =============================================================================
// Activity Action Formatter Tests
// =============================================================================

import { describe, it, expect } from 'vitest'
import { formatActivityAction } from './activity-format'

const entry = (
    method: string,
    resource: string,
    entityId: string | null = null
) => ({ method, resource, entityId })

describe('formatActivityAction', () => {
    it('maps PUT to Updated with the friendly noun', () => {
        expect(formatActivityAction(entry('PUT', 'schedules', 'ade-1'))).toBe(
            'Updated schedule \u00b7 ade-1'
        )
    })

    it('keeps short entity ids intact', () => {
        expect(
            formatActivityAction(entry('PUT', 'priority-roads', 'syd-1'))
        ).toBe('Updated priority roads \u00b7 syd-1')
    })

    it('truncates long GUID-like entity ids', () => {
        expect(
            formatActivityAction(
                entry(
                    'PUT',
                    'voices/profiles',
                    'vp-2fbc21d3f2b9443c88812fd8f091bc47'
                )
            )
        ).toBe('Updated voice profile \u00b7 vp-2fb...bc47')
    })

    it('maps POST to Created', () => {
        expect(formatActivityAction(entry('POST', 'voices/profiles'))).toBe(
            'Created voice profile'
        )
    })

    it('renders POST broadcast as Requested bulletin', () => {
        expect(formatActivityAction(entry('POST', 'broadcast'))).toBe(
            'Requested bulletin'
        )
    })

    it('renders job creation as Created bulletin', () => {
        expect(
            formatActivityAction(entry('POST', 'jobs', 'arn-job-00007'))
        ).toBe('Created bulletin · arn-job-00007')
    })

    it('maps DELETE to Deleted', () => {
        expect(
            formatActivityAction(entry('DELETE', 'safety-terms', 'st-global-3'))
        ).toBe('Deleted banned words \u00b7 st-global-3')
    })

    it('falls back to the raw resource segment for unknown resources', () => {
        expect(
            formatActivityAction(entry('PUT', 'operators', 'aad-operator-001'))
        ).toBe('Updated operators · aad-op...-001')
    })

    it('falls back to the raw verb for unknown methods', () => {
        expect(formatActivityAction(entry('PATCH', 'guidance', 'syd-1'))).toBe(
            'PATCH guidance \u00b7 syd-1'
        )
    })

    it('omits the suffix when there is no entity id', () => {
        expect(formatActivityAction(entry('POST', 'config'))).toBe(
            'Created config'
        )
    })
})
