import { describe, it, expect } from 'vitest'
import {
    BookendApiSchema,
    BookendsListResponseSchema,
    BookendPreviewResponseSchema,
} from './bookend-schemas'

/** A minimal, valid PascalCase bookend pair as the backend serializes it. */
const BOOKEND = {
    Id: 'bk-abc123',
    Name: 'Morning drive pair',
    Description: 'Warm opening and close for the morning drive bulletins.',
    Status: 'active',
    IntroText: 'Looking over {city} today, here is your traffic.',
    OutroText: 'Thanks for joining us on {station}, safe travels.',
    CurrentVersion: 2,
    CreatedAt: '2026-06-22T00:00:00Z',
    UpdatedAt: '2026-06-25T00:00:00Z',
    CreatedBy: 'avery.admin@mock.arn.com.au',
    AssignedStations: ['KIIS 1065'],
    Tokens: ['city', 'station'],
    Versions: [
        {
            Version: 1,
            IntroText: 'Old opening.',
            OutroText: 'Old close.',
            CreatedAt: '2026-06-01T00:00:00Z',
        },
        {
            Version: 2,
            IntroText: 'Looking over {city} today, here is your traffic.',
            OutroText: 'Thanks for joining us on {station}, safe travels.',
            ChangeNote: 'Added the city token.',
            CreatedAt: '2026-06-22T00:00:00Z',
        },
    ],
}

describe('BookendsListResponseSchema', () => {
    it('parses a normal PascalCase list response', () => {
        const r = BookendsListResponseSchema.parse({
            Items: [BOOKEND],
            Total: 1,
        })
        expect(r.total).toBe(1)
        expect(r.bookends).toHaveLength(1)
        expect(r.bookends[0].name).toBe('Morning drive pair')
        expect(r.bookends[0].introText).toContain('{city}')
    })

    it('degrades a missing/empty envelope to an empty library (no throw)', () => {
        // Regression: a body without Items/Total used to throw a Zod parse
        // error and break the gallery page. It must now yield an empty list.
        expect(BookendsListResponseSchema.parse({})).toEqual({
            bookends: [],
            total: 0,
        })
    })
})

describe('BookendApiSchema', () => {
    it('camelizes the wire DTO and defaults missing optionals', () => {
        const b = BookendApiSchema.parse(BOOKEND)
        expect(b.currentVersion).toBe(2)
        expect(b.assignedStations).toEqual(['KIIS 1065'])
        expect(b.tokens).toEqual(['city', 'station'])
        expect(b.versions).toHaveLength(2)
        expect(b.versions[0].changeNote).toBeNull()
        expect(b.versions[1].changeNote).toBe('Added the city token.')
        expect(b.createdBy).toBe('avery.admin@mock.arn.com.au')
    })

    it('defaults a legacy payload missing Description/Status/AssignedStations', () => {
        const b = BookendApiSchema.parse({
            Id: 'bk-1',
            Name: 'Sign-off pair',
            IntroText: '',
            OutroText: 'Safe travels on {station}.',
            CurrentVersion: 1,
            CreatedAt: '2026-06-22T00:00:00Z',
            UpdatedAt: '2026-06-22T00:00:00Z',
            Versions: [
                {
                    Version: 1,
                    IntroText: '',
                    OutroText: 'Safe travels on {station}.',
                    CreatedAt: '2026-06-22T00:00:00Z',
                },
            ],
        })
        expect(b.description).toBeNull()
        expect(b.status).toBe('active')
        expect(b.assignedStations).toEqual([])
        expect(b.tokens).toEqual([])
    })

    it('carries an empty side of a pair (intro-only)', () => {
        const b = BookendApiSchema.parse({
            Id: 'bk-intro-only',
            Name: 'Intro only',
            IntroText: 'Looking over {city} today.',
            OutroText: '',
            CurrentVersion: 1,
            CreatedAt: '2026-06-22T00:00:00Z',
            UpdatedAt: '2026-06-22T00:00:00Z',
            Versions: [
                {
                    Version: 1,
                    IntroText: 'Looking over {city} today.',
                    OutroText: '',
                    CreatedAt: '2026-06-22T00:00:00Z',
                },
            ],
        })
        expect(b.introText).toContain('{city}')
        expect(b.outroText).toBe('')
    })
})

describe('BookendPreviewResponseSchema', () => {
    it('parses a two-line preview response and defaults missing lists', () => {
        const p = BookendPreviewResponseSchema.parse({
            ResolvedIntro: 'Looking over Sydney today, here is your traffic.',
            ResolvedOutro: 'Thanks for joining us on KIIS 1065, safe travels.',
            UnresolvedTokens: [],
            Values: { city: 'Sydney', market: 'sydney' },
        })
        expect(p.resolvedIntro).toContain('Sydney')
        expect(p.resolvedOutro).toContain('KIIS 1065')
        expect(p.unresolvedTokens).toEqual([])
        expect(p.values).toEqual({ city: 'Sydney', market: 'sydney' })
    })

    it('defaults a legacy single-line preview response', () => {
        // A payload missing the second line degrades instead of throwing.
        const p = BookendPreviewResponseSchema.parse({
            ResolvedIntro: 'Looking over Sydney today.',
        })
        expect(p.resolvedIntro).toContain('Sydney')
        expect(p.resolvedOutro).toBe('')
        expect(p.unresolvedTokens).toEqual([])
        expect(p.values).toEqual({})
    })
})
