import { describe, it, expect } from 'vitest'
import {
    GuidanceSchema,
    GuidanceListSchema,
    ComposedGuidanceSchema,
    GuidanceCandidateSchema,
} from './schemas'

const apiEntry = {
    Id: 'g-1',
    Scope: 'Market',
    ScopeId: 'syd-1',
    Type: 'Style',
    Text: 'Lean into the slang',
    Enabled: true,
    CreatedBy: 'producer.syd',
    CreatedAtUtc: '2026-06-17T00:00:00Z',
    UpdatedAtUtc: '2026-06-17T01:00:00Z',
    Source: 'manual',
    ReviewAfterUtc: null,
}

describe('GuidanceSchema', () => {
    it('camelizes PascalCase API keys', () => {
        const parsed = GuidanceSchema.parse(apiEntry)

        expect(parsed.id).toBe('g-1')
        expect(parsed.scope).toBe('Market')
        expect(parsed.scopeId).toBe('syd-1')
        expect(parsed.type).toBe('Style')
        expect(parsed.text).toBe('Lean into the slang')
        expect(parsed.enabled).toBe(true)
        expect(parsed.createdBy).toBe('producer.syd')
        expect(parsed.createdAtUtc).toBe('2026-06-17T00:00:00Z')
        expect(parsed.updatedAtUtc).toBe('2026-06-17T01:00:00Z')
        expect(parsed.source).toBe('manual')
        expect(parsed.reviewAfterUtc).toBeNull()
    })

    it('tolerates a missing optional source/review date', () => {
        const { Source, ReviewAfterUtc, ...rest } = apiEntry
        void Source
        void ReviewAfterUtc
        const parsed = GuidanceSchema.parse(rest)

        expect(parsed.source ?? null).toBeNull()
        expect(parsed.reviewAfterUtc ?? null).toBeNull()
    })

    it('parses a list', () => {
        expect(GuidanceListSchema.parse([apiEntry])).toHaveLength(1)
    })
})

describe('ComposedGuidanceSchema', () => {
    it('parses the inEffect/droppedCount preview shape', () => {
        const parsed = ComposedGuidanceSchema.parse({
            inEffect: [apiEntry],
            droppedCount: 2,
        })

        expect(parsed.droppedCount).toBe(2)
        expect(parsed.inEffect[0].scope).toBe('Market')
    })
})

describe('GuidanceCandidateSchema', () => {
    it('camelizes the future-lesson wire shape', () => {
        const parsed = GuidanceCandidateSchema.parse({
            Id: 'fl-1',
            Station: 'KIIS 101.1',
            JobId: 'arn-job-1',
            Category: 'WordingStyle',
            Text: 'Lead with the closure next time.',
            CreatedAtUtc: '2026-06-17T00:00:00Z',
            LastSeenAtUtc: '2026-06-18T00:00:00Z',
            OccurrenceCount: 2,
            ExpiresAtUtc: '2026-07-17T00:00:00Z',
        })

        expect(parsed.station).toBe('KIIS 101.1')
        expect(parsed.jobId).toBe('arn-job-1')
        expect(parsed.category).toBe('WordingStyle')
        expect(parsed.text).toBe('Lead with the closure next time.')
        expect(parsed.occurrenceCount).toBe(2)
        // Promoted is a client-side default until the wire carries it.
        expect(parsed.promoted).toBe(false)
    })
})
