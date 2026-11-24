import type { CoveragePoint, CoverageShapeType } from '@/lib/api/schemas/shared'
import {
    EXTENT_WARNING_RATIO,
    MAX_BBOX_DEGREES,
    MAX_SHAPE_SPAN_DEGREES,
    MIN_POLYGON_POINTS,
    validateCoverageShape,
} from './coverage-validation'
import {
    bboxDimensionsKm,
    corridorLengthKm,
    maxExtentDegrees,
    polygonAreaKm2,
    polygonTileGridSize,
    tileGridSize,
} from './coverage-shapes'

export const MODE_META: Record<
    CoverageShapeType,
    { label: string; tooltip: string }
> = {
    circle: {
        label: 'Radius',
        tooltip: 'Everything within a set distance of a point',
    },
    bbox: { label: 'Box', tooltip: 'A rectangular region' },
    corridor: {
        label: 'Route',
        tooltip: 'Along a drawn route, within a set width',
    },
    polygon: {
        label: 'Area',
        tooltip: 'Exactly the shape you draw. Traffic outside it is excluded',
    },
}

export interface PillState {
    tone: 'default' | 'info' | 'warning' | 'destructive'
    text: string
    hint?: string
}

/** Live status for the on-map coverage pill. */
export function buildPillState(args: {
    shapeType: CoverageShapeType
    radius: number
    west?: number
    south?: number
    east?: number
    north?: number
    latitude: number
    corridorPoints?: CoveragePoint[]
    corridorRadiusMeters: number
    polygonPoints?: CoveragePoint[]
}): PillState {
    const adjustHint = 'Drag points to adjust · Alt+click removes'
    switch (args.shapeType) {
        case 'bbox': {
            if (
                args.west === undefined ||
                args.south === undefined ||
                args.east === undefined ||
                args.north === undefined
            ) {
                return {
                    tone: 'default',
                    text: 'Click the map to place the box',
                }
            }
            const { west, south, east, north } = args
            const { w, h } = bboxDimensionsKm({
                west,
                south,
                east,
                north,
                latitude: args.latitude,
            })
            const metric = `~${w.toFixed(0)} × ${h.toFixed(0)} km box`
            const issues = validateCoverageShape({
                shapeType: 'bbox',
                west,
                south,
                east,
                north,
            })
            if (issues.some((i) => i.code === 'bbox-too-large')) {
                return {
                    tone: 'destructive',
                    text: 'Too large. HERE caps a single query at ~111 km per side',
                    hint: metric,
                }
            }
            const extent = Math.max(east - west, north - south)
            if (extent > MAX_BBOX_DEGREES) {
                const { tiles } = tileGridSize(west, south, east, north)
                const requestNote = `Exceeds HERE's ~111 km single-query limit — fetched as ${tiles} separate HERE requests`
                if (extent > MAX_SHAPE_SPAN_DEGREES * EXTENT_WARNING_RATIO) {
                    return {
                        tone: 'warning',
                        text: metric,
                        hint: `Approaching the max size · ${requestNote.toLowerCase()}`,
                    }
                }
                return { tone: 'info', text: metric, hint: requestNote }
            }
            return {
                tone: 'default',
                text: metric,
                hint: 'Click to move · drag corners to resize',
            }
        }
        case 'corridor': {
            const pts = args.corridorPoints ?? []
            if (pts.length === 0) {
                return {
                    tone: 'default',
                    text: 'Click the map to start the route',
                }
            }
            if (pts.length === 1) {
                return {
                    tone: 'default',
                    text: 'Click to add the next waypoint',
                }
            }
            return {
                tone: 'default',
                text: `${corridorLengthKm(pts).toFixed(0)} km route · ±${args.corridorRadiusMeters} m`,
                hint: adjustHint,
            }
        }
        case 'polygon': {
            const pts = args.polygonPoints ?? []
            if (pts.length === 0) {
                return {
                    tone: 'default',
                    text: 'Click the map to start drawing the area',
                }
            }
            if (pts.length < MIN_POLYGON_POINTS) {
                const left = MIN_POLYGON_POINTS - pts.length
                return {
                    tone: 'default',
                    text: `Add ${left} more point${left === 1 ? '' : 's'} to close the area`,
                }
            }
            const metric = `~${polygonAreaKm2(pts).toFixed(0)} km² · ${pts.length} points`
            const issues = validateCoverageShape({
                shapeType: 'polygon',
                polygonPoints: pts,
            })
            if (issues.some((i) => i.code === 'polygon-too-large')) {
                return {
                    tone: 'destructive',
                    text: 'Too large. HERE caps a single query at ~111 km per side',
                    hint: metric,
                }
            }
            const extent = maxExtentDegrees(pts)
            if (extent > MAX_BBOX_DEGREES) {
                const { tiles } = polygonTileGridSize(pts)
                const requestNote = `Exceeds HERE's ~111 km single-query limit — fetched as ${tiles} separate HERE requests`
                if (extent > MAX_SHAPE_SPAN_DEGREES * EXTENT_WARNING_RATIO) {
                    return {
                        tone: 'warning',
                        text: metric,
                        hint: `Approaching the max size · ${requestNote.toLowerCase()}`,
                    }
                }
                return { tone: 'info', text: metric, hint: requestNote }
            }
            return { tone: 'default', text: metric, hint: adjustHint }
        }
        default:
            return {
                tone: 'default',
                text: `${args.radius} km around the centre`,
                hint: 'Click to move · drag the edge to resize',
            }
    }
}
