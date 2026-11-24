import { useMemo, useState } from 'react'
import { toast } from 'sonner'
// Selected-chip tick: ICON carries only `success: CircleCheck`, and the established
// selected-chip pair in this app is a bare Check with Plus (AssignOperatorDialog).
import { Check as CheckIcon } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ComposerField } from '@/components/ComposerField'
import { Slider } from '@/components/ui/slider'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { RuleTab } from '@/components/RuleTab'
import { RuleRow } from '@/components/RuleRow'
import { RuleComposer } from '@/components/RuleComposer'
import { RuleToolbar } from '@/components/RuleToolbar'
import {
    ScopeGroupedList,
    type ScopeGroup,
} from '@/components/ScopeGroupedList'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { useIsAdmin } from '@/hooks/use-current-user'
import {
    useAllPriorityRoads,
    useSetPriorityRoads,
    PRIORITY_ROADS_MAX,
    PRIORITY_ROAD_MAX_LENGTH,
    type PriorityRoadEntry,
    type FlowTier,
} from '../api/priority-roads-hooks'

const AddIcon = ICON.add
const EditIcon = ICON.edit
const ResetIcon = ICON.reset

interface MarketOption {
    readonly marketId: string
    readonly label: string
}

interface RoadRow {
    readonly marketId: string
    readonly entry: PriorityRoadEntry
}

/**
 * The two independent jobs a priority road can do. Both optional: a road can do either,
 * both, or neither. Matches the pipeline exactly — `BoostIncidents` (an impact multiplier
 * on this road's incident corridors) and `FlowTier != None` (it competes for a slot while
 * flowing). "Good runs" is the term the review surfaces already use for a road reported as
 * flowing well, so the two places name the same thing the same way.
 */
const ROAD_USES = [
    { value: 'incidents', label: 'Incidents' },
    { value: 'goodRuns', label: 'Good runs' },
] as const

type RoadUse = (typeof ROAD_USES)[number]['value']

/**
 * What a road is used for, as ONE multi-choice control.
 *
 * The two jobs used to be two different idioms: a bare "Incidents" pill whose off state was
 * only a missing tint, beside a select whose values ("Normal", "High") never said which
 * dimension they graded — and which carried a third, duplicate off switch ("Don't mention").
 * One group says the true shape of the setting: pick the jobs this road does. Turning a job
 * off now happens in exactly one place, and the tier select is left to grade only the job it
 * belongs to.
 */
function RoadUseToggle({
    uses,
    roadName,
    onChange,
    disabled,
}: {
    readonly uses: readonly RoadUse[]
    readonly roadName: string
    readonly onChange: (uses: readonly RoadUse[]) => void
    readonly disabled?: boolean
}) {
    return (
        <ToggleGroup
            type="multiple"
            value={[...uses]}
            onValueChange={(next) => onChange(next as RoadUse[])}
            disabled={disabled}
            aria-label={`What ${roadName} is used for`}
            className="gap-1"
        >
            {ROAD_USES.map((use) => {
                const on = uses.includes(use.value)
                return (
                    <ToggleGroupItem
                        key={use.value}
                        value={use.value}
                        variant="outline"
                        className={cn(
                            // The one row-pill scale in this hub: `Badge` is
                            // `px-2.5 py-0.5 text-xs rounded-full`, and the filler tab's row
                            // action is `h-7 text-xs` with a `size-3.5` icon. h-7 because the
                            // shared toggle sizes start at h-9, twice this row's other controls.
                            'h-7 gap-1.5 rounded-full px-2.5 text-xs font-medium',
                            '[&_svg]:size-3.5',
                            'text-muted-foreground data-[state=on]:border-primary/40',
                            'data-[state=on]:bg-primary/10 data-[state=on]:text-primary-on-tint',
                            // The shared outline variant hovers to `bg-accent`, which is close
                            // enough to the on-tint that an OFF chip on the hovered row read as
                            // on (seen live, 2026-08-12). Hover moves the text, never the fill.
                            'hover:bg-transparent hover:text-foreground',
                            'data-[state=on]:hover:bg-primary/15'
                        )}
                    >
                        {/* State carried by a glyph as well as the tint: colour alone cannot
                            distinguish it from hover, and a tint is not a state for anyone
                            reading without colour. */}
                        {on ? (
                            <CheckIcon aria-hidden />
                        ) : (
                            <AddIcon className="opacity-50" aria-hidden />
                        )}
                        {use.label}
                    </ToggleGroupItem>
                )
            })}
        </ToggleGroup>
    )
}
const DEFAULT_FLOW_TIER: FlowTier = 'high'

function tierToBonus(tier: FlowTier): number {
    if (tier === 'critical') return 80
    if (tier === 'high') return 40
    if (tier === 'normal') return 20
    return 0
}

function bonusToTier(bonus: number): FlowTier {
    if (bonus >= 60) return 'critical'
    if (bonus >= 30) return 'high'
    if (bonus > 0) return 'normal'
    return 'none'
}

function FlowBoostSlider({
    value,
    roadName,
    onChange,
    disabled,
}: {
    readonly value: FlowTier
    readonly roadName: string
    readonly onChange: (tier: FlowTier) => void
    readonly disabled?: boolean
}) {
    const bonus = tierToBonus(value)
    const [localVal, setLocalVal] = useState<number>(bonus)

    const isPositive = localVal > 0

    return (
        <div className="flex items-center gap-2 bg-muted/40 border border-border/80 px-2 py-0.5 rounded-md">
            <span className="text-2xs text-muted-foreground font-mono">
                Flow:
            </span>
            <div className="w-20 sm:w-28">
                <Slider
                    value={[localVal]}
                    min={0}
                    max={100}
                    step={10}
                    disabled={disabled}
                    aria-label={`Flow boost for ${roadName}`}
                    onValueChange={(vals) => {
                        const next = vals[0] ?? 0
                        setLocalVal(next)
                    }}
                    onValueCommit={(vals) => {
                        const next = vals[0] ?? 0
                        onChange(bonusToTier(next))
                    }}
                />
            </div>
            <span
                className={`text-xs font-mono font-semibold min-w-[32px] text-right ${
                    isPositive ? 'text-primary' : 'text-muted-foreground'
                }`}
            >
                {isPositive ? `+${localVal}` : '0'}
            </span>
        </div>
    )
}

/**
 * Per-market priority-roads manager, hosted in the Bulletin-rules hub. A road can
 * boost its incident corridors and/or compete for a slot when flowing — the operator
 * can change them inline. Each change commits immediately; there is no separate Save
 * step. The row's edit button renames the road in place, keeping both dials, so fixing
 * a misspelling does not mean deleting the row and rebuilding its configuration.
 */
export function PriorityRoadsManager({
    markets,
    station,
}: {
    readonly markets: readonly MarketOption[]
    readonly station: string
}) {
    const marketIds = useMemo(() => markets.map((m) => m.marketId), [markets])
    const { data: inventory, isLoading } = useAllPriorityRoads(marketIds)
    const save = useSetPriorityRoads()
    const isAdmin = useIsAdmin()
    const submitting = save.isPending
    const [composerOpen, setComposerOpen] = useState(false)
    // The road being RENAMED. Non-null puts the shared composer in edit mode against that
    // row's market; null means the composer adds a new road.
    const [editing, setEditing] = useState<RoadRow | null>(null)
    const composing = composerOpen || editing !== null
    const scope = editing
        ? editing.marketId
        : station || (markets[0]?.marketId ?? '')
    const [road, setRoad] = useState('')

    const [search, setSearch] = useState('')

    const trimmed = road.trim()
    const unchanged = editing !== null && trimmed === editing.entry.name
    const valid =
        trimmed.length > 0 &&
        trimmed.length <= PRIORITY_ROAD_MAX_LENGTH &&
        !unchanged &&
        scope !== ''

    const roadsFor = (marketId: string): readonly PriorityRoadEntry[] =>
        inventory?.find((m) => m.marketId === marketId)?.roads ?? []

    const labelFor = (marketId: string) =>
        markets.find((m) => m.marketId === marketId)?.label ?? marketId

    const active = search.trim() !== ''

    const groups = useMemo<ScopeGroup<RoadRow>[]>(() => {
        const q = search.trim().toLowerCase()
        return (inventory ?? [])
            .filter((m) => m.marketId === station)
            .map((m) => ({
                key: m.marketId,
                label: labelFor(m.marketId),
                items: m.roads
                    .filter((r) => !q || r.name.toLowerCase().includes(q))
                    .map((r) => ({ marketId: m.marketId, entry: r })),
            }))
            .filter((g) => g.items.length > 0)
            .sort((a, b) => a.label.localeCompare(b.label))
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [inventory, markets, search, station])

    const handleAdd = async () => {
        if (!valid || submitting) return
        const current = roadsFor(scope)
        if (
            current.some((r) => r.name.toLowerCase() === trimmed.toLowerCase())
        ) {
            toast.error('That road is already listed for this market')
            return
        }
        if (current.length >= PRIORITY_ROADS_MAX) {
            toast.error(`Maximum ${PRIORITY_ROADS_MAX} roads per market`)
            return
        }
        try {
            await save.mutateAsync({
                marketId: scope,
                roads: [
                    ...current,
                    {
                        name: trimmed,
                        boostIncidents: true,
                        flowTier: 'high',
                        enabled: true,
                    },
                ],
            })
            setRoad('')
            setComposerOpen(false)
            toast.success('Priority road added')
        } catch (err: unknown) {
            toast.error('Failed to add priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const startEdit = (item: RoadRow) => {
        setEditing(item)
        setRoad(item.entry.name)
        setComposerOpen(false)
    }

    const closeComposer = () => {
        setEditing(null)
        setComposerOpen(false)
        setRoad('')
    }

    /**
     * Rename a road in place. The list position and both dials (incident boost, flow tier)
     * ride along, which is the whole point: a misspelled road name used to mean deleting the
     * row and rebuilding its configuration from scratch. Identity here is the NAME — there is
     * no id — so the collision check excludes the row being renamed.
     */
    const handleSaveEdit = async () => {
        if (!editing || !valid || submitting) return
        const current = roadsFor(editing.marketId)
        if (
            current.some(
                (r) =>
                    r.name !== editing.entry.name &&
                    r.name.toLowerCase() === trimmed.toLowerCase()
            )
        ) {
            toast.error('That road is already listed for this market')
            return
        }
        try {
            await save.mutateAsync({
                marketId: editing.marketId,
                roads: current.map((r) =>
                    r.name === editing.entry.name ? { ...r, name: trimmed } : r
                ),
            })
            closeComposer()
            toast.success('Priority road renamed')
        } catch (err: unknown) {
            toast.error('Failed to rename priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleSetEnabled = async (item: RoadRow, enabled: boolean) => {
        const current = roadsFor(item.marketId)
        const next = current.map((r) =>
            r.name === item.entry.name
                ? {
                      ...r,
                      enabled,
                      // When enabling with nothing configured, restore sensible defaults
                      boostIncidents:
                          enabled && !r.boostIncidents && r.flowTier === 'none'
                              ? true
                              : r.boostIncidents,
                      flowTier:
                          enabled && !r.boostIncidents && r.flowTier === 'none'
                              ? ('high' as FlowTier)
                              : r.flowTier,
                  }
                : r
        )
        try {
            await save.mutateAsync({ marketId: item.marketId, roads: next })
        } catch (err: unknown) {
            toast.error('Failed to update priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleDelete = async (item: RoadRow) => {
        const current = roadsFor(item.marketId)
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                roads: current.filter((r) => r.name !== item.entry.name),
            })
            if (editing?.entry.name === item.entry.name) closeComposer()
            toast.success('Priority road removed')
        } catch (err: unknown) {
            toast.error('Failed to remove priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    /**
     * Commit which jobs this road does. Switching Good runs off stores tier `none` (that IS
     * how the pipeline reads "not a good-run candidate"), so the previous strength is not
     * recoverable — switching it back on restores the same default a newly added road gets,
     * rather than silently reviving a level the operator can no longer see.
     */
    const handleSetUses = async (item: RoadRow, uses: readonly RoadUse[]) => {
        const current = roadsFor(item.marketId)
        const wantsGoodRuns = uses.includes('goodRuns')
        const next = current.map((r) =>
            r.name === item.entry.name
                ? {
                      ...r,
                      boostIncidents: uses.includes('incidents'),
                      flowTier: wantsGoodRuns
                          ? r.flowTier === 'none'
                              ? DEFAULT_FLOW_TIER
                              : r.flowTier
                          : ('none' as FlowTier),
                  }
                : r
        )
        try {
            await save.mutateAsync({ marketId: item.marketId, roads: next })
        } catch (err: unknown) {
            toast.error('Failed to update priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleSetTier = async (item: RoadRow, flowTier: FlowTier) => {
        const current = roadsFor(item.marketId)
        const next = current.map((r) =>
            r.name === item.entry.name ? { ...r, flowTier } : r
        )
        try {
            await save.mutateAsync({ marketId: item.marketId, roads: next })
        } catch (err: unknown) {
            toast.error('Failed to update priority road', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    return (
        <RuleTab>
            <RuleToolbar
                add={{
                    what: 'a priority road',
                    onClick: () => {
                        // Pressing Add mid-rename abandons it and starts a blank create.
                        const wasEditing = editing !== null
                        setEditing(null)
                        setRoad('')
                        setComposerOpen((v) => wasEditing || !v)
                    },
                    active: composing,
                    hint: (
                        <>
                            A road can do two jobs, and you pick which on its
                            row. <strong>Incidents</strong>: its incidents get
                            picked over equally bad ones elsewhere.{' '}
                            <strong>Good runs</strong>: it can earn a mention
                            for running well, at the strength you choose. Either
                            job competes on score, so neither is a guarantee.
                        </>
                    ),
                }}
                search={search}
                onSearchChange={setSearch}
                placeholder="Search roads…"
            />

            {composing && (
                <RuleComposer
                    title={editing ? 'Rename a priority road' : undefined}
                    onCancel={closeComposer}
                    submit={{
                        label: editing ? 'Save changes' : 'Add',
                        icon: editing ? EditIcon : AddIcon,
                        onClick: editing ? handleSaveEdit : handleAdd,
                        disabled: !valid || submitting,
                        ready: valid,
                        submitting,
                        tooltip: unchanged
                            ? 'Change the name to save.'
                            : undefined,
                    }}
                >
                    <ComposerField
                        label="Road name"
                        htmlFor="priority-road"
                        width="flex-1"
                    >
                        <Input
                            id="priority-road"
                            maxLength={PRIORITY_ROAD_MAX_LENGTH}
                            placeholder="for example, M1 Pacific Motorway"
                            value={road}
                            onChange={(e) => setRoad(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key !== 'Enter') return
                                if (editing) handleSaveEdit()
                                else handleAdd()
                            }}
                        />
                    </ComposerField>
                </RuleComposer>
            )}

            {Boolean(station) && isAdmin && (
                <ScopedActivityHistory
                    key={station}
                    resource="priority-roads"
                    entityId={station}
                    label={`History for ${labelFor(station)}`}
                />
            )}

            <ScopeGroupedList<RoadRow>
                groups={groups}
                isLoading={isLoading}
                empty={
                    <EmptyState
                        action={
                            active && (
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={() => {
                                        setSearch('')
                                    }}
                                >
                                    <ResetIcon />
                                    Clear filters
                                </Button>
                            )
                        }
                    >
                        {active
                            ? 'No roads match these filters.'
                            : 'No priority roads yet. Add one above, then choose whether it counts for incidents, good runs, or both.'}
                    </EmptyState>
                }
                rowKey={(item) => `${item.marketId}:${item.entry.name}`}
                renderRow={(item) => {
                    const goodRuns = item.entry.flowTier !== 'none'
                    const uses: RoadUse[] = [
                        ...(item.entry.boostIncidents
                            ? (['incidents'] as const)
                            : []),
                        ...(goodRuns ? (['goodRuns'] as const) : []),
                    ]
                    // Switched on but asked to do nothing — reachable, and previously only
                    // legible by reading a missing tint plus a select value.
                    const rowInert = item.entry.enabled && uses.length === 0
                    return (
                        <RuleRow
                            enabled={item.entry.enabled}
                            onToggle={() =>
                                handleSetEnabled(item, !item.entry.enabled)
                            }
                            label={item.entry.name}
                            actions={
                                <span
                                    className={cn(
                                        'flex shrink-0 items-center gap-1.5',
                                        rowInert && 'saturate-50'
                                    )}
                                >
                                    <RoadUseToggle
                                        uses={uses}
                                        roadName={item.entry.name}
                                        onChange={(next) =>
                                            handleSetUses(item, next)
                                        }
                                        disabled={submitting}
                                    />
                                    {/* Only meaningful for the job it grades, so it exists
                                        only while that job is on. */}
                                    {goodRuns && (
                                        <FlowBoostSlider
                                            key={`${item.marketId}:${item.entry.name}:${item.entry.flowTier}`}
                                            value={item.entry.flowTier}
                                            roadName={item.entry.name}
                                            onChange={(tier) =>
                                                handleSetTier(item, tier)
                                            }
                                            disabled={submitting}
                                        />
                                    )}
                                    {rowInert && (
                                        <Badge
                                            variant="outline"
                                            className="shrink-0 text-muted-foreground"
                                        >
                                            Not in use
                                        </Badge>
                                    )}
                                </span>
                            }
                            onEdit={() => startEdit(item)}
                            editLabel={`Rename ${item.entry.name}`}
                            onDelete={() => handleDelete(item)}
                            deleteLabel={`Delete ${item.entry.name}`}
                            itemLabel="priority road"
                            disabled={submitting}
                        />
                    )
                }}
            />
        </RuleTab>
    )
}
