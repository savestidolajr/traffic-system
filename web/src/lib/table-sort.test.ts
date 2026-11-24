// =============================================================================
// Shared table-sort tests
// =============================================================================

import { describe, it, expect } from 'vitest'
import {
    compareSortValues,
    cycleSort,
    sortRows,
    toggleSort,
    type SortColumns,
} from './table-sort'

interface Row {
    readonly name: string
    readonly count: number
    readonly owner: string | null
    readonly enabled: boolean
}

type Key = 'name' | 'count' | 'owner' | 'enabled'

const columns: SortColumns<Row, Key> = {
    name: { value: (row) => row.name, first: 'asc' },
    count: { value: (row) => row.count, first: 'desc' },
    owner: { value: (row) => row.owner, first: 'asc' },
    enabled: { value: (row) => row.enabled, first: 'desc' },
}

const rows: readonly Row[] = [
    { name: 'Market 10', count: 2, owner: null, enabled: false },
    { name: 'market 2', count: 30, owner: 'Zoe', enabled: true },
    { name: 'Alpha', count: 2, owner: 'Ada', enabled: false },
]

const names = (result: readonly Row[]) => result.map((row) => row.name)

describe('compareSortValues', () => {
    it('orders strings numerically so Market 2 precedes Market 10', () => {
        expect(compareSortValues('Market 2', 'Market 10', 'asc')).toBeLessThan(
            0
        )
    })

    it('ignores case when ordering strings', () => {
        expect(compareSortValues('alpha', 'Alpha', 'asc')).toBe(0)
    })

    it('orders false before true so desc puts "on" first', () => {
        expect(compareSortValues(false, true, 'asc')).toBeLessThan(0)
        expect(compareSortValues(false, true, 'desc')).toBeGreaterThan(0)
    })

    it('sorts empty values last in BOTH directions', () => {
        for (const direction of ['asc', 'desc'] as const) {
            expect(compareSortValues(null, 'Ada', direction)).toBeGreaterThan(0)
            expect(compareSortValues(undefined, 1, direction)).toBeGreaterThan(
                0
            )
            expect(compareSortValues('', 'Ada', direction)).toBeGreaterThan(0)
        }
    })

    it('treats zero as a real value, not as empty', () => {
        expect(compareSortValues(0, 5, 'asc')).toBeLessThan(0)
    })
})

describe('sortRows', () => {
    it('returns the source order untouched when nothing is sorted', () => {
        expect(sortRows(rows, null, columns)).toBe(rows)
    })

    it('sorts text ascending with natural number ordering', () => {
        expect(
            names(sortRows(rows, { key: 'name', direction: 'asc' }, columns))
        ).toEqual(['Alpha', 'market 2', 'Market 10'])
    })

    it('keeps source order for rows that tie', () => {
        // Both count-2 rows must stay in their original relative order.
        expect(
            names(sortRows(rows, { key: 'count', direction: 'desc' }, columns))
        ).toEqual(['market 2', 'Market 10', 'Alpha'])
    })

    it('keeps rows with no value at the bottom when the direction flips', () => {
        expect(
            names(sortRows(rows, { key: 'owner', direction: 'asc' }, columns))
        ).toEqual(['Alpha', 'market 2', 'Market 10'])
        expect(
            names(sortRows(rows, { key: 'owner', direction: 'desc' }, columns))
        ).toEqual(['market 2', 'Alpha', 'Market 10'])
    })

    it('does not mutate the input array', () => {
        const source = [...rows]
        sortRows(source, { key: 'name', direction: 'asc' }, columns)
        expect(names(source)).toEqual(names(rows))
    })
})

describe('cycleSort', () => {
    it('applies the column first direction, flips, then clears', () => {
        const first = cycleSort(null, 'name', 'asc')
        expect(first).toEqual({ key: 'name', direction: 'asc' })

        const flipped = cycleSort(first, 'name', 'asc')
        expect(flipped).toEqual({ key: 'name', direction: 'desc' })

        expect(cycleSort(flipped, 'name', 'asc')).toBeNull()
    })

    it('honours a desc-first column', () => {
        expect(cycleSort(null, 'count', 'desc')).toEqual({
            key: 'count',
            direction: 'desc',
        })
    })

    it('starts a different column fresh rather than flipping it', () => {
        expect(
            cycleSort({ key: 'name', direction: 'desc' }, 'count', 'desc')
        ).toEqual({ key: 'count', direction: 'desc' })
    })
})

describe('toggleSort', () => {
    it('never clears the sort, unlike cycleSort', () => {
        const desc = toggleSort(null, 'count')
        expect(desc).toEqual({ key: 'count', direction: 'desc' })
        const asc = toggleSort(desc, 'count')
        expect(asc).toEqual({ key: 'count', direction: 'asc' })
        expect(toggleSort(asc, 'count')).toEqual(desc)
    })
})
