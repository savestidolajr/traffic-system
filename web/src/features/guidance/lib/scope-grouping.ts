import type { ScopeGroup } from '@/components/ScopeGroupedList'

/** Sentinel scope key for the Global group (global rows carry an empty scopeId). */
export const GLOBAL_GROUP_ID = '__global__'

/** Stable group key for a scoped item — global rows collapse onto one sentinel. */
export function scopeKey<T extends { scope: string; scopeId: string }>(
    item: T
): string {
    return item.scope === 'Global' ? GLOBAL_GROUP_ID : item.scopeId
}

// The page header always names a concrete station; there is no "all stations"
// state. Global rows survive the station filter because Global and Market
// rules COMPOSE - they are the ARN defaults every station starts from.

/**
 * Does a Global-or-Market scoped row apply to the station selected in the page
 * header? Priority roads and filler statements have no Global tier, so they
 * use a plain marketId equality instead of this.
 */
export function appliesToStation(
    item: { scope: string; scopeId: string },
    station: string
): boolean {
    return item.scope === 'Global' || item.scopeId === station
}

interface ScopeGroupingOptions<T> extends ScopeFilterOptions<T> {
    /** Optional within-group ordering; insertion order is retained otherwise. */
    readonly compareItems?: (a: T, b: T) => number
}

export interface ScopeFilterOptions<T> {
    readonly scope?: string
    readonly include?: (item: T) => boolean
}

/** Applies the shared scope filter and a feature-specific predicate. */
export function filterByScope<T>(
    items: readonly T[],
    keyOf: (item: T) => string,
    options: ScopeFilterOptions<T> = {}
): T[] {
    return items.filter((item) => {
        if (
            options.scope &&
            options.scope !== 'all' &&
            keyOf(item) !== options.scope
        )
            return false
        return options.include ? options.include(item) : true
    })
}

/**
 * Groups items by a stable scope key and orders Global first, then scopes by
 * their display label. Filtering is optional so feature-specific predicates
 * can share the same tested grouping and ordering rules.
 */
export function filterAndGroupByScope<T>(
    items: readonly T[],
    keyOf: (item: T) => string,
    labelFor: (scopeId: string) => string,
    options: ScopeGroupingOptions<T> = {}
): ScopeGroup<T>[] {
    const byKey = new Map<string, T[]>()
    for (const item of filterByScope(items, keyOf, {
        scope: options.scope,
        include: options.include,
    })) {
        const key = keyOf(item)
        const bucket = byKey.get(key)
        if (bucket) bucket.push(item)
        else byKey.set(key, [item])
    }

    const groups = [...byKey.entries()].map(([key, bucket]) => ({
        key,
        label: key === GLOBAL_GROUP_ID ? 'Global · all markets' : labelFor(key),
        items: options.compareItems
            ? [...bucket].sort(options.compareItems)
            : bucket,
    }))

    return groups.sort((a, b) => {
        if (a.key === GLOBAL_GROUP_ID) return -1
        if (b.key === GLOBAL_GROUP_ID) return 1
        return a.label.localeCompare(b.label)
    })
}

/** Alias for callers that only need grouping and do not filter. */
export const groupByScope = <T>(
    items: readonly T[],
    keyOf: (item: T) => string,
    labelFor: (scopeId: string) => string,
    compareItems?: (a: T, b: T) => number
): ScopeGroup<T>[] =>
    filterAndGroupByScope(items, keyOf, labelFor, {
        compareItems,
    })

export { type ScopeGroup }
