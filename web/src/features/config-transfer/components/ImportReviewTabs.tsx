import { useMemo, useState } from 'react'
import { EmptyState } from '@/components/EmptyState'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import { cn } from '@/lib/utils'
import { ICON } from '@/lib/design/icons'
import type {
    ConfigImportItem,
    ConfigImportReport,
} from '../api/config-transfer-schemas'
import {
    SECTIONS,
    isSection,
    sectionIcon,
    sectionLabel,
    findEntityInBundle,
} from '../lib/section-meta'
import { diffEntities } from '@/lib/entity-diff'
import { actionBadgeVariant } from '../lib/action-badge'
import { EntityDiffView } from '@/components/EntityDiffView'

const ExpandIcon = ICON.expand

interface ImportReviewTabsProps {
    readonly report: ConfigImportReport
    readonly beforeBundle: unknown
    readonly afterBundle: unknown
}

/**
 * The "flick through tabs to see what changed" review surface. One tab per
 * touched section (empty sections are omitted — with 12 possible sections a
 * small edit would otherwise drown in empty tabs); each entity row expands
 * into a real field-level diff via `EntityDiffView`.
 */
export function ImportReviewTabs({
    report,
    beforeBundle,
    afterBundle,
}: ImportReviewTabsProps) {
    // The REPORT is the authority for what is reviewable, not this build's
    // section map. Rows used to be filtered through `isSection` and the tab
    // list built from `SECTIONS`, so anything the map did not name vanished
    // from the review while still counting toward the metric cards above —
    // an operator read "Errors 1" or "Update 1" with nothing to look at, and
    // under the prod two-person gate an approver signed off on a change they
    // could not see. Reachable two ways today: the plan's pseudo-sections
    // (`Unknown` for a typo'd top-level property, `SchemaVersion` for a
    // version mismatch), and the next section added to `ConfigSections.All`.
    const bySection = useMemo(() => {
        const groups = new Map<string, ConfigImportItem[]>()
        for (const item of report.items) {
            const list = groups.get(item.section) ?? []
            list.push(item)
            groups.set(item.section, list)
        }
        return groups
    }, [report.items])

    // Known sections first, in dependency order, so the familiar layout is
    // unchanged; anything unrecognised is appended rather than dropped.
    const touchedSections = useMemo(() => {
        const known = SECTIONS.filter((s) => bySection.has(s))
        const unknown = [...bySection.keys()].filter((s) => !isSection(s))
        return [...known, ...unknown]
    }, [bySection])

    if (touchedSections.length === 0) {
        return (
            <EmptyState>
                Nothing in this bundle differs from the current config.
            </EmptyState>
        )
    }

    return (
        <Tabs defaultValue={touchedSections[0]}>
            <TabsList>
                {touchedSections.map((section) => {
                    const items = bySection.get(section) ?? []
                    const Icon = sectionIcon(section)
                    const changedCount = items.filter(
                        (i) => i.action !== 'Unchanged'
                    ).length
                    const hasErrors = items.some((i) => i.action === 'Error')
                    return (
                        <TabsTrigger
                            key={section}
                            value={section}
                            className="gap-1.5"
                        >
                            <Icon className="size-3.5" aria-hidden />
                            {sectionLabel(section)}
                            {changedCount > 0 && (
                                <Badge
                                    variant={
                                        hasErrors ? 'destructive' : 'secondary'
                                    }
                                    className="ml-0.5 px-1.5 py-0 text-3xs"
                                >
                                    {changedCount}
                                </Badge>
                            )}
                        </TabsTrigger>
                    )
                })}
            </TabsList>
            {touchedSections.map((section) => (
                <TabsContent key={section} value={section}>
                    <SectionReview
                        section={section}
                        items={bySection.get(section) ?? []}
                        beforeBundle={beforeBundle}
                        afterBundle={afterBundle}
                    />
                </TabsContent>
            ))}
        </Tabs>
    )
}

function SectionReview({
    section,
    items,
    beforeBundle,
    afterBundle,
}: {
    readonly section: string
    readonly items: ConfigImportItem[]
    readonly beforeBundle: unknown
    readonly afterBundle: unknown
}) {
    const [expanded, setExpanded] = useState<Set<string>>(new Set())
    const unchanged = items.filter((i) => i.action === 'Unchanged')
    // Errors surface first — they block Apply and need the reviewer's eyes.
    const changed = items
        .filter((i) => i.action !== 'Unchanged')
        .sort(
            (a, b) =>
                Number(b.action === 'Error') - Number(a.action === 'Error')
        )

    /**
     * A report key is only unique per (section, id): id-less entries fall back
     * to the entity name (errors to "(unnamed)"), so two entries can share one
     * key in a section. Give each row a per-occurrence identity (same pattern
     * as `keyById` in lib/entity-diff.ts) so React never sees duplicate keys
     * and each row owns its own expansion state.
     */
    const rows = useMemo(() => {
        const counts = new Map<string, number>()
        return changed.map((item) => {
            const n = counts.get(item.key) ?? 0
            counts.set(item.key, n + 1)
            return { item, rowKey: n === 0 ? item.key : `${item.key}#${n + 1}` }
        })
    }, [changed])

    const toggle = (key: string) =>
        setExpanded((prev) => {
            const next = new Set(prev)
            if (next.has(key)) next.delete(key)
            else next.add(key)
            return next
        })

    return (
        <div className="flex flex-col gap-2">
            {rows.map(({ item, rowKey }) => (
                <EntityReviewRow
                    key={rowKey}
                    section={section}
                    item={item}
                    expanded={expanded.has(rowKey)}
                    onToggle={() => toggle(rowKey)}
                    beforeBundle={beforeBundle}
                    afterBundle={afterBundle}
                />
            ))}
            {unchanged.length > 0 && (
                <p className="px-2 py-1 text-xs text-muted-foreground">
                    {unchanged.length} unchanged:{' '}
                    {unchanged.map((i) => i.name ?? i.key).join(', ')}
                </p>
            )}
        </div>
    )
}

function EntityReviewRow({
    section,
    item,
    expanded,
    onToggle,
    beforeBundle,
    afterBundle,
}: {
    readonly section: string
    readonly item: ConfigImportItem
    readonly expanded: boolean
    readonly onToggle: () => void
    readonly beforeBundle: unknown
    readonly afterBundle: unknown
}) {
    const canExpand = item.action !== 'Error'
    const diffs = useMemo(() => {
        if (!canExpand) return []
        const before = findEntityInBundle(beforeBundle, section, item.key)
        const after = findEntityInBundle(afterBundle, section, item.key)
        return diffEntities(before, after)
    }, [canExpand, beforeBundle, afterBundle, section, item.key])

    return (
        <div className="rounded-lg border">
            <button
                type="button"
                onClick={canExpand ? onToggle : undefined}
                aria-expanded={canExpand ? expanded : undefined}
                disabled={!canExpand}
                className="flex w-full items-center gap-2 px-3 py-2 text-left disabled:cursor-default"
            >
                <Badge
                    variant={actionBadgeVariant(item.action)}
                    className="shrink-0"
                >
                    {item.action}
                </Badge>
                <span className="flex-1 truncate text-sm font-medium">
                    {item.name ?? item.key}
                </span>
                {canExpand && (
                    <ExpandIcon
                        className={cn(
                            'size-4 shrink-0 text-muted-foreground transition-transform',
                            expanded && 'rotate-180'
                        )}
                        aria-hidden
                    />
                )}
            </button>
            {item.action === 'Error' && item.detail && (
                <p className="border-t px-3 py-2 text-xs text-destructive">
                    {item.detail}
                </p>
            )}
            {canExpand && expanded && (
                <div className="border-t px-1 py-1">
                    <EntityDiffView entries={diffs} />
                </div>
            )}
        </div>
    )
}
