import { Drama, Radio } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { EmptyState } from '@/components/EmptyState'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { formatRelativeTime } from '@/lib/formatting'
import { cueLabel, sanitizeTags } from '../cues'
import { strengthLabel, toStrength } from '../strength'
import type { DeliveryStyle } from '../types'

/** Permitted cues shown before the "+N more" fold. */
const CUE_PREVIEW_LIMIT = 5

interface DeliveryStyleCardProps {
    readonly style: DeliveryStyle
    readonly assignedStations: readonly string[]
    readonly onView: (styleId: string) => void
    readonly onEdit: (styleId: string) => void
    readonly onDuplicate: (style: DeliveryStyle) => void
    readonly onDelete: (style: DeliveryStyle) => void
    readonly onArchive: (style: DeliveryStyle) => void
    readonly onUnarchive: (style: DeliveryStyle) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * A delivery style in the gallery — mirrors VoiceVariantCard. The plain-language
 * on-air direction is the focal point; the permitted cues and assignment sit
 * below. No inline audition (a fresh synth costs money); the card opens the
 * Studio where the operator hears it.
 */
export function DeliveryStyleCard({
    style,
    assignedStations,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: DeliveryStyleCardProps) {
    const policy = style.versions.at(-1)?.policy
    const cues = policy ? sanitizeTags(policy.allowedTags) : []
    const strength = strengthLabel(toStrength(policy?.intensity ?? 2))
    const tone = policy?.toneDescription?.trim()
    const isArchived = style.status === 'archived'

    const stationLabel =
        assignedStations.length === 0
            ? 'Unassigned'
            : `${assignedStations.length} station${assignedStations.length === 1 ? '' : 's'}`

    const shownCues = cues.slice(0, CUE_PREVIEW_LIMIT)
    const extraCues = cues.length - shownCues.length

    // Pointer-only enhancement: the whole card opens View details. Keyboard
    // users reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => onView(style.id)

    return (
        <div onClick={handleCardClick} className="h-full cursor-pointer">
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    {/* Identity row */}
                    <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-primary/10 text-primary-on-tint">
                                <Drama className="size-5" aria-hidden />
                            </div>
                            <div className="min-w-0">
                                <h2 className="flex items-center gap-2 truncate text-base font-semibold leading-tight">
                                    <span className="truncate">
                                        {style.name}
                                    </span>
                                    {isArchived && (
                                        <Badge variant="muted">Archived</Badge>
                                    )}
                                </h2>
                                <p className="truncate text-xs text-muted-foreground">
                                    v{style.currentVersion} · {strength}
                                </p>
                            </div>
                        </div>
                        <EntityActionsMenu
                            onView={() => onView(style.id)}
                            onEdit={() => onEdit(style.id)}
                            onDuplicate={() => onDuplicate(style)}
                            onDelete={
                                canDelete ? () => onDelete(style) : undefined
                            }
                            onArchive={
                                isArchived ? undefined : () => onArchive(style)
                            }
                            onUnarchive={
                                isArchived
                                    ? () => onUnarchive(style)
                                    : undefined
                            }
                            ariaLabel="Style actions"
                            className="shrink-0"
                        />
                    </div>

                    {/* On-air direction — the focal point */}
                    {tone ? (
                        <button
                            type="button"
                            onClick={(e) => {
                                e.stopPropagation()
                                onEdit(style.id)
                            }}
                            onKeyDown={(e: React.KeyboardEvent) =>
                                e.stopPropagation()
                            }
                            className="rounded-lg border border-border/60 bg-background/40 px-4 py-3 text-left text-sm leading-snug text-foreground transition-colors hover:bg-muted/40"
                        >
                            <span className="line-clamp-2">{tone}</span>
                        </button>
                    ) : (
                        <EmptyState variant="row" className="bg-muted/20">
                            No delivery direction set yet.
                        </EmptyState>
                    )}

                    {/* Permitted cues */}
                    {shownCues.length > 0 && (
                        <div className="flex flex-wrap items-center gap-1.5">
                            {shownCues.map((tag) => (
                                <span
                                    key={tag}
                                    className="rounded-full bg-muted px-2 py-px text-2xs font-medium text-muted-foreground"
                                >
                                    {cueLabel(tag)}
                                </span>
                            ))}
                            {extraCues > 0 && (
                                <span className="text-2xs text-muted-foreground">
                                    +{extraCues} more
                                </span>
                            )}
                        </div>
                    )}

                    {/* Footer */}
                    <div className="flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs text-muted-foreground">
                        <span className="inline-flex items-center gap-1">
                            <Radio className="size-3" />
                            {stationLabel}
                        </span>
                        <span>
                            Updated {formatRelativeTime(style.updatedAt)}
                        </span>
                    </div>
                </div>
            </SpotlightCard>
        </div>
    )
}
