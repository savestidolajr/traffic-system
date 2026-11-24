import { Suspense, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import {
    formatDateTime,
    formatRelativeTime,
    formatRoadName,
} from '@/lib/formatting'
import { Map as MapIcon, Zap } from 'lucide-react'
import type { JobIncident, TrafficCorridor, FlowRoad } from '../../../types'
import type { IncidentRow } from '../../../hooks/use-bulletin-review'
import type { CoverageArea } from '@/lib/api/schemas/shared'
import { EvidenceMap } from '../../evidence-map-lazy'
import { MapUnavailable } from '@/components/map-picker/MapUnavailable'
import { useMapConfig } from '../../../api/map-config-hooks'
import {
    corridorDisplayLabel,
    corridorSeverityDotClass,
    corridorTierByIncidentKey,
    flowClearness,
    incidentEvent,
    incidentRoad,
    magnitudeSeverity,
    roadClassLabel,
} from '../../../lib/incidents/incident-labels'
import type { CorridorTier } from '../../../lib/incidents/incident-labels'
import {
    googleMapsTrafficUrl,
    openGoogleMapsTrafficPopup,
} from '../../../lib/map/map-links'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { InfoHint } from '@/components/InfoHint'
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/dialog'
import { groupByRoad } from '../../../lib/incidents/road-groups'
import { RoadGroup, TabTrigger } from '../../road-group-tabs'

export interface ExplorerMapProps {
    readonly corridors: readonly TrafficCorridor[]
    readonly incidents: readonly JobIncident[]
    readonly mutedKeys: readonly string[]
    readonly flowRoads: readonly FlowRoad[]
    readonly bbox?: readonly number[]
    readonly coverage?: CoverageArea
}

interface AdjustMapExplorerProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly mapProps: ExplorerMapProps
    readonly includedRows: readonly IncidentRow[]
    readonly availableRows: readonly IncidentRow[]
    readonly highlightedIncidentKeys?: readonly string[]
    readonly onMarkerClick?: (key: string) => void
}

/** One rail entry — an incident (in the bulletin or not) or a flowing-well
 *  road with no incident at all. Flow roads carry no HERE coordinates
 *  (`FlowRoad` has none), so they're searchable but never mapped. */
type RailRow =
    | {
          readonly kind: 'incident'
          readonly incident: JobIncident
          readonly included: boolean
          readonly rank: number | null
      }
    | { readonly kind: 'flow'; readonly flow: FlowRoad }

/** The selected row, kept as a discriminated union rather than a bare key —
 *  flow roads and incidents don't share a key space, and only an incident
 *  selection can drive the map (`focusedKey`/`onMarkerClick`). */
type Selection =
    | { readonly kind: 'incident'; readonly key: string }
    | { readonly kind: 'flow'; readonly roadName: string }

/** A labelled value line in the dossier; renders nothing without a value. */
function Field({
    label,
    children,
}: {
    readonly label: string
    readonly children: ReactNode
}) {
    if (children == null || children === '') return null
    return (
        <div className="flex gap-2 text-xs">
            <dt className="w-24 shrink-0 text-muted-foreground">{label}</dt>
            <dd className="min-w-0 flex-1">{children}</dd>
        </div>
    )
}

/**
 * The expanded evidence explorer: searchable rail beside a full-size map
 * with two-way linked selection. Covers EVERY incident and flow reading
 * HERE returned for this job's coverage area — in the bulletin, available
 * but not selected, and roads flowing freely with no incident — not just
 * what airs, and says so explicitly (a prior version's "available" section
 * already covered this; the gap users actually hit was flow data being
 * absent entirely). Clicking a rail row flies the map to an incident and
 * opens its dossier; flow roads have no HERE coordinates, so selecting one
 * shows its dossier without moving the map. Include/exclude decisions stay
 * in the rundown; selecting an incident here also pins it there.
 */
export function AdjustMapExplorer({
    open,
    onOpenChange,
    mapProps,
    includedRows,
    availableRows,
    highlightedIncidentKeys,
    onMarkerClick,
}: AdjustMapExplorerProps) {
    const [sidebarTab, setSidebarTab] = useState<
        'included' | 'available' | 'flowing'
    >('included')
    const [selection, setSelection] = useState<Selection | null>(null)
    // Transient rail hover: flies the map to the incident while the pointer is
    // on its row, without pinning it (click still pins). Cleared on leave so a
    // later hover of the same row re-flies.
    const [hoverKey, setHoverKey] = useState<string | null>(null)
    const { data: mapConfig, isLoading: mapConfigLoading } = useMapConfig()

    const rows = useMemo((): RailRow[] => {
        return [
            ...includedRows.map((r): RailRow => ({
                kind: 'incident',
                incident: r.incident,
                included: true,
                rank: r.rank,
            })),
            ...availableRows.map((r): RailRow => ({
                kind: 'incident',
                incident: r.incident,
                included: false,
                rank: null,
            })),
            ...mapProps.flowRoads.map((flow): RailRow => ({
                kind: 'flow',
                flow,
            })),
        ]
    }, [includedRows, availableRows, mapProps.flowRoads])

    // Severity tier per incident, from its carrying corridor's commuter
    // magnitude — the same colouring the map marks use.
    const tierByKey = useMemo(
        () => corridorTierByIncidentKey(mapProps.corridors),
        [mapProps.corridors]
    )

    const includedIncidents = rows.filter(
        (r): r is Extract<RailRow, { kind: 'incident' }> =>
            r.kind === 'incident' && r.included
    )
    const availableIncidents = rows.filter(
        (r): r is Extract<RailRow, { kind: 'incident' }> =>
            r.kind === 'incident' && !r.included
    )
    const flowing = rows.filter(
        (r): r is Extract<RailRow, { kind: 'flow' }> => r.kind === 'flow'
    )

    // ── Road grouping / open state ──────────────────────────────────────────

    const [openRoads, setOpenRoads] = useState<ReadonlySet<string>>(
        () => new Set()
    )
    const toggleRoad = (road: string) =>
        setOpenRoads((prev) => {
            const next = new Set(prev)
            if (next.has(road)) next.delete(road)
            else next.add(road)
            return next
        })

    // ── Selection ───────────────────────────────────────────────────────────

    const selectedIncident =
        selection?.kind === 'incident'
            ? (rows.find(
                  (r) =>
                      r.kind === 'incident' && r.incident.key === selection.key
              ) as Extract<RailRow, { kind: 'incident' }> | undefined)
            : undefined
    const selectedFlow =
        selection?.kind === 'flow'
            ? mapProps.flowRoads.find((f) => f.roadName === selection.roadName)
            : undefined

    const select = (row: RailRow) => {
        if (row.kind === 'incident') {
            setSelection({ kind: 'incident', key: row.incident.key })
            onMarkerClick?.(row.incident.key)
        } else {
            setSelection({ kind: 'flow', roadName: row.flow.roadName })
        }
    }

    const selectIncidentKey = (key: string) => {
        const row = rows.find(
            (r): r is Extract<RailRow, { kind: 'incident' }> =>
                r.kind === 'incident' && r.incident.key === key
        )
        if (row) select(row)
    }

    // ── Render helpers ──────────────────────────────────────────────────────

    const incidentRow = (row: Extract<RailRow, { kind: 'incident' }>) => {
        const i = row.incident
        const tier = magnitudeSeverity(
            tierByKey.get(i.key)?.magnitude ?? null,
            tierByKey.get(i.key)?.severity ?? i.priority
        )
        const isSelected =
            selection?.kind === 'incident' && selection.key === i.key
        return (
            <button
                key={i.key}
                type="button"
                onClick={() => select(row)}
                onMouseEnter={() => setHoverKey(i.key)}
                onMouseLeave={() => setHoverKey(null)}
                aria-current={isSelected || undefined}
                className={cn(
                    'flex w-full items-start gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors',
                    isSelected
                        ? 'bg-accent text-accent-foreground'
                        : 'hover:bg-muted'
                )}
            >
                <span
                    className={cn(
                        'mt-1 shrink-0 rounded-full',
                        row.included
                            ? 'size-2'
                            : 'size-2 border-[1.5px] bg-transparent border-muted-foreground',
                        row.included && corridorSeverityDotClass(tier)
                    )}
                    aria-hidden
                />
                <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">
                        {formatRoadName(incidentRoad(i.headline))}
                    </span>
                    <span className="block truncate text-muted-foreground">
                        {incidentEvent(i.headline)}
                        {row.rank != null && ` · airing #${row.rank}`}
                    </span>
                </span>
            </button>
        )
    }

    const flowRow = (row: Extract<RailRow, { kind: 'flow' }>) => {
        const f = row.flow
        const isSelected =
            selection?.kind === 'flow' && selection.roadName === f.roadName
        return (
            <button
                key={`flow:${f.roadName}`}
                type="button"
                onClick={() => select(row)}
                aria-current={isSelected || undefined}
                className={cn(
                    'flex w-full items-start gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors',
                    isSelected
                        ? 'bg-accent text-accent-foreground'
                        : 'hover:bg-muted'
                )}
            >
                <span
                    className="mt-1 size-2 shrink-0 rounded-full bg-success"
                    aria-hidden
                />
                <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">
                        {formatRoadName(f.roadName)}
                    </span>
                    <span className="block truncate text-success">
                        {flowClearness(f.jamFactor)}
                        {f.isSelected && ' · aired as good run'}
                    </span>
                </span>
            </button>
        )
    }

    /** Road-grouped incident list for one bucket — the exact same grouping +
     *  tab machinery as the Review page's evidence lens (shared `groupByRoad`
     *  + `RoadGroup`), so the two surfaces never visually or behaviourally
     *  diverge. Single-incident "groups" render as a flat row (no chevron),
     *  matching Review's behaviour. */
    const renderIncidentGroups = (
        incidentRows: readonly Extract<RailRow, { kind: 'incident' }>[],
        emptyMessage: string
    ): ReactNode => {
        if (incidentRows.length === 0) {
            return (
                <p className="px-3 py-4 text-xs text-muted-foreground">
                    {emptyMessage}
                </p>
            )
        }
        return groupByRoad(incidentRows.map((r) => r.incident)).map((group) => {
            if (group.incidents.length === 1) {
                const row = incidentRows.find(
                    (r) => r.incident.key === group.incidents[0].key
                )
                return row ? incidentRow(row) : null
            }
            const hasFocus = group.incidents.some(
                (i) => selection?.kind === 'incident' && selection.key === i.key
            )
            const collapsed = !hasFocus && !openRoads.has(group.road)
            return (
                <RoadGroup
                    key={group.road}
                    group={group}
                    collapsed={collapsed}
                    onToggle={toggleRoad}
                >
                    {group.incidents.map((inc) => {
                        const row = incidentRows.find(
                            (r) => r.incident.key === inc.key
                        )
                        return row ? incidentRow(row) : null
                    })}
                </RoadGroup>
            )
        })
    }

    const sidebarContent = (): ReactNode => {
        if (sidebarTab === 'included') {
            return renderIncidentGroups(
                includedIncidents,
                'No incidents in this bulletin.'
            )
        }
        if (sidebarTab === 'available') {
            return renderIncidentGroups(
                availableIncidents,
                'No available incidents.'
            )
        }
        if (flowing.length === 0) {
            return (
                <p className="px-3 py-4 text-xs text-muted-foreground">
                    No flowing-well roads.
                </p>
            )
        }
        return flowing.map((row) => flowRow(row))
    }

    const hereApiKey = mapConfig?.hereApiKey ?? ''
    // Hover is the transient lens: it flies the camera to the incident under
    // the pointer and lights its mark, without pinning it. Click still pins
    // (persistent highlight + dossier); leaving the row falls back to the
    // pinned selection so the camera never snaps away from what was chosen.
    const focusKey =
        hoverKey ?? (selection?.kind === 'incident' ? selection.key : null)
    const explorerMapProps = {
        corridors: mapProps.corridors,
        incidents: mapProps.incidents,
        bbox: mapProps.bbox,
        coverage: mapProps.coverage,
        mutedKeys: mapProps.mutedKeys,
        fill: true,
        highlightedKeys:
            focusKey != null
                ? [focusKey]
                : selection?.kind === 'incident'
                  ? [selection.key]
                  : highlightedIncidentKeys,
        focusedKey: focusKey,
        onMarkerClick: selectIncidentKey,
    }

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="flex h-[85vh] w-[92vw] max-w-[92vw] flex-col gap-3 p-4">
                <DialogHeader>
                    <DialogTitle className="flex items-center gap-1.5 text-sm">
                        <MapIcon
                            className="size-4 text-muted-foreground"
                            aria-hidden
                        />
                        Coverage area
                        <InfoHint label="What does the coverage area show?">
                            Every incident and flow reading HERE returned for
                            this job, not just what's airing.
                        </InfoHint>
                    </DialogTitle>
                </DialogHeader>
                <div className="flex min-h-0 flex-1 gap-3">
                    <aside className="flex w-80 shrink-0 flex-col gap-2">
                        {/* Tab bar */}
                        <div className="flex gap-0.5 rounded-md bg-muted/30 p-0.5">
                            <TabTrigger
                                active={sidebarTab === 'included'}
                                onClick={() => setSidebarTab('included')}
                                label="In bulletin"
                                count={includedIncidents.length}
                            />
                            {availableIncidents.length > 0 && (
                                <TabTrigger
                                    active={sidebarTab === 'available'}
                                    onClick={() => setSidebarTab('available')}
                                    label="Available"
                                    count={availableIncidents.length}
                                    tone="warning"
                                />
                            )}
                            {flowing.length > 0 && (
                                <TabTrigger
                                    active={sidebarTab === 'flowing'}
                                    onClick={() => setSidebarTab('flowing')}
                                    label="Flowing well"
                                    count={flowing.length}
                                    tone="muted"
                                />
                            )}
                        </div>
                        {/* Incident list */}
                        <div className="min-h-0 flex-1 overflow-y-auto rounded-md border bg-card">
                            {sidebarContent()}
                        </div>
                        {/* Dossier on selection */}
                        {selectedIncident && (
                            <IncidentDossier
                                incident={selectedIncident.incident}
                                included={selectedIncident.included}
                                rank={selectedIncident.rank}
                                tier={tierByKey.get(
                                    selectedIncident.incident.key
                                )}
                            />
                        )}
                        {selectedFlow && <FlowDossier flow={selectedFlow} />}
                    </aside>
                    <div className="min-h-0 flex-1">
                        <Suspense fallback={null}>
                            {mapConfigLoading ? null : hereApiKey ? (
                                <EvidenceMap
                                    apiKey={hereApiKey}
                                    {...explorerMapProps}
                                />
                            ) : (
                                <MapUnavailable />
                            )}
                        </Suspense>
                    </div>
                </div>
            </DialogContent>
        </Dialog>
    )
}

/**
 * Every field the pipeline persists from HERE for one incident. Fields
 * absent from the feed render nothing — no empty scaffolding. The full raw
 * HERE payload is NOT stored on jobs today; this is the complete persisted
 * subset (see JobIncidentDto).
 */
function IncidentDossier({
    incident: i,
    included,
    rank,
    tier,
}: {
    readonly incident: JobIncident
    readonly included: boolean
    readonly rank: number | null
    readonly tier?: CorridorTier
}) {
    const hasReported = Boolean(i.entryTimeUtc)
    const hasCoords = i.latitude != null && i.longitude != null
    return (
        <div className="max-h-[45%] shrink-0 overflow-y-auto rounded-md border bg-card p-3">
            <p className="flex flex-wrap items-center gap-1.5 text-xs font-semibold">
                {formatRoadName(incidentRoad(i.headline))}
                <span className="font-normal text-muted-foreground">
                    {incidentEvent(i.headline)}
                </span>
                {i.isNew && (
                    <Badge
                        variant="info"
                        className="px-1.5 py-0.5 text-2xs font-medium"
                    >
                        New
                    </Badge>
                )}
                {i.roadClosed && (
                    <Badge
                        variant="destructive"
                        className="px-1.5 py-0.5 text-2xs font-medium"
                    >
                        Road closed
                    </Badge>
                )}
                <InfoHint label="What does this dossier show?">
                    All fields the pipeline stores from the HERE feed for this
                    incident.
                </InfoHint>
            </p>
            <p className="mt-1 text-xs text-muted-foreground">{i.detail}</p>
            <dl className="mt-2 flex flex-col gap-1">
                <Field label="Placement">
                    {included
                        ? rank != null
                            ? `In bulletin, airing #${rank}`
                            : 'In bulletin'
                        : 'Available (not in bulletin)'}
                </Field>
                <Field label="Severity">
                    {corridorDisplayLabel(
                        tier?.magnitude ?? null,
                        tier?.severity ?? i.priority
                    )}
                </Field>
                <Field label="Direction">{i.direction}</Field>
                <Field label="Lanes">{i.laneDetail}</Field>
                <Field label="Authority note">{i.comment}</Field>
                <Field label="Road class">
                    {roadClassLabel(i.functionalClass)}
                </Field>
                <Field label="Length">
                    {i.lengthMeters != null && i.lengthMeters > 0
                        ? `${(i.lengthMeters / 1000).toFixed(1)} km`
                        : null}
                </Field>
                <Field label="Reported">
                    {hasReported && (
                        <span title={formatDateTime(i.entryTimeUtc as string)}>
                            {formatRelativeTime(i.entryTimeUtc as string)}
                        </span>
                    )}
                </Field>
                <Field label="Location">
                    {hasCoords && (
                        <span className="font-mono tabular-nums">
                            {(i.latitude as number).toFixed(4)},{' '}
                            {(i.longitude as number).toFixed(4)}
                            {' · '}
                            <a
                                href={googleMapsTrafficUrl(
                                    i.latitude as number,
                                    i.longitude as number
                                )}
                                target="_blank"
                                rel="noopener noreferrer"
                                onClick={(e) =>
                                    openGoogleMapsTrafficPopup(
                                        i.latitude as number,
                                        i.longitude as number,
                                        e
                                    )
                                }
                                className="font-sans text-primary underline-offset-2 hover:underline"
                            >
                                Check in Google Maps
                            </a>
                        </span>
                    )}
                </Field>
                <Field label="HERE key">
                    <span className="break-all font-mono text-2xs text-muted-foreground">
                        {i.key}
                    </span>
                </Field>
            </dl>
        </div>
    )
}

/**
 * Every field the pipeline persists from HERE's flow feed for one flowing
 * road. HERE's Traffic Flow API reports these as aggregate segment stats
 * (worst jam factor, total length), never a single point — so, unlike
 * incidents, there is genuinely no map location to show or Google Maps
 * cross-check to offer. Said explicitly rather than silently omitted.
 */
function FlowDossier({ flow: f }: { readonly flow: FlowRoad }) {
    return (
        <div className="max-h-[45%] shrink-0 overflow-y-auto rounded-md border bg-card p-3">
            <p className="flex flex-wrap items-center gap-1.5 text-xs font-semibold">
                {formatRoadName(f.roadName)}
                <span className="font-normal text-success">
                    {flowClearness(f.jamFactor)}
                </span>
                {f.isSelected && (
                    <Badge
                        variant="success"
                        className="px-1.5 py-0.5 text-2xs font-medium"
                    >
                        Aired as good run
                    </Badge>
                )}
                <InfoHint label="Why is there no map location?">
                    HERE's flow feed reports this as an aggregate road segment,
                    not a point, so no map location or Google Maps cross-check
                    is available for flow data.
                </InfoHint>
            </p>
            <dl className="mt-2 flex flex-col gap-1">
                <Field label="Jam factor">
                    <span className="font-mono tabular-nums">
                        {f.jamFactor.toFixed(1)} / 10 (0 clear, 10 blocked)
                    </span>
                </Field>
                <Field label="Length">
                    {f.lengthKm > 0 ? `${f.lengthKm.toFixed(1)} km` : null}
                </Field>
                <Field label="Road class">
                    {roadClassLabel(f.functionalClass)}
                </Field>
                <Field label="Priority road">
                    {f.priorityBoosted && (
                        <span className="inline-flex items-center gap-1">
                            <Zap className="size-3 text-warning" aria-hidden />
                            Boosted: market priority road
                        </span>
                    )}
                </Field>
                <Field label="Weighted">
                    {f.isWeighted
                        ? 'On the station/priority good-run list'
                        : null}
                </Field>
            </dl>
        </div>
    )
}
