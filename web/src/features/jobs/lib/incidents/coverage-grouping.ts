// =============================================================================
// Coverage grouping — the pure structure behind the Coverage Board: corridors
// as the spine (the broadcast unit), incidents nested under their road (the
// evidence unit). Incident → road resolution reuses the best-ranked corridor
// already stamped on each IncidentRow by useBulletinReview.
// =============================================================================

import type { JobIncident, TrafficCorridor } from '../../types'

/** The corridor an incident was aggregated into, for display. */
export interface IncidentCorridorRef {
    readonly roadName: string
    readonly rank: number
    readonly isSelected: boolean
    /** Commuter magnitude tier (Severe/Major/…) + raw severity, so a row can
     * colour by the corridor's magnitude rather than flat incident severity. */
    readonly magnitude: string | null
    readonly severity: number
}

/** An incident positioned in either the bulletin or the available pool. */
export interface IncidentRow {
    readonly incident: JobIncident
    /** 1-based airing position when included; null when available. */
    readonly rank: number | null
    readonly included: boolean
    readonly isFirst: boolean
    readonly isLast: boolean
    /** Which ranked corridor this incident belongs to, when known. */
    readonly corridor: IncidentCorridorRef | null
}

/**
 * Operator curation of the corridor list for the next regeneration
 * (WS2-010): which roads make the bulletin, in on-air order.
 */
export interface CorridorCuration {
    /** Curated road names in on-air order; null = machine ranking untouched. */
    readonly order: readonly string[] | null
    /** Effective on-air list (curated order, or the machine selection). */
    readonly effective: readonly string[]
    readonly isOn: (roadName: string) => boolean
    readonly toggle: (roadName: string) => void
    readonly moveUp: (roadName: string) => void
    readonly moveDown: (roadName: string) => void
    readonly reset: () => void
}

export interface CorridorGroup {
    readonly corridor: TrafficCorridor
    /** 1-based on-air position; null for below-the-cut groups. */
    readonly position: number | null
    readonly onAir: boolean
    /** Included incidents on this road, in airing order. */
    readonly rows: IncidentRow[]
    /** Fetched-but-not-included incidents on this road. */
    readonly alsoOnRoad: IncidentRow[]
    /**
     * True when an on-air road HAD linked incidents and the operator dropped
     * every one — the board hints that the road may belong off air.
     */
    readonly emptied: boolean
}

export interface CoverageGroups {
    readonly onAir: CorridorGroup[]
    /** Machine-rejected (or operator-cleared) roads, by machine rank. */
    readonly belowCut: CorridorGroup[]
    /** Incidents no ranked corridor claims, included first. */
    readonly loose: IncidentRow[]
    /** True when the job carries no corridors at all (legacy payloads). */
    readonly degraded: boolean
}

/**
 * Group incident rows under their corridors in on-air order.
 *
 * @param onAirOverride curated road order when the operator has touched the
 * ranking (`curation.effective`); null/undefined = machine selection by rank.
 */
export function buildCoverageGroups(
    corridors: readonly TrafficCorridor[],
    includedRows: readonly IncidentRow[],
    availableRows: readonly IncidentRow[],
    onAirOverride?: readonly string[] | null
): CoverageGroups {
    if (corridors.length === 0) {
        return {
            onAir: [],
            belowCut: [],
            loose: [...includedRows, ...availableRows],
            degraded: true,
        }
    }

    // A long road can yield several corridors (one per direction/segment). The road is the
    // bulletin unit — curation, selection, and the script all key by name — so collapse to one
    // corridor per road name (keep the highest-impact representative; its incidents are unioned
    // by name below). Without this, a road appears twice and shares a drag id (dragging one moves
    // both). New jobs may retain spatially distinct same-road clusters, so also dedupe the curated
    // override rather than assuming the backend has already collapsed them.
    const byRoad = new Map<string, TrafficCorridor>()
    for (const c of corridors) {
        const existing = byRoad.get(c.roadName)
        if (existing === undefined || c.impactScore > existing.impactScore)
            byRoad.set(c.roadName, c)
    }
    const machineRanked = [...byRoad.values()].sort((a, b) => a.rank - b.rank)
    const explicitlySelected = machineRanked.filter((c) => c.isSelected)
    const defaultSelected =
        explicitlySelected.length > 0
            ? explicitlySelected
            : machineRanked.slice(0, 10)
    const onAirRoads = [
        ...new Set(onAirOverride ?? defaultSelected.map((c) => c.roadName)),
    ]
    const rowsByRoad = new Map<string, IncidentRow[]>()
    const availByRoad = new Map<string, IncidentRow[]>()
    const loose: IncidentRow[] = []

    const claim = (
        bucket: Map<string, IncidentRow[]>,
        row: IncidentRow
    ): void => {
        const road = row.corridor?.roadName
        if (road !== undefined && byRoad.has(road)) {
            const list = bucket.get(road)
            if (list) list.push(row)
            else bucket.set(road, [row])
        } else {
            loose.push(row)
        }
    }
    for (const row of includedRows) claim(rowsByRoad, row)
    for (const row of availableRows) claim(availByRoad, row)

    const toGroup = (
        corridor: TrafficCorridor,
        position: number | null,
        onAir: boolean
    ): CorridorGroup => {
        const rows = rowsByRoad.get(corridor.roadName) ?? []
        return {
            corridor,
            position,
            onAir,
            rows,
            alsoOnRoad: availByRoad.get(corridor.roadName) ?? [],
            emptied:
                onAir &&
                (corridor.incidentKeys?.length ?? 0) > 0 &&
                rows.length === 0,
        }
    }

    const onAir = onAirRoads
        .map((road) => byRoad.get(road))
        .filter((c): c is TrafficCorridor => c !== undefined)
        .map((c, i) => toGroup(c, i + 1, true))

    const onAirSet = new Set(onAirRoads)
    const belowCut = machineRanked
        .filter((c) => !onAirSet.has(c.roadName))
        .map((c) => toGroup(c, null, false))

    return { onAir, belowCut, loose, degraded: false }
}
