import { isRecord } from './json'

// =============================================================================
// Generic client-side structural diff. Reach for this whenever a UI needs to
// show "what actually changed" between two JSON-shaped values and the source
// of truth doesn't already hand back a field-level diff — a dry-run report
// that only says Create/Update/Unchanged, or an unsaved editor draft versus
// the last-saved version. Works across both wire-PascalCase DTOs (config
// bundles) and camelCase UI state (style rules, ...): identity-field
// matching is case-insensitive.
//
// Rule: objects always flatten to field-level leaves (recursing through
// nested records); arrays diff at item granularity — an added/removed
// element collapses to one entry instead of exploding into its own fields,
// matched elements recurse into their fields. Array items are identified by
// id/name/market/marketId/stringToReplace/term (case-insensitive), then the
// item's own JSON as a last resort.
// =============================================================================

export interface DiffEntry {
    readonly kind: 'added' | 'removed' | 'changed'
    /** `item` = a whole array element added/removed; `field` = a leaf value. */
    readonly scope: 'field' | 'item'
    readonly path: readonly string[]
    readonly before?: unknown
    readonly after?: unknown
}

/**
 * Bookkeeping fields that double as an entity's own upsert/identity key.
 * They are never emitted as a top-level diff leaf — a brand-new entity's id
 * is not something a reviewer needs to eyeball, and it never differs on an
 * update (that IS how the two sides were matched). Compared case-insensitively
 * so both `Id` (wire DTOs) and `id` (UI state) match.
 */
const IDENTITY_FIELD_NAMES = new Set(['id', 'marketid', 'market'])

function isIdentityField(name: string): boolean {
    return IDENTITY_FIELD_NAMES.has(name.toLowerCase())
}

function deepEqual(a: unknown, b: unknown): boolean {
    if (a === b) return true
    if (a === null || a === undefined || b === null || b === undefined) {
        return (
            (a === null || a === undefined) && (b === null || b === undefined)
        )
    }
    if (Array.isArray(a) || Array.isArray(b)) {
        return (
            Array.isArray(a) &&
            Array.isArray(b) &&
            a.length === b.length &&
            a.every((v, i) => deepEqual(v, b[i]))
        )
    }
    if (isRecord(a) && isRecord(b)) {
        const keys = new Set([...Object.keys(a), ...Object.keys(b)])
        for (const key of keys) {
            if (!deepEqual(a[key], b[key])) return false
        }
        return true
    }
    return false
}

/** The identity an array item is matched by across before/after (case-insensitive field names). */
function itemKey(item: unknown): string {
    if (isRecord(item)) {
        for (const field of [
            'id',
            'name',
            'stringtoreplace',
            'market',
            'marketid',
            'term',
        ]) {
            const matchKey = Object.keys(item).find(
                (k) => k.toLowerCase() === field
            )
            const candidate =
                matchKey === undefined ? undefined : item[matchKey]
            if (typeof candidate === 'string' && candidate.length > 0)
                return candidate
        }
    }
    return JSON.stringify(item)
}

interface KeyedItem {
    /** Occurrence-disambiguated key (base, base#1, base#2, …) — doubles as the diff path segment. */
    readonly key: string
    /** The bare identity (id/name/market/… or the item's own JSON) before occurrence disambiguation. */
    readonly base: string
    readonly item: unknown
}

/** Build an identity->item map, disambiguating colliding keys by occurrence
 *  order so two items sharing an identity are never silently dropped. */
function keyById(items: unknown[]): Map<string, KeyedItem> {
    const map = new Map<string, KeyedItem>()
    const seen = new Map<string, number>()
    for (const item of items) {
        const base = itemKey(item)
        const n = seen.get(base) ?? 0
        seen.set(base, n + 1)
        const key = n === 0 ? base : `${base}#${n}`
        map.set(key, { key, base, item })
    }
    return map
}

function diffArray(
    before: unknown[],
    after: unknown[],
    path: readonly string[],
    out: DiffEntry[]
): void {
    const beforeByKey = keyById(before)
    const afterByKey = keyById(after)

    // Same-identity items that are CONTENT-equal pair up first, wherever they
    // sit in the list. Without this, removing one of several items sharing an
    // identity misaligned the survivors by occurrence: before [A v1, A v2] →
    // after [A v2] used to report "A v1 changed to v2" AND "A v2 removed"
    // instead of the honest "A v1 removed" (B2 — the same duplicate-value
    // class as the schedule-times reference bug).
    const consumedBefore = new Set<string>()
    const consumedAfter = new Set<string>()
    for (const [key, b] of beforeByKey) {
        if (consumedBefore.has(key)) continue
        for (const [afterKey, a] of afterByKey) {
            if (consumedAfter.has(afterKey)) continue
            if (b.base === a.base && deepEqual(b.item, a.item)) {
                consumedBefore.add(key)
                consumedAfter.add(afterKey)
                break
            }
        }
    }

    const beforeLeft = [...beforeByKey].filter(
        ([key]) => !consumedBefore.has(key)
    )
    let afterLeft = [...afterByKey].filter(([key]) => !consumedAfter.has(key))

    // Pure additions first (after items with no same-identity survivor),
    // preserving the added-before-removed render order of the old algorithm.
    const addable = afterLeft.filter(([, a]) =>
        beforeLeft.every(([, b]) => b.base !== a.base)
    )
    for (const [afterKey, a] of addable) {
        out.push({
            kind: 'added',
            scope: 'item',
            path: [...path, afterKey],
            after: a.item,
        })
    }
    const addableKeys = new Set(addable.map(([key]) => key))
    afterLeft = afterLeft.filter(([key]) => !addableKeys.has(key))

    // Leftovers pair by occurrence within the same identity; an unpaired
    // before item is a removal, an unpaired after item an addition.
    for (const [key, b] of beforeLeft) {
        const matchIdx = afterLeft.findIndex(([, a]) => a.base === b.base)
        if (matchIdx === -1) {
            out.push({
                kind: 'removed',
                scope: 'item',
                path: [...path, key],
                before: b.item,
            })
        } else {
            const [, a] = afterLeft.splice(matchIdx, 1)[0]
            diffValue(b.item, a.item, [...path, key], out)
        }
    }

    for (const [afterKey, a] of afterLeft) {
        out.push({
            kind: 'added',
            scope: 'item',
            path: [...path, afterKey],
            after: a.item,
        })
    }
}

function diffValue(
    before: unknown,
    after: unknown,
    path: readonly string[],
    out: DiffEntry[]
): void {
    if (deepEqual(before, after)) return
    if (path.length === 1 && isIdentityField(path[0])) return

    if (Array.isArray(before) || Array.isArray(after)) {
        diffArray(
            Array.isArray(before) ? before : [],
            Array.isArray(after) ? after : [],
            path,
            out
        )
        return
    }
    if (isRecord(before) || isRecord(after)) {
        const b = isRecord(before) ? before : {}
        const a = isRecord(after) ? after : {}
        for (const key of new Set([...Object.keys(b), ...Object.keys(a)])) {
            diffValue(b[key], a[key], [...path, key], out)
        }
        return
    }
    if (before === undefined)
        out.push({ kind: 'added', scope: 'field', path, after })
    else if (after === undefined)
        out.push({ kind: 'removed', scope: 'field', path, before })
    else out.push({ kind: 'changed', scope: 'field', path, before, after })
}

/**
 * Diffs one value (a voice profile, a schedule, an array of style rules
 * wrapped in `{ Rules: [...] }`...) between its current state and a draft.
 * `before` undefined/empty means everything in `after` flattens as `added`.
 */
export function diffEntities(before: unknown, after: unknown): DiffEntry[] {
    const out: DiffEntry[] = []
    const b = isRecord(before) ? before : {}
    const a = isRecord(after) ? after : {}
    for (const key of new Set([...Object.keys(b), ...Object.keys(a)])) {
        diffValue(b[key], a[key], [key], out)
    }
    return out
}

/** "SimilarityBoost" -> "Similarity Boost". Path segments render through this. */
export function humanizePathSegment(segment: string): string {
    return segment.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
}

export function formatDiffValue(value: unknown): string {
    if (value === null || value === undefined) return '\u2014'
    if (typeof value === 'boolean') return value ? 'Yes' : 'No'
    if (Array.isArray(value)) {
        return value.length === 0
            ? '(empty)'
            : value.map(formatDiffValue).join(', ')
    }
    if (isRecord(value)) return summarizeEntity(value)
    return String(value)
}

/** Compact one-line rendering of a whole added/removed array item. */
export function summarizeEntity(value: unknown): string {
    if (!isRecord(value)) return formatDiffValue(value)
    const parts = Object.entries(value)
        .filter(([key, v]) => {
            if (isIdentityField(key)) return false
            if (v === null || v === undefined) return false
            return !(Array.isArray(v) && v.length === 0)
        })
        .map(([key, v]) => `${humanizePathSegment(key)}: ${formatDiffValue(v)}`)
    return parts.length > 0 ? parts.join(' \u00b7 ') : '(no fields)'
}
