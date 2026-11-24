import { useEffect, useRef } from 'react'
import { useMap } from 'react-leaflet'
import L from 'leaflet'
import type { CoverageArea } from '@/lib/api/schemas/shared'
import { getShapeType } from './coverage-shapes'

/**
 * Frames the viewport around the coverage shape ONCE, on mount. Without this,
 * an existing box/route/area opens at a fixed zoom with the shape half
 * off-screen. Subsequent camera moves belong to the user (and to
 * `FollowCenter`, which reacts to centre changes after mount).
 */
export function FitToShape({ value }: { readonly value?: CoverageArea }) {
    const map = useMap()
    const done = useRef(false)

    useEffect(() => {
        if (done.current || !value) return
        done.current = true

        const bounds = shapeBounds(value)
        if (bounds) map.fitBounds(bounds, { padding: [4, 4], animate: false })
    }, [map, value])

    return null
}

function shapeBounds(value: CoverageArea): L.LatLngBounds | null {
    switch (getShapeType(value)) {
        case 'bbox': {
            const { west, south, east, north } = value
            if (
                west === undefined ||
                south === undefined ||
                east === undefined ||
                north === undefined
            )
                return null
            return L.latLngBounds([south, west], [north, east])
        }
        case 'corridor': {
            const pts = value.corridorPoints
            if (!pts || pts.length < 2) return null
            return L.latLngBounds(pts.map((p) => [p.lat, p.lng]))
        }
        case 'polygon': {
            const pts = value.polygonPoints
            if (!pts || pts.length < 3) return null
            return L.latLngBounds(pts.map((p) => [p.lat, p.lng]))
        }
        default:
            return L.latLng(value.latitude, value.longitude).toBounds(
                value.radiusKm * 2000
            )
    }
}
