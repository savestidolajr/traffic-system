import { cn } from '@/lib/utils'
import { MapPicker } from '@/components/map-picker'
import type { CoverageArea } from '@/lib/api/schemas/shared'

export interface CoverageMapPreviewProps {
    readonly coverage: CoverageArea
    readonly className?: string
}

/**
 * Compact read-only Leaflet map preview for station cards. Wraps
 * `<MapPicker readOnly />` with zoom controls hidden and pointer events
 * disabled — the map is visual-only, no accidental pan/zoom in a grid.
 */
export function CoverageMapPreview({
    coverage,
    className,
}: CoverageMapPreviewProps) {
    return (
        <div
            className={cn(
                'pointer-events-none shrink-0',
                // Suppress zoom controls at card scale; the HERE attribution
                // stays visible (licence requirement), shrunk to fit.
                '[&_.leaflet-control-zoom]:hidden [&_.leaflet-control-attribution]:!text-[8px] [&_.leaflet-control-attribution]:!leading-tight',
                className
            )}
        >
            <MapPicker value={coverage} readOnly height="48px" />
        </div>
    )
}
