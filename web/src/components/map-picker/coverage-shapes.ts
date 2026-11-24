import type {
    CoverageArea,
    CoveragePoint,
    CoverageShapeType,
} from '@/lib/api/schemas/shared'
import {
    MAX_BBOX_DEGREES,
    MAX_SHAPE_SPAN_DEGREES,
} from '@/lib/api/schemas/shared'

/**
 * Pure helpers for coverage shapes. The enclosing-circle derivations mirror
 * the backend (`CoverageArea.CreateBoundingBox` / `CreateCorridor`) exactly so
 * the values the form previews match what the server will persist.
 */

const EARTH_RADIUS_KM = 6371

/** Kilometres per degree of latitude (near-constant across the globe). */
export const KM_PER_DEGREE_LAT = 110.574
/**
 * Kilometres per degree of longitude at the equator — multiply by
 * cos(latitude) for the spacing at a given latitude.
 */
export const KM_PER_DEGREE_LON = 111.32

export function greatCircleKm(
    lat1: number,
    lng1: number,
    lat2: number,
    lng2: number
): number {
    const dLat = ((lat2 - lat1) * Math.PI) / 180
    const dLng = ((lng2 - lng1) * Math.PI) / 180
    const a =
        Math.sin(dLat / 2) * Math.sin(dLat / 2) +
        Math.cos((lat1 * Math.PI) / 180) *
            Math.cos((lat2 * Math.PI) / 180) *
            Math.sin(dLng / 2) *
            Math.sin(dLng / 2)
    return EARTH_RADIUS_KM * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}

/** Total polyline length following the drawn waypoints. */
export function corridorLengthKm(
    points: readonly CoveragePoint[] | undefined
): number {
    if (!points || points.length < 2) return 0
    let total = 0
    for (let i = 1; i < points.length; i++) {
        total += greatCircleKm(
            points[i - 1].lat,
            points[i - 1].lng,
            points[i].lat,
            points[i].lng
        )
    }
    return total
}

/**
 * Approximate ring area via the shoelace formula on a local equirectangular
 * projection (lng scaled by cos of the ring's mid-latitude). Plenty accurate
 * at the sub-degree extents HERE allows.
 */
export function polygonAreaKm2(
    points: readonly CoveragePoint[] | undefined
): number {
    if (!points || points.length < 3) return 0
    let south = Infinity
    let north = -Infinity
    for (const p of points) {
        if (p.lat < south) south = p.lat
        if (p.lat > north) north = p.lat
    }
    const midLatRad = (((south + north) / 2) * Math.PI) / 180
    const kmPerLat = KM_PER_DEGREE_LAT
    const kmPerLng = KM_PER_DEGREE_LON * Math.cos(midLatRad)
    let twiceArea = 0
    for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
        const xi = points[i].lng * kmPerLng
        const yi = points[i].lat * kmPerLat
        const xj = points[j].lng * kmPerLng
        const yj = points[j].lat * kmPerLat
        twiceArea += xj * yi - xi * yj
    }
    return Math.abs(twiceArea) / 2
}

/** Largest side of the points' enclosing box, in degrees (drawable cap is 5°; HERE tiles at 1°). */
export function maxExtentDegrees(
    points: readonly CoveragePoint[] | undefined
): number {
    if (!points || points.length === 0) return 0
    let south = Infinity
    let north = -Infinity
    let west = Infinity
    let east = -Infinity
    for (const p of points) {
        if (p.lat < south) south = p.lat
        if (p.lat > north) north = p.lat
        if (p.lng < west) west = p.lng
        if (p.lng > east) east = p.lng
    }
    return Math.max(north - south, east - west)
}

/**
 * Mirrors the backend `HereGeoFilter.TileBoundingBox` grid math: how many
 * sub-1-degree HERE queries a bbox splits into. A box already within HERE's
 * per-tile limit is a single tile (no extra requests).
 */
export function tileGridSize(
    west: number,
    south: number,
    east: number,
    north: number
): { cols: number; rows: number; tiles: number } {
    const cols = Math.max(1, Math.ceil((east - west) / MAX_BBOX_DEGREES))
    const rows = Math.max(1, Math.ceil((north - south) / MAX_BBOX_DEGREES))
    return { cols, rows, tiles: cols * rows }
}

/** `tileGridSize` for a polygon ring's enclosing box (what the backend actually tiles). */
export function polygonTileGridSize(
    points: readonly CoveragePoint[] | undefined
): { cols: number; rows: number; tiles: number } {
    if (!points || points.length === 0) return { cols: 1, rows: 1, tiles: 1 }
    let south = Infinity
    let north = -Infinity
    let west = Infinity
    let east = -Infinity
    for (const p of points) {
        if (p.lat < south) south = p.lat
        if (p.lat > north) north = p.lat
        if (p.lng < west) west = p.lng
        if (p.lng > east) east = p.lng
    }
    return tileGridSize(west, south, east, north)
}

export interface EnclosingCircle {
    latitude: number
    longitude: number
    radiusKm: number
}

/** Centre = box midpoint, radius = half-diagonal (matches backend). */
export function deriveBboxCircle(
    west: number,
    south: number,
    east: number,
    north: number
): EnclosingCircle {
    const latitude = (south + north) / 2
    const longitude = (west + east) / 2
    return {
        latitude,
        longitude,
        radiusKm: Math.max(1, greatCircleKm(latitude, longitude, north, east)),
    }
}

/** Centre of the points' bbox, radius = half-diagonal + corridor width. */
export function deriveCorridorCircle(
    points: readonly CoveragePoint[],
    radiusMeters: number
): EnclosingCircle {
    let south = Infinity
    let north = -Infinity
    let west = Infinity
    let east = -Infinity
    for (const p of points) {
        if (p.lat < south) south = p.lat
        if (p.lat > north) north = p.lat
        if (p.lng < west) west = p.lng
        if (p.lng > east) east = p.lng
    }
    const latitude = (south + north) / 2
    const longitude = (west + east) / 2
    return {
        latitude,
        longitude,
        radiusKm: Math.max(
            1,
            greatCircleKm(latitude, longitude, north, east) +
                radiusMeters / 1000
        ),
    }
}

/**
 * Seed a bbox around a centre when the user switches into box mode: 0.3° per
 * side — comfortably inside HERE's 1° limit and a useful metro-sized start.
 */
export function seedBboxAround(
    latitude: number,
    longitude: number
): Pick<CoverageArea, 'west' | 'south' | 'east' | 'north'> {
    const half = 0.15
    return {
        west: Math.max(-180, longitude - half),
        south: Math.max(-90, latitude - half),
        east: Math.min(180, longitude + half),
        north: Math.min(90, latitude + half),
    }
}

export function getShapeType(
    area: Pick<CoverageArea, 'shapeType'> | null | undefined
): CoverageShapeType {
    return area?.shapeType ?? 'circle'
}

/** True when the bbox is fully specified and inside the drawable extent cap. */
export function isValidBbox(
    area: Pick<CoverageArea, 'west' | 'south' | 'east' | 'north'>
): boolean {
    const { west, south, east, north } = area
    if (
        west === undefined ||
        south === undefined ||
        east === undefined ||
        north === undefined
    )
        return false
    return (
        east > west &&
        north > south &&
        east - west <= MAX_SHAPE_SPAN_DEGREES &&
        north - south <= MAX_SHAPE_SPAN_DEGREES
    )
}

/**
 * True when the ring has at least 3 points and its enclosing box fits the
 * drawable extent cap (the backend tiles the enclosing box into sub-1-degree
 * HERE queries, then post-filters point-in-polygon).
 */
export function isValidPolygonRing(
    points: readonly CoveragePoint[] | undefined
): boolean {
    if (!points || points.length < 3) return false
    let south = Infinity
    let north = -Infinity
    let west = Infinity
    let east = -Infinity
    for (const p of points) {
        if (p.lat < south) south = p.lat
        if (p.lat > north) north = p.lat
        if (p.lng < west) west = p.lng
        if (p.lng > east) east = p.lng
    }
    return (
        north - south <= MAX_SHAPE_SPAN_DEGREES &&
        east - west <= MAX_SHAPE_SPAN_DEGREES
    )
}

/** Approximate box dimensions in km (width = lng span at the box latitude). */
export function bboxDimensionsKm(
    area: Pick<CoverageArea, 'west' | 'south' | 'east' | 'north' | 'latitude'>
): { w: number; h: number } {
    const w =
        ((area.east ?? 0) - (area.west ?? 0)) *
        KM_PER_DEGREE_LON *
        Math.cos((area.latitude * Math.PI) / 180)
    const h = ((area.north ?? 0) - (area.south ?? 0)) * KM_PER_DEGREE_LAT
    return { w, h }
}

/**
 * One-line factual description of a coverage area, used by studio step
 * summaries and the read-only map caption.
 */
export function formatCoverageSummary(
    area: CoverageArea | null | undefined
): string {
    if (!area) return 'Not configured'
    switch (getShapeType(area)) {
        case 'bbox': {
            if (!isValidBbox(area)) return 'Box (incomplete)'
            const { w, h } = bboxDimensionsKm(area)
            return `Box · ~${w.toFixed(0)} × ${h.toFixed(0)} km`
        }
        case 'corridor': {
            const length = corridorLengthKm(area.corridorPoints)
            const width = area.corridorRadiusMeters ?? 0
            if (length === 0) return 'Route (incomplete)'
            return `Route · ${length.toFixed(0)} km · ±${width} m`
        }
        case 'polygon': {
            const count = area.polygonPoints?.length ?? 0
            const area2 = polygonAreaKm2(area.polygonPoints)
            if (area2 === 0)
                return `Area (incomplete · ${count} point${count === 1 ? '' : 's'})`
            return `Area · ~${area2.toFixed(0)} km² · ${count} points`
        }
        default:
            return `${area.radiusKm} km radius`
    }
}
