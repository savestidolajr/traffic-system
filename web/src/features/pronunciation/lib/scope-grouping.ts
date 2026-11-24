import type { Dictionary } from '../types'

/** Group keys, mirroring the Global-first ordering the guidance inventory uses. */
export const PLATFORM_GROUP_ID = '__platform__'
export const STATION_GROUP_ID = '__station__'

export interface DictionaryScopeGroup {
    readonly key: string
    readonly label: string
    readonly items: readonly Dictionary[]
}

/**
 * Splits the library into a platform-wide group and a per-station group, platform-wide FIRST.
 *
 * Ordering is not cosmetic: the platform-wide dictionary sits underneath every station's own, so
 * reading the gallery top-down matches how the layers actually compose on air. Empty groups are
 * dropped so a library with no platform-wide dictionary looks exactly as it did before.
 *
 * Mirrors `features/guidance/lib/scope-grouping.ts`, which groups Global before Market for the
 * same reason.
 */
export function groupDictionariesByScope(
    dictionaries: readonly Dictionary[]
): readonly DictionaryScopeGroup[] {
    const platformWide = dictionaries.filter((d) => d.scope === 'Global')
    const perStation = dictionaries.filter((d) => d.scope !== 'Global')

    const groups: DictionaryScopeGroup[] = []
    if (platformWide.length > 0) {
        groups.push({
            key: PLATFORM_GROUP_ID,
            label: 'Platform-wide, every station',
            items: platformWide,
        })
    }
    if (perStation.length > 0) {
        groups.push({
            key: STATION_GROUP_ID,
            label: 'Per station',
            items: perStation,
        })
    }
    return groups
}

/**
 * The library ordered platform-wide first, for views that cannot carry group headers
 * (the sortable table sorts on its own columns, so this only sets the initial order).
 */
export function sortPlatformWideFirst(
    dictionaries: readonly Dictionary[]
): readonly Dictionary[] {
    return [...dictionaries].sort((a, b) => {
        const aGlobal = a.scope === 'Global' ? 0 : 1
        const bGlobal = b.scope === 'Global' ? 0 : 1
        return aGlobal - bGlobal
    })
}
