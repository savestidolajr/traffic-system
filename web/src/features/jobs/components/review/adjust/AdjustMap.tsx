import { useMemo, useState, Suspense } from 'react'
import { Map as MapIcon, Maximize2 } from 'lucide-react'
import type { Job, JobIncident } from '../../../types'
import { hasTraffic } from '../../../types'
import type { IncidentRow } from '../../../hooks/use-bulletin-review'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { InfoHint } from '@/components/InfoHint'
import { EvidenceMap } from '../../evidence-map-lazy'
import { AdjustMapExplorer } from './AdjustMapExplorer'
import { MapUnavailable } from '@/components/map-picker/MapUnavailable'
import { useMapConfig } from '../../../api/map-config-hooks'
import { EmergencyPanel } from './EmergencyPanel'
import { findEmergencyDiagnostic } from '../../../lib/emergency'
interface AdjustMapProps {
    readonly job: Job
    /** Live bulletin picks — drive the pins so toggles update the map. */
    readonly includedRows: readonly IncidentRow[]
    readonly availableRows: readonly IncidentRow[]
    readonly highlightedIncidentKeys?: readonly string[]
    /** Clicking a pin pins its incident (same gesture as the evidence rail). */
    readonly onMarkerClick?: (key: string) => void
    /** Selected or pinned incident key to center/highlight on the map. */
    readonly focusedKey?: string | null
}

/**
 * The coverage map as a working tool, not a thumbnail: tall enough to read,
 * radius drawn, pins clickable. Geography is the most intuitive answer to
 * "why was this merged" and "why isn't that crash included" — anything
 * outside the dashed circle never reached the ranker.
 *
 * Pins come from the live review rows so include/exclude toggles update the
 * map instantly: included incidents are solid, available ones hollow.
 */
export function AdjustMap({
    job,
    includedRows,
    availableRows,
    highlightedIncidentKeys,
    onMarkerClick,
    focusedKey,
}: AdjustMapProps) {
    const [expanded, setExpanded] = useState(false)
    const { data: mapConfig, isLoading: mapConfigLoading } = useMapConfig()
    const mapProps = useMemo(() => {
        if (!hasTraffic(job)) return null
        const incidents: JobIncident[] = [
            ...includedRows.map((r) => r.incident),
            ...availableRows.map((r) => r.incident),
        ]
        return {
            corridors: job.traffic?.corridors ?? [],
            incidents,
            mutedKeys: availableRows.map((r) => r.incident.key),
            bbox: job.traffic?.bbox ?? undefined,
            coverage: job.coverage,
            flowRoads: job.traffic?.flowRoads ?? [],
        }
    }, [job, includedRows, availableRows])

    if (!mapProps) return null

    const hereApiKey = mapConfig?.hereApiKey ?? ''
    const commonMapProps = {
        corridors: mapProps.corridors,
        incidents: mapProps.incidents,
        bbox: mapProps.bbox,
        coverage: mapProps.coverage,
        mutedKeys: mapProps.mutedKeys,
        fill: true,
        highlightedKeys: highlightedIncidentKeys,
        onMarkerClick,
        focusedKey,
        chrome: 'minimal' as const,
        footnote: false,
    }

    return (
        <div className="flex h-full min-h-0 flex-col gap-3">
            <SpotlightCard className="isolate relative z-0 h-96 min-h-[360px] shrink-0 overflow-hidden p-3">
                <div className="flex h-full flex-col gap-3">
                    <span className="flex shrink-0 items-center gap-1.5 pb-1 text-xs font-semibold">
                        <MapIcon
                            className="size-3.5 text-muted-foreground"
                            aria-hidden
                        />
                        Coverage area
                        <InfoHint label="What does the map overlay show?">
                            Flow colouring and incident markers are HERE's own
                            live overlay; our marks are the bulletin's evidence
                            at fetch time.
                        </InfoHint>
                        <button
                            type="button"
                            onClick={() => setExpanded(true)}
                            aria-label="Expand the coverage map"
                            title="Expand the coverage map"
                            className="ml-auto flex size-6 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
                        >
                            <Maximize2 className="size-3.5" aria-hidden />
                        </button>
                    </span>
                    <div className="min-h-0 flex-1">
                        <Suspense fallback={null}>
                            {mapConfigLoading ? null : hereApiKey ? (
                                <EvidenceMap
                                    apiKey={hereApiKey}
                                    {...commonMapProps}
                                />
                            ) : (
                                <MapUnavailable />
                            )}
                        </Suspense>
                    </div>
                    <AdjustMapExplorer
                        open={expanded}
                        onOpenChange={setExpanded}
                        mapProps={mapProps}
                        includedRows={includedRows}
                        availableRows={availableRows}
                        highlightedIncidentKeys={highlightedIncidentKeys}
                        onMarkerClick={onMarkerClick}
                    />
                </div>
            </SpotlightCard>
            {job.emergencyEvents !== undefined && (
                // JobDetailDtoBase currently has no EmergencyEvents and
                // TrafficEvidenceBuilder.ApplyMarketContent drops them. Mount only
                // after that backend projection is restored.
                <EmergencyPanel
                    events={job.emergencyEvents}
                    diagnostic={findEmergencyDiagnostic(job.sourceDiagnostics)}
                />
            )}
        </div>
    )
}
