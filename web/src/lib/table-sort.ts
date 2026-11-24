/** Generic column-sort state shared by every sortable data table in the app. */

import { useMemo, useState } from 'react'

export type SortDirection = 'asc' | 'desc'

export interface SortState<TKey extends string> {
    readonly key: TKey
    readonly direction: SortDirection
}

/**
 * Clicking the already-active column flips its direction; clicking a different column selects
 * it fresh at `defaultDirection` (descending by default — most columns here are "biggest/newest
 * first" on first click, matching how every other sortable list in the wild behaves).
 *
 * For tables whose *unsorted* state is meaningful (source order), use `cycleSort` instead so a
 * third click can get back to it.
 */
export function toggleSort<TKey extends string>(
    current: SortState<TKey> | null,
    key: TKey,
    defaultDirection: SortDirection = 'desc'
): SortState<TKey> {
    if (current?.key === key) {
        return { key, direction: current.direction === 'asc' ? 'desc' : 'asc' }
    }
    return { key, direction: defaultDirection }
}

/**
 * Tri-state cycle for tables that default to *source order* (the same order the card gallery
 * shows): first click sorts at the column's own first direction, second flips it, third clears
 * the sort. Without the third state an operator who sorts once can never get the original
 * order back short of a reload.
 */
export function cycleSort<TKey extends string>(
    current: SortState<TKey> | null,
    key: TKey,
    first: SortDirection
): SortState<TKey> | null {
    if (current?.key !== key) return { key, direction: first }
    if (current.direction === first) {
        return { key, direction: first === 'asc' ? 'desc' : 'asc' }
    }
    return null
}

// =============================================================================
// Comparing cell values
// =============================================================================

/** What a sortable column extracts from a row. */
export type SortValue = string | number | boolean | null | undefined

export interface SortColumn<TRow> {
    /** The comparable value for this column, extracted from a row. */
    readonly value: (row: TRow) => SortValue
    /** Direction the FIRST click applies. Text columns want `asc` (A-Z), counts and
     *  timestamps want `desc` (most/newest first). */
    readonly first: SortDirection
}

/** The full set of sortable columns for one table, keyed by the header's sort key. */
export type SortColumns<TRow, TKey extends string> = Readonly<
    Record<TKey, SortColumn<TRow>>
>

/** `numeric` so "Market 2" precedes "Market 10"; `base` so case never decides an order. */
const collator = new Intl.Collator(undefined, {
    numeric: true,
    sensitivity: 'base',
})

/**
 * Compare two cell values for a given direction. Empty values always sort last, in BOTH
 * directions — missing data is not "smallest", it is absent, and flipping the direction must
 * not flood the top of the table with blanks.
 */
export function compareSortValues(
    a: SortValue,
    b: SortValue,
    direction: SortDirection
): number {
    const emptyA = a === null || a === undefined || a === ''
    const emptyB = b === null || b === undefined || b === ''
    if (emptyA || emptyB) return Number(emptyA) - Number(emptyB)

    const sign = direction === 'asc' ? 1 : -1
    if (typeof a === 'string' && typeof b === 'string') {
        return sign * collator.compare(a, b)
    }
    // Numbers and booleans: `false` (0) before `true` (1), so `desc` puts "on" first.
    return sign * (Number(a) - Number(b))
}

/**
 * Sort a row list by the active column. Returns the input array untouched when nothing is
 * sorted, so an unsorted table keeps the exact order its data source produced.
 */
export function sortRows<TRow, TKey extends string>(
    rows: readonly TRow[],
    sort: SortState<TKey> | null,
    columns: SortColumns<TRow, TKey>
): readonly TRow[] {
    if (!sort) return rows
    const column = columns[sort.key]
    if (!column) return rows
    // Array.prototype.sort is stable, so rows that tie keep their source order.
    return [...rows].sort((a, b) =>
        compareSortValues(column.value(a), column.value(b), sort.direction)
    )
}

// =============================================================================
// Hooks
// =============================================================================

const STORAGE_PREFIX = 'arn:table-sort:'

function readStored<TRow, TKey extends string>(
    storageKey: string,
    columns: SortColumns<TRow, TKey>
): SortState<TKey> | null {
    try {
        const raw = window.localStorage.getItem(STORAGE_PREFIX + storageKey)
        if (!raw) return null
        const [key, direction] = raw.split(':')
        // A column removed or renamed since the preference was written must not resurrect a
        // sort key the table no longer has.
        if (!key || !(key in columns)) return null
        if (direction !== 'asc' && direction !== 'desc') return null
        return { key: key as TKey, direction }
    } catch {
        // Private mode / disabled storage — fall back to source order.
        return null
    }
}

function writeStored<TKey extends string>(
    storageKey: string,
    sort: SortState<TKey> | null
): void {
    try {
        const name = STORAGE_PREFIX + storageKey
        if (sort) {
            window.localStorage.setItem(name, `${sort.key}:${sort.direction}`)
        } else {
            window.localStorage.removeItem(name)
        }
    } catch {
        // Ignore write failures (quota / private mode); state still updates.
    }
}

/**
 * Column-sort state for a table that defaults to source order, persisted per table under a
 * stable `storageKey` so an operator's chosen ordering survives reloads and navigation — the
 * same treatment `useViewMode` gives the card/table toggle sitting right above it.
 *
 * Use this directly when one sort drives more than one `<Table>` (the stations page renders an
 * in-scope and an out-of-scope section); otherwise reach for `useTableSort`.
 */
export function useSortState<TRow, TKey extends string>(
    columns: SortColumns<TRow, TKey>,
    storageKey: string
): {
    readonly sort: SortState<TKey> | null
    readonly onSort: (key: TKey) => void
} {
    const [sort, setSort] = useState<SortState<TKey> | null>(() =>
        readStored(storageKey, columns)
    )

    // Deliberately not memoized: it must read the CURRENT `columns` for the clicked column's
    // first direction, and no consumer of a header button is memoized, so a stable identity
    // would buy nothing while risking a stale closure.
    const onSort = (key: TKey) =>
        setSort((current) => {
            const next = cycleSort(current, key, columns[key].first)
            writeStored(storageKey, next)
            return next
        })

    return { sort, onSort }
}

/**
 * Sorted rows plus the state a `DataTableColumnHeader` needs. Memoize `columns` at the call
 * site (it closes over derived props) — a new map each render just re-sorts, but there is no
 * reason to pay for it.
 */
export function useTableSort<TRow, TKey extends string>(
    rows: readonly TRow[],
    columns: SortColumns<TRow, TKey>,
    storageKey: string
): {
    readonly rows: readonly TRow[]
    readonly sort: SortState<TKey> | null
    readonly onSort: (key: TKey) => void
} {
    const { sort, onSort } = useSortState(columns, storageKey)
    const sorted = useMemo(
        () => sortRows(rows, sort, columns),
        [rows, sort, columns]
    )
    return { rows: sorted, sort, onSort }
}
