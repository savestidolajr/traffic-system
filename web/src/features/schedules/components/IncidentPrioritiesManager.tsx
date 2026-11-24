import { useMemo, useState } from 'react'
import { EmptyState } from '@/components/EmptyState'
import { Button } from '@/components/ui/button'
import { Slider } from '@/components/ui/slider'
import { RuleRow } from '@/components/RuleRow'
import { RuleTab } from '@/components/RuleTab'
import { RuleToolbar } from '@/components/RuleToolbar'
import {
    ScopeGroupedList,
    type ScopeGroup,
} from '@/components/ScopeGroupedList'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { useIsAdmin } from '@/hooks/use-current-user'
import { ICON } from '@/lib/design/icons'
import { toast } from 'sonner'
import {
    useAllIncidentPriorities,
    useSetIncidentPriorities,
    type IncidentPriorityRule,
} from '../api/incident-priorities-hooks'
import {
    CANONICAL_INCIDENT_CLASSES,
    type CanonicalIncidentItem,
} from '../data/incident-landscape-data'

const ResetIcon = ICON.reset

interface MarketOption {
    readonly marketId: string
    readonly label: string
}

interface IncidentRuleRow {
    readonly marketId: string
    readonly entry: IncidentPriorityRule
    readonly meta: CanonicalIncidentItem
}
function DraggableBoostSlider({
    value,
    incidentName,
    onCommit,
    disabled,
}: {
    readonly value: number
    readonly incidentName: string
    readonly onCommit: (val: number) => void
    readonly disabled?: boolean
}) {
    const [localVal, setLocalVal] = useState<number>(value)

    const isPositive = localVal > 0
    const isNegative = localVal < 0
    const displayStr = isPositive ? `+${localVal}` : `${localVal}`

    return (
        <div className="flex items-center gap-2.5 bg-muted/40 border border-border/80 px-2.5 py-1 rounded-md">
            <span className="text-2xs text-muted-foreground font-mono">
                Boost:
            </span>
            <div className="w-24 sm:w-36">
                <Slider
                    value={[localVal]}
                    min={-100}
                    max={100}
                    step={5}
                    disabled={disabled}
                    aria-label={`Score boost for ${incidentName}`}
                    onValueChange={(vals) => {
                        const next = vals[0]
                        if (typeof next === 'number') setLocalVal(next)
                    }}
                    onValueCommit={(vals) => {
                        const next = vals[0]
                        if (typeof next === 'number') onCommit(next)
                    }}
                />
            </div>
            <span
                className={`text-xs font-mono font-semibold min-w-[42px] text-right ${
                    isPositive
                        ? 'text-primary'
                        : isNegative
                          ? 'text-destructive'
                          : 'text-muted-foreground'
                }`}
            >
                {displayStr}
            </span>
        </div>
    )
}

/**
 * Clean Row-Based Incident Priorities Manager.
 * Single unified list with draggable shadcn slider (-100 to +100), firing only on release (onValueCommit).
 * Dynamic accumulation from live provider data per station starting now.
 */
export function IncidentPrioritiesManager({
    markets,
    station,
}: {
    readonly markets: readonly MarketOption[]
    readonly station: string
}) {
    const marketIds = useMemo(() => markets.map((m) => m.marketId), [markets])
    const { data: inventory, isLoading: prioritiesLoading } =
        useAllIncidentPriorities(marketIds)
    const save = useSetIncidentPriorities()
    const isAdmin = useIsAdmin()
    const submitting = save.isPending

    const [search, setSearch] = useState('')

    const effectiveMarkets = useMemo<readonly MarketOption[]>(() => {
        if (markets.length > 0) return markets
        return [
            { marketId: 'sydney', label: 'Sydney' },
            { marketId: 'melbourne', label: 'Melbourne' },
            { marketId: 'brisbane', label: 'Brisbane' },
            { marketId: 'adelaide', label: 'Adelaide' },
            { marketId: 'perth', label: 'Perth' },
            { marketId: 'goldcoast', label: 'Gold Coast' },
        ]
    }, [markets])

    const labelFor = (marketId: string) =>
        effectiveMarkets.find((m) => m.marketId === marketId)?.label ?? marketId

    const rulesFor = (marketId: string): readonly IncidentPriorityRule[] => {
        const stored = inventory?.find((m) => m.marketId === marketId)?.rules
        if (stored && stored.length > 0) return stored
        return CANONICAL_INCIDENT_CLASSES.map((c) => ({
            incidentClass: c.id,
            displayName: c.name,
            scoreBonus: c.defaultBonus,
            enabled: true,
        }))
    }

    const active = search.trim() !== ''

    const groups = useMemo<ScopeGroup<IncidentRuleRow>[]>(() => {
        const q = search.trim().toLowerCase()
        return effectiveMarkets
            .filter((m) => m.marketId === station)
            .map((m) => {
                const effectiveRules = rulesFor(m.marketId)
                return {
                    key: m.marketId,
                    label: labelFor(m.marketId),
                    items: CANONICAL_INCIDENT_CLASSES.filter(
                        (c) =>
                            !q ||
                            c.name.toLowerCase().includes(q) ||
                            c.id.toLowerCase().includes(q) ||
                            c.description.toLowerCase().includes(q)
                    ).map((meta) => {
                        const rule = effectiveRules.find(
                            (r) => r.incidentClass === meta.id
                        ) ?? {
                            incidentClass: meta.id,
                            displayName: meta.name,
                            scoreBonus: meta.defaultBonus,
                            enabled: true,
                        }
                        return {
                            marketId: m.marketId,
                            entry: rule,
                            meta,
                        }
                    }),
                }
            })
            .filter((g) => g.items.length > 0)
            .sort((a, b) => a.label.localeCompare(b.label))
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [effectiveMarkets, inventory, search, station])

    const handleToggleEnabled = async (item: IncidentRuleRow) => {
        const current = rulesFor(item.marketId)
        const next = current.map((r) =>
            r.incidentClass === item.entry.incidentClass
                ? { ...r, enabled: !r.enabled }
                : r
        )
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                rules: next,
            })
        } catch (err: unknown) {
            toast.error('Failed to toggle incident priority', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleSetBonus = async (item: IncidentRuleRow, newBonus: number) => {
        const current = rulesFor(item.marketId)
        const next = current.map((r) =>
            r.incidentClass === item.entry.incidentClass
                ? { ...r, scoreBonus: newBonus }
                : r
        )
        try {
            await save.mutateAsync({ marketId: item.marketId, rules: next })
            toast.success(`Score boost updated for ${item.entry.displayName}`)
        } catch (err: unknown) {
            toast.error('Failed to update boost', {
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
                search={search}
                onSearchChange={setSearch}
                placeholder="Search incident classes or wording…"
            />

            {Boolean(station) && isAdmin && (
                <ScopedActivityHistory
                    key={station}
                    resource="incident-priorities"
                    entityId={station}
                    label={`History for ${labelFor(station)}`}
                />
            )}

            <ScopeGroupedList<IncidentRuleRow>
                groups={groups}
                isLoading={prioritiesLoading && !inventory}
                empty={
                    <EmptyState
                        action={
                            active && (
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={() => setSearch('')}
                                >
                                    <ResetIcon />
                                    Clear filters
                                </Button>
                            )
                        }
                    >
                        {active
                            ? 'No incident classes match these filters.'
                            : 'No incident priorities found.'}
                    </EmptyState>
                }
                rowKey={(item) =>
                    `${item.marketId}:${item.entry.incidentClass}`
                }
                renderRow={(item) => (
                    <RuleRow
                        enabled={item.entry.enabled}
                        onToggle={() => handleToggleEnabled(item)}
                        label={item.entry.displayName}
                        secondary={item.meta.description}
                        actions={
                            <DraggableBoostSlider
                                key={`${item.marketId}:${item.entry.incidentClass}:${item.entry.scoreBonus}`}
                                value={item.entry.scoreBonus}
                                incidentName={item.entry.displayName}
                                onCommit={(val) => handleSetBonus(item, val)}
                                disabled={submitting || !item.entry.enabled}
                            />
                        }
                        disabled={submitting}
                    />
                )}
            />
        </RuleTab>
    )
}
