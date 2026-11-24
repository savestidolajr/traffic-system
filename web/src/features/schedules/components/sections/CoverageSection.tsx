import { useState } from 'react'
import type { UseFormReturn } from 'react-hook-form'
import { AlertCircle, CloudSun, Fuel, MapPin, Radio } from 'lucide-react'
import { MapPicker } from '@/components/map-picker'
import { Button } from '@/components/ui/button'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import type { CoverageArea } from '@/lib/api/schemas/shared'
import type { ScheduleFormData } from '../../types'

type CoverageLayer = 'traffic' | 'weather' | 'fuel'

interface CoverageSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
    /** Map height; the studio's coverage step uses a taller canvas. */
    readonly mapHeight?: string
}

const TRAFFIC_FIELDS = [
    'latitude',
    'longitude',
    'radiusKm',
    'shapeType',
    'west',
    'south',
    'east',
    'north',
    'corridorPoints',
    'corridorRadiusMeters',
    'polygonPoints',
] as const

function coverageFromForm(form: UseFormReturn<ScheduleFormData>): CoverageArea {
    return {
        latitude: form.watch('latitude'),
        longitude: form.watch('longitude'),
        radiusKm: form.watch('radiusKm'),
        shapeType: form.watch('shapeType'),
        west: form.watch('west'),
        south: form.watch('south'),
        east: form.watch('east'),
        north: form.watch('north'),
        corridorPoints: form.watch('corridorPoints'),
        corridorRadiusMeters: form.watch('corridorRadiusMeters'),
        polygonPoints: form.watch('polygonPoints'),
    }
}

function inheritedCircle(
    traffic: CoverageArea,
    layer: Exclude<CoverageLayer, 'traffic'>
): CoverageArea {
    return {
        latitude: traffic.latitude,
        longitude: traffic.longitude,
        // Weather is point-based; the radius is only a visual editing handle.
        // Fuel's provider has a 30 km platform cap, so never preview beyond it.
        radiusKm:
            layer === 'fuel' ? Math.min(Math.max(traffic.radiusKm, 1), 30) : 1,
        shapeType: 'circle',
    }
}

export function CoverageSection({
    form,
    mapHeight = '300px',
}: CoverageSectionProps) {
    const [layer, setLayer] = useState<CoverageLayer>('traffic')
    const traffic = coverageFromForm(form)
    const weatherCoverage = form.watch('weatherCoverage')
    const fuelCoverage = form.watch('fuelCoverage')
    const override =
        layer === 'weather'
            ? weatherCoverage
            : layer === 'fuel'
              ? fuelCoverage
              : undefined
    const value =
        layer === 'traffic'
            ? traffic
            : (override ?? inheritedCircle(traffic, layer))
    const inherited = layer !== 'traffic' && !override

    const setTrafficCoverage = (coverage: CoverageArea) => {
        for (const field of TRAFFIC_FIELDS) {
            const value = coverage[field]
            form.setValue(field, value as never, {
                shouldValidate: true,
                shouldDirty: true,
            })
        }
    }

    const setSourceCoverage = (coverage: CoverageArea) => {
        form.setValue(
            layer === 'weather' ? 'weatherCoverage' : 'fuelCoverage',
            coverage,
            { shouldValidate: true, shouldDirty: true }
        )
    }

    const shapeError =
        layer === 'traffic'
            ? form.formState.errors.shapeType?.message
            : undefined

    return (
        <div className="flex flex-col gap-4">
            <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <MapPin className="size-4" />
                Coverage areas
            </div>

            <div className="flex flex-col gap-2">
                <ToggleGroup
                    type="single"
                    value={layer}
                    onValueChange={(next) => {
                        if (next) setLayer(next as CoverageLayer)
                    }}
                    aria-label="Coverage layer"
                    className="w-fit justify-start"
                    size="sm"
                >
                    <ToggleGroupItem value="traffic" className="gap-1.5">
                        <Radio className="size-4" />
                        Traffic
                    </ToggleGroupItem>
                    <ToggleGroupItem value="weather" className="gap-1.5">
                        <CloudSun className="size-4" />
                        Weather
                    </ToggleGroupItem>
                    <ToggleGroupItem value="fuel" className="gap-1.5">
                        <Fuel className="size-4" />
                        Fuel
                    </ToggleGroupItem>
                </ToggleGroup>
                {layer !== 'fuel' && (
                    <p className="text-xs text-muted-foreground">
                        {layer === 'traffic'
                            ? 'The traffic area defines the station licence-area coverage.'
                            : 'Weather uses a separate location. Edit the circle to centre it on the listening population.'}
                    </p>
                )}
                {!inherited && layer !== 'traffic' && (
                    <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="w-fit"
                        onClick={() =>
                            form.setValue(
                                layer === 'weather'
                                    ? 'weatherCoverage'
                                    : 'fuelCoverage',
                                undefined,
                                { shouldValidate: true, shouldDirty: true }
                            )
                        }
                    >
                        Use traffic area again
                    </Button>
                )}
            </div>

            <MapPicker
                value={value}
                onChange={(coverage: CoverageArea) =>
                    layer === 'traffic'
                        ? setTrafficCoverage(coverage)
                        : setSourceCoverage(coverage)
                }
                height={mapHeight}
                className="w-full"
                showAddressSearch={layer === 'traffic'}
                allowedShapeTypes={layer === 'traffic' ? undefined : ['circle']}
                maxRadius={layer === 'fuel' ? 30 : undefined}
            />
            {shapeError && (
                <div className="flex items-center gap-2 text-sm text-destructive">
                    <AlertCircle className="size-4" />
                    {shapeError}
                </div>
            )}
        </div>
    )
}
