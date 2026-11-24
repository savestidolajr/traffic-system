import { Radio } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { formatRelativeTime } from '@/lib/formatting'
import { cn } from '@/lib/utils'
import { BOOKEND_COPY, BOOKEND_ICON } from '../lib/copy'
import { BUILT_IN_TOKENS, extractTokensAcross } from '../lib/tokens'
import type { Bookend } from '../types'

/** Tokens shown before the "+N more" fold. */
const TOKEN_PREVIEW_LIMIT = 5

const BUILT_IN_SET = new Set(
    BUILT_IN_TOKENS.map((token) => token.toLowerCase())
)

/** Renders an empty side of a pair as an em dash, labelled. */
function BookendSide({
    label,
    text,
}: {
    readonly label: string
    readonly text: string
}) {
    return (
        <div className="rounded-lg border border-border/60 bg-background/40 px-4 py-3">
            <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                {label}
            </p>
            {text.trim() ? (
                <p className="mt-1 text-sm leading-snug text-foreground">
                    <span className="line-clamp-3">{text}</span>
                </p>
            ) : (
                <p className="mt-1 text-sm leading-snug text-muted-foreground-subtle">
                    -
                </p>
            )}
        </div>
    )
}

interface BookendCardProps {
    readonly bookend: Bookend
    readonly assignedStations: readonly string[]
    readonly onView: (bookendId: string) => void
    readonly onEdit: (bookendId: string) => void
    readonly onDuplicate: (bookend: Bookend) => void
    readonly onDelete: (bookend: Bookend) => void
    readonly onArchive: (bookend: Bookend) => void
    readonly onUnarchive: (bookend: Bookend) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * A bookend pair in the gallery — mirrors DeliveryStyleCard. The intro and
 * outro lines are the focal point (an em dash marks a side the pair leaves
 * empty); the token chips (built-in vs custom) and assignment sit below. The
 * card opens the Studio where the author edits and previews both lines.
 */
export function BookendCard({
    bookend,
    assignedStations,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: BookendCardProps) {
    const Icon = BOOKEND_ICON
    const copy = BOOKEND_COPY
    // The API always computes tokens across both lines, but fall back to live
    // extraction for stale cached payloads.
    const tokens =
        bookend.tokens.length > 0
            ? bookend.tokens
            : extractTokensAcross([bookend.introText, bookend.outroText])
    const isArchived = bookend.status === 'archived'

    const stationLabel =
        assignedStations.length === 0
            ? 'Unassigned'
            : `${assignedStations.length} station${assignedStations.length === 1 ? '' : 's'}`

    const shownTokens = tokens.slice(0, TOKEN_PREVIEW_LIMIT)
    const extraTokens = tokens.length - shownTokens.length

    // Pointer-only enhancement: the whole card opens View details. Keyboard
    // users reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => onView(bookend.id)

    return (
        <div onClick={handleCardClick} className="h-full cursor-pointer">
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    {/* Identity row */}
                    <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-primary/10 text-primary-on-tint">
                                <Icon className="size-5" aria-hidden />
                            </div>
                            <div className="min-w-0">
                                <h2 className="flex items-center gap-2 truncate text-base font-semibold leading-tight">
                                    <span className="truncate">
                                        {bookend.name}
                                    </span>
                                    {isArchived && (
                                        <Badge variant="muted">Archived</Badge>
                                    )}
                                </h2>
                                <p className="truncate text-xs text-muted-foreground">
                                    v{bookend.currentVersion} ·{' '}
                                    {tokens.length === 0
                                        ? 'no tokens'
                                        : `${tokens.length} token${tokens.length === 1 ? '' : 's'}`}
                                </p>
                            </div>
                        </div>
                        <EntityActionsMenu
                            onView={() => onView(bookend.id)}
                            onEdit={() => onEdit(bookend.id)}
                            onDuplicate={() => onDuplicate(bookend)}
                            onDelete={
                                canDelete ? () => onDelete(bookend) : undefined
                            }
                            onArchive={
                                isArchived
                                    ? undefined
                                    : () => onArchive(bookend)
                            }
                            onUnarchive={
                                isArchived
                                    ? () => onUnarchive(bookend)
                                    : undefined
                            }
                            ariaLabel={`${copy.title} actions`}
                            className="shrink-0"
                        />
                    </div>

                    {/* The pair — both lines, labelled */}
                    <div className="grid gap-2 sm:grid-cols-2">
                        <BookendSide label="Intro" text={bookend.introText} />
                        <BookendSide label="Outro" text={bookend.outroText} />
                    </div>

                    {/* Tokens — custom ones need a per-station value */}
                    {shownTokens.length > 0 && (
                        <div className="flex flex-wrap items-center gap-1.5">
                            {shownTokens.map((token) => {
                                const isBuiltIn = BUILT_IN_SET.has(
                                    token.toLowerCase()
                                )
                                return (
                                    <span
                                        key={token}
                                        title={
                                            isBuiltIn
                                                ? 'Filled automatically from the schedule'
                                                : 'Needs a per-station value in the schedule studio'
                                        }
                                        className={cn(
                                            'rounded-full border px-2 py-px font-mono text-2xs font-medium',
                                            isBuiltIn
                                                ? 'border-border bg-muted text-muted-foreground'
                                                : 'border-warning/40 bg-warning/10 text-warning'
                                        )}
                                    >
                                        {'{'}
                                        {token}
                                        {'}'}
                                    </span>
                                )
                            })}
                            {extraTokens > 0 && (
                                <span className="text-2xs text-muted-foreground">
                                    +{extraTokens} more
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
                            Updated {formatRelativeTime(bookend.updatedAt)}
                        </span>
                    </div>
                </div>
            </SpotlightCard>
        </div>
    )
}
