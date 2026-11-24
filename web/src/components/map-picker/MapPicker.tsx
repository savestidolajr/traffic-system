import { MapContainer, Marker } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import './leaflet-defaults'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import {
    Circle as CircleIcon,
    Square,
    Route,
    Undo2,
    AlertCircle,
    RotateCcw,
    Pentagon,
    AlertTriangle,
    Info,
} from 'lucide-react'
import {
    COVERAGE_SHAPE_TYPES,
    type CoverageShapeType,
    MAX_SHAPE_SPAN_DEGREES,
} from '@/lib/api/schemas/shared'
import { MAX_CIRCLE_RADIUS_KM } from './coverage-validation'
import { maxExtentDegrees, formatCoverageSummary } from './coverage-shapes'
import type { MapPickerProps } from './types'
import { MapClickHandler } from './MapClickHandler'
import { KeyboardHandler } from './KeyboardHandler'
import { DraggableCircle } from './DraggableCircle'
import { AddressSearch } from './AddressSearch'
import { FollowCenter } from './FollowCenter'
import { FitToShape } from './FitToShape'
import { MODE_META, buildPillState } from './coverage-pill'
import { useCoverageShapeState } from './useCoverageShapeState'
import { HereTileLayer } from './HereTileLayer'
import { MapUnavailable } from './MapUnavailable'
import { useMapConfig } from '@/features/jobs/api/map-config-hooks'
import {
    BboxEditor,
    CorridorEditor,
    PolygonEditor,
    ReadOnlyCircle,
    ReadOnlyBbox,
    ReadOnlyCorridor,
    ReadOnlyPolygon,
} from './ShapeEditors'

export function MapPicker({
    value,
    onChange,
    defaultCenter = [39.8283, -98.5795],
    defaultZoom = 10,
    disabled = false,
    className,
    height = '500px',
    readOnly = false,
    loading = false,
    error,
    minRadius = 1,
    // HERE's incidents API caps circles at 50 km — wider selections only
    // produce bulletins that fail at the traffic fetch.
    maxRadius = MAX_CIRCLE_RADIUS_KM,
    allowedShapeTypes = COVERAGE_SHAPE_TYPES,
    showAddressSearch = true,
}: MapPickerProps) {
    const {
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
    } = useCoverageShapeState({
        value,
        defaultCenter,
        disabled,
        maxRadius,
        onChange,
    })
    const { data: mapConfig, isLoading: mapConfigLoading } = useMapConfig()
    const hereApiKey = mapConfig?.hereApiKey ?? ''

    if (loading || mapConfigLoading) {
        return (
            <div className={cn('flex flex-col gap-3', className)}>
                <Skeleton className="h-10 w-full" />
                <Skeleton className="w-full rounded-lg" style={{ height }} />
            </div>
        )
    }

    if (error) {
        return (
            <div
                className={cn(
                    'flex items-center gap-2 rounded-lg border border-destructive/40 p-4 text-sm text-destructive',
                    className
                )}
            >
                <AlertCircle className="size-4 shrink-0" />
                {error}
            </div>
        )
    }

    /**
     * The two boolean props are independent axes (see MapPickerProps):
     * `readOnly` swaps in the view-only summary, `disabled` keeps the editor
     * chrome but switches the editing affordances off. Pan/zoom is always
     * available in view-only mode and only locked when an editable map is
     * disabled — hence `readOnly || !disabled`.
     */
    const canPanZoom = readOnly || !disabled

    const isCircle = currentShapeType === 'circle'
    const isBbox = currentShapeType === 'bbox'
    const isCorridor = currentShapeType === 'corridor'
    const isPolygon = currentShapeType === 'polygon'
    const bboxComplete =
        west !== undefined &&
        south !== undefined &&
        east !== undefined &&
        north !== undefined
    const corridorComplete =
        corridorPoints !== undefined && corridorPoints.length >= 2
    const polygonComplete =
        polygonPoints !== undefined && polygonPoints.length >= 3
    const polygonTooLarge =
        isPolygon &&
        polygonComplete &&
        maxExtentDegrees(polygonPoints) > MAX_SHAPE_SPAN_DEGREES

    const pill = buildPillState({
        shapeType: currentShapeType,
        radius,
        west,
        south,
        east,
        north,
        latitude: center[0],
        corridorPoints,
        corridorRadiusMeters,
        polygonPoints,
    })

    // ── Map block ──

    const mapBlock = (
        <div
            className="relative z-0 rounded-lg overflow-hidden border bg-muted/20"
            style={{ height: `min(${height}, 70vh)` }}
            role="application"
            aria-label={
                readOnly
                    ? 'Map showing the coverage area'
                    : 'Interactive map for selecting coverage area'
            }
        >
            {hereApiKey ? (
                <MapContainer
                    center={center}
                    zoom={defaultZoom}
                    zoomControl={true}
                    scrollWheelZoom={canPanZoom}
                    dragging={canPanZoom}
                    doubleClickZoom={canPanZoom}
                    keyboard={canPanZoom}
                    className="h-full w-full z-0 rounded-lg"
                >
                    <HereTileLayer apiKey={hereApiKey} />
                    <FitToShape value={initialValue} />
                    <FollowCenter center={center} />

                    {!readOnly && isCircle && (
                        <MapClickHandler onMapClick={handleMapClick} />
                    )}
                    {!readOnly && isCircle && (
                        <KeyboardHandler
                            onCenterChange={handleCenterChange}
                            onRadiusChange={handleRadiusChange}
                            currentRadius={radius}
                            minRadius={minRadius}
                            maxRadius={maxRadius}
                        />
                    )}

                    {readOnly ? (
                        <>
                            {isCircle && (
                                <ReadOnlyCircle
                                    center={center}
                                    radiusKm={radius}
                                />
                            )}
                            {isBbox && bboxComplete && (
                                <ReadOnlyBbox
                                    west={west}
                                    south={south}
                                    east={east}
                                    north={north}
                                />
                            )}
                            {isBbox && !bboxComplete && (
                                <ReadOnlyCircle
                                    center={center}
                                    radiusKm={radius}
                                />
                            )}
                            {isCorridor && corridorComplete && (
                                <ReadOnlyCorridor points={corridorPoints} />
                            )}
                            {isCorridor && !corridorComplete && (
                                <ReadOnlyCircle
                                    center={center}
                                    radiusKm={radius}
                                />
                            )}
                            {isPolygon && polygonComplete && (
                                <ReadOnlyPolygon points={polygonPoints} />
                            )}
                            {isPolygon && !polygonComplete && (
                                <ReadOnlyCircle
                                    center={center}
                                    radiusKm={radius}
                                />
                            )}
                        </>
                    ) : isCircle ? (
                        <>
                            <Marker position={center} />
                            <DraggableCircle
                                center={center}
                                radius={radius}
                                onRadiusChange={handleRadiusChange}
                            />
                        </>
                    ) : isBbox && bboxComplete ? (
                        <BboxEditor
                            west={west}
                            south={south}
                            east={east}
                            north={north}
                            onChange={handleBboxChange}
                            disabled={disabled}
                        />
                    ) : isCorridor ? (
                        <CorridorEditor
                            points={corridorPoints ?? []}
                            radiusMeters={corridorRadiusMeters}
                            onChange={handleCorridorChange}
                            disabled={disabled}
                        />
                    ) : isPolygon ? (
                        <PolygonEditor
                            points={polygonPoints ?? []}
                            onChange={handlePolygonChange}
                            disabled={disabled}
                            invalid={polygonTooLarge}
                        />
                    ) : null}
                </MapContainer>
            ) : (
                <MapUnavailable compact={parseFloat(height) < 160} />
            )}

            {/* Live status pill — metric, draw progress, tiling notice, or limit problem */}
            {!readOnly && (
                <div
                    className="pointer-events-none absolute inset-x-0 bottom-3 z-10 flex justify-center"
                    aria-live="polite"
                >
                    <div
                        className={cn(
                            'flex max-w-[calc(100%-1rem)] flex-col items-center rounded-md border bg-background/90 px-3 py-1.5 text-center shadow-sm',
                            pill.tone === 'info' && 'border-info/50',
                            pill.tone === 'warning' && 'border-warning/50',
                            pill.tone === 'destructive' &&
                                'border-destructive/60'
                        )}
                    >
                        <span
                            className={cn(
                                'flex items-center gap-1.5 text-xs font-medium',
                                pill.tone === 'info' && 'text-info',
                                pill.tone === 'warning' && 'text-warning',
                                pill.tone === 'destructive' &&
                                    'text-destructive'
                            )}
                        >
                            {pill.tone === 'info' && (
                                <Info className="size-3.5 shrink-0" />
                            )}
                            {pill.tone === 'warning' && (
                                <AlertTriangle className="size-3.5 shrink-0" />
                            )}
                            {pill.tone === 'destructive' && (
                                <AlertCircle className="size-3.5 shrink-0" />
                            )}
                            {pill.text}
                        </span>
                        {pill.hint && (
                            <span className="text-2xs text-muted-foreground">
                                {pill.hint}
                            </span>
                        )}
                    </div>
                </div>
            )}
        </div>
    )

    // ── View-only: just the map plus a factual caption ──

    if (readOnly) {
        return (
            <div className={cn('isolate flex flex-col gap-2', className)}>
                {mapBlock}
                <p className="text-xs text-muted-foreground">
                    {formatCoverageSummary(
                        value ?? {
                            latitude: center[0],
                            longitude: center[1],
                            radiusKm: radius,
                        }
                    )}
                </p>
            </div>
        )
    }

    // ── Edit layout ──

    return (
        <div className={cn('isolate flex flex-col gap-3', className)}>
            {/* Toolbar */}
            <div className="flex flex-wrap items-end gap-2">
                {showAddressSearch && (
                    <div className="flex-none w-72">
                        <AddressSearch
                            onCenterChange={handleCenterChange}
                            disabled={disabled}
                        />
                    </div>
                )}

                {/* Mode switcher */}
                <ToggleGroup
                    type="single"
                    value={currentShapeType}
                    onValueChange={(v) => {
                        if (v) handleModeChange(v as CoverageShapeType)
                    }}
                    aria-label="Coverage shape"
                    size="sm"
                    className="shrink-0 pb-0.5"
                >
                    {(
                        [
                            ['circle', CircleIcon],
                            ['bbox', Square],
                            ['corridor', Route],
                            ['polygon', Pentagon],
                        ] as const
                    )
                        .filter(([mode]) => allowedShapeTypes.includes(mode))
                        .map(([mode, Icon]) => (
                            <Tooltip key={mode} delayDuration={300}>
                                <TooltipTrigger asChild>
                                    <ToggleGroupItem
                                        value={mode}
                                        aria-label={`${MODE_META[mode].label} coverage`}
                                        className="gap-1.5"
                                    >
                                        <Icon className="size-4" />
                                        <span className="hidden lg:inline">
                                            {MODE_META[mode].label}
                                        </span>
                                    </ToggleGroupItem>
                                </TooltipTrigger>
                                <TooltipContent side="bottom">
                                    <p>{MODE_META[mode].tooltip}</p>
                                </TooltipContent>
                            </Tooltip>
                        ))}
                </ToggleGroup>

                {/* One-click undo after a shape-type switch, until the
                    new shape is edited */}
                {prevShape && (
                    <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-9 shrink-0 gap-1.5 text-xs text-muted-foreground hover:text-foreground"
                        onClick={handleRestorePrevShape}
                    >
                        <RotateCcw className="size-3.5" aria-hidden />
                        Restore previous shape
                    </Button>
                )}

                {/* Mode-specific controls */}
                {isCircle && (
                    <div className="flex shrink-0 items-center gap-1.5 pb-0.5">
                        <Label
                            htmlFor="coverage-radius"
                            className="text-muted-foreground"
                        >
                            Radius
                        </Label>
                        <Input
                            id="coverage-radius"
                            type="number"
                            min={minRadius}
                            max={maxRadius}
                            value={radius}
                            onChange={(e) => {
                                const val = parseFloat(e.target.value)
                                if (
                                    !isNaN(val) &&
                                    val >= minRadius &&
                                    val <= maxRadius
                                ) {
                                    handleRadiusChange(val)
                                }
                            }}
                            disabled={disabled}
                            className="h-9 w-20"
                            aria-label="Coverage radius in kilometers"
                        />
                        <span className="text-xs text-muted-foreground">
                            km
                        </span>
                    </div>
                )}

                {isCorridor && (
                    <div className="flex shrink-0 items-center gap-1.5 pb-0.5">
                        <Label
                            htmlFor="corridor-width"
                            className="text-muted-foreground"
                        >
                            Width
                        </Label>
                        <Input
                            id="corridor-width"
                            type="number"
                            min={1}
                            max={5000}
                            value={corridorRadiusMeters}
                            onChange={(e) => {
                                const val = parseFloat(e.target.value)
                                if (!isNaN(val)) {
                                    handleCorridorWidthChange(val)
                                }
                            }}
                            disabled={disabled}
                            className="h-9 w-20"
                            aria-label="Corridor width in meters"
                        />
                        <span className="text-xs text-muted-foreground">m</span>
                    </div>
                )}

                {(isCorridor || isPolygon) && (
                    <Tooltip delayDuration={300}>
                        <TooltipTrigger asChild>
                            <Button
                                type="button"
                                variant="outline"
                                size="icon"
                                className="size-9 shrink-0"
                                onClick={
                                    isCorridor
                                        ? handleUndoCorridorPoint
                                        : handleUndoPolygonPoint
                                }
                                disabled={
                                    disabled ||
                                    (isCorridor
                                        ? !corridorPoints?.length
                                        : !polygonPoints?.length)
                                }
                                aria-label="Undo last point"
                            >
                                <Undo2 />
                            </Button>
                        </TooltipTrigger>
                        <TooltipContent side="bottom">
                            <p>Undo last point (or press Backspace)</p>
                        </TooltipContent>
                    </Tooltip>
                )}

                <Tooltip delayDuration={300}>
                    <TooltipTrigger asChild>
                        <Button
                            type="button"
                            variant="outline"
                            size="icon"
                            className="size-9 shrink-0"
                            onClick={handleReset}
                            disabled={disabled}
                            aria-label="Undo all coverage changes"
                        >
                            <RotateCcw />
                        </Button>
                    </TooltipTrigger>
                    <TooltipContent side="bottom">
                        <p>Undo all coverage changes</p>
                    </TooltipContent>
                </Tooltip>
            </div>
            {mapBlock}
        </div>
    )
}
