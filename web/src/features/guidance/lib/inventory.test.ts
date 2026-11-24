import { describe, it, expect } from 'vitest'
import {
    applyGuidanceFilters,
    groupGuidance,
    hasActiveFilters,
    scopeKey,
    GLOBAL_GROUP_ID,
    EMPTY_FILTERS,
    type GuidanceFilters,
} from './inventory'
import { appliesToStation } from './scope-grouping'
import type { Guidance } from '../api/schemas'

const g = (over: Partial<Guidance>): Guidance => ({
    id: 'id',
    scope: 'Global',
    scopeId: '',
    type: 'Style',
    text: 'x',
    enabled: true,
    createdBy: 'u',
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    source: null,
    reviewAfterUtc: null,
    ...over,
})

const filters = (over: Partial<GuidanceFilters>): GuidanceFilters => ({
    ...EMPTY_FILTERS,
    ...over,
})

const labelFor = (id: string) =>
    ({ '2gb': '2GB · Sydney', '3aw': '3AW · Melbourne' })[id] ?? id

const a = g({
    id: 'a',
    type: 'Style',
    text: 'Keep it short',
    updatedAtUtc: '2026-01-03T00:00:00Z',
})
const b = g({
    id: 'b',
    type: 'Prohibition',
    text: 'Avoid gridlock',
    updatedAtUtc: '2026-01-05T00:00:00Z',
})
const c = g({
    id: 'c',
    scope: 'Market',
    scopeId: '2gb',
    type: 'Lexicon',
    text: 'Call the M1 the Monash',
    updatedAtUtc: '2026-01-02T00:00:00Z',
})
const d = g({
    id: 'd',
    scope: 'Market',
    scopeId: '3aw',
    type: 'Editorial',
    text: 'Lead with the worst',
    enabled: false,
    updatedAtUtc: '2026-01-01T00:00:00Z',
})
const all = [a, b, c, d]

describe('scopeKey', () => {
    it('collapses global rows onto the sentinel and keeps market ids', () => {
        expect(scopeKey(a)).toBe(GLOBAL_GROUP_ID)
        expect(scopeKey(c)).toBe('2gb')
    })
})

describe('hasActiveFilters', () => {
    it('is false only for the empty filter set', () => {
        expect(hasActiveFilters(EMPTY_FILTERS)).toBe(false)
        expect(hasActiveFilters(filters({ search: 'x' }))).toBe(true)
        expect(hasActiveFilters(filters({ type: 'Lexicon' }))).toBe(true)
    })
})

describe('applyGuidanceFilters', () => {
    it('matches text case-insensitively', () => {
        expect(
            applyGuidanceFilters(all, filters({ search: 'GRIDLOCK' }))
        ).toEqual([b])
    })
    it('filters by type', () => {
        expect(applyGuidanceFilters(all, filters({ type: 'Lexicon' }))).toEqual(
            [c]
        )
    })
    it('leaves scope alone — the station picker owns that axis', () => {
        expect(applyGuidanceFilters(all, EMPTY_FILTERS)).toEqual(all)
    })
})

describe('appliesToStation', () => {
    it('keeps a selected market AND every global row, because they compose', () => {
        expect(all.filter((x) => appliesToStation(x, '2gb'))).toEqual([a, b, c])
    })
    it('drops other markets', () => {
        expect(all.filter((x) => appliesToStation(x, '3aw'))).toEqual([a, b, d])
    })
})

describe('groupGuidance', () => {
    const groups = groupGuidance(all, labelFor)

    it('orders Global first, then markets alphabetically by label', () => {
        expect(groups.map((x) => x.key)).toEqual([
            GLOBAL_GROUP_ID,
            '2gb',
            '3aw',
        ])
        expect(groups[0].label).toBe('Global · all markets')
        expect(groups[1].label).toBe('2GB · Sydney')
    })

    it('clusters rows by guidance type (composer/filter order), newest first within a type', () => {
        // a=Style (rank 0, 01-03), b=Prohibition (rank 3, 01-05) — despite b
        // being newer, type order wins so Style sorts ahead of Prohibition.
        expect(groups[0].items).toHaveLength(2)
        expect(groups[0].items.map((x) => x.id)).toEqual(['a', 'b'])
    })

    it('breaks ties within the same type by recency', () => {
        const sameType = groupGuidance(
            [
                g({
                    id: 'old',
                    type: 'Style',
                    updatedAtUtc: '2026-01-01T00:00:00Z',
                }),
                g({
                    id: 'new',
                    type: 'Style',
                    updatedAtUtc: '2026-01-02T00:00:00Z',
                }),
            ],
            labelFor
        )
        expect(sameType[0].items.map((x) => x.id)).toEqual(['new', 'old'])
    })

    it('tags group scope correctly', () => {
        expect(groups[0].scope).toBe('Global')
        expect(groups[1].scope).toBe('Market')
        expect(groups[1].scopeId).toBe('2gb')
    })
})
