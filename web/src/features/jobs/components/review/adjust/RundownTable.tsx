import { useMemo, useState } from 'react'
import {
    DndContext,
    PointerSensor,
    useSensor,
    useSensors,
    pointerWithin,
    useDraggable,
    useDroppable,
    type DragEndEvent,
    type DraggableAttributes,
    type DraggableSyntheticListeners,
} from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import {
    Check,
    ChevronRight,
    ExternalLink,
    GripVertical,
    VolumeX,
} from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { SourceChips } from './SourceChips'
import { Badge } from '@/components/ui/badge'
import type { TrafficCorridor, FlowRoad } from '../../../types'
import type {
    IncidentRow,
    CorridorCuration,
} from '../../../hooks/use-bulletin-review'
import type { ProvenanceResult } from '../../../lib/verification/provenance'
import {
    buildCoverageGroups,
    type CorridorGroup,
} from '../../../lib/incidents/coverage-grouping'
import {
    corridorDisplayDotClass,
    corridorDisplayLabel,
    incidentFactLine,
    incidentSourceTag,
    roadClassLabel,
    corridorTypeLabel,
    flowClearness,
} from '../../../lib/incidents/incident-labels'
import {
    googleMapsTrafficUrl,
    openGoogleMapsTrafficPopup,
} from '../../../lib/map/map-links'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { InfoHint } from '@/components/InfoHint'
import { sourceDisplayName } from '../../../lib/source-display-names'
import { formatRelativeTime, formatRoadName } from '@/lib/formatting'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { cn } from '@/lib/utils'

// ---------------------------------------------------------------------------
// Mobile: handle · road · direction · severity · score (5 cols, no
// horizontal scroll). Desktop (md+): adds Km; Type and Class rejoin only
// from 2xl, once the card is wide enough that they cannot starve the road
// name. The road column takes all remaining space: it is the identity of a
// row the operator is re-ranking, so low-cardinality columns are sized to
// their longest real value instead of holding a fixed budget. Direction is
// its own column (not a second text line under the road name) so every row
// keeps the same height.

const COLS =
    'grid grid-cols-[auto_minmax(0,1fr)_auto_auto_auto] gap-x-1.5 md:grid-cols-[18px_minmax(0,1fr)_68px_64px_42px_84px] md:gap-x-2 2xl:grid-cols-[18px_minmax(0,1fr)_68px_78px_68px_64px_42px_88px] items-center'
// ---------------------------------------------------------------------------
// Unified row model — incident corridors and flowing roads share one scale.
// ---------------------------------------------------------------------------

interface CorridorRowModel {
    readonly id: string
    readonly kind: 'corridor'
    readonly onAir: boolean
    readonly score: number
    readonly roadName: string
    readonly group: CorridorGroup
}
interface FlowRowModel {
    readonly id: string
    readonly kind: 'flow'
    readonly onAir: boolean
    readonly score: number
    readonly roadName: string
    readonly flow: FlowRoad
}
type RowModel = CorridorRowModel | FlowRowModel
function isVoicedRoad(
    provenance: ProvenanceResult | null | undefined,
    roadName: string,
    incidents?: readonly IncidentRow[],
    incidentKeys?: readonly string[] | null
): boolean {
    if (!provenance) return false
    const roadKey = roadName.trim().toLowerCase()
    return (
        (provenance.byCorridorRoad[roadKey]?.sentenceIndexes.length ?? 0) > 0 ||
        (incidents?.some(
            (row) =>
                (provenance.byIncidentKey[row.incident.key]?.sentenceIndexes
                    .length ?? 0) > 0
        ) ??
            false) ||
        (incidentKeys?.some(
            (key) =>
                (provenance.byIncidentKey[key]?.sentenceIndexes.length ?? 0) > 0
        ) ??
            false)
    )
}

// ---------------------------------------------------------------------------
// Incident sub-row (revealed when a corridor is expanded) — the only checkboxes
// in the table: which individual incidents on an included road to mention.
// ---------------------------------------------------------------------------

/** One event's movement vs the previous bulletin, joined from the Event Ledger delta. */
export interface RowDelta {
    readonly status: 'New' | 'Changed' | 'Cleared' | 'Unchanged'
}

function IncidentDetailRow({
    row,
    roadOnAir,
    canEdit,
    onToggleInclude,
    mentionsCount,
    isHighlighted,
    onHover,
    delta,
}: {
    readonly row: IncidentRow
    readonly roadOnAir: boolean
    readonly canEdit: boolean
    readonly onToggleInclude?: (key: string) => void
    readonly mentionsCount: number | null
    readonly isHighlighted: boolean
    readonly onHover?: (key: string | null) => void
    readonly delta?: RowDelta
}) {
    const { incident, included } = row
    const disabled = !canEdit || !roadOnAir || !onToggleInclude
    const factLine = incidentFactLine(incident)
    const sourceTag = incidentSourceTag(incident.sources, sourceDisplayName)
    return (
        <div
            className={cn(
                'flex items-start gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-muted/20',
                isHighlighted && 'bg-primary/5',
                !roadOnAir && 'opacity-50'
            )}
            onMouseEnter={() => onHover?.(incident.key)}
            onMouseLeave={() => onHover?.(null)}
        >
            <button
                type="button"
                role="checkbox"
                aria-checked={included}
                aria-label={
                    included ? 'Drop this incident' : 'Mention this incident'
                }
                disabled={disabled}
                onClick={() => onToggleInclude?.(incident.key)}
                className={cn(
                    'mt-0.5 flex size-4 shrink-0 items-center justify-center rounded border transition-colors',
                    included
                        ? 'border-primary bg-primary text-primary-foreground'
                        : 'border-muted-foreground/40 hover:border-muted-foreground',
                    disabled && 'cursor-not-allowed opacity-60'
                )}
            >
                {included && <Check className="size-3" aria-hidden />}
            </button>
            <div className="min-w-0 flex-1">
                <span
                    className={cn(
                        'block truncate text-xs leading-snug',
                        !roadOnAir && 'line-through'
                    )}
                >
                    {incident.headline}
                    {(incident.isNew || delta?.status === 'New') && (
                        <span className="ml-1.5 text-3xs font-semibold uppercase tracking-wide text-primary">
                            new
                        </span>
                    )}
                    {delta?.status === 'Changed' && (
                        <span className="ml-1.5 text-3xs font-semibold uppercase tracking-wide text-warning">
                            changed
                        </span>
                    )}
                    {incident.roadClosed && (
                        <span className="ml-1.5 text-2xs font-semibold uppercase tracking-wide text-destructive">
                            closed
                        </span>
                    )}
                    {roadOnAir && included && mentionsCount === 0 && (
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span className="ml-1.5 inline-flex translate-y-px text-warning">
                                    <VolumeX className="size-3" aria-hidden />
                                </span>
                            </TooltipTrigger>
                            <TooltipContent side="top">
                                In the bulletin but not mentioned in the script
                            </TooltipContent>
                        </Tooltip>
                    )}
                    {incident.latitude != null &&
                        incident.longitude != null && (
                            <a
                                href={googleMapsTrafficUrl(
                                    incident.latitude,
                                    incident.longitude
                                )}
                                target="_blank"
                                rel="noopener noreferrer"
                                onClick={(e) => {
                                    e.stopPropagation()
                                    openGoogleMapsTrafficPopup(
                                        incident.latitude as number,
                                        incident.longitude as number,
                                        e
                                    )
                                }}
                                className="ml-1.5 inline-flex translate-y-px text-muted-foreground hover:text-foreground"
                                aria-label="Open traffic view in Google Maps"
                                title="Open traffic view in Google Maps"
                            >
                                <ExternalLink className="size-3" aria-hidden />
                            </a>
                        )}
                    {incident.entryTimeUtc && (
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span className="ml-1.5 inline-flex translate-y-px text-muted-foreground hover:text-foreground">
                                    <ICON.time className="size-3" aria-hidden />
                                </span>
                            </TooltipTrigger>
                            <TooltipContent side="top">
                                Reported{' '}
                                {formatRelativeTime(incident.entryTimeUtc)}
                            </TooltipContent>
                        </Tooltip>
                    )}
                </span>
                {(factLine || sourceTag) && (
                    <span
                        className="line-clamp-1 text-2xs text-muted-foreground"
                        title={factLine ?? undefined}
                    >
                        {sourceTag && (
                            <span className="mr-1.5 rounded-sm bg-muted px-1 py-px font-medium text-foreground/70">
                                {sourceTag}
                            </span>
                        )}
                        {factLine}
                    </span>
                )}
            </div>
        </div>
    )
}

// ---------------------------------------------------------------------------
// Score cell
// ---------------------------------------------------------------------------

function ScoreCell({
    score,
    maxScore,
    priorityBoosted = false,
    boostAmount,
    boostLabel,
}: {
    readonly score: number
    readonly maxScore: number
    readonly priorityBoosted?: boolean
    readonly boostAmount?: number
    readonly boostLabel?: string
}) {
    const pct = maxScore > 0 ? Math.max((score / maxScore) * 100, 4) : 0
    const roundedScore = Math.round(score)
    // Null/undefined means the amount is genuinely unknown (a job persisted before the
    // AudioFinalizer flow-road field fix, which never stored BoostAmount). A KNOWN boost
    // must never fall back to the bare "(+)": Math.round(0.4) is 0, which is falsy, so
    // testing truthiness would report a real sub-unit boost as unknown.
    const hasNumericBoost = boostAmount != null && boostAmount > 0
    const roundedBoost = hasNumericBoost
        ? Math.max(1, Math.round(boostAmount))
        : null

    const scoreDisplay = (
        <span
            className={cn(
                'inline-flex items-baseline justify-end gap-1 font-mono text-right tabular-nums whitespace-nowrap min-w-[54px]',
                priorityBoosted
                    ? 'text-warning font-bold cursor-help'
                    : 'text-foreground font-semibold text-xs'
            )}
        >
            <span
                className={cn(
                    'text-xs min-w-[24px] text-right',
                    priorityBoosted && 'text-warning'
                )}
            >
                {roundedScore}
            </span>
            {priorityBoosted ? (
                <span className="text-3xs font-semibold text-warning tracking-tight min-w-[26px] text-left">
                    {roundedBoost ? `(+${roundedBoost})` : '(+)'}
                </span>
            ) : (
                <span className="min-w-[26px]" aria-hidden="true" />
            )}
        </span>
    )

    return (
        <span className="flex items-center justify-end gap-2 w-full">
            <span className="hidden h-1 w-6 overflow-hidden rounded-full bg-muted lg:block shrink-0">
                <span
                    className={cn(
                        'block h-full rounded-full',
                        priorityBoosted
                            ? 'bg-warning'
                            : 'bg-linear-to-r from-primary/50 to-primary'
                    )}
                    style={{ width: `${pct}%` }}
                />
            </span>
            {priorityBoosted ? (
                <Tooltip>
                    <TooltipTrigger asChild>{scoreDisplay}</TooltipTrigger>
                    <TooltipContent side="top" className="text-xs">
                        {boostLabel ??
                            (roundedBoost
                                ? `Priority boost: +${roundedBoost}`
                                : 'Priority boosted')}
                    </TooltipContent>
                </Tooltip>
            ) : (
                scoreDisplay
            )}
        </span>
    )
}

// ---------------------------------------------------------------------------
// Drag handle: drag to move the road across the cut, click (or Enter/Space
// on the focused button) to toggle it in or out. Both paths perform the
// same state change the drop performs, so the cut stays reachable without
// a pointing device. The PointerSensor distance constraint suppresses the
// trailing click after a real drag, so a drag never double-fires the toggle.
// ---------------------------------------------------------------------------

function DragHandle({
    listeners,
    attributes,
    label,
    onClick,
}: {
    readonly listeners: DraggableSyntheticListeners
    readonly attributes: DraggableAttributes
    readonly label: string
    readonly onClick?: (e: React.MouseEvent) => void
}) {
    return (
        <button
            type="button"
            aria-label={label}
            className="flex shrink-0 cursor-grab touch-none items-center justify-center rounded p-1 text-muted-foreground-subtle hover:text-foreground active:cursor-grabbing pointer-coarse:min-h-11 pointer-coarse:min-w-11"
            onClick={onClick}
            {...attributes}
            {...listeners}
        >
            <GripVertical className="size-3.5" aria-hidden />
        </button>
    )
}

// ---------------------------------------------------------------------------
// Corridor row
// ---------------------------------------------------------------------------

function CorridorRow({
    model,
    maxScore,
    canEdit,
    getMentions,
    highlightSet,
    pinnedIncidentKey,
    onToggleInclude,
    onIncidentHover,
    onCorridorHover,
    onToggle,
    deltaByKey,
    provenance,
}: {
    readonly model: CorridorRowModel
    readonly maxScore: number
    readonly canEdit: boolean
    readonly getMentions: (key: string) => number | null
    readonly highlightSet: ReadonlySet<string>
    readonly pinnedIncidentKey?: string | null
    readonly onToggleInclude?: (key: string) => void
    readonly onIncidentHover?: (key: string | null) => void
    readonly onCorridorHover?: (keys: readonly string[] | null) => void
    readonly onToggle?: () => void
    readonly deltaByKey?: Record<string, RowDelta>
    readonly provenance?: ProvenanceResult | null
}) {
    const [expanded, setExpanded] = useState(false)
    const { corridor } = model.group
    const incidents = useMemo(
        () => [...model.group.rows, ...model.group.alsoOnRoad],
        [model.group.rows, model.group.alsoOnRoad]
    )
    const { attributes, listeners, setNodeRef, transform, isDragging } =
        useDraggable({ id: model.id, disabled: !canEdit })
    const style = transform
        ? { transform: CSS.Translate.toString(transform), zIndex: 20 }
        : undefined
    const dot = corridorDisplayDotClass(
        corridor.magnitude ?? null,
        corridor.severity
    )
    const sev = corridorDisplayLabel(
        corridor.magnitude ?? null,
        corridor.severity
    )
    const cls = roadClassLabel(corridor.functionalClass)

    return (
        <div
            ref={setNodeRef}
            style={style}
            className={cn(
                'rounded-md border transition-colors',
                model.onAir
                    ? 'border-success/30 bg-success/5'
                    : 'border-transparent bg-muted/10 opacity-80',
                isDragging && 'opacity-90 shadow-lg'
            )}
            onMouseEnter={() => onCorridorHover?.(corridor.incidentKeys ?? [])}
            onMouseLeave={() => onCorridorHover?.(null)}
        >
            <div
                className={cn(COLS, 'cursor-pointer px-1.5 py-1.5')}
                onClick={() => setExpanded((v) => !v)}
                /* Keyboard parity with the click-to-expand: the per-incident
                   picks live INSIDE the collapsed row, and a keyboard-only
                   operator could never reach them. No `role="button"` here —
                   the row also contains the drag-handle button, and a button
                   role wrapper would nest interactive elements (axe
                   nested-interactive). */
                tabIndex={0}
                onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        setExpanded((v) => !v)
                    }
                }}
            >
                {canEdit ? (
                    <DragHandle
                        listeners={listeners}
                        attributes={attributes}
                        label={
                            model.onAir
                                ? `Remove ${formatRoadName(corridor.roadName)} from the bulletin`
                                : `Add ${formatRoadName(corridor.roadName)} to the bulletin`
                        }
                        onClick={(e) => {
                            e.stopPropagation()
                            onToggle?.()
                        }}
                    />
                ) : (
                    <span />
                )}

                {/* Road */}
                <span className="flex min-w-0 items-center gap-1">
                    {incidents.length > 0 ? (
                        expanded ? (
                            <ICON.expand
                                className="size-3.5 shrink-0 text-muted-foreground"
                                aria-hidden
                            />
                        ) : (
                            <ChevronRight
                                className="size-3.5 shrink-0 text-muted-foreground"
                                aria-hidden
                            />
                        )
                    ) : (
                        <span className="size-3.5 shrink-0" />
                    )}
                    <span className="min-w-0">
                        <span className="block truncate text-sm font-medium leading-tight">
                            {formatRoadName(corridor.roadName)}
                            {corridor.suburb && (
                                <span className="ml-1.5 text-2xs font-normal text-muted-foreground">
                                    · {corridor.suburb}
                                </span>
                            )}
                            <span className="ml-1.5 inline-flex items-center gap-1.5 align-middle">
                                <SourceChips
                                    corridor={corridor}
                                    incidents={incidents.map((r) => r.incident)}
                                />
                                {isVoicedRoad(
                                    provenance,
                                    corridor.roadName,
                                    incidents,
                                    corridor.incidentKeys
                                ) ? (
                                    <Badge
                                        variant="success"
                                        className="px-1.5 py-0 text-[10px] font-semibold"
                                    >
                                        VOICED
                                    </Badge>
                                ) : null}
                            </span>
                        </span>
                    </span>
                </span>
                <span className="truncate text-xs text-muted-foreground">
                    {corridor.direction ?? '-'}
                </span>

                <span className="hidden 2xl:block truncate text-xs text-muted-foreground">
                    {corridorTypeLabel(corridor.primaryType)}
                </span>
                <span className="hidden 2xl:block truncate text-xs text-muted-foreground">
                    {cls ?? '-'}
                </span>
                <span className="flex items-center gap-1.5 text-xs">
                    <span
                        className={cn('size-1.5 shrink-0 rounded-full', dot)}
                        aria-hidden
                    />
                    <span className="truncate">{sev}</span>
                </span>
                <span className="hidden md:block text-right font-mono text-2xs tabular-nums text-muted-foreground">
                    {corridor.totalLengthKm != null
                        ? corridor.totalLengthKm.toFixed(1)
                        : '-'}
                </span>
                <ScoreCell
                    score={model.score}
                    maxScore={maxScore}
                    priorityBoosted={corridor.priorityBoosted}
                    boostAmount={corridor.boostAmount ?? undefined}
                    boostLabel="Priority road & incident boost"
                />
            </div>

            {expanded && (
                <div className="border-t border-border/40 px-2.5 py-2">
                    {incidents.length > 0 ? (
                        <div className="flex flex-col gap-0.5">
                            {incidents.map((r) => (
                                <IncidentDetailRow
                                    key={r.incident.key}
                                    row={r}
                                    roadOnAir={model.onAir}
                                    canEdit={canEdit}
                                    onToggleInclude={onToggleInclude}
                                    mentionsCount={getMentions(r.incident.key)}
                                    isHighlighted={
                                        highlightSet.has(r.incident.key) ||
                                        pinnedIncidentKey === r.incident.key
                                    }
                                    onHover={onIncidentHover}
                                    delta={deltaByKey?.[r.incident.key]}
                                />
                            ))}
                        </div>
                    ) : (
                        <p className="text-2xs text-muted-foreground">
                            No individual incidents linked to this road.
                        </p>
                    )}
                </div>
            )}
        </div>
    )
}

// ---------------------------------------------------------------------------
// Flow row (a flowing "good run" — scored on the same scale, damped low)
// ---------------------------------------------------------------------------

function FlowRow({
    model,
    maxScore,
    canEdit,
    onToggle,
    provenance,
}: {
    readonly model: FlowRowModel
    readonly maxScore: number
    readonly canEdit: boolean
    readonly onToggle?: () => void
    readonly provenance?: ProvenanceResult | null
}) {
    const { flow } = model
    const { attributes, listeners, setNodeRef, transform, isDragging } =
        useDraggable({ id: model.id, disabled: !canEdit })
    const style = transform
        ? { transform: CSS.Translate.toString(transform), zIndex: 20 }
        : undefined
    const cls = roadClassLabel(flow.functionalClass)

    return (
        <div
            ref={setNodeRef}
            style={style}
            className={cn(
                'rounded-md border transition-colors',
                model.onAir
                    ? 'border-success/30 bg-success/5'
                    : 'border-transparent bg-muted/10 opacity-80',
                isDragging && 'opacity-90 shadow-lg'
            )}
        >
            <div className={cn(COLS, 'px-1.5 py-1.5')}>
                {canEdit ? (
                    <DragHandle
                        listeners={listeners}
                        attributes={attributes}
                        label={
                            model.onAir
                                ? `Remove ${formatRoadName(flow.roadName)} from the bulletin`
                                : `Add ${formatRoadName(flow.roadName)} to the bulletin`
                        }
                        onClick={() => onToggle?.()}
                    />
                ) : (
                    <span />
                )}

                <span className="flex min-w-0 items-center gap-1">
                    <span className="size-3.5 shrink-0" />
                    <span className="block truncate text-sm font-medium leading-tight">
                        {formatRoadName(flow.roadName)}
                        {flow.isWeighted && !flow.priorityBoosted && (
                            <span className="ml-1.5 text-2xs font-medium text-primary">
                                watched
                            </span>
                        )}
                        {isVoicedRoad(provenance, flow.roadName) ? (
                            <Badge
                                variant="success"
                                className="ml-1.5 px-1.5 py-0 text-[10px] font-semibold"
                            >
                                VOICED
                            </Badge>
                        ) : null}
                    </span>
                </span>

                <span className="truncate text-xs text-muted-foreground">
                    -
                </span>

                <span className="hidden 2xl:block truncate text-xs text-success">
                    Good run
                </span>
                <span className="hidden 2xl:block truncate text-xs text-muted-foreground">
                    {cls ?? '-'}
                </span>
                <span className="truncate text-xs text-success">
                    {flowClearness(flow.jamFactor)}
                </span>
                <span className="hidden md:block text-right font-mono text-2xs tabular-nums text-muted-foreground">
                    {flow.lengthKm > 0 ? flow.lengthKm.toFixed(1) : '-'}
                </span>
                <ScoreCell
                    score={model.score}
                    maxScore={maxScore}
                    priorityBoosted={flow.priorityBoosted}
                    boostAmount={flow.boostAmount ?? undefined}
                    boostLabel="Priority road boost"
                />
            </div>
        </div>
    )
}

// ---------------------------------------------------------------------------
// Droppable zone
// ---------------------------------------------------------------------------

function Zone({
    id,
    children,
    className,
}: {
    readonly id: string
    readonly children: React.ReactNode
    readonly className?: string
}) {
    const { setNodeRef, isOver } = useDroppable({ id })
    return (
        <div
            ref={setNodeRef}
            className={cn(
                className,
                isOver && 'rounded-md ring-1 ring-inset ring-primary/40'
            )}
        >
            {children}
        </div>
    )
}

// ---------------------------------------------------------------------------
// Rundown table
// ---------------------------------------------------------------------------

interface RundownTableProps {
    readonly corridors: readonly TrafficCorridor[]
    readonly includedRows: readonly IncidentRow[]
    readonly availableRows: readonly IncidentRow[]
    readonly flowRoads: readonly FlowRoad[]
    readonly canEdit: boolean
    readonly curation?: CorridorCuration
    readonly isFlowSelected: (roadName: string) => boolean
    readonly toggleFlowRoad: (roadName: string) => void
    readonly onToggleInclude?: (key: string) => void
    readonly provenance?: ProvenanceResult | null
    readonly highlightedIncidentKeys?: readonly string[]
    readonly pinnedIncidentKey?: string | null
    readonly onIncidentHover?: (key: string | null) => void
    readonly onCorridorHover?: (keys: readonly string[] | null) => void
    /** Per-incident-key movement vs the previous bulletin (Event Ledger delta). */
    readonly deltaByKey?: Record<string, RowDelta>
    /** Events the previous bulletin carried that this one no longer does. */
    readonly clearedEvents?: ReadonlyArray<{
        readonly key: string
        readonly roadName: string
        readonly headline: string
    }>
}

export function RundownTable({
    corridors,
    includedRows,
    availableRows,
    flowRoads,
    canEdit,
    curation,
    isFlowSelected,
    toggleFlowRoad,
    onToggleInclude,
    provenance,
    highlightedIncidentKeys,
    pinnedIncidentKey,
    onIncidentHover,
    onCorridorHover,
    deltaByKey,
    clearedEvents,
}: RundownTableProps) {
    const onAirOverride = canEdit && curation ? curation.effective : null
    const groups = useMemo(
        () =>
            buildCoverageGroups(
                corridors,
                includedRows,
                availableRows,
                onAirOverride
            ),
        [corridors, includedRows, availableRows, onAirOverride]
    )

    const highlightSet = useMemo(
        () => new Set(highlightedIncidentKeys ?? []),
        [highlightedIncidentKeys]
    )
    const getMentions = useMemo(
        () =>
            (key: string): number | null =>
                provenance?.byIncidentKey[key]?.sentenceIndexes.length ?? null,
        [provenance]
    )

    // One ranked list: incident corridors and flowing good-runs scored on the
    // same scale. The on-air set is the backend's unified top-N (or the
    // operator's curation), so sorting both zones by score never inverts —
    // every on-air row out-scores everything below the cut.
    const { onAirRows, belowRows, maxScore } = useMemo(() => {
        const corridorRows: CorridorRowModel[] = [
            ...groups.onAir.map((g) => ({
                id: `c:${g.corridor.roadName}`,
                kind: 'corridor' as const,
                onAir: true,
                score: g.corridor.impactScore,
                roadName: g.corridor.roadName,
                group: g,
            })),
            ...groups.belowCut.map((g) => ({
                id: `c:${g.corridor.roadName}`,
                kind: 'corridor' as const,
                onAir: false,
                score: g.corridor.impactScore,
                roadName: g.corridor.roadName,
                group: g,
            })),
        ]
        const flowRows: FlowRowModel[] = flowRoads.map((f) => ({
            id: `f:${f.roadName}`,
            kind: 'flow' as const,
            onAir: isFlowSelected(f.roadName),
            score: f.score,
            roadName: f.roadName,
            flow: f,
        }))
        const all: RowModel[] = [...corridorRows, ...flowRows]
        const byScore = (a: RowModel, b: RowModel) => b.score - a.score
        return {
            onAirRows: all.filter((r) => r.onAir).sort(byScore),
            belowRows: all.filter((r) => !r.onAir).sort(byScore),
            maxScore: all.reduce((m, r) => Math.max(m, r.score), 0),
        }
    }, [groups, flowRoads, isFlowSelected])

    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
    )

    // Drag a row across the cut to include/exclude it: corridors toggle the
    // corridor curation, good-runs toggle the flow selection.
    const handleDragEnd = (event: DragEndEvent) => {
        const over = event.over ? String(event.over.id) : null
        if (over !== 'zone:onair' && over !== 'zone:belowcut') return
        const targetOnAir = over === 'zone:onair'
        const id = String(event.active.id)
        const road = id.slice(2)
        if (id.startsWith('c:')) {
            if ((curation?.isOn(road) ?? false) !== targetOnAir)
                curation?.toggle(road)
        } else if (isFlowSelected(road) !== targetOnAir) {
            toggleFlowRoad(road)
        }
    }

    const renderRow = (m: RowModel) =>
        m.kind === 'corridor' ? (
            <CorridorRow
                key={m.id}
                model={m}
                maxScore={maxScore}
                canEdit={canEdit}
                getMentions={getMentions}
                highlightSet={highlightSet}
                pinnedIncidentKey={pinnedIncidentKey}
                onToggleInclude={onToggleInclude}
                onIncidentHover={onIncidentHover}
                onCorridorHover={onCorridorHover}
                onToggle={() => curation?.toggle(m.roadName)}
                deltaByKey={deltaByKey}
                provenance={provenance}
            />
        ) : (
            <FlowRow
                key={m.id}
                model={m}
                maxScore={maxScore}
                canEdit={canEdit}
                onToggle={() => toggleFlowRoad(m.flow.roadName)}
                provenance={provenance}
            />
        )

    const looseIncluded = groups.loose.filter((r) => r.included)
    const hasNothing =
        onAirRows.length === 0 &&
        belowRows.length === 0 &&
        looseIncluded.length === 0

    return (
        <div className="flex flex-col gap-3">
            <SpotlightCard className="p-3">
                {hasNothing ? (
                    <p className="px-2 py-3 text-sm text-muted-foreground">
                        No roads in this bulletin.
                    </p>
                ) : (
                    <div className="overflow-x-auto overscroll-x-contain">
                        <div className="md:min-w-130">
                            <div className="flex items-center justify-between px-1.5 pb-1">
                                <span className="flex items-center gap-1 text-xs text-muted-foreground">
                                    <span className="font-medium text-success">
                                        {onAirRows.length}
                                    </span>{' '}
                                    sent to writer · max 20
                                    {belowRows.length > 0 && (
                                        <>
                                            {' · '}
                                            <span className="text-muted-foreground">
                                                {belowRows.length} not sent
                                            </span>
                                        </>
                                    )}
                                </span>
                                {canEdit && (
                                    <InfoHint label="How does the rundown ranking work?">
                                        Incidents and flowing good-runs are
                                        ranked together by score. Good runs sit
                                        well below incidents, so they only make
                                        the cut when they out-rank a minor
                                        incident or are a priority road. Drag a
                                        row in or out of the bulletin; expand an
                                        incident road to choose which incidents
                                        to mention. Picks apply on the next
                                        regenerate.
                                    </InfoHint>
                                )}
                            </div>
                            {/* Column header */}
                            <div
                                className={cn(
                                    COLS,
                                    'px-1.5 pb-1 text-2xs font-medium uppercase tracking-wide text-muted-foreground-subtle'
                                )}
                            >
                                <span />
                                <span>Road</span>
                                <span>Direction</span>
                                <span className="hidden 2xl:block">Type</span>
                                <span className="hidden 2xl:block">Class</span>
                                <span>Severity</span>
                                <span className="hidden md:block text-right">
                                    Km
                                </span>
                                <span className="text-right">Score</span>
                            </div>

                            <DndContext
                                sensors={sensors}
                                collisionDetection={pointerWithin}
                                onDragEnd={handleDragEnd}
                            >
                                <Zone
                                    id="zone:onair"
                                    className="flex min-h-2 flex-col gap-1"
                                >
                                    {onAirRows.map(renderRow)}
                                    {onAirRows.length === 0 && (
                                        <p className="px-2 py-2 text-2xs text-muted-foreground">
                                            Drag a road here to put it on air.
                                        </p>
                                    )}
                                </Zone>

                                {belowRows.length > 0 && (
                                    <div className="my-2 flex items-center gap-2 px-1">
                                        <span className="h-px flex-1 bg-border/60" />
                                        <span className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">
                                            NOT SENT TO WRITER
                                        </span>
                                        <span className="h-px flex-1 bg-border/60" />
                                    </div>
                                )}

                                <Zone
                                    id="zone:belowcut"
                                    className="flex min-h-2 flex-col gap-1"
                                >
                                    {belowRows.map(renderRow)}
                                </Zone>
                            </DndContext>

                            {/* What the previous bulletin carried that this one no longer
                                does. Same divider idiom as the cut line: a disappearing
                                story is never invisible, and never needs a click to find. */}
                            {(clearedEvents?.length ?? 0) > 0 && (
                                <>
                                    <div className="my-1.5 flex items-center gap-2 px-1">
                                        <span className="h-px flex-1 bg-border" />
                                        <span className="text-2xs font-medium uppercase tracking-wider text-muted-foreground-subtle">
                                            cleared since last bulletin
                                        </span>
                                        <span className="h-px flex-1 bg-border" />
                                    </div>
                                    <div className="flex flex-col gap-0.5">
                                        {clearedEvents!.map((e) => (
                                            <div
                                                key={e.key}
                                                className="flex items-baseline gap-2 rounded-md px-2 py-1 opacity-70"
                                                data-testid="cleared-row"
                                            >
                                                <span className="text-xs font-medium text-muted-foreground line-through">
                                                    {formatRoadName(e.roadName)}
                                                </span>
                                                <span className="truncate text-2xs text-muted-foreground">
                                                    {e.headline}
                                                </span>
                                            </div>
                                        ))}
                                    </div>
                                </>
                            )}

                            {/* Loose incidents with no ranked road */}
                            {looseIncluded.length > 0 && (
                                <div className="mt-2 border-t border-border/40 pt-2">
                                    <p className="px-1 pb-1 text-xs font-semibold text-muted-foreground">
                                        Elsewhere in the coverage area
                                    </p>
                                    <div className="flex flex-col gap-0.5">
                                        {looseIncluded.map((row) => (
                                            <IncidentDetailRow
                                                key={row.incident.key}
                                                row={row}
                                                roadOnAir
                                                canEdit={canEdit}
                                                onToggleInclude={
                                                    onToggleInclude
                                                }
                                                mentionsCount={getMentions(
                                                    row.incident.key
                                                )}
                                                isHighlighted={
                                                    highlightSet.has(
                                                        row.incident.key
                                                    ) ||
                                                    pinnedIncidentKey ===
                                                        row.incident.key
                                                }
                                                onHover={onIncidentHover}
                                            />
                                        ))}
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                )}
            </SpotlightCard>
        </div>
    )
}
