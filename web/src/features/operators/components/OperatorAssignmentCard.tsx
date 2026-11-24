import { useState } from 'react'
import { Link } from 'react-router'
import { Info } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Badge } from '@/components/ui/badge'
import {
    formatDateTime,
    formatMarketName,
    formatRelativeTime,
} from '@/lib/formatting'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { ROUTE_STATIONS } from '@/lib/routes'
import { cn } from '@/lib/utils'
import type { OperatorDirectory, OperatorDirectoryEntry } from '../api/schemas'
import {
    MISCONFIGURED_NOTICE,
    RETAINED_ROW_NOTICE,
    ROLE_UNKNOWN_NOTICE,
    SIGN_IN_ROLE_NOTICE,
    formatSource,
} from '../lib/access-copy'

interface OperatorAssignmentCardProps {
    readonly entry: OperatorDirectoryEntry
    /** Directory read state used to distinguish unknown roles from defects. */
    readonly directoryStatus: OperatorDirectory['directoryStatus']
    /** marketId (lowercased) → friendly label, e.g. "2GB · Sydney Breakfast". */
    readonly marketLabels?: ReadonlyMap<string, string>
    readonly onEdit: (entry: OperatorDirectoryEntry) => void
}

export function OperatorAssignmentCard({
    entry,
    directoryStatus,
    marketLabels,
    onEdit,
}: OperatorAssignmentCardProps) {
    const displayName =
        entry.displayName || entry.userPrincipalName || 'Unknown operator'
    const initial = displayName.charAt(0).toUpperCase()
    const [expanded, setExpanded] = useState(false)
    const MARKET_CHIP_LIMIT = 6
    const overflow = entry.markets.length - MARKET_CHIP_LIMIT
    const visibleMarkets = expanded
        ? entry.markets
        : entry.markets.slice(0, MARKET_CHIP_LIMIT)
    const isEditable = !entry.isAdmin
    const isDirectorySource =
        entry.source === 'Direct' || entry.source.startsWith('Group:')
    const hasDirectoryRole = isDirectorySource && entry.roles.length > 0
    const isDirectoryMisconfigured =
        isDirectorySource && entry.roles.length === 0
    const isStoredAssignment = entry.source === 'Assignment'
    // Roles on a stored row are what the person's own token presented at their last sign-in —
    // worth showing, but never dressed up as a directory read.
    const hasSignInRole = isStoredAssignment && entry.roles.length > 0

    // Pointer-only enhancement: the whole card opens Edit. Keyboard users
    // reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => {
        if (isEditable) onEdit(entry)
    }

    return (
        <div
            onClick={handleCardClick}
            className={cn('h-full', isEditable && 'cursor-pointer')}
        >
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border border-primary/30 bg-primary/10 text-primary-on-tint">
                                <span className="text-sm font-bold leading-none">
                                    {initial}
                                </span>
                            </div>
                            <div className="min-w-0">
                                <h2 className="truncate text-base font-semibold leading-tight">
                                    {displayName}
                                </h2>
                                <p className="truncate font-mono text-xs text-muted-foreground">
                                    {entry.userPrincipalName ??
                                        'UPN unavailable'}
                                </p>
                            </div>
                        </div>
                        {isEditable && (
                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="ghost"
                                        size="icon"
                                        className="size-8"
                                        aria-label={`Actions for ${displayName}`}
                                        onClick={(e) => e.stopPropagation()}
                                        onKeyDown={(e) => e.stopPropagation()}
                                    >
                                        <ICON.more />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent
                                    align="end"
                                    onClick={(e) => e.stopPropagation()}
                                >
                                    <DropdownMenuGroup>
                                        <DropdownMenuItem
                                            onClick={() => onEdit(entry)}
                                        >
                                            <ICON.edit />
                                            Edit station access
                                        </DropdownMenuItem>
                                    </DropdownMenuGroup>
                                </DropdownMenuContent>
                            </DropdownMenu>
                        )}
                    </div>

                    <div className="flex flex-wrap items-center gap-1.5">
                        {hasDirectoryRole || hasSignInRole ? (
                            entry.roles.map((role) => (
                                <Badge
                                    key={role}
                                    variant={
                                        role === 'Admin'
                                            ? 'default'
                                            : 'secondary'
                                    }
                                    title={
                                        hasSignInRole
                                            ? 'Recorded from this person’s own token at their last sign-in'
                                            : undefined
                                    }
                                >
                                    {role}
                                </Badge>
                            ))
                        ) : isDirectoryMisconfigured ? (
                            <Badge variant="warning">Misconfigured</Badge>
                        ) : isStoredAssignment ? (
                            <Badge
                                variant={
                                    directoryStatus === 'Ok'
                                        ? 'warning'
                                        : 'secondary'
                                }
                            >
                                Role unknown
                            </Badge>
                        ) : null}
                        <span className="text-xs text-muted-foreground">
                            From {formatSource(entry.source)}
                        </span>
                    </div>

                    {isDirectoryMisconfigured && (
                        <div
                            role="alert"
                            className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-xs text-warning"
                        >
                            <ICON.warning
                                className="mt-0.5 size-3.5 shrink-0"
                                aria-hidden
                            />
                            <span>{MISCONFIGURED_NOTICE}</span>
                        </div>
                    )}

                    {/* A row whose role came from the person's own sign-in needs no notice: the
                        directory being unreadable is the steady state, so a per-row sentence about
                        it would appear on every card forever. The badge carries the provenance and
                        "From Assignment" names the source. A row with NO role, or one missing from
                        a SUCCESSFUL directory read, is a genuine anomaly and still speaks up. */}
                    {isStoredAssignment &&
                        !(hasSignInRole && directoryStatus !== 'Ok') && (
                            <div
                                role={
                                    directoryStatus === 'Ok'
                                        ? 'alert'
                                        : 'status'
                                }
                                className={
                                    directoryStatus === 'Ok'
                                        ? 'flex items-start gap-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-xs text-warning'
                                        : 'flex items-start gap-2 rounded-md border border-border bg-muted p-3 text-xs text-muted-foreground'
                                }
                            >
                                {directoryStatus === 'Ok' ? (
                                    <ICON.warning
                                        className="mt-0.5 size-3.5 shrink-0"
                                        aria-hidden
                                    />
                                ) : (
                                    <Info
                                        className="mt-0.5 size-3.5 shrink-0"
                                        aria-hidden
                                    />
                                )}
                                <span>
                                    {directoryStatus === 'Ok'
                                        ? RETAINED_ROW_NOTICE
                                        : hasSignInRole
                                          ? SIGN_IN_ROLE_NOTICE
                                          : ROLE_UNKNOWN_NOTICE}
                                </span>
                            </div>
                        )}

                    {entry.isAdmin ? (
                        <div className="rounded-md border border-primary/30 bg-primary/10 px-3 py-2 text-sm font-medium text-primary-on-tint">
                            All stations (admin)
                        </div>
                    ) : entry.markets.length === 0 ? (
                        <div className="rounded-md border border-border bg-muted px-3 py-2 text-sm text-muted-foreground">
                            All stations (unscoped)
                        </div>
                    ) : (
                        <div
                            className="flex flex-wrap gap-1.5"
                            onClick={(e) => e.stopPropagation()}
                        >
                            {visibleMarkets.map((market) => {
                                const label =
                                    marketLabels?.get(market.toLowerCase()) ??
                                    formatMarketName(market)
                                return (
                                    <Link
                                        key={market}
                                        to={ROUTE_STATIONS}
                                        title={`View stations in ${label}`}
                                        className="max-w-60 truncate rounded-md border border-border/50 bg-background/40 px-2.5 py-1 text-xs text-muted-foreground transition-colors hover:border-border hover:bg-muted/50 hover:text-foreground"
                                    >
                                        {label}
                                    </Link>
                                )
                            })}
                            {overflow > 0 && (
                                <button
                                    type="button"
                                    onClick={() => setExpanded((v) => !v)}
                                    className="rounded-md border border-dashed border-border/60 px-2.5 py-1 text-xs text-muted-foreground transition-colors hover:border-border hover:text-foreground"
                                >
                                    {expanded
                                        ? 'Show less'
                                        : `+${overflow} more`}
                                </button>
                            )}
                        </div>
                    )}
                    <div onClick={(e) => e.stopPropagation()}>
                        <ScopedActivityHistory
                            resource="operators"
                            entityId={entry.objectId}
                        />
                    </div>

                    <div className="flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs text-muted-foreground">
                        <span>
                            {entry.isAdmin
                                ? 'Unrestricted'
                                : entry.markets.length === 0
                                  ? 'Unscoped'
                                  : `${entry.markets.length} station${entry.markets.length === 1 ? '' : 's'}`}
                        </span>
                        {entry.updatedAtUtc ? (
                            <span title={formatDateTime(entry.updatedAtUtc)}>
                                Updated {formatRelativeTime(entry.updatedAtUtc)}
                            </span>
                        ) : (
                            <span>No saved assignment</span>
                        )}
                    </div>
                </div>
            </SpotlightCard>
        </div>
    )
}
