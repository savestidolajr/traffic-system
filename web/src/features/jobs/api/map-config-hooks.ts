import { apiClient } from '../../../lib/api/client'
import { useQueryFactory } from '../../../lib/api/use-query-factory'
import { MAP_CONFIG } from '@/lib/api/paths'

/**
 * Console runtime configuration, fetched from the authenticated backend
 * (`GET /api/map-config`) instead of being baked into the static bundle — the
 * HERE Maps JS key is a credential and must never ship to anonymous callers.
 * An empty key means no browser map key is configured (`Here--MapApiKey` in
 * Key Vault); every map then shows `MapUnavailable` rather than a basemap.
 */
export interface MapConfig {
    readonly hereApiKey: string
    /**
     * Whether playout delivery is enabled AT ALL in this environment
     * (`Zetta:Enabled`). False in development, which has never delivered.
     * A station's own `playoutEnabled` only matters when this is true —
     * delivery needs both — so surfaces that describe what a publish will do
     * MUST consult this before claiming a bulletin reaches playout.
     */
    readonly playoutEnabled: boolean
}

const fetchMapConfig = async (): Promise<MapConfig> => {
    const { data } = await apiClient.get(MAP_CONFIG)
    // Backend serializes PascalCase; tolerate both casings.
    return {
        hereApiKey: data?.HereApiKey ?? data?.hereApiKey ?? '',
        // Absent (a backend predating this field) reads as enabled, so the console
        // keeps its previous behaviour rather than newly claiming playout is off.
        playoutEnabled: data?.PlayoutEnabled ?? data?.playoutEnabled ?? true,
    }
}

export const useMapConfig = () =>
    useQueryFactory<MapConfig>(['map-config'], fetchMapConfig, {
        // The key changes only on a config/rotation, never mid-session.
        staleTime: 1000 * 60 * 60,
        retry: false,
    })
