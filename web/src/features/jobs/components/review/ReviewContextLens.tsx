import { useCallback, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { Pin } from 'lucide-react'
import type { Job, JobIncident, TrafficCorridor } from '../../types'
import { hasTraffic } from '../../types'
import type { FocusKey } from './JobReviewCockpit'
import type { ProvenanceResult } from '../../lib/verification/provenance'
import {
    corridorDisplayDotClass,
    corridorDisplayLabel,
    corridorTierByIncidentKey,
    type CorridorTier,
    corridorTypeLabel,
    roadClassLabel,
    incidentEvent,
    incidentAnchor,
} from '../../lib/incidents/incident-labels'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { formatRoadName } from '@/lib/formatting'
import { groupByRoad } from '../../lib/incidents/road-groups'
import { RoadGroup, TabTrigger } from '../road-group-tabs'

interface ReviewContextLensProps {
    readonly job: Job
    readonly focusKey: FocusKey
    readonly provenance: ProvenanceResult | null
    /** Keys of incidents currently included in the bulletin. */
    readonly includedKeys: readonly string[]
    /** Hovering an index row highlights its sentences in the script. */
    readonly onIncidentHover?: (key: string | null) => void
    /** Clicking an index row pins/unpins its highlight. */
    readonly onIncidentPin?: (key: string) => void
    /** The currently pinned incident, if any. */
    readonly pinnedIncidentKey?: string | null
    /** Hovering a flow-corridor row highlights its sentences in the script. */
    readonly onRoadHover?: (road: string | null) => void
    /** Clicking a flow-corridor row pins/unpins its highlight. */
    readonly onRoadPin?: (road: string) => void
    /** The currently pinned corridor road name, if any. */
    readonly pinnedRoad?: string | null
    /** True when provenance came from model-attributed lineage. */
    readonly modelProvenance?: boolean
}

type EvidenceBucket = 'cited' | 'unused' | 'available' | 'selected'

function sentenceLabel(indexes: readonly number[]): string | null {
    if (indexes.length === 0) return null
    const ordered = [...indexes].sort((a, b) => a - b)
    const first = `S${ordered[0] + 1}`
    return ordered.length === 1 ? first : `${first} +${ordered.length - 1}`
}

function bucketTone(bucket: EvidenceBucket): string {
    switch (bucket) {
        case 'cited':
            return 'border-success/30 bg-success/10 text-success'
        case 'unused':
            return 'border-warning/30 bg-warning/10 text-warning'
        case 'available':
            return 'border-border/60 bg-muted/30 text-muted-foreground'
        case 'selected':
            return 'border-primary/30 bg-primary/10 text-primary'
    }
}
interface EvidenceSummary {
    readonly rowCount: number
    readonly sourceRecordCount: number
}

function summarizeEvidence(
    incidents: readonly JobIncident[],
    corridors: readonly TrafficCorridor[]
): EvidenceSummary {
    return {
        rowCount: groupByRoad(incidents).length + corridors.length,
        sourceRecordCount: incidents.length + corridors.length,
    }
}

function IndexRow({
    incident,
    tier,
    included,
    grouped,
    sentenceIndexes,
    showMentionCount,
    highlighted,
    pinned,
    bucket,
    onHover,
    onPin,
}: {
    readonly incident: JobIncident
    readonly tier: CorridorTier | null
    readonly included: boolean
    /**
     * Rendered under a road header: show the distinguishing event + anchor
     * ("Roadworks · At Anzac Highway") instead of the repeated "{type} on {road}"
     * headline, and drop the road-class (it sits in the header).
     */
    readonly grouped?: boolean
    /** Zero-based script sentence indexes that cite this incident. */
    readonly sentenceIndexes: readonly number[]
    /** Counts are only displayable when lineage is model-attributed. */
    readonly showMentionCount: boolean
    readonly highlighted: boolean
    readonly pinned: boolean
    readonly bucket: EvidenceBucket
    readonly onHover?: (key: string | null) => void
    readonly onPin?: (key: string) => void
}) {
    const severityDot = corridorDisplayDotClass(
        tier?.magnitude ?? null,
        tier?.severity ?? incident.priority
    )
    const severityLabel = corridorDisplayLabel(
        tier?.magnitude ?? null,
        tier?.severity ?? incident.priority
    )
    const road = roadClassLabel(incident.functionalClass)
    const anchor = incident.detail ? incidentAnchor(incident.detail) : null
    const primary = grouped
        ? anchor
            ? `${incidentEvent(incident.headline)} · ${anchor}`
            : incidentEvent(incident.headline)
        : incident.headline
    const label = sentenceLabel(sentenceIndexes)

    return (
        <Tooltip>
            <TooltipTrigger asChild>
                <div
                    role={onPin ? 'button' : undefined}
                    tabIndex={onPin ? 0 : undefined}
                    aria-pressed={onPin ? pinned : undefined}
                    className={cn(
                        'grid grid-cols-[minmax(0,1fr)_auto] gap-x-2 gap-y-0.5 border-t border-border/35 px-2 py-1.5 text-xs transition-[background-color,box-shadow] duration-200 first:border-t-0',
                        highlighted || pinned
                            ? 'bg-primary/10 ring-1 ring-primary/30 shadow-[0_0_18px_-6px_hsl(var(--primary)/0.45)]'
                            : 'hover:bg-muted/40',
                        bucket === 'available' && 'opacity-75',
                        onPin && 'cursor-pointer'
                    )}
                    onMouseEnter={() => onHover?.(incident.key)}
                    onMouseLeave={() => onHover?.(null)}
                    onClick={() => onPin?.(incident.key)}
                    onKeyDown={(e) => {
                        if (onPin && (e.key === 'Enter' || e.key === ' ')) {
                            e.preventDefault()
                            onPin(incident.key)
                        }
                    }}
                >
                    <div className="flex min-w-0 items-start gap-2">
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span
                                    className={cn(
                                        'mt-1.5 size-1.5 shrink-0 rounded-full',
                                        severityDot
                                    )}
                                />
                            </TooltipTrigger>
                            <TooltipContent side="top">
                                {severityLabel}
                            </TooltipContent>
                        </Tooltip>
                        <span
                            className={cn(
                                'min-w-0 flex-1 truncate leading-snug',
                                highlighted
                                    ? 'text-foreground'
                                    : included
                                      ? 'text-foreground/85'
                                      : 'text-muted-foreground'
                            )}
                        >
                            {primary}
                        </span>
                    </div>
                    <div className="flex items-start justify-end gap-1">
                        {label && (
                            <Tooltip>
                                <TooltipTrigger asChild>
                                    <span
                                        className={cn(
                                            'cursor-default rounded border px-1.5 py-px font-mono text-3xs font-medium tabular-nums',
                                            bucketTone(bucket)
                                        )}
                                    >
                                        {label}
                                    </span>
                                </TooltipTrigger>
                                <TooltipContent side="top">
                                    {sentenceIndexes.length === 1
                                        ? `S${sentenceIndexes[0] + 1}: cited in sentence ${sentenceIndexes[0] + 1} of the script`
                                        : `S${sentenceIndexes[0] + 1} +${sentenceIndexes.length - 1}: cited in sentence ${sentenceIndexes[0] + 1} and ${sentenceIndexes.length - 1} more (${sentenceIndexes.length} in total)`}
                                </TooltipContent>
                            </Tooltip>
                        )}
                        {pinned && (
                            <Pin
                                className="mt-0.5 size-3 shrink-0 text-primary"
                                aria-label="Pinned"
                            />
                        )}
                    </div>
                    <div className="col-start-1 flex min-w-0 items-center gap-1.5 pl-3.5 text-2xs text-muted-foreground">
                        {incident.isNew && (
                            <span className="font-medium text-primary">
                                new
                            </span>
                        )}
                        {!grouped && road && (
                            <span className="truncate">{road}</span>
                        )}
                        {showMentionCount && sentenceIndexes.length >= 2 && (
                            <Tooltip>
                                <TooltipTrigger asChild>
                                    <span className="shrink-0">
                                        ×{sentenceIndexes.length}
                                    </span>
                                </TooltipTrigger>
                                <TooltipContent side="top">
                                    Mentioned in {sentenceIndexes.length}{' '}
                                    sentences
                                </TooltipContent>
                            </Tooltip>
                        )}
                    </div>
                </div>
            </TooltipTrigger>
            {/* The detail used to expand inline, growing the rail's height on
                transient hover. It lives in the tooltip now. Do NOT gate this on
                `highlighted`: that flag arrives from the parent one state
                round-trip after the tooltip opens, so gating swapped the
                tooltip's body mid-open and left the detail unreachable on a
                fast hover. */}
            {incident.detail || onPin ? (
                <TooltipContent side="top" className="max-w-sm">
                    {incident.detail}
                    {onPin && (
                        <span
                            className={cn(
                                'block text-2xs text-muted-foreground',
                                incident.detail &&
                                    'mt-1.5 border-t border-border/40 pt-1.5'
                            )}
                        >
                            {pinned ? 'Click to unpin' : 'Click to pin'}
                        </span>
                    )}
                </TooltipContent>
            ) : null}
        </Tooltip>
    )
}

/**
 * A flow/congestion corridor row. These corridors carry no discrete incident
 * ("Stanmore Road's moving slowly" is HERE flow data, not a reported event), so
 * the corridor IS the evidence — there is no incident row for them to hide
 * under, and on a congestion-led bulletin they are most of what the script
 * said. Same visual grammar as IndexRow, minus the fields a flow corridor has
 * no equivalent for (headline, location anchor, new-since-last-bulletin).
 */
function CorridorRow({
    corridor,
    sentenceIndexes,
    showMentionCount,
    highlighted,
    pinned,
    bucket,
    onHover,
    onPin,
}: {
    readonly corridor: TrafficCorridor
    /** Zero-based script sentence indexes that cite this corridor. */
    readonly sentenceIndexes: readonly number[]
    readonly showMentionCount: boolean
    readonly highlighted: boolean
    readonly pinned: boolean
    readonly bucket: EvidenceBucket
    readonly onHover?: (road: string | null) => void
    readonly onPin?: (road: string) => void
}) {
    const severityDot = corridorDisplayDotClass(
        corridor.magnitude ?? null,
        corridor.severity
    )
    const severityLabel = corridorDisplayLabel(
        corridor.magnitude ?? null,
        corridor.severity
    )
    const road = roadClassLabel(corridor.functionalClass)
    const label = sentenceLabel(sentenceIndexes)
    const delay =
        corridor.delayMinutes != null && corridor.delayMinutes > 0
            ? `${Math.round(corridor.delayMinutes)} min delay`
            : null

    return (
        <Tooltip>
            <TooltipTrigger asChild>
                <div
                    role={onPin ? 'button' : undefined}
                    tabIndex={onPin ? 0 : undefined}
                    aria-pressed={onPin ? pinned : undefined}
                    className={cn(
                        'grid grid-cols-[minmax(0,1fr)_auto] gap-x-2 gap-y-0.5 border-t border-border/35 px-2 py-1.5 text-xs transition-[background-color,box-shadow] duration-200 first:border-t-0',
                        highlighted || pinned
                            ? 'bg-primary/10 ring-1 ring-primary/30 shadow-[0_0_18px_-6px_hsl(var(--primary)/0.45)]'
                            : 'hover:bg-muted/40',
                        bucket === 'available' && 'opacity-75',
                        onPin && 'cursor-pointer'
                    )}
                    onMouseEnter={() => onHover?.(corridor.roadName)}
                    onMouseLeave={() => onHover?.(null)}
                    onClick={() => onPin?.(corridor.roadName)}
                    onKeyDown={(e) => {
                        if (onPin && (e.key === 'Enter' || e.key === ' ')) {
                            e.preventDefault()
                            onPin(corridor.roadName)
                        }
                    }}
                >
                    <div className="flex min-w-0 items-start gap-2">
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span
                                    className={cn(
                                        'mt-1.5 size-1.5 shrink-0 rounded-full',
                                        severityDot
                                    )}
                                />
                            </TooltipTrigger>
                            <TooltipContent side="top">
                                {severityLabel}
                            </TooltipContent>
                        </Tooltip>
                        <span
                            className={cn(
                                'min-w-0 flex-1 truncate leading-snug',
                                highlighted
                                    ? 'text-foreground'
                                    : bucket === 'available'
                                      ? 'text-muted-foreground'
                                      : 'text-foreground/85'
                            )}
                        >
                            {corridorTypeLabel(corridor.primaryType)} on{' '}
                            {formatRoadName(corridor.roadName)}
                        </span>
                    </div>
                    <div className="flex items-start justify-end gap-1">
                        {label && (
                            <Tooltip>
                                <TooltipTrigger asChild>
                                    <span
                                        className={cn(
                                            'cursor-default rounded border px-1.5 py-px font-mono text-3xs font-medium tabular-nums',
                                            bucketTone(bucket)
                                        )}
                                    >
                                        {label}
                                    </span>
                                </TooltipTrigger>
                                <TooltipContent side="top">
                                    {sentenceIndexes.length === 1
                                        ? `S${sentenceIndexes[0] + 1}: cited in sentence ${sentenceIndexes[0] + 1} of the script`
                                        : `S${sentenceIndexes[0] + 1} +${sentenceIndexes.length - 1}: cited in sentence ${sentenceIndexes[0] + 1} and ${sentenceIndexes.length - 1} more (${sentenceIndexes.length} in total)`}
                                </TooltipContent>
                            </Tooltip>
                        )}
                        {pinned && (
                            <Pin
                                className="mt-0.5 size-3 shrink-0 text-primary"
                                aria-label="Pinned"
                            />
                        )}
                    </div>
                    <div className="col-start-1 flex min-w-0 items-center gap-1.5 pl-3.5 text-2xs text-muted-foreground">
                        <span className="shrink-0">flow data</span>
                        {road && <span className="truncate">{road}</span>}
                        {delay && <span className="shrink-0">{delay}</span>}
                        {showMentionCount && sentenceIndexes.length >= 2 && (
                            <Tooltip>
                                <TooltipTrigger asChild>
                                    <span className="shrink-0">
                                        ×{sentenceIndexes.length}
                                    </span>
                                </TooltipTrigger>
                                <TooltipContent side="top">
                                    Mentioned in {sentenceIndexes.length}{' '}
                                    sentences
                                </TooltipContent>
                            </Tooltip>
                        )}
                    </div>
                </div>
            </TooltipTrigger>
            <TooltipContent side="top" className="max-w-sm">
                No reported incident on this road: HERE flow data has it{' '}
                {corridor.description || 'running slowly'}.
                {onPin && (
                    <span className="mt-1.5 block border-t border-border/40 pt-1.5 text-2xs text-muted-foreground">
                        {pinned ? 'Click to unpin' : 'Click to pin'}
                    </span>
                )}
            </TooltipContent>
        </Tooltip>
    )
}

/**
 * The review evidence lens verifies the spoken script first. The full fetched
 * traffic universe belongs on Adjust, so Review shows cited evidence by default
 * and keeps unused selected / below-cut evidence collapsed behind summaries.
 *
 * Evidence is incidents AND flow corridors. A congestion-led bulletin ("Dacey
 * Avenue, Park Parade and Solander Drive are all a bit slow") is built almost
 * entirely from flow corridors that carry no reported incident, so an
 * incident-only rail leaves most of the script unaccounted for.
 */
export function ReviewContextLens({
    job,
    focusKey,
    provenance,
    includedKeys,
    onIncidentHover,
    onIncidentPin,
    pinnedIncidentKey = null,
    onRoadHover,
    onRoadPin,
    pinnedRoad = null,
    modelProvenance = false,
}: ReviewContextLensProps) {
    // Roads the operator has manually opened. Multi-incident roads default
    // closed, then auto-open while a member is lit or pinned.
    const [openRoads, setOpenRoads] = useState<ReadonlySet<string>>(
        () => new Set()
    )
    const [evidenceTab, setEvidenceTab] = useState<
        'cited' | 'unused' | 'below'
    >('cited')
    const toggleRoad = useCallback((road: string) => {
        setOpenRoads((prev) => {
            const next = new Set(prev)
            if (next.has(road)) next.delete(road)
            else next.add(road)
            return next
        })
    }, [])

    const incidents = useMemo<readonly JobIncident[]>(
        () => (hasTraffic(job) ? (job.traffic?.incidents ?? []) : []),
        [job]
    )

    const tierByKey = useMemo(
        () =>
            corridorTierByIncidentKey(
                hasTraffic(job) ? (job.traffic?.corridors ?? []) : []
            ),
        [job]
    )

    // Flow/congestion corridors only: a corridor that aggregates incidents is
    // already on screen as those incident rows, so listing it again would
    // double-count one piece of evidence. Several segments of one road collapse
    // to the first — corridors arrive rank-ordered, so that is the worst one.
    const flowCorridors = useMemo<readonly TrafficCorridor[]>(() => {
        const corridors = hasTraffic(job) ? (job.traffic?.corridors ?? []) : []
        const seen = new Set<string>()
        const rows: TrafficCorridor[] = []
        for (const corridor of corridors) {
            if (corridor.incidentKeys && corridor.incidentKeys.length > 0)
                continue
            const road = corridor.roadName.toLowerCase()
            if (seen.has(road)) continue
            seen.add(road)
            rows.push(corridor)
        }
        return rows
    }, [job])

    const includedSet = useMemo(() => {
        const map: Record<string, true> = {}
        for (const k of includedKeys) map[k] = true
        return map
    }, [includedKeys])

    // Which incident keys are lit by the current focus. A 'road' focus is a
    // flow corridor with no incidents behind it, so it lights no incident row.
    const litKeys = useMemo<Record<string, true>>(() => {
        const map: Record<string, true> = {}
        if (!focusKey) return map
        if (focusKey.kind === 'incident') {
            map[focusKey.key] = true
            return map
        }
        if (focusKey.kind === 'corridor') {
            for (const k of focusKey.keys) map[k] = true
            return map
        }
        if (focusKey.kind === 'road') return map
        const entry = provenance?.bySentence[focusKey.index]
        for (const k of entry?.incidentKeys ?? []) map[k] = true
        return map
    }, [focusKey, provenance])

    // Which corridor roads are lit by the current focus. Hovering a sentence
    // lights the flow corridors it cited, the mirror of litKeys for incidents.
    const litRoads = useMemo<Record<string, true>>(() => {
        const map: Record<string, true> = {}
        if (!focusKey) return map
        if (focusKey.kind === 'road') {
            map[focusKey.road.toLowerCase()] = true
            return map
        }
        if (focusKey.kind !== 'sentence' || !provenance) return map
        for (const [road, entry] of Object.entries(provenance.byCorridorRoad)) {
            if (entry.sentenceIndexes.includes(focusKey.index)) map[road] = true
        }
        return map
    }, [focusKey, provenance])

    const corridorIndexes = useCallback(
        (corridor: TrafficCorridor): readonly number[] =>
            provenance?.byCorridorRoad[corridor.roadName.toLowerCase()]
                ?.sentenceIndexes ?? [],
        [provenance]
    )

    const sentenceIndexesByKey = useMemo<
        Record<string, readonly number[]>
    >(() => {
        const map: Record<string, readonly number[]> = {}
        if (!provenance) return map
        for (const [key, entry] of Object.entries(provenance.byIncidentKey)) {
            map[key] = entry.sentenceIndexes
        }
        return map
    }, [provenance])

    const included = incidents.filter((i) => includedSet[i.key])
    const available = incidents.filter((i) => !includedSet[i.key])
    const cited = provenance
        ? included.filter((i) => (sentenceIndexesByKey[i.key]?.length ?? 0) > 0)
        : included
    const unusedSelected = provenance
        ? included.filter(
              (i) => (sentenceIndexesByKey[i.key]?.length ?? 0) === 0
          )
        : []

    const selectedCorridors = flowCorridors.filter((c) => c.isSelected)
    const citedCorridors = provenance
        ? flowCorridors.filter((c) => corridorIndexes(c).length > 0)
        : selectedCorridors
    const unusedCorridors = provenance
        ? selectedCorridors.filter((c) => corridorIndexes(c).length === 0)
        : []
    const belowCutCorridors = provenance
        ? flowCorridors.filter(
              (c) => !c.isSelected && corridorIndexes(c).length === 0
          )
        : flowCorridors.filter((c) => !c.isSelected)
    const selectedSummary = summarizeEvidence(included, selectedCorridors)
    const citedSummary = summarizeEvidence(cited, citedCorridors)
    const unusedSummary = summarizeEvidence(unusedSelected, unusedCorridors)
    const belowSummary = summarizeEvidence(available, belowCutCorridors)
    const activeSummary =
        provenance && evidenceTab === 'cited'
            ? citedSummary
            : provenance && evidenceTab === 'unused'
              ? unusedSummary
              : provenance
                ? belowSummary
                : evidenceTab === 'cited'
                  ? selectedSummary
                  : belowSummary
    const activeSummaryLabel =
        provenance && evidenceTab === 'cited'
            ? 'voiced road/story rows'
            : provenance && evidenceTab === 'unused'
              ? 'unused selected road/story rows'
              : provenance
                ? 'below-cut road/story rows'
                : evidenceTab === 'cited'
                  ? 'selected road/story rows'
                  : 'available road/story rows'

    if (incidents.length === 0 && flowCorridors.length === 0) {
        return (
            <SpotlightCard className="p-4 text-xs text-muted-foreground">
                No traffic evidence attached to this bulletin.
            </SpotlightCard>
        )
    }

    const renderRow = (
        incident: JobIncident,
        isIncluded: boolean,
        grouped: boolean,
        bucket: EvidenceBucket
    ): ReactNode => (
        <IndexRow
            key={incident.key}
            incident={incident}
            tier={tierByKey.get(incident.key) ?? null}
            included={isIncluded}
            grouped={grouped}
            sentenceIndexes={sentenceIndexesByKey[incident.key] ?? []}
            showMentionCount={modelProvenance}
            highlighted={litKeys[incident.key] === true}
            pinned={pinnedIncidentKey === incident.key}
            bucket={bucket}
            onHover={onIncidentHover}
            onPin={onIncidentPin}
        />
    )

    const renderCorridorRow = (
        corridor: TrafficCorridor,
        bucket: EvidenceBucket
    ): ReactNode => (
        <CorridorRow
            key={`corridor:${corridor.roadName}`}
            corridor={corridor}
            sentenceIndexes={corridorIndexes(corridor)}
            showMentionCount={modelProvenance}
            highlighted={litRoads[corridor.roadName.toLowerCase()] === true}
            pinned={
                pinnedRoad?.toLowerCase() === corridor.roadName.toLowerCase()
            }
            bucket={bucket}
            onHover={onRoadHover}
            onPin={onRoadPin}
        />
    )

    const renderBucket = (
        rows: readonly JobIncident[],
        corridorRows: readonly TrafficCorridor[],
        isIncluded: boolean,
        bucket: EvidenceBucket
    ): ReactNode => {
        if (rows.length === 0 && corridorRows.length === 0) {
            return (
                <p className="rounded-md bg-muted/30 px-2.5 py-2 text-xs leading-snug text-muted-foreground">
                    {bucket === 'cited'
                        ? 'No cited evidence.'
                        : 'No evidence here.'}
                </p>
            )
        }

        return (
            <>
                {groupByRoad(rows).map((group) => {
                    if (group.incidents.length === 1)
                        return renderRow(
                            group.incidents[0],
                            isIncluded,
                            false,
                            bucket
                        )

                    const hasFocus = group.incidents.some(
                        (i) =>
                            litKeys[i.key] === true ||
                            pinnedIncidentKey === i.key
                    )
                    const collapsed = !hasFocus && !openRoads.has(group.road)
                    return (
                        <RoadGroup
                            key={`${bucket}:${group.road}`}
                            group={group}
                            collapsed={collapsed}
                            onToggle={toggleRoad}
                        >
                            {group.incidents.map((inc) =>
                                renderRow(inc, isIncluded, true, bucket)
                            )}
                        </RoadGroup>
                    )
                })}
                {corridorRows.map((c) => renderCorridorRow(c, bucket))}
            </>
        )
    }

    return (
        <SpotlightCard className="p-3">
            <div className="flex flex-col gap-1.5">
                <span className="pb-1 text-xs font-semibold">Evidence</span>

                {/* Tab bar */}
                <div className="flex gap-0.5 rounded-md bg-muted/30 p-0.5">
                    {provenance ? (
                        <>
                            <TabTrigger
                                active={evidenceTab === 'cited'}
                                onClick={() => setEvidenceTab('cited')}
                                label="Cited"
                                count={citedSummary.rowCount}
                            />
                            {unusedSelected.length + unusedCorridors.length >
                                0 && (
                                <TabTrigger
                                    active={evidenceTab === 'unused'}
                                    onClick={() => setEvidenceTab('unused')}
                                    label="Unused"
                                    count={unusedSummary.rowCount}
                                    tone="warning"
                                />
                            )}
                            {available.length + belowCutCorridors.length >
                                0 && (
                                <TabTrigger
                                    active={evidenceTab === 'below'}
                                    onClick={() => setEvidenceTab('below')}
                                    label="Below cut"
                                    count={belowSummary.rowCount}
                                    tone="muted"
                                />
                            )}
                        </>
                    ) : (
                        <>
                            <TabTrigger
                                active={evidenceTab === 'cited'}
                                onClick={() => setEvidenceTab('cited')}
                                label="Selected"
                                count={selectedSummary.rowCount}
                            />
                            {available.length + belowCutCorridors.length >
                                0 && (
                                <TabTrigger
                                    active={evidenceTab === 'below'}
                                    onClick={() => setEvidenceTab('below')}
                                    label="Available"
                                    count={belowSummary.rowCount}
                                    tone="muted"
                                />
                            )}
                        </>
                    )}
                </div>
                <p
                    className="px-1 text-2xs text-muted-foreground"
                    data-testid="evidence-summary"
                >
                    {activeSummary.rowCount} {activeSummaryLabel} ·{' '}
                    {activeSummary.sourceRecordCount} source records
                </p>
                <p className="px-1 text-3xs text-muted-foreground-subtle">
                    Counts group incident segments and flow readings by road or
                    story.
                </p>

                {/* Tab content */}
                {provenance ? (
                    <>
                        {evidenceTab === 'cited' &&
                            renderBucket(cited, citedCorridors, true, 'cited')}
                        {evidenceTab === 'unused' && (
                            <>
                                {job.status === 'FALLBACK_USED' && (
                                    <p className="rounded-md bg-muted/30 px-2.5 py-2 text-xs leading-snug text-muted-foreground">
                                        <span className="font-medium text-foreground/80">
                                            Fallback script.
                                        </span>{' '}
                                        Live traffic was available but none of
                                        it was referenced.
                                    </p>
                                )}
                                {renderBucket(
                                    unusedSelected,
                                    unusedCorridors,
                                    true,
                                    'unused'
                                )}
                            </>
                        )}
                        {evidenceTab === 'below' &&
                            renderBucket(
                                available,
                                belowCutCorridors,
                                false,
                                'available'
                            )}
                    </>
                ) : (
                    <>
                        {evidenceTab === 'cited' &&
                            renderBucket(
                                included,
                                citedCorridors,
                                true,
                                'selected'
                            )}
                        {evidenceTab === 'below' &&
                            renderBucket(
                                available,
                                belowCutCorridors,
                                false,
                                'available'
                            )}
                    </>
                )}
            </div>
        </SpotlightCard>
    )
}
