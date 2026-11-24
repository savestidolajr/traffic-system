import {
    GUIDANCE_TYPES,
    type Guidance,
    type GuidanceTypeName,
} from '../api/schemas'
import { GLOBAL_GROUP_ID, groupByScope, scopeKey } from './scope-grouping'

export { GLOBAL_GROUP_ID, scopeKey } from './scope-grouping'

// =============================================================================
// Pure filter + scope-grouping logic for the guidance inventory. Kept free of
// React so the precedence/grouping rules are unit-testable in isolation.
// =============================================================================

export interface GuidanceFilters {
    /** Free-text match against guidance text (case-insensitive). */
    readonly search: string
    /** 'all' | GuidanceTypeName */
    readonly type: string
}

export const EMPTY_FILTERS: GuidanceFilters = {
    search: '',
    type: 'all',
}

export interface GuidanceGroup {
    /** GLOBAL_GROUP_ID for global, otherwise the market id or station name. */
    readonly key: string
    readonly scope: 'Global' | 'Market' | 'Station'
    /** '' for global, otherwise the market id or station name. */
    readonly scopeId: string
    readonly label: string
    readonly items: readonly Guidance[]
}

export function hasActiveFilters(f: GuidanceFilters): boolean {
    return f.search.trim() !== '' || f.type !== 'all'
}

export function applyGuidanceFilters(
    items: readonly Guidance[],
    f: GuidanceFilters
): Guidance[] {
    // Scope is no longer a filter axis here: the page-header station picker
    // owns it, and it composes Global + the chosen market rather than
    // selecting exactly one scope. See `appliesToStation`.
    const q = f.search.trim().toLowerCase()
    return items.filter((g) => {
        if (q && !g.text.toLowerCase().includes(q)) return false
        if (f.type !== 'all' && g.type !== f.type) return false
        return true
    })
}

/**
 * Groups guidance by scope and orders them Global-first, then markets
 * alphabetically by label. Rows within a group cluster by guidance type
 * (the same order as the type filter/composer dropdowns), newest-updated
 * first within a type — so same-badge guidance sits together instead of
 * interleaving by recency. `labelFor` resolves a market id to a human label
 * (falls back to the id).
 */
export function groupGuidance(
    items: readonly Guidance[],
    labelFor: (scopeId: string) => string
): GuidanceGroup[] {
    const groups = groupByScope(items, scopeKey, labelFor, (a, b) => {
        const typeDiff =
            GUIDANCE_TYPES.indexOf(a.type as GuidanceTypeName) -
            GUIDANCE_TYPES.indexOf(b.type as GuidanceTypeName)
        if (typeDiff !== 0) return typeDiff
        return b.updatedAtUtc.localeCompare(a.updatedAtUtc)
    })

    return groups.map((group) => ({
        ...group,
        // A group keyed by a station name holds Station rows; market ids and
        // station names cannot collide in practice (ids are slugs, stations
        // are display names), so the first item's scope labels the group.
        scope:
            (group.items[0]?.scope as GuidanceGroup['scope']) ??
            (group.key === GLOBAL_GROUP_ID ? 'Global' : 'Market'),
        scopeId: group.key === GLOBAL_GROUP_ID ? '' : group.key,
    }))
}
