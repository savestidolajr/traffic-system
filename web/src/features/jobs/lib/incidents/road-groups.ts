import type { JobIncident } from '../../types'
import {
    incidentAnchor,
    incidentEvent,
    incidentRoad,
    roadClassLabel,
} from './incident-labels'

// ---------------------------------------------------------------------------
// Shared road-grouping for any surface that lists incidents bucketed by road
// (Review's evidence lens, the Adjust map explorer). One definition so the
// two surfaces never disagree on how a roadworks zone's many short same-road
// segments collapse into a single group.
// ---------------------------------------------------------------------------

export interface RoadGroupData {
    readonly road: string
    /** Most-major road class across the group (lowest HERE functional class). */
    readonly roadClass: string | null
    readonly incidents: readonly JobIncident[]
}

/**
 * Group incidents by the road their headline anchors on, preserving first-seen
 * (impact) order. HERE fragments one roadworks zone into many short same-road
 * segments, so without this the index reads as a wall of identical-looking
 * "{type} on {road}" rows.
 */
export function groupByRoad(
    incidents: readonly JobIncident[]
): RoadGroupData[] {
    const byRoad = new Map<string, JobIncident[]>()
    for (const inc of incidents) {
        const road = incidentRoad(inc.headline)
        const list = byRoad.get(road)
        if (list) list.push(inc)
        else byRoad.set(road, [inc])
    }
    // Map preserves insertion (first-seen / impact) order.
    return [...byRoad].map(([road, list]) => {
        const minClass = list.reduce<number | undefined>(
            (min, i) =>
                i.functionalClass != null &&
                (min == null || i.functionalClass < min)
                    ? i.functionalClass
                    : min,
            undefined
        )
        return { road, roadClass: roadClassLabel(minClass), incidents: list }
    })
}

/** Distinct location anchors across a road group's incidents, for the group
 *  header subtitle ("At Anzac Highway, At Smith Street +2 more"). */
export function anchorsSummary(
    incidents: readonly JobIncident[]
): string | null {
    const anchors = incidents
        .map((i) => (i.detail ? incidentAnchor(i.detail) : null))
        .filter((anchor): anchor is string => Boolean(anchor))
    if (anchors.length === 0) return null

    const distinct = [...new Set(anchors)]
    const visible = distinct.slice(0, 3).join(', ')
    const hidden = distinct.length - 3
    return hidden > 0 ? `${visible} +${hidden} more` : visible
}

/** The dominant event type across a road group's incidents, for the group
 *  header ("Roadworks +2 more"). */
export function eventSummary(incidents: readonly JobIncident[]): string {
    const counts = new Map<string, number>()
    for (const incident of incidents) {
        const event = incidentEvent(incident.headline)
        counts.set(event, (counts.get(event) ?? 0) + 1)
    }
    const entries = [...counts.entries()].sort((a, b) => b[1] - a[1])
    if (entries.length === 0) return 'Evidence'
    const [event, count] = entries[0]
    if (entries.length === 1) {
        return count === 1 ? event : `${count} ${event.toLowerCase()} segments`
    }
    return `${event} +${entries.length - 1} more`
}
