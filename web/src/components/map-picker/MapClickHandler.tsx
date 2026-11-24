import { useMapEvents } from 'react-leaflet'

export function MapClickHandler({
    onMapClick,
}: {
    onMapClick: (lat: number, lng: number) => void
}) {
    useMapEvents({
        click: (e) => {
            onMapClick(e.latlng.lat, e.latlng.lng)
        },
    })
    return null
}
