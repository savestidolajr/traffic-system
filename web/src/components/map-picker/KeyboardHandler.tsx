import { useEffect } from 'react'
import { useMap } from 'react-leaflet'

// This handler attaches to the Leaflet map container (not window), so
// useGlobalKeydown (window listener) does not apply. Keep as-is.

export function KeyboardHandler({
    onCenterChange,
    onRadiusChange,
    currentRadius,
    minRadius = 1,
    maxRadius = 500,
}: {
    onCenterChange: (lat: number, lng: number) => void
    onRadiusChange: (radius: number) => void
    currentRadius: number
    minRadius?: number
    maxRadius?: number
}) {
    const map = useMap()

    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (
                e.target instanceof HTMLInputElement ||
                e.target instanceof HTMLTextAreaElement
            ) {
                return
            }

            const center = map.getCenter()
            const zoom = map.getZoom()
            const moveAmount = 0.001 * Math.pow(2, 13 - zoom)

            switch (e.key) {
                case 'ArrowUp':
                    e.preventDefault()
                    onCenterChange(center.lat + moveAmount, center.lng)
                    break
                case 'ArrowDown':
                    e.preventDefault()
                    onCenterChange(center.lat - moveAmount, center.lng)
                    break
                case 'ArrowLeft':
                    e.preventDefault()
                    onCenterChange(center.lat, center.lng - moveAmount)
                    break
                case 'ArrowRight':
                    e.preventDefault()
                    onCenterChange(center.lat, center.lng + moveAmount)
                    break
                case '+':
                case '=':
                    e.preventDefault()
                    onRadiusChange(Math.min(maxRadius, currentRadius + 1))
                    break
                case '-':
                    e.preventDefault()
                    onRadiusChange(Math.max(minRadius, currentRadius - 1))
                    break
            }
        }

        map.getContainer().addEventListener('keydown', handleKeyDown)
        map.getContainer().setAttribute('tabindex', '0')

        return () => {
            map.getContainer().removeEventListener('keydown', handleKeyDown)
        }
    }, [
        map,
        onCenterChange,
        onRadiusChange,
        currentRadius,
        minRadius,
        maxRadius,
    ])

    return null
}
