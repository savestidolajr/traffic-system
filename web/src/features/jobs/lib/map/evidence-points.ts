import type { TrafficCorridor, JobIncident } from '../../types'
import { formatRoadName } from '@/lib/formatting'
import {
    corridorDisplayPinColor,
    corridorSeverityPinColor,
    corridorTierByIncidentKey,
    magnitudeSeverity,
} from '../incidents/incident-labels'

/** One mark on the evidence map: an incident (line when HERE gave us road
 *  geometry, dot otherwise) or a corridor-centroid fallback. */
export interface EvidencePoint {
    key: string
    lat: number
    lng: number
    label: string
    color: string
    tier: number
    shape?: [number, number][]
}

/** Neutral stroke used for evidence available from HERE but not in the bulletin. */
export const AVAILABLE_STROKE = corridorSeverityPinColor(4)

/** Compute radius in meters from bbox [west, south, east, north]. */
export function bboxRadiusMeters(bbox: readonly number[]): number {
    const centerLat = (bbox[1] + bbox[3]) / 2
    const latDiff = Math.abs(bbox[3] - bbox[1]) / 2
    const lngDiff = Math.abs(bbox[2] - bbox[0]) / 2
    const latMeters = latDiff * 111_320
    const lngMeters = lngDiff * 111_320 * Math.cos((centerLat * Math.PI) / 180)
    return Math.max(latMeters, lngMeters)
}

/**
 * Build the marks for a bulletin's evidence map. Every incident gets a mark —
 * incidents are what the operator ranks; corridors only fill in when a job
 * carries no incident coordinates. Colour comes from the carrying corridor's
 * commuter MAGNITUDE (the delay a listener actually feels), not flat HERE
 * incident severity, which floors every freeway incident at "critical".
 *
 * Used by the HERE evidence map (`HereLocationMap`) to decide what each mark
 * means.
 */
export function buildEvidencePoints(
    corridors: readonly TrafficCorridor[],
    incidents: readonly JobIncident[]
): EvidencePoint[] {
    const corridorByIncident = corridorTierByIncidentKey(corridors)
    const pts: EvidencePoint[] = []

    for (const i of incidents) {
        if (i.latitude != null && i.longitude != null) {
            const c = corridorByIncident.get(i.key)
            pts.push({
                key: i.key,
                lat: i.latitude,
                lng: i.longitude,
                label: i.headline,
                color: corridorDisplayPinColor(
                    c?.magnitude ?? null,
                    c?.severity ?? i.priority
                ),
                tier: magnitudeSeverity(
                    c?.magnitude ?? null,
                    c?.severity ?? i.priority
                ),
                shape:
                    i.shape && i.shape.length >= 2
                        ? i.shape.map((p) => [p.lat, p.lng] as [number, number])
                        : undefined,
            })
        }
    }

    if (pts.length > 0) return pts

    // No incident coordinates — fall back to corridor centroids.
    for (const c of corridors) {
        if (c.latitude != null && c.longitude != null) {
            pts.push({
                key: c.roadName,
                lat: c.latitude,
                lng: c.longitude,
                label: formatRoadName(c.roadName),
                color: corridorDisplayPinColor(c.magnitude, c.severity),
                tier: magnitudeSeverity(c.magnitude, c.severity),
            })
        }
    }

    return pts
}
