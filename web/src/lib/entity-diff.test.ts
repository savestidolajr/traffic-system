// =============================================================================
// Entity-diff engine — array semantics (B2: duplicate values)
//
// The engine must answer two questions the rest of the UI depends on:
//   1. Does a repeated array value collapse or survive? (Reference bug class:
//      the schedule-times chips that could not be removed because duplicate
//      values broke identity.)
//   2. Is the diff index-based (a pure reorder reads as changes) or identity-
//      based?
// =============================================================================

import { describe, it, expect } from 'vitest'
import { diffEntities } from './entity-diff'

describe('diffEntities array semantics with repeated values', () => {
    it('does not collapse duplicates: removing one of two identical items reports exactly one removal', () => {
        const before = { Items: ['a', 'a'] }
        const after = { Items: ['a'] }
        const entries = diffEntities(before, after)
        // Occurrence-disambiguated keys: '"a"' stays, '"a"#1' is removed.
        // No "changed" entry is emitted for the survivor.
        expect(entries).toEqual([
            {
                kind: 'removed',
                scope: 'item',
                path: ['Items', '"a"#1'],
                before: 'a',
            },
        ])
    })

    it('reports each duplicate addition separately', () => {
        const entries = diffEntities({ Items: [] }, { Items: ['a', 'a'] })
        expect(entries).toHaveLength(2)
        expect(
            entries.every((e) => e.kind === 'added' && e.scope === 'item')
        ).toBe(true)
        expect(entries.map((e) => e.path[1])).toEqual(['"a"', '"a"#1'])
    })

    it('is identity-based, not index-based: a pure reorder produces no entries', () => {
        const before = {
            Rules: [
                { id: '1', text: 'x' },
                { id: '2', text: 'y' },
            ],
        }
        const after = {
            Rules: [
                { id: '2', text: 'y' },
                { id: '1', text: 'x' },
            ],
        }
        expect(diffEntities(before, after)).toEqual([])
    })

    it('removing the FIRST of two same-identity items reports the honest removal, not a false change', () => {
        // before [A v1, A v2] -> after [A v2]: the survivor is v2, so the only
        // true diff is "A v1 removed". The old occurrence-aligned algorithm
        // reported "A v1 changed to v2" AND "A v2 removed".
        const before = {
            Rules: [
                { term: 'A', text: 'v1' },
                { term: 'A', text: 'v2' },
            ],
        }
        const after = { Rules: [{ term: 'A', text: 'v2' }] }
        expect(diffEntities(before, after)).toEqual([
            {
                kind: 'removed',
                scope: 'item',
                path: ['Rules', 'A'],
                before: { term: 'A', text: 'v1' },
            },
        ])
    })

    it('still reports a real change when a single same-identity item changes', () => {
        const before = { Rules: [{ term: 'A', text: 'x' }] }
        const after = { Rules: [{ term: 'A', text: 'y' }] }
        expect(diffEntities(before, after)).toEqual([
            {
                kind: 'changed',
                scope: 'field',
                path: ['Rules', 'A', 'text'],
                before: 'x',
                after: 'y',
            },
        ])
    })

    it('keeps unchanged duplicates silent when a sibling of a different identity changes', () => {
        const before = {
            Items: [
                { name: 'dup', v: 1 },
                { name: 'dup', v: 1 },
                { name: 'other', v: 0 },
            ],
        }
        const after = {
            Items: [
                { name: 'other', v: 9 },
                { name: 'dup', v: 1 },
                { name: 'dup', v: 1 },
            ],
        }
        expect(diffEntities(before, after)).toEqual([
            {
                kind: 'changed',
                scope: 'field',
                path: ['Items', 'other', 'v'],
                before: 0,
                after: 9,
            },
        ])
    })
})
