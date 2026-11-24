import { BookText, Radio } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import { formatRelativeTime } from '@/lib/formatting'
import type { Dictionary } from '../types'
import { formatRuleCountSummary } from '../lib/rule-count'

interface PronunciationDictionaryCardProps {
    readonly dict: Dictionary
    readonly assignedStations: readonly string[]
    /** Total stations, used to describe a platform-wide dictionary's reach. */
    readonly stationCount: number
    readonly onView: (id: string) => void
    readonly onEdit: (id: string) => void
    readonly onDuplicate: (dict: Dictionary) => void
    readonly onDelete: (dict: Dictionary) => void
    readonly onArchive: (dict: Dictionary) => void
    readonly onUnarchive: (dict: Dictionary) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * A pronunciation dictionary in the gallery — mirrors DeliveryStyleCard.
 * Shows rule count, word previews, and station assignment.
 */
export function PronunciationDictionaryCard({
    dict,
    assignedStations,
    stationCount,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: PronunciationDictionaryCardProps) {
    const isArchived = dict.status === 'archived'
    const ruleCount = dict.rules.length
    const aliasCount = dict.rules.filter((r) => r.type === 'alias').length
    const phonemeCount = dict.rules.filter((r) => r.type === 'phoneme').length
    // Show up to 3 word previews
    const previewWords = dict.rules.slice(0, 3).map((r) => r.stringToReplace)
    const extraWords = ruleCount - previewWords.length

    // A platform-wide dictionary is referenced by no schedule, so the assignment list is empty by
    // design. Showing "Unassigned" would be the opposite of the truth.
    const isPlatformWide = dict.scope === 'Global'
    const stationLabel = isPlatformWide
        ? `Every station${stationCount > 0 ? ` (${stationCount})` : ''}`
        : assignedStations.length === 0
          ? 'Unassigned'
          : `${assignedStations.length} station${assignedStations.length === 1 ? '' : 's'}`

    // Pointer-only enhancement: the whole card opens View details. Keyboard
    // users reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => onView(dict.id)

    return (
        <div onClick={handleCardClick} className="h-full cursor-pointer">
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    {/* Identity row */}
                    <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-primary/10 text-primary-on-tint">
                                <BookText className="size-5" aria-hidden />
                            </div>
                            <div className="min-w-0">
                                <h2 className="flex items-center gap-2 truncate text-base font-semibold leading-tight">
                                    <span className="truncate">
                                        {dict.name}
                                    </span>
                                    {/* No Platform-wide badge here: the gallery
                                        groups platform-wide dictionaries under
                                        a "Platform-wide" section header, so the
                                        badge would be a third repetition. */}
                                    {isArchived && (
                                        <span className="shrink-0 rounded-full bg-muted px-2 py-px text-3xs font-medium text-muted-foreground">
                                            Archived
                                        </span>
                                    )}
                                </h2>
                                <p className="truncate text-xs text-muted-foreground">
                                    v{dict.currentVersion} · {ruleCount} rule
                                    {ruleCount === 1 ? '' : 's'}
                                    {formatRuleCountSummary(
                                        aliasCount,
                                        phonemeCount
                                    )}
                                </p>
                            </div>
                        </div>
                        <EntityActionsMenu
                            onView={() => onView(dict.id)}
                            onEdit={() => onEdit(dict.id)}
                            onDuplicate={() => onDuplicate(dict)}
                            onDelete={
                                canDelete ? () => onDelete(dict) : undefined
                            }
                            onArchive={
                                isArchived ? undefined : () => onArchive(dict)
                            }
                            onUnarchive={
                                isArchived ? () => onUnarchive(dict) : undefined
                            }
                            ariaLabel="Dictionary actions"
                            className="shrink-0"
                        />
                    </div>

                    {/* Rule preview chips */}
                    {previewWords.length > 0 && (
                        <div className="flex flex-wrap items-center gap-1.5">
                            {previewWords.map((word, index) => (
                                <span
                                    key={`${word}-${index}`}
                                    className="rounded-full bg-muted px-2 py-px text-2xs font-medium text-muted-foreground"
                                >
                                    {word}
                                </span>
                            ))}
                            {extraWords > 0 && (
                                <span className="text-2xs text-muted-foreground">
                                    +{extraWords} more
                                </span>
                            )}
                        </div>
                    )}

                    {ruleCount === 0 && (
                        <EmptyState variant="row">
                            No pronunciation rules yet.
                        </EmptyState>
                    )}

                    {/* Footer */}
                    <div className="flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs text-muted-foreground">
                        <span className="inline-flex items-center gap-1">
                            <Radio className="size-3" />
                            {stationLabel}
                        </span>
                        <span>
                            Updated {formatRelativeTime(dict.updatedAt)}
                        </span>
                    </div>
                </div>
            </SpotlightCard>
        </div>
    )
}
