import type { ReactNode } from 'react'
import { EmptyState } from '@/components/EmptyState'
import { RuleRow } from '@/components/RuleRow'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ScopeGroupedList } from '@/components/ScopeGroupedList'
import { RuleToolbar } from '@/components/RuleToolbar'
import {
    Select,
    SelectTrigger,
    SelectValue,
    SelectContent,
    SelectItem,
} from '@/components/ui/select'
import { ICON } from '@/lib/design/icons'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { useIsAdmin } from '@/hooks/use-current-user'
import { TYPE_BADGE, TYPE_LABEL } from '../lib/display'
import {
    hasActiveFilters,
    type GuidanceFilters,
    type GuidanceGroup,
} from '../lib/inventory'
import { GUIDANCE_TYPES } from '../api/schemas'
import type { Guidance, GuidanceTypeName } from '../api/schemas'

const ResetIcon = ICON.reset

interface GuidanceInventoryProps {
    readonly groups: readonly GuidanceGroup[]
    readonly isLoading: boolean
    readonly filters: GuidanceFilters
    /** Page-header station scope (a marketId; always concrete). */
    readonly station: string
    /** Human label for `station`, when one is selected. */
    readonly stationLabel?: string
    readonly onFilterChange: (patch: Partial<GuidanceFilters>) => void
    readonly onClear: () => void
    readonly onAdd: () => void
    readonly addActive: boolean
    readonly composer?: ReactNode
    readonly canEditScope: (group: GuidanceGroup) => boolean
    readonly onEdit: (item: Guidance) => void
    readonly onToggle: (item: Guidance) => void
    readonly onDelete: (item: Guidance) => void
}

interface PreambleGroup extends GuidanceGroup {
    /** House-style entries for this scope, rendered as a preamble block instead of a row — mirrors the backend, which composes HouseStyle into its own prompt slot rather than the bulleted <guidance> list. */
    readonly houseStyle: readonly Guidance[]
}

export function GuidanceInventory({
    groups,
    isLoading,
    filters,
    station,
    stationLabel,
    onFilterChange,
    onClear,
    onAdd,
    addActive,
    composer,
    canEditScope,
    onEdit,
    onToggle,
    onDelete,
}: GuidanceInventoryProps) {
    const active = hasActiveFilters(filters)
    const isAdmin = useIsAdmin()

    const rowGroups: PreambleGroup[] = groups.map((group) => ({
        ...group,
        items: group.items.filter((item) => item.type !== 'HouseStyle'),
        houseStyle: group.items.filter((item) => item.type === 'HouseStyle'),
    }))

    return (
        <div className="flex flex-col gap-4">
            <RuleToolbar
                add={{
                    what: 'a guidance note',
                    onClick: onAdd,
                    active: addActive,
                    hint: "Extra steering for the writer: tone, running order, naming, things to avoid. A market's bulletin uses the 8 most specific.",
                }}
                search={filters.search}
                onSearchChange={(search: string) => onFilterChange({ search })}
                placeholder="Search guidance…"
            >
                {/* The tab's ONLY filter. Scope left this toolbar entirely: the
                    page-header station picker owns it now, and it composes
                    Global + the chosen market rather than selecting one scope.
                    Export left too — Data transfer is the one door for config. */}
                <Select
                    value={filters.type}
                    onValueChange={(type) => onFilterChange({ type })}
                >
                    <SelectTrigger
                        aria-label="Filter by type"
                        className="w-full sm:w-40"
                    >
                        <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="all">All types</SelectItem>
                        {GUIDANCE_TYPES.map((t) => (
                            <SelectItem key={t} value={t}>
                                {TYPE_LABEL[t]}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            </RuleToolbar>

            {composer}

            {isAdmin && (
                <ScopedActivityHistory
                    resource="guidance"
                    entityId={station}
                    label={`History for ${stationLabel ?? station}`}
                />
            )}

            <ScopeGroupedList<Guidance, PreambleGroup>
                groups={rowGroups}
                isLoading={isLoading}
                empty={
                    <EmptyState
                        action={
                            active ? (
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={onClear}
                                >
                                    <ResetIcon />
                                    Clear filters
                                </Button>
                            ) : undefined
                        }
                    >
                        {active
                            ? 'No guidance matches these filters.'
                            : 'Add your first guidance note above.'}
                    </EmptyState>
                }
                rowKey={(item) => item.id}
                renderHeaderExtra={(group) =>
                    group.houseStyle.length === 0 ? null : (
                        <div className="border-b border-border/60 bg-muted/10 px-4 py-3">
                            <span className="mb-1.5 block text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                                House style preamble
                            </span>
                            <div className="flex flex-col gap-1.5">
                                {group.houseStyle.map((item) => {
                                    const canEdit = canEditScope(group)
                                    return (
                                        <div
                                            key={item.id}
                                            className="group flex items-center gap-3"
                                        >
                                            <RuleRow
                                                enabled={item.enabled}
                                                onToggle={() => onToggle(item)}
                                                label={item.text}
                                                expandable
                                                quote
                                                onEdit={
                                                    canEdit
                                                        ? () => onEdit(item)
                                                        : undefined
                                                }
                                                editLabel="Edit house style preamble"
                                                onDelete={() => onDelete(item)}
                                                deleteLabel="Delete house style preamble"
                                                readOnly={!canEdit}
                                                itemLabel="house style preamble"
                                            />
                                        </div>
                                    )
                                })}
                            </div>
                        </div>
                    )
                }
                renderRow={(item, group) => {
                    const type = item.type as GuidanceTypeName
                    const canEdit = canEditScope(group)
                    return (
                        <RuleRow
                            enabled={item.enabled}
                            onToggle={() => onToggle(item)}
                            label={item.text}
                            expandable
                            badge={
                                <Badge
                                    variant="outline"
                                    className={TYPE_BADGE[type]}
                                >
                                    {TYPE_LABEL[type] ?? item.type}
                                </Badge>
                            }
                            onEdit={canEdit ? () => onEdit(item) : undefined}
                            editLabel="Edit guidance"
                            onDelete={() => onDelete(item)}
                            deleteLabel="Delete guidance"
                            readOnly={!canEdit}
                            itemLabel="guidance"
                        />
                    )
                }}
            />
        </div>
    )
}
