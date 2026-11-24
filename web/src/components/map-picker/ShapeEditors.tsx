import { useCallback, useMemo, useRef, useState } from 'react'
import {
    Polygon,
    Rectangle,
    Polyline,
    Marker,
    Circle,
    useMapEvents,
    useMap,
} from 'react-leaflet'
import L from 'leaflet'
import type { CoveragePoint } from '@/lib/api/schemas/shared'
import { MAX_SHAPE_SPAN_DEGREES } from '@/lib/api/schemas/shared'

const FILL = 'hsl(var(--primary))'
/** Stroke used when the drawn shape violates a HERE limit (paired with the
 * status pill's destructive message — never color alone). */
const STROKE_INVALID = 'hsl(var(--destructive))'

const vertexIcon = L.divIcon({
    className: 'bg-transparent',
    html: '<div style="width:14px;height:14px;border-radius:50%;background:hsl(var(--primary));border:2px solid hsl(var(--background));box-shadow:0 1px 3px rgba(0,0,0,.4);cursor:grab" />',
    iconSize: [14, 14],
    iconAnchor: [7, 7],
})

let nextVertexId = 0

function useVertexEditing(
    points: readonly CoveragePoint[],
    onChange: (points: CoveragePoint[]) => void,
    { minPoints, disabled }: { minPoints: number; disabled?: boolean }
) {
    const vertexIds = useRef(new WeakMap<object, string>())
    const getVertexId = useCallback((point: CoveragePoint) => {
        let id = vertexIds.current.get(point)
        if (!id) {
            id = `coverage-vertex-${++nextVertexId}`
            vertexIds.current.set(point, id)
        }
        return id
    }, [])

    const handleVertexDrag = useCallback(
        (i: number) => (e: L.LeafletEvent) => {
            if (disabled) return
            const marker = e.target as L.Marker
            const pos = marker.getLatLng()
            const newPoint = { lat: pos.lat, lng: pos.lng }
            const oldPoint = points[i]
            if (oldPoint) vertexIds.current.set(newPoint, getVertexId(oldPoint))
            const newPoints = [...points]
            newPoints[i] = newPoint
            onChange(newPoints)
        },
        [disabled, points, onChange, getVertexId]
    )

    const handleVertexClick = useCallback(
        (i: number) => (e: L.LeafletMouseEvent) => {
            if (
                e.originalEvent.altKey &&
                !disabled &&
                points.length > minPoints
            ) {
                L.DomEvent.stopPropagation(e.originalEvent)
                onChange(points.filter((_, idx) => idx !== i))
            }
        },
        [disabled, points, minPoints, onChange]
    )

    return { getVertexId, handleVertexDrag, handleVertexClick }
}

// ── BboxEditor ────────────────────────────────────────────

interface BboxEditorProps {
    west: number
    south: number
    east: number
    north: number
    onChange: (fields: {
        west: number
        south: number
        east: number
        north: number
    }) => void
    disabled?: boolean
}

export function BboxEditor({
    west,
    south,
    east,
    north,
    onChange,
    disabled,
}: BboxEditorProps) {
    const bounds: [[number, number], [number, number]] = useMemo(
        () => [
            [south, west],
            [north, east],
        ],
        [south, west, north, east]
    )

    // Map click → relocate the box centre, preserve extents
    useMapEvents({
        click: (e) => {
            if (disabled) return
            const extentW = east - west
            const extentH = north - south
            const halfW = extentW / 2
            const halfH = extentH / 2
            onChange({
                west: Math.max(-180, e.latlng.lng - halfW),
                south: Math.max(-90, e.latlng.lat - halfH),
                east: Math.min(180, e.latlng.lng + halfW),
                north: Math.min(90, e.latlng.lat + halfH),
            })
        },
    })

    const handleSW = useCallback(
        (e: L.LeafletEvent) => {
            if (disabled) return
            const marker = e.target as L.Marker
            const pos = marker.getLatLng()
            // Keep west < east and south < north, then clamp to MAX_SHAPE_SPAN_DEGREES
            const newWest = Math.max(-180, Math.min(pos.lng, east - 0.001))
            const newSouth = Math.max(-90, Math.min(pos.lat, north - 0.001))
            onChange({
                west: Math.max(newWest, east - MAX_SHAPE_SPAN_DEGREES),
                south: Math.max(newSouth, north - MAX_SHAPE_SPAN_DEGREES),
                east,
                north,
            })
        },
        [disabled, east, north, onChange]
    )

    const handleNE = useCallback(
        (e: L.LeafletEvent) => {
            if (disabled) return
            const marker = e.target as L.Marker
            const pos = marker.getLatLng()
            const newEast = Math.min(180, Math.max(pos.lng, west + 0.001))
            const newNorth = Math.min(90, Math.max(pos.lat, south + 0.001))
            onChange({
                west,
                south,
                east: Math.min(newEast, west + MAX_SHAPE_SPAN_DEGREES),
                north: Math.min(newNorth, south + MAX_SHAPE_SPAN_DEGREES),
            })
        },
        [disabled, west, south, onChange]
    )

    return (
        <>
            <Rectangle
                bounds={bounds}
                pathOptions={{
                    color: FILL,
                    fillColor: FILL,
                    fillOpacity: 0.08,
                    weight: 2,
                }}
                interactive={false}
            />
            <Marker
                position={[south, west]}
                draggable={!disabled}
                eventHandlers={{ dragend: handleSW }}
            />
            <Marker
                position={[north, east]}
                draggable={!disabled}
                eventHandlers={{ dragend: handleNE }}
            />
        </>
    )
}

// ── CorridorEditor ────────────────────────────────────────

interface CorridorEditorProps {
    points: readonly CoveragePoint[]
    radiusMeters: number
    onChange: (fields: {
        corridorPoints: CoveragePoint[]
        corridorRadiusMeters?: number
    }) => void
    disabled?: boolean
}

export function CorridorEditor({
    points,
    radiusMeters,
    onChange,
    disabled,
}: CorridorEditorProps) {
    const undoLastPoint = () => {
        if (disabled || points.length === 0) return
        onChange({
            corridorPoints: points.slice(0, -1),
            corridorRadiusMeters: radiusMeters,
        })
    }
    const map = useMap()
    const [zoom, setZoom] = useState(map.getZoom())

    const { getVertexId, handleVertexDrag, handleVertexClick } =
        useVertexEditing(
            points,
            (newPoints) =>
                onChange({
                    corridorPoints: newPoints,
                    corridorRadiusMeters: radiusMeters,
                }),
            { minPoints: 2, disabled }
        )

    useMapEvents({
        click: (e) => {
            if (disabled) return
            const point = { lat: e.latlng.lat, lng: e.latlng.lng }
            getVertexId(point)
            onChange({
                corridorPoints: [...points, point],
                corridorRadiusMeters: radiusMeters,
            })
        },
        zoomend: () => {
            setZoom(map.getZoom())
        },
        keydown: (e) => {
            if (
                e.originalEvent.key === 'Backspace' ||
                e.originalEvent.key === 'Delete'
            ) {
                e.originalEvent.preventDefault()
                undoLastPoint()
            }
        },
    })

    // Pixel width for corridor visualisation
    const avgLat =
        points.length > 0
            ? points.reduce((s, p) => s + p.lat, 0) / points.length
            : map.getCenter().lat
    const latRad = (avgLat * Math.PI) / 180
    const metersPerPixel =
        (40075016.686 * Math.cos(latRad)) / Math.pow(2, zoom + 8)
    const visualWeight = Math.min(
        120,
        Math.max(1, (radiusMeters * 2) / metersPerPixel)
    )

    const positions: [number, number][] = useMemo(
        () => points.map((p) => [p.lat, p.lng]),
        [points]
    )

    return (
        <>
            {/* Width visualisation (underlay) */}
            {points.length >= 2 && (
                <Polyline
                    positions={positions}
                    pathOptions={{
                        color: FILL,
                        weight: visualWeight,
                        opacity: 0.15,
                        lineCap: 'round' as const,
                        lineJoin: 'round' as const,
                        fill: false,
                    }}
                    interactive={false}
                />
            )}
            {/* Centerline */}
            {points.length >= 2 && (
                <Polyline
                    positions={positions}
                    pathOptions={{
                        color: FILL,
                        weight: 3,
                        opacity: 1,
                    }}
                    interactive={false}
                />
            )}
            {/* Vertex markers */}
            {points.map((p, i) => (
                <Marker
                    key={getVertexId(p)}
                    position={[p.lat, p.lng]}
                    icon={vertexIcon}
                    draggable={!disabled}
                    eventHandlers={{
                        dragend: handleVertexDrag(i),
                        click: handleVertexClick(i),
                    }}
                />
            ))}
        </>
    )
}
// ── PolygonEditor ─────────────────────────────────────────

interface PolygonEditorProps {
    points: readonly CoveragePoint[]
    onChange: (fields: { polygonPoints: CoveragePoint[] }) => void
    disabled?: boolean
    /** Ring exceeds a HERE limit — stroke turns destructive. */
    invalid?: boolean
}

export function PolygonEditor({
    points,
    onChange,
    disabled,
    invalid,
}: PolygonEditorProps) {
    const { getVertexId, handleVertexDrag, handleVertexClick } =
        useVertexEditing(
            points,
            (newPoints) => onChange({ polygonPoints: newPoints }),
            { minPoints: 3, disabled }
        )

    useMapEvents({
        click: (e) => {
            if (disabled) return
            const point = { lat: e.latlng.lat, lng: e.latlng.lng }
            getVertexId(point)
            onChange({ polygonPoints: [...points, point] })
        },
        keydown: (e) => {
            if (
                e.originalEvent.key === 'Backspace' ||
                e.originalEvent.key === 'Delete'
            ) {
                e.originalEvent.preventDefault()
                if (!disabled && points.length > 0)
                    onChange({ polygonPoints: points.slice(0, -1) })
            }
        },
    })

    const positions: [number, number][] = useMemo(
        () => points.map((p) => [p.lat, p.lng]),
        [points]
    )

    return (
        <>
            <Polygon
                positions={positions}
                pathOptions={{
                    color: invalid ? STROKE_INVALID : FILL,
                    weight: 2,
                    fillColor: invalid ? STROKE_INVALID : FILL,
                    fillOpacity: 0.12,
                }}
                interactive={false}
            />
            {points.map((p, i) => (
                <Marker
                    key={getVertexId(p)}
                    position={[p.lat, p.lng]}
                    icon={vertexIcon}
                    draggable={!disabled}
                    eventHandlers={{
                        dragend: handleVertexDrag(i),
                        click: handleVertexClick(i),
                    }}
                />
            ))}
        </>
    )
}

// ── Read-only shape renderers ─────────────────────────────

export function ReadOnlyCircle({
    center,
    radiusKm,
}: {
    center: [number, number]
    radiusKm: number
}) {
    return (
        <Circle
            center={center}
            radius={radiusKm * 1000}
            pathOptions={{
                color: FILL,
                fillColor: FILL,
                fillOpacity: 0.08,
                weight: 2,
            }}
            interactive={false}
        />
    )
}

export function ReadOnlyBbox({
    west,
    south,
    east,
    north,
}: {
    west: number
    south: number
    east: number
    north: number
}) {
    return (
        <Rectangle
            bounds={
                [
                    [south, west],
                    [north, east],
                ] as [[number, number], [number, number]]
            }
            pathOptions={{
                color: FILL,
                fillColor: FILL,
                fillOpacity: 0.08,
                weight: 2,
            }}
            interactive={false}
        />
    )
}

export function ReadOnlyCorridor({
    points,
}: {
    points: readonly CoveragePoint[]
}) {
    const positions: [number, number][] = useMemo(
        () => points.map((p) => [p.lat, p.lng]),
        [points]
    )
    if (points.length < 2) return null
    return (
        <Polyline
            positions={positions}
            pathOptions={{
                color: FILL,
                weight: 3,
                opacity: 1,
            }}
            interactive={false}
        />
    )
}
export function ReadOnlyPolygon({
    points,
}: {
    points: readonly CoveragePoint[]
}) {
    const positions: [number, number][] = useMemo(
        () => points.map((p) => [p.lat, p.lng]),
        [points]
    )
    if (points.length < 3) return null
    return (
        <Polygon
            positions={positions}
            pathOptions={{
                color: FILL,
                weight: 2,
                fillColor: FILL,
                fillOpacity: 0.12,
            }}
            interactive={false}
        />
    )
}
