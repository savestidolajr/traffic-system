import { describe, it, expect } from 'vitest'
import { DeliveryStylesListResponseSchema } from './delivery-schemas'

/** A minimal, valid PascalCase style as the backend serializes it. */
const STYLE = {
    Id: 's1',
    Name: 'Peak urgency',
    CurrentVersion: 1,
    CreatedAt: '2026-06-22T00:00:00Z',
    UpdatedAt: '2026-06-22T00:00:00Z',
    Versions: [
        {
            Version: 1,
            Policy: {
                ToneDescription: 'Controlled urgency.',
                AllowedTags: ['[urgent]', '[serious]'],
                Intensity: 2,
            },
            CreatedAt: '2026-06-22T00:00:00Z',
        },
    ],
}

describe('DeliveryStylesListResponseSchema', () => {
    it('parses a normal PascalCase list response', () => {
        const r = DeliveryStylesListResponseSchema.parse({
            Items: [STYLE],
            Total: 1,
        })
        expect(r.total).toBe(1)
        expect(r.styles).toHaveLength(1)
        expect(r.styles[0].name).toBe('Peak urgency')
    })

    it('degrades a missing/empty envelope to an empty library (no throw)', () => {
        // Regression: a body without Items/Total used to throw a Zod parse
        // error and break the Delivery page. It must now yield an empty list.
        expect(DeliveryStylesListResponseSchema.parse({})).toEqual({
            styles: [],
            total: 0,
        })
    })
})
