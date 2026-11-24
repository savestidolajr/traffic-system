import { useMemo, useState, useCallback } from 'react'
import { Clock3, ListChecks } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useNow } from '@/hooks/use-second-tick'
import { cn } from '@/lib/utils'
import type {
    Job,
    JobIncident,
    TrafficCorridor,
    FlowRoad,
    TrafficData,
} from '../../types'
import { RundownTable } from '../review/adjust/RundownTable'
import { AdjustMap } from '../review/adjust/AdjustMap'
import type {
    CorridorCuration,
    IncidentRow,
    IncidentCorridorRef,
} from '../../hooks/use-bulletin-review'
type CurationJob = Job & {
    readonly traffic?: TrafficData
    readonly curationDeadlineAt?: string | null
}

function formatCountdown(seconds: number): string {
    const safe = Math.max(0, seconds)
    const minutes = Math.floor(safe / 60)
    const remaining = safe % 60
    return `${minutes}:${remaining.toString().padStart(2, '0')}`
}

export function CurationPane({
    job,
    onProceed,
    isProceeding = false,
}: {
    readonly job: Job
    readonly onProceed: (
        editedIncidents: readonly {
            incidentId: string
            included: boolean
            priority: number
        }[]
    ) => Promise<void>
    readonly isProceeding?: boolean
}) {
    const curationJob = job as CurationJob
    const traffic = curationJob.traffic
    const now = useNow()
    const deadlineAt = curationJob.curationDeadlineAt?.trim() || null
    const deadlineMs = deadlineAt ? Date.parse(deadlineAt) : Number.NaN
    const secondsRemaining = Number.isFinite(deadlineMs)
        ? Math.ceil((deadlineMs - now) / 1000)
        : null

    // All corridors and raw incidents from fetched traffic
    const rawCorridors = useMemo<readonly TrafficCorridor[]>(
        () => traffic?.corridors ?? [],
        [traffic?.corridors]
    )
    const rawIncidents = useMemo<readonly JobIncident[]>(
        () => traffic?.incidents ?? [],
        [traffic?.incidents]
    )
    const flowRoads = useMemo<readonly FlowRoad[]>(
        () => traffic?.flowRoads ?? [],
        [traffic?.flowRoads]
    )

    // Machine selected corridor order
    const machineCorridorOrder = useMemo<string[]>(() => {
        const explicitlySelected = rawCorridors.filter((c) => c.isSelected)
        const candidates =
            explicitlySelected.length > 0
                ? explicitlySelected
                : rawCorridors.slice(0, 10)
        return candidates
            .slice()
            .sort((a, b) => a.rank - b.rank)
            .map((c) => c.roadName)
    }, [rawCorridors])

    // Operator corridor curation state (order & included roads)
    const [corridorOrder, setCorridorOrder] = useState<string[] | null>(null)
    const effectiveCorridors = corridorOrder ?? machineCorridorOrder

    const toggleCorridor = useCallback(
        (roadName: string) => {
            setCorridorOrder((prev) => {
                const base = prev ?? machineCorridorOrder
                if (base.includes(roadName)) {
                    if (base.length === 1) return prev
                    return base.filter((r) => r !== roadName)
                }
                return [...base, roadName]
            })
        },
        [machineCorridorOrder]
    )

    const moveCorridor = useCallback(
        (roadName: string, delta: -1 | 1) => {
            setCorridorOrder((prev) => {
                const base = [...(prev ?? machineCorridorOrder)]
                const index = base.indexOf(roadName)
                const nextIndex = index + delta
                if (index < 0 || nextIndex < 0 || nextIndex >= base.length)
                    return prev
                ;[base[index], base[nextIndex]] = [base[nextIndex], base[index]]
                return base
            })
        },
        [machineCorridorOrder]
    )

    const corridorCuration = useMemo<CorridorCuration>(() => {
        return {
            order: corridorOrder,
            effective: effectiveCorridors,
            isOn: (roadName) => effectiveCorridors.includes(roadName),
            toggle: toggleCorridor,
            moveUp: (roadName) => moveCorridor(roadName, -1),
            moveDown: (roadName) => moveCorridor(roadName, 1),
            reset: () => setCorridorOrder(null),
        }
    }, [corridorOrder, effectiveCorridors, toggleCorridor, moveCorridor])

    // Flow selections
    const [flowSelection, setFlowSelection] = useState<string[]>(() =>
        flowRoads.map((f) => f.roadName)
    )
    const isFlowSelected = useCallback(
        (roadName: string) => flowSelection.includes(roadName),
        [flowSelection]
    )
    const toggleFlowRoad = useCallback((roadName: string) => {
        setFlowSelection((prev) =>
            prev.includes(roadName)
                ? prev.filter((r) => r !== roadName)
                : [...prev, roadName]
        )
    }, [])

    // Incident-level inclusion state
    const [excludedIncidentKeys, setExcludedIncidentKeys] = useState<
        Set<string>
    >(() => new Set())

    const handleToggleInclude = useCallback((key: string) => {
        setExcludedIncidentKeys((prev) => {
            const next = new Set(prev)
            if (next.has(key)) {
                next.delete(key)
            } else {
                next.add(key)
            }
            return next
        })
    }, [])

    // Map raw incidents to IncidentRows with corridor resolution
    const corridorByKey = useMemo(() => {
        const map = new Map<string, IncidentCorridorRef>()
        for (const corridor of rawCorridors) {
            for (const key of corridor.incidentKeys ?? []) {
                const existing = map.get(key)
                if (!existing || corridor.rank < existing.rank) {
                    map.set(key, {
                        roadName: corridor.roadName,
                        rank: corridor.rank,
                        isSelected: corridor.isSelected,
                        magnitude: corridor.magnitude ?? null,
                        severity: corridor.severity,
                    })
                }
            }
        }
        return map
    }, [rawCorridors])

    const { includedRows, availableRows } = useMemo(() => {
        const incRows: IncidentRow[] = []
        const availRows: IncidentRow[] = []

        for (let i = 0; i < rawIncidents.length; i++) {
            const incident = rawIncidents[i]
            if (!incident) continue
            const isIncluded = !excludedIncidentKeys.has(incident.key)
            const corridor = corridorByKey.get(incident.key) ?? null
            const row: IncidentRow = {
                incident,
                rank: isIncluded ? i + 1 : null,
                included: isIncluded,
                isFirst: i === 0,
                isLast: i === rawIncidents.length - 1,
                corridor,
            }
            if (isIncluded) {
                incRows.push(row)
            } else {
                availRows.push(row)
            }
        }
        return { includedRows: incRows, availableRows: availRows }
    }, [rawIncidents, excludedIncidentKeys, corridorByKey])

    // Interactive hover / pin / highlight state for the map and table
    const [pinnedIncidentKey, setPinnedIncidentKey] = useState<string | null>(
        null
    )
    const [highlightedIncidentKeys, setHighlightedIncidentKeys] = useState<
        readonly string[]
    >([])

    const handleIncidentPin = useCallback((key: string) => {
        setPinnedIncidentKey((prev) => (prev === key ? null : key))
    }, [])

    const handleIncidentHover = useCallback((key: string | null) => {
        setHighlightedIncidentKeys(key ? [key] : [])
    }, [])

    const handleCorridorHover = useCallback(
        (keys: readonly string[] | null) => {
            setHighlightedIncidentKeys(keys ?? [])
        },
        []
    )

    const proceed = async () => {
        const editedIncidents = rawIncidents.map((incident, index) => {
            const corridorIndex = rawCorridors.findIndex((corridor) =>
                corridor.incidentKeys?.includes(incident.key)
            )
            const corridor =
                corridorIndex >= 0 ? rawCorridors[corridorIndex] : undefined
            const isCorridorOnAir = corridor
                ? effectiveCorridors.includes(corridor.roadName)
                : true
            const isIncidentIncluded =
                !excludedIncidentKeys.has(incident.key) && isCorridorOnAir

            // Priority derived from curated corridor ranking
            let priority = index + 1
            if (corridor) {
                const onAirRank = effectiveCorridors.indexOf(corridor.roadName)
                priority =
                    onAirRank >= 0
                        ? onAirRank + 1
                        : effectiveCorridors.length + corridorIndex + 1
            }

            return {
                incidentId: incident.key,
                included: isIncidentIncluded,
                priority,
            }
        })
        await onProceed(editedIncidents)
    }

    return (
        <div className="flex flex-col gap-5 pb-24" data-testid="curation-pane">
            <section
                aria-labelledby="curation-heading"
                className="border-b border-border/70 pb-4"
            >
                <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3">
                    <div className="flex min-w-0 items-center gap-3">
                        <div className="flex size-8 shrink-0 items-center justify-center rounded-lg border border-warning/40 bg-warning/10 text-warning">
                            <Clock3 className="size-4" aria-hidden />
                        </div>
                        <div className="min-w-0">
                            <h2
                                id="curation-heading"
                                className="text-sm font-semibold tracking-tight"
                            >
                                Curation window open
                            </h2>
                            <p className="mt-0.5 text-sm text-muted-foreground">
                                Adjust the rundown before drafting.
                            </p>
                        </div>
                    </div>
                    <div
                        className="flex shrink-0 items-center gap-3"
                        aria-live="polite"
                    >
                        <div className="text-right">
                            <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                                Time left
                            </div>
                            <div
                                className={cn(
                                    'font-mono text-xl font-semibold tabular-nums',
                                    secondsRemaining != null &&
                                        secondsRemaining <= 30
                                        ? 'text-destructive'
                                        : 'text-warning'
                                )}
                            >
                                {secondsRemaining == null
                                    ? '-'
                                    : secondsRemaining > 0
                                      ? formatCountdown(secondsRemaining)
                                      : '00:00'}
                            </div>
                        </div>
                        <div className="hidden max-w-32 text-xs leading-4 text-muted-foreground sm:block">
                            {secondsRemaining == null
                                ? 'No server deadline supplied'
                                : 'Auto-proceeds if unattended'}
                        </div>
                    </div>
                </div>
            </section>

            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h2 className="text-lg font-semibold">Traffic rundown</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                        {effectiveCorridors.length} road
                        {effectiveCorridors.length === 1 ? '' : 's'} on air ·
                        drag across the cut line or expand rows to exclude
                        specific incidents.
                    </p>
                </div>
                <Button
                    data-testid="proceed-draft-button"
                    onClick={() => void proceed()}
                    disabled={isProceeding || secondsRemaining === 0}
                    className="gap-2"
                >
                    <ListChecks className="size-4" aria-hidden />
                    {isProceeding ? 'Starting draft…' : 'Proceed to draft'}
                </Button>
            </div>

            <div className="grid gap-3 @4xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
                <div className="min-w-0">
                    <RundownTable
                        corridors={rawCorridors}
                        includedRows={includedRows}
                        availableRows={availableRows}
                        flowRoads={flowRoads}
                        canEdit={true}
                        curation={corridorCuration}
                        isFlowSelected={isFlowSelected}
                        toggleFlowRoad={toggleFlowRoad}
                        onToggleInclude={handleToggleInclude}
                        highlightedIncidentKeys={highlightedIncidentKeys}
                        pinnedIncidentKey={pinnedIncidentKey}
                        onIncidentHover={handleIncidentHover}
                        onCorridorHover={handleCorridorHover}
                    />
                </div>
                <div className="flex min-w-0 flex-col gap-3 lg:sticky lg:top-4">
                    <div className="min-h-72 flex-1">
                        <AdjustMap
                            job={job}
                            includedRows={includedRows}
                            availableRows={availableRows}
                            highlightedIncidentKeys={highlightedIncidentKeys}
                            onMarkerClick={handleIncidentPin}
                            focusedKey={
                                pinnedIncidentKey ??
                                (highlightedIncidentKeys &&
                                highlightedIncidentKeys.length === 1
                                    ? highlightedIncidentKeys[0]
                                    : null)
                            }
                        />
                    </div>
                </div>
            </div>
        </div>
    )
}
