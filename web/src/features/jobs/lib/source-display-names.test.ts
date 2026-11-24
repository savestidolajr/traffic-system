import { describe, expect, it } from 'vitest'
import { sourceDisplayName } from './source-display-names'

describe('sourceDisplayName', () => {
    it('keeps the operator labels used by evidence and diagnostics', () => {
        expect(sourceDisplayName('here')).toBe('HERE')
        expect(sourceDisplayName('qldtraffic')).toBe('QLDTraffic')
        expect(sourceDisplayName('here-weather')).toBe('HERE Weather')
        expect(sourceDisplayName('here-fuel')).toBe('HERE Fuel')
    })

    it('is case insensitive and preserves unknown ids', () => {
        expect(sourceDisplayName('QLDTRAFFIC')).toBe('QLDTraffic')
        expect(sourceDisplayName('new-feed')).toBe('new-feed')
    })
})
