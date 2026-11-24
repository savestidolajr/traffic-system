import { useRef, useEffect } from 'react'
import { Circle, useMap } from 'react-leaflet'
import L from 'leaflet'
import { KM_PER_DEGREE_LON } from './coverage-shapes'

export function DraggableCircle({
    center,
    radius,
    onRadiusChange,
    minRadius = 1,
    maxRadius = 500,
}: {
    center: [number, number]
    radius: number
    onRadiusChange: (radius: number) => void
    minRadius?: number
    maxRadius?: number
}) {
    const map = useMap()
    const resizeHandleRef = useRef<L.CircleMarker | null>(null)
    const centerRef = useRef(center)
    const radiusRef = useRef(radius)
    const isDraggingRef = useRef(false)
    useEffect(() => {
        centerRef.current = center
        radiusRef.current = radius
    })

    useEffect(() => {
        const initialCenter = centerRef.current
        const initialRadius = radiusRef.current
        const resizeHandle = L.circleMarker(
            [
                initialCenter[0] + (initialRadius / KM_PER_DEGREE_LON) * 0.0001,
                initialCenter[1],
            ],
            {
                color: 'hsl(var(--primary))',
                fillColor: 'hsl(var(--primary))',
                fillOpacity: 1,
                radius: 6,
                weight: 2,
            }
        ).addTo(map)
        resizeHandleRef.current = resizeHandle

        const handleMouseDown = () => {
            isDraggingRef.current = true
            map.dragging.disable()
        }

        const handleMouseMove = (e: L.LeafletMouseEvent) => {
            if (!isDraggingRef.current) return

            const currentCenter = centerRef.current
            const centerLatLng = L.latLng(currentCenter[0], currentCenter[1])
            const distance = map.distance(centerLatLng, e.latlng) / 1000
            const newRadius = Math.max(
                minRadius,
                Math.min(maxRadius, Math.round(distance))
            )
            onRadiusChange(newRadius)
        }

        const handleMouseUp = () => {
            isDraggingRef.current = false
            map.dragging.enable()
        }

        resizeHandle.on('mousedown', handleMouseDown)
        map.on('mousemove', handleMouseMove)
        map.on('mouseup', handleMouseUp)

        return () => {
            resizeHandle.remove()
            resizeHandleRef.current = null
            map.off('mousemove', handleMouseMove)
            map.off('mouseup', handleMouseUp)
        }
    }, [map, onRadiusChange, minRadius, maxRadius])

    useEffect(() => {
        const resizeHandle = resizeHandleRef.current
        if (!resizeHandle) return

        const angle = 0
        const latOffset =
            (radius / KM_PER_DEGREE_LON) *
            Math.cos((angle * Math.PI) / 180) *
            0.001
        const lngOffset =
            (radius /
                (KM_PER_DEGREE_LON * Math.cos((center[0] * Math.PI) / 180))) *
            Math.sin((angle * Math.PI) / 180) *
            0.001

        resizeHandle.setLatLng([center[0] + latOffset, center[1] + lngOffset])
    }, [center, radius])

    return (
        <Circle
            center={center}
            radius={radius * 1000}
            pathOptions={{
                color: 'hsl(var(--primary))',
                fillColor: 'hsl(var(--primary))',
                fillOpacity: 0.1,
                weight: 2,
            }}
        />
    )
}
