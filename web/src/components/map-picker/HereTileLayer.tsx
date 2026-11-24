import { TileLayer } from 'react-leaflet'

/**
 * HERE Raster Tile API v3 basemap for Leaflet maps, in HERE's own dark
 * `lite.night` style so it matches the console and the HERE JS evidence map.
 * The key is the browser map key served by `GET /api/map-config`; callers
 * render `<MapUnavailable />` instead when it is empty. There is deliberately
 * no keyless fallback: the public OpenStreetMap tile server is not a service a
 * commercial console may depend on (no SLA, blocks without notice).
 */
export function HereTileLayer({ apiKey }: { readonly apiKey: string }) {
    return (
        <TileLayer
            attribution="&copy; HERE"
            url={`https://maps.hereapi.com/v3/base/mc/{z}/{x}/{y}/png8?style=lite.night&size=256&apiKey=${encodeURIComponent(apiKey)}`}
        />
    )
}
