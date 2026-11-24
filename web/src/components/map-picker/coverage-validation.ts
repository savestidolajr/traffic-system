import type { CoveragePoint, CoverageShapeType } from '@/lib/api/schemas/shared'
import {
    MAX_BBOX_DEGREES,
    MAX_SHAPE_SPAN_DEGREES,
} from '@/lib/api/schemas/shared'
import { maxExtentDegrees } from './coverage-shapes'

/**
 * Single source of truth for coverage-shape constraints. The schedule form
 * schema, the trigger form schema, and the map pill all consume this module
 * so the thresholds cannot drift apart. Presentation-level concerns (e.g.
 * pill warnings at 80% of a limit, site-specific message copy) stay with the
 * consumers — only the hard limits and the pass/fail logic live here.
 */

/** HERE's incidents API rejects circles larger than 50 km (E608017). */
export const MAX_CIRCLE_RADIUS_KM = 50
/** A corridor needs at least a start and an end waypoint. */
export const MIN_CORRIDOR_POINTS = 2
/** A polygon ring needs at least 3 points to enclose anything. */
export const MIN_POLYGON_POINTS = 3
/** Pill warnings fire at this fraction of a hard extent limit. */
export const EXTENT_WARNING_RATIO = 0.8

// The platform's operating region — mirrors MarketSchedule.cs's
// MinOperatingLatitude/MaxOperatingLatitude/MinOperatingLongitude/
// MaxOperatingLongitude, which ScheduleHttpTrigger enforces at the PUT
// boundary. The platform broadcasts the Australian Traffic Network's markets,
// so an operator-supplied area anywhere else on Earth (a Paris polygon, a
// sign-typo of "45" instead of "-45") would silently fetch — and air — the
// wrong region's incidents. Mirrored here so the picker blocks before the 400.
export const MIN_OPERATING_LATITUDE = -50
export const MAX_OPERATING_LATITUDE = 0
export const MIN_OPERATING_LONGITUDE = 100
export const MAX_OPERATING_LONGITUDE = 180

export { MAX_BBOX_DEGREES, MAX_SHAPE_SPAN_DEGREES }

export type CoverageIssueCode =
    | 'circle-too-large'
    | 'bbox-incomplete'
    | 'bbox-inverted'
    | 'bbox-too-large'
    | 'corridor-too-few-points'
    | 'polygon-too-few-points'
    | 'polygon-too-large'
    | 'outside-operating-region'

export interface CoverageIssue {
    readonly code: CoverageIssueCode
    /** The form field the issue should attach to. */
    readonly path: 'radiusKm' | 'shapeType' | 'latitude'
    /** Default user-facing message; consumers may override per code. */
    readonly message: string
}

/** The subset of coverage fields the shape constraints look at. */
export interface CoverageShapeFields {
    readonly shapeType?: CoverageShapeType
    readonly latitude?: number
    readonly longitude?: number
    readonly radiusKm?: number
    readonly west?: number
    readonly south?: number
    readonly east?: number
    readonly north?: number
    readonly corridorPoints?: readonly CoveragePoint[]
    readonly polygonPoints?: readonly CoveragePoint[]
}

/** True when a point lies outside the operating region (mirrors the backend's
 *  CoverageArea.OutsideOperatingRegion). */
function outsideOperatingRegion(lat: number, lng: number): boolean {
    return (
        lat < MIN_OPERATING_LATITUDE ||
        lat > MAX_OPERATING_LATITUDE ||
        lng < MIN_OPERATING_LONGITUDE ||
        lng > MAX_OPERATING_LONGITUDE
    )
}

/** The backend's refusal wording, so a client block and a server 400 agree. */
const OPERATING_REGION_MESSAGE =
    `Coverage must lie inside the platform's operating region ` +
    `(lat ${MIN_OPERATING_LATITUDE}..${MAX_OPERATING_LATITUDE}, ` +
    `lng ${MIN_OPERATING_LONGITUDE}..${MAX_OPERATING_LONGITUDE}); this area is outside it.`

function pushOperatingRegionIssue(
    issues: CoverageIssue[],
    path: CoverageIssue['path']
): void {
    issues.push({
        code: 'outside-operating-region',
        path,
        message: OPERATING_REGION_MESSAGE,
    })
}

/**
 * Validate a coverage shape against the HERE-derived constraints. Returns
 * structured issues — empty when the shape is acceptable. Shapes default to
 * circle when `shapeType` is absent (legacy areas).
 */
export function validateCoverageShape(
    area: CoverageShapeFields
): CoverageIssue[] {
    const issues: CoverageIssue[] = []
    const shapeType = area.shapeType ?? 'circle'

    switch (shapeType) {
        case 'circle': {
            if ((area.radiusKm ?? 0) > MAX_CIRCLE_RADIUS_KM) {
                issues.push({
                    code: 'circle-too-large',
                    path: 'radiusKm',
                    message: `HERE’s incidents API rejects circles larger than ${MAX_CIRCLE_RADIUS_KM} km (E608017)`,
                })
            }
            if (
                area.latitude !== undefined &&
                area.longitude !== undefined &&
                outsideOperatingRegion(area.latitude, area.longitude)
            ) {
                // Attached to latitude — the coordinate an operator is likeliest
                // to sign-typo ("45" instead of "-45") — but the message names
                // the whole region so longitude is covered too.
                pushOperatingRegionIssue(issues, 'latitude')
            }
            break
        }
        case 'bbox': {
            const { west, south, east, north } = area
            if (
                west === undefined ||
                south === undefined ||
                east === undefined ||
                north === undefined
            ) {
                issues.push({
                    code: 'bbox-incomplete',
                    path: 'shapeType',
                    message: 'Bounding box needs west, south, east and north',
                })
                break
            }
            if (east <= west || north <= south) {
                issues.push({
                    code: 'bbox-inverted',
                    path: 'shapeType',
                    message:
                        'Box must have positive extent (west < east, south < north)',
                })
            }
            if (
                east - west > MAX_SHAPE_SPAN_DEGREES ||
                north - south > MAX_SHAPE_SPAN_DEGREES
            ) {
                issues.push({
                    code: 'bbox-too-large',
                    path: 'shapeType',
                    message: `HERE limits a coverage box to ${MAX_SHAPE_SPAN_DEGREES} degrees per side`,
                })
            }
            // The box is axis-aligned, so its two extreme corners bound every
            // point of it — mirrors CoverageArea.WithinOperatingRegion.
            if (
                outsideOperatingRegion(south, west) ||
                outsideOperatingRegion(north, east)
            ) {
                pushOperatingRegionIssue(issues, 'shapeType')
            }
            break
        }
        case 'corridor': {
            const corridorPoints = area.corridorPoints ?? []
            if (corridorPoints.length < MIN_CORRIDOR_POINTS) {
                issues.push({
                    code: 'corridor-too-few-points',
                    path: 'shapeType',
                    message: 'Add at least 2 corridor waypoints on the map',
                })
            }
            if (
                corridorPoints.some((p) => outsideOperatingRegion(p.lat, p.lng))
            ) {
                pushOperatingRegionIssue(issues, 'shapeType')
            }
            break
        }
        case 'polygon': {
            const points = area.polygonPoints ?? []
            if (points.length < MIN_POLYGON_POINTS) {
                issues.push({
                    code: 'polygon-too-few-points',
                    path: 'shapeType',
                    message: `Draw at least 3 polygon points (the area must fit inside ${MAX_SHAPE_SPAN_DEGREES} degrees per side)`,
                })
            } else if (maxExtentDegrees(points) > MAX_SHAPE_SPAN_DEGREES) {
                issues.push({
                    code: 'polygon-too-large',
                    path: 'shapeType',
                    message: `Too large — the area must fit inside ${MAX_SHAPE_SPAN_DEGREES} degrees per side`,
                })
            }
            if (points.some((p) => outsideOperatingRegion(p.lat, p.lng))) {
                pushOperatingRegionIssue(issues, 'shapeType')
            }
            break
        }
    }

    return issues
}
