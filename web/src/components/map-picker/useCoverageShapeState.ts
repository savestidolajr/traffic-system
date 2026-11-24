import { useCallback, useDeferredValue, useRef, useState } from 'react'
import type {
    CoverageArea,
    CoveragePoint,
    CoverageShapeType,
} from '@/lib/api/schemas/shared'
import {
    MAX_CORRIDOR_RADIUS_M,
    MIN_CORRIDOR_RADIUS_M,
} from '@/lib/api/schemas/shared'
import {
    deriveBboxCircle,
    deriveCorridorCircle,
    getShapeType,
    seedBboxAround,
} from './coverage-shapes'

interface ShapeStash {
    circle?: { center: [number, number]; radius: number }
    bbox?: { west: number; south: number; east: number; north: number }
    corridor?: { points: CoveragePoint[]; radiusMeters: number }
    polygon?: { points: CoveragePoint[] }
}

interface UseCoverageShapeStateArgs {
    value?: CoverageArea
    defaultCenter: [number, number]
    disabled: boolean
    maxRadius: number
    onChange?: (coverage: CoverageArea) => void
}

export function useCoverageShapeState({
    value,
    defaultCenter,
    disabled,
    maxRadius,
    onChange,
}: UseCoverageShapeStateArgs) {
    const [center, setCenter] = useState<[number, number]>(
        value ? [value.latitude, value.longitude] : defaultCenter
    )
    const [radius, setRadius] = useState<number>(value?.radiusKm || 50)
    const [currentShapeType, setCurrentShapeType] = useState<CoverageShapeType>(
        getShapeType(value)
    )
    const [west, setWest] = useState<number | undefined>(value?.west)
    const [south, setSouth] = useState<number | undefined>(value?.south)
    const [east, setEast] = useState<number | undefined>(value?.east)
    const [north, setNorth] = useState<number | undefined>(value?.north)
    const [corridorPoints, setCorridorPoints] = useState<
        CoveragePoint[] | undefined
    >(value?.corridorPoints)
    const [corridorRadiusMeters, setCorridorRadiusMeters] = useState<number>(
        value?.corridorRadiusMeters ?? 500
    )
    const [polygonPoints, setPolygonPoints] = useState<
        CoveragePoint[] | undefined
    >(value?.polygonPoints)

    const [initialValue] = useState(value)
    const stash = useRef<ShapeStash>({})
    const [prevShape, setPrevShape] = useState<CoverageArea | null>(null)

    // External value -> local-state sync. Every setState below uses a
    // functional update that RETURNS THE CURRENT STATE when nothing changed,
    // so an unchanged incoming value cannot force re-renders (React bails out
    // on identical state). Without the guards this loop ran 11 setStates on
    // every deferred value change, churning renders exactly when a shape-type
    // switch was saving — the dead Save observed live (bug-bash-2026-08-05
    // finding 30: click fires, no submit event, until any further edit).
    const deferredValue = useDeferredValue(value)
    const [syncedValue, setSyncedValue] = useState(deferredValue)
    if (deferredValue !== syncedValue) {
        setSyncedValue(deferredValue)
        if (deferredValue) {
            const next = deferredValue
            setCenter((c) =>
                c[0] === next.latitude && c[1] === next.longitude
                    ? c
                    : [next.latitude, next.longitude]
            )
            setRadius((r) => (r === next.radiusKm ? r : next.radiusKm))
            const nextType = getShapeType(next)
            setCurrentShapeType((t) => (t === nextType ? t : nextType))
            setWest((w) => (w === next.west ? w : next.west))
            setSouth((s) => (s === next.south ? s : next.south))
            setEast((e) => (e === next.east ? e : next.east))
            setNorth((n) => (n === next.north ? n : next.north))
            setCorridorPoints((p) =>
                p === next.corridorPoints ? p : next.corridorPoints
            )
            setCorridorRadiusMeters((m) =>
                m === (next.corridorRadiusMeters ?? 500)
                    ? m
                    : (next.corridorRadiusMeters ?? 500)
            )
            setPolygonPoints((p) =>
                p === next.polygonPoints ? p : next.polygonPoints
            )
        }
    }

    const handleMapClick = useCallback(
        (lat: number, lng: number) => {
            if (disabled) return
            const newCenter: [number, number] = [lat, lng]
            setCenter(newCenter)
            setPrevShape(null)
            onChange?.({
                latitude: lat,
                longitude: lng,
                radiusKm: radius,
                shapeType: 'circle',
            })
        },
        [disabled, radius, onChange]
    )

    const handleRadiusChange = useCallback(
        (newRadius: number) => {
            if (disabled) return
            setRadius(newRadius)
            setPrevShape(null)
            onChange?.({
                latitude: center[0],
                longitude: center[1],
                radiusKm: newRadius,
                shapeType: 'circle',
            })
        },
        [disabled, center, onChange]
    )

    const handleCenterChange = useCallback(
        (lat: number, lng: number) => {
            if (disabled) return
            const newCenter: [number, number] = [lat, lng]
            setCenter(newCenter)
            setPrevShape(null)

            if (
                currentShapeType === 'bbox' &&
                west !== undefined &&
                south !== undefined &&
                east !== undefined &&
                north !== undefined
            ) {
                const extentW = east - west
                const extentH = north - south
                const halfW = extentW / 2
                const halfH = extentH / 2
                const newWest = Math.max(-180, lng - halfW)
                const newSouth = Math.max(-90, lat - halfH)
                const newEast = Math.min(180, lng + halfW)
                const newNorth = Math.min(90, lat + halfH)
                setWest(newWest)
                setSouth(newSouth)
                setEast(newEast)
                setNorth(newNorth)
                const circle = deriveBboxCircle(
                    newWest,
                    newSouth,
                    newEast,
                    newNorth
                )
                setRadius(circle.radiusKm)
                onChange?.({
                    latitude: circle.latitude,
                    longitude: circle.longitude,
                    radiusKm: circle.radiusKm,
                    shapeType: 'bbox',
                    west: newWest,
                    south: newSouth,
                    east: newEast,
                    north: newNorth,
                })
            } else if (
                currentShapeType === 'corridor' ||
                currentShapeType === 'polygon'
            ) {
                // Just pan — the drawn geometry is left alone
            } else {
                onChange?.({
                    latitude: lat,
                    longitude: lng,
                    radiusKm: radius,
                    shapeType: 'circle',
                })
            }
        },
        [disabled, radius, onChange, currentShapeType, west, south, east, north]
    )

    const handleModeChange = useCallback(
        (newType: CoverageShapeType) => {
            if (disabled || newType === currentShapeType) return

            setPrevShape({
                latitude: center[0],
                longitude: center[1],
                radiusKm: radius,
                shapeType: currentShapeType,
                west,
                south,
                east,
                north,
                corridorPoints,
                corridorRadiusMeters,
                polygonPoints,
            })

            if (currentShapeType === 'circle') {
                stash.current.circle = { center, radius }
            } else if (
                currentShapeType === 'bbox' &&
                west !== undefined &&
                south !== undefined &&
                east !== undefined &&
                north !== undefined
            ) {
                stash.current.bbox = { west, south, east, north }
            } else if (
                currentShapeType === 'corridor' &&
                corridorPoints &&
                corridorPoints.length > 0
            ) {
                stash.current.corridor = {
                    points: corridorPoints,
                    radiusMeters: corridorRadiusMeters,
                }
            } else if (
                currentShapeType === 'polygon' &&
                polygonPoints &&
                polygonPoints.length > 0
            ) {
                stash.current.polygon = { points: polygonPoints }
            }

            setCurrentShapeType(newType)

            if (newType === 'circle') {
                const restored = stash.current.circle
                const nextCenter = restored?.center ?? center
                const nextRadius = Math.min(
                    restored?.radius ?? radius,
                    maxRadius
                )
                setCenter(nextCenter)
                setRadius(nextRadius)
                setWest(undefined)
                setSouth(undefined)
                setEast(undefined)
                setNorth(undefined)
                setCorridorPoints(undefined)
                setPolygonPoints(undefined)
                // Every mode switch must carry the target shapeType: the
                // schedule form keys its coverage off it, and a payload without
                // it clears the field instead of changing it (the dead-Save
                // cause in bug-bash-2026-08-05 finding 30).
                onChange?.({
                    latitude: nextCenter[0],
                    longitude: nextCenter[1],
                    radiusKm: nextRadius,
                    shapeType: 'circle',
                })
            } else if (newType === 'bbox') {
                const box =
                    stash.current.bbox ?? seedBboxAround(center[0], center[1])
                const circle = deriveBboxCircle(
                    box.west ?? 0,
                    box.south ?? 0,
                    box.east ?? 0,
                    box.north ?? 0
                )
                setWest(box.west)
                setSouth(box.south)
                setEast(box.east)
                setNorth(box.north)
                setCorridorPoints(undefined)
                setPolygonPoints(undefined)
                setCenter([circle.latitude, circle.longitude])
                setRadius(circle.radiusKm)
                onChange?.({
                    latitude: circle.latitude,
                    longitude: circle.longitude,
                    radiusKm: circle.radiusKm,
                    shapeType: 'bbox',
                    west: box.west,
                    south: box.south,
                    east: box.east,
                    north: box.north,
                })
            } else if (newType === 'corridor') {
                const restored = stash.current.corridor
                const pts = restored?.points ?? []
                const width = restored?.radiusMeters ?? 500
                setCorridorPoints(pts)
                setCorridorRadiusMeters(width)
                setWest(undefined)
                setSouth(undefined)
                setEast(undefined)
                setNorth(undefined)
                setPolygonPoints(undefined)
                if (pts.length >= 2) {
                    const circle = deriveCorridorCircle(pts, width)
                    setCenter([circle.latitude, circle.longitude])
                    setRadius(circle.radiusKm)
                    onChange?.({
                        latitude: circle.latitude,
                        longitude: circle.longitude,
                        radiusKm: circle.radiusKm,
                        shapeType: 'corridor',
                        corridorPoints: pts,
                        corridorRadiusMeters: width,
                    })
                } else {
                    onChange?.({
                        latitude: center[0],
                        longitude: center[1],
                        radiusKm: radius,
                        shapeType: 'corridor',
                        corridorPoints: pts,
                        corridorRadiusMeters: width,
                    })
                }
            } else {
                const restored = stash.current.polygon
                const pts = restored?.points ?? []
                setPolygonPoints(pts)
                setCorridorPoints(undefined)
                setWest(undefined)
                setSouth(undefined)
                setEast(undefined)
                setNorth(undefined)
                if (pts.length >= 3) {
                    const circle = deriveCorridorCircle(pts, 0)
                    setCenter([circle.latitude, circle.longitude])
                    setRadius(circle.radiusKm)
                    onChange?.({
                        latitude: circle.latitude,
                        longitude: circle.longitude,
                        radiusKm: circle.radiusKm,
                        shapeType: 'polygon',
                        polygonPoints: pts,
                    })
                } else {
                    onChange?.({
                        latitude: center[0],
                        longitude: center[1],
                        radiusKm: radius,
                        shapeType: 'polygon',
                        polygonPoints: pts,
                    })
                }
            }
        },
        [
            disabled,
            currentShapeType,
            center,
            radius,
            maxRadius,
            west,
            south,
            east,
            north,
            corridorPoints,
            corridorRadiusMeters,
            polygonPoints,
            onChange,
        ]
    )

    const handleBboxChange = useCallback(
        (fields: {
            west: number
            south: number
            east: number
            north: number
        }) => {
            setPrevShape(null)
            setWest(fields.west)
            setSouth(fields.south)
            setEast(fields.east)
            setNorth(fields.north)
            const circle = deriveBboxCircle(
                fields.west,
                fields.south,
                fields.east,
                fields.north
            )
            setCenter([circle.latitude, circle.longitude])
            setRadius(circle.radiusKm)
            onChange?.({
                latitude: circle.latitude,
                longitude: circle.longitude,
                radiusKm: circle.radiusKm,
                shapeType: 'bbox',
                west: fields.west,
                south: fields.south,
                east: fields.east,
                north: fields.north,
            })
        },
        [onChange]
    )

    const handleCorridorChange = useCallback(
        (fields: {
            corridorPoints: CoveragePoint[]
            corridorRadiusMeters?: number
        }) => {
            const pts = fields.corridorPoints
            const rm = fields.corridorRadiusMeters ?? corridorRadiusMeters
            setPrevShape(null)
            setCorridorPoints(pts)
            setCorridorRadiusMeters(rm)

            if (pts.length >= 2) {
                const circle = deriveCorridorCircle(pts, rm)
                setCenter([circle.latitude, circle.longitude])
                setRadius(circle.radiusKm)
                onChange?.({
                    latitude: circle.latitude,
                    longitude: circle.longitude,
                    radiusKm: circle.radiusKm,
                    shapeType: 'corridor',
                    corridorPoints: pts,
                    corridorRadiusMeters: rm,
                })
            } else {
                onChange?.({
                    latitude: center[0],
                    longitude: center[1],
                    radiusKm: radius,
                    shapeType: 'corridor',
                    corridorPoints: pts,
                    corridorRadiusMeters: rm,
                })
            }
        },
        [corridorRadiusMeters, center, radius, onChange]
    )

    const handlePolygonChange = useCallback(
        (fields: { polygonPoints: CoveragePoint[] }) => {
            const pts = fields.polygonPoints
            setPrevShape(null)
            setPolygonPoints(pts)

            if (pts.length >= 3) {
                const circle = deriveCorridorCircle(pts, 0)
                setCenter([circle.latitude, circle.longitude])
                setRadius(circle.radiusKm)
                onChange?.({
                    latitude: circle.latitude,
                    longitude: circle.longitude,
                    radiusKm: circle.radiusKm,
                    shapeType: 'polygon',
                    polygonPoints: pts,
                })
            } else {
                onChange?.({
                    latitude: center[0],
                    longitude: center[1],
                    radiusKm: radius,
                    shapeType: 'polygon',
                    polygonPoints: pts,
                })
            }
        },
        [center, radius, onChange]
    )

    const handleCorridorWidthChange = useCallback(
        (newWidth: number) => {
            const clamped = Math.min(
                MAX_CORRIDOR_RADIUS_M,
                Math.max(MIN_CORRIDOR_RADIUS_M, newWidth)
            )
            handleCorridorChange({
                corridorPoints: corridorPoints ?? [],
                corridorRadiusMeters: clamped,
            })
        },
        [corridorPoints, handleCorridorChange]
    )

    const handleUndoCorridorPoint = useCallback(() => {
        const pts = corridorPoints ?? []
        if (pts.length === 0) return
        handleCorridorChange({ corridorPoints: pts.slice(0, -1) })
    }, [corridorPoints, handleCorridorChange])

    const handleUndoPolygonPoint = useCallback(() => {
        const pts = polygonPoints ?? []
        if (pts.length === 0) return
        handlePolygonChange({ polygonPoints: pts.slice(0, -1) })
    }, [polygonPoints, handlePolygonChange])

    const handleRestorePrevShape = useCallback(() => {
        if (disabled || !prevShape) return
        setCenter([prevShape.latitude, prevShape.longitude])
        setRadius(prevShape.radiusKm)
        setCurrentShapeType(getShapeType(prevShape))
        setWest(prevShape.west)
        setSouth(prevShape.south)
        setEast(prevShape.east)
        setNorth(prevShape.north)
        setCorridorPoints(prevShape.corridorPoints)
        setCorridorRadiusMeters(prevShape.corridorRadiusMeters ?? 500)
        setPolygonPoints(prevShape.polygonPoints)
        setPrevShape(null)
        onChange?.(prevShape)
    }, [disabled, prevShape, onChange])

    const handleReset = useCallback(() => {
        if (disabled) return
        stash.current = {}
        setPrevShape(null)
        const initial = initialValue
        if (initial) {
            setCenter([initial.latitude, initial.longitude])
            setRadius(initial.radiusKm)
            setCurrentShapeType(getShapeType(initial))
            setWest(initial.west)
            setSouth(initial.south)
            setEast(initial.east)
            setNorth(initial.north)
            setCorridorPoints(initial.corridorPoints)
            setCorridorRadiusMeters(initial.corridorRadiusMeters ?? 500)
            setPolygonPoints(initial.polygonPoints)
            onChange?.(initial)
            return
        }
        setCenter(defaultCenter)
        setRadius(50)
        setCurrentShapeType('circle')
        setWest(undefined)
        setSouth(undefined)
        setEast(undefined)
        setNorth(undefined)
        setCorridorPoints(undefined)
        setCorridorRadiusMeters(500)
        setPolygonPoints(undefined)
        onChange?.({
            latitude: defaultCenter[0],
            longitude: defaultCenter[1],
            radiusKm: 50,
            shapeType: 'circle',
        })
    }, [disabled, defaultCenter, onChange, initialValue])

    return {
        center,
        radius,
        currentShapeType,
        west,
        south,
        east,
        north,
        corridorPoints,
        corridorRadiusMeters,
        polygonPoints,
        initialValue,
        prevShape,
        handleMapClick,
        handleRadiusChange,
        handleCenterChange,
        handleModeChange,
        handleBboxChange,
        handleCorridorChange,
        handlePolygonChange,
        handleCorridorWidthChange,
        handleUndoCorridorPoint,
        handleUndoPolygonPoint,
        handleRestorePrevShape,
        handleReset,
    }
}
