import { describe, it, expect } from 'vitest'
import {
    groupDictionariesByScope,
    sortPlatformWideFirst,
    PLATFORM_GROUP_ID,
    STATION_GROUP_ID,
} from './scope-grouping'
import type { Dictionary } from '../types'

const dict = (id: string, scope: 'Station' | 'Global', name = id): Dictionary =>
    ({
        id,
        dictionaryId: `el-${id}`,
        name,
        description: null,
        status: 'active',
        scope,
        currentVersion: 1,
        createdAt: '2026-07-31T00:00:00Z',
        updatedAt: '2026-07-31T00:00:00Z',
        etag: null,
        createdBy: null,
        assignedStations: [],
        rules: [],
        versions: [],
    }) as unknown as Dictionary

describe('groupDictionariesByScope', () => {
    it('puts the platform-wide group first', () => {
        const groups = groupDictionariesByScope([
            dict('a', 'Station'),
            dict('g', 'Global'),
            dict('b', 'Station'),
        ])

        expect(groups.map((group) => group.key)).toEqual([
            PLATFORM_GROUP_ID,
            STATION_GROUP_ID,
        ])
        expect(groups[0].items.map((d) => d.id)).toEqual(['g'])
        expect(groups[1].items.map((d) => d.id)).toEqual(['a', 'b'])
    })

    it('labels the groups in operator language, not developer language', () => {
        const groups = groupDictionariesByScope([
            dict('g', 'Global'),
            dict('a', 'Station'),
        ])

        expect(groups[0].label).toBe('Platform-wide, every station')
        expect(groups[1].label).toBe('Per station')
    })

    it('drops empty groups so a library with no platform-wide dictionary is unchanged', () => {
        const groups = groupDictionariesByScope([
            dict('a', 'Station'),
            dict('b', 'Station'),
        ])

        expect(groups).toHaveLength(1)
        expect(groups[0].key).toBe(STATION_GROUP_ID)
    })

    it('returns no groups for an empty library', () => {
        expect(groupDictionariesByScope([])).toEqual([])
    })
})

describe('sortPlatformWideFirst', () => {
    it('orders the platform-wide dictionary ahead of every station one', () => {
        const sorted = sortPlatformWideFirst([
            dict('a', 'Station'),
            dict('b', 'Station'),
            dict('g', 'Global'),
        ])

        expect(sorted.map((d) => d.id)).toEqual(['g', 'a', 'b'])
    })

    it('does not mutate the input', () => {
        const input = [dict('a', 'Station'), dict('g', 'Global')]
        sortPlatformWideFirst(input)

        expect(input.map((d) => d.id)).toEqual(['a', 'g'])
    })
})
