import type { CoverageArea, CoverageShapeType } from '@/lib/api/schemas/shared'

/** One address-search hit, normalised from HERE Geocoding & Search. */
export interface GeocodeResult {
    readonly id: string
    /** Full address, e.g. "12 Smith St, Cairns QLD 4870, Australia". */
    readonly label: string
    readonly lat: number
    readonly lng: number
}

/**
 * `disabled` and `readOnly` are two independent axes:
 * - `disabled`: editing affordances off, but the editor chrome (toolbar,
 *   status pill) still renders — and pan/zoom is locked with it.
 * - `readOnly`: a view-only summary — no toolbar, no pill, no handles; the
 *   map still pans and zooms so the shape can be inspected.
 */
export interface MapPickerProps {
    value?: CoverageArea
    onChange?: (coverage: CoverageArea) => void
    defaultCenter?: [number, number]
    defaultZoom?: number
    /** Editing affordances off; editor chrome stays (see interface doc). */
    disabled?: boolean
    className?: string
    height?: string
    /** View-only: pan/zoom allowed, but no edit affordances at all —
     *  no toolbar, no status pill, no resize handles. */
    readOnly?: boolean
    /** Shape modes shown in the toolbar; source-specific maps are circle-only. */
    allowedShapeTypes?: readonly CoverageShapeType[]
    /** Show the address search control; source-specific maps use direct map placement. */
    showAddressSearch?: boolean
    loading?: boolean
    error?: string
    minRadius?: number
    maxRadius?: number
}
