import { useEffect, useRef } from 'react'
import { useMap } from 'react-leaflet'

/**
 * Keeps the Leaflet viewport on the picked centre. `MapContainer`'s
 * `center` prop only applies at mount — without this, selecting an
 * address moves the marker but leaves the camera behind. The first run
 * is skipped: initial framing belongs to `FitToShape`.
 */
export function FollowCenter({
    center,
}: {
    readonly center: readonly [number, number]
}) {
    const map = useMap()
    const mounted = useRef(false)
    const [lat, lng] = center
    useEffect(() => {
        if (!mounted.current) {
            mounted.current = true
            return
        }
        map.setView([lat, lng])
    }, [map, lat, lng])
    return null
}
