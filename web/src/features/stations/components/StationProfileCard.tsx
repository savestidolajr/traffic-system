import { Switch } from '@/components/ui/switch'
import { Button } from '@/components/ui/button'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ScheduleNextRun } from '@/features/schedules/components/ScheduleNextRun'
import { CronScheduleGlyph } from '@/features/schedules/components/CronScheduleGlyph'
import { VoiceModeGlyph } from '@/features/schedules/components/VoiceModeGlyph'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { useMapConfig } from '@/features/jobs/api/map-config-hooks'

import { formatDateTime, formatRelativeTime } from '@/lib/formatting'
import { Radio, Users } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'

import { CoverageMapPreview } from './CoverageMapPreview'
import { getStationAttentionIssues } from '../lib/station-attention'
import { useDeleteConfirm } from '../lib/use-delete-confirm'
import { useIsAdmin } from '@/hooks/use-current-user'
import type { MarketSchedule } from '@/features/schedules/types'

interface StationProfileCardProps {
    readonly schedule: MarketSchedule
    readonly onTrigger: (schedule: MarketSchedule) => void
    readonly onView: (schedule: MarketSchedule) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onDuplicate: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
    readonly onTogglePlayout: (schedule: MarketSchedule) => void
    readonly onDelete: (schedule: MarketSchedule) => void
    /** When true, render dimmed with disabled actions — station is outside the operator's market scope. */
    readonly outOfScope?: boolean
    /** Admin-only context: display names of operators assigned to this market. */
    readonly operatorNames?: readonly string[]
}

export function StationProfileCard({
    schedule,
    onTrigger,
    onView,
    onEdit,
    onDuplicate,
    onToggle,
    onTogglePlayout,
    onDelete,
    outOfScope = false,
    operatorNames,
}: StationProfileCardProps) {
    const { isOpen, requestDelete, onOpenChange, confirmDelete, pendingItem } =
        useDeleteConfirm(onDelete)
    // Duplicate and Delete are Admin-only server-side. View, Edit/toggle and Trigger now
    // are not: PUT /schedules/{marketId} and POST /broadcast both resolve a market and run
    // EnsureMarketAccessAsync, so an operator reaches exactly their assigned stations and
    // `outOfScope` withholds the rest.
    const isAdmin = useIsAdmin()
    const attentionIssues = getStationAttentionIssues(schedule)
    // Delivery needs BOTH gates, so the card cannot say what a publish will do from the
    // station flag alone: development has playout off environment-wide and would otherwise
    // have claimed a bulletin reaches playout on every station.
    const { data: runtimeConfig } = useMapConfig()
    const envPlayoutEnabled = runtimeConfig?.playoutEnabled ?? true
    const { data: voiceProfiles } = useVoiceProfiles()
    const voiceName =
        voiceProfiles?.profiles.find((p) => p.id === schedule.voiceProfileId)
            ?.name ?? 'Default'

    // Pointer-only enhancement: the whole card opens Edit. Keyboard users
    // reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => onEdit(schedule)

    return (
        <>
            <div onClick={handleCardClick} className="cursor-pointer">
                <SpotlightCard
                    className={cn(
                        'flex h-full flex-col shadow-inner-light',
                        // De-emphasis by desaturation and surface, NOT opacity.
                        // `opacity-70` on the card composited EVERY string
                        // inside it toward the page background — the Trigger
                        // button's label measured 3.10:1 — and `opacity-50`
                        // was worse. Both states already announce themselves
                        // with a badge and a disabled control; the colour only
                        // has to be quieter, not unreadable.
                        !schedule.enabled && 'saturate-50',
                        outOfScope && 'saturate-0'
                    )}
                >
                    <div className="flex h-full flex-col gap-4 p-5">
                        {/* Identity row */}
                        <div className="flex items-start justify-between gap-3">
                            <div className="flex min-w-0 items-center gap-3">
                                <div
                                    className={cn(
                                        'flex size-10 shrink-0 items-center justify-center rounded-lg border transition-colors',
                                        schedule.enabled
                                            ? 'border-primary/30 bg-primary/10 text-primary-on-tint'
                                            : 'border-border bg-muted text-muted-foreground'
                                    )}
                                >
                                    <Radio className="size-5" aria-hidden />
                                </div>
                                <div className="min-w-0">
                                    <div className="flex min-w-0 items-center gap-1.5">
                                        <span className="min-w-0 truncate text-base font-semibold leading-tight">
                                            {schedule.station ??
                                                schedule.marketId}
                                        </span>
                                        {/* Fixed slot — never affects title truncation */}
                                        <span
                                            className={cn(
                                                'inline-flex shrink-0 size-3.5',
                                                attentionIssues.length === 0 &&
                                                    'invisible'
                                            )}
                                            aria-hidden={
                                                attentionIssues.length === 0
                                            }
                                        >
                                            {attentionIssues.length > 0 && (
                                                <Tooltip>
                                                    <TooltipTrigger asChild>
                                                        <button
                                                            type="button"
                                                            onClick={(e) =>
                                                                e.stopPropagation()
                                                            }
                                                            className="flex cursor-help text-warning"
                                                            aria-label="Attention needed"
                                                        >
                                                            <ICON.warning className="size-3.5" />
                                                        </button>
                                                    </TooltipTrigger>
                                                    <TooltipContent>
                                                        <ul className="list-disc space-y-0.5 pl-4">
                                                            {attentionIssues.map(
                                                                (issue) => (
                                                                    <li
                                                                        key={
                                                                            issue.key
                                                                        }
                                                                    >
                                                                        {
                                                                            issue.label
                                                                        }
                                                                    </li>
                                                                )
                                                            )}
                                                        </ul>
                                                    </TooltipContent>
                                                </Tooltip>
                                            )}
                                        </span>
                                    </div>
                                    <p className="wrap-break-word text-xs leading-normal text-muted-foreground">
                                        {schedule.name ?? schedule.marketId}
                                    </p>
                                </div>
                            </div>
                            <div className="flex shrink-0 items-center gap-1.5">
                                <span
                                    className={cn(
                                        'text-2xs font-medium',
                                        schedule.enabled
                                            ? 'text-muted-foreground'
                                            : 'text-warning'
                                    )}
                                >
                                    {schedule.enabled ? 'Active' : 'Paused'}
                                </span>
                                <Switch
                                    checked={schedule.enabled}
                                    onClick={(e) => e.stopPropagation()}
                                    onCheckedChange={
                                        outOfScope
                                            ? undefined
                                            : () => onToggle(schedule)
                                    }
                                    disabled={outOfScope}
                                    aria-label={
                                        outOfScope
                                            ? 'Station active (outside your assigned markets)'
                                            : 'Station active'
                                    }
                                />
                                <DropdownMenu>
                                    <DropdownMenuTrigger asChild>
                                        <Button
                                            variant="ghost"
                                            size="icon"
                                            className="size-8"
                                            aria-label={`Actions for ${schedule.station ?? schedule.marketId} (${schedule.marketId})`}
                                            onClick={(e) => e.stopPropagation()}
                                            onKeyDown={(e) =>
                                                e.stopPropagation()
                                            }
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
                                                onClick={() => onView(schedule)}
                                            >
                                                <ICON.view />
                                                View details
                                            </DropdownMenuItem>
                                            <DropdownMenuItem
                                                onClick={() => onEdit(schedule)}
                                            >
                                                <ICON.edit />
                                                Edit
                                            </DropdownMenuItem>
                                            {isAdmin && (
                                                <DropdownMenuItem
                                                    onClick={() =>
                                                        onDuplicate(schedule)
                                                    }
                                                >
                                                    <ICON.duplicate />
                                                    Duplicate
                                                </DropdownMenuItem>
                                            )}
                                        </DropdownMenuGroup>
                                        {isAdmin && (
                                            <>
                                                <DropdownMenuSeparator />
                                                <DropdownMenuItem
                                                    onClick={() =>
                                                        requestDelete(schedule)
                                                    }
                                                    className="text-destructive focus:text-destructive"
                                                >
                                                    <ICON.delete />
                                                    Delete
                                                </DropdownMenuItem>
                                            </>
                                        )}
                                    </DropdownMenuContent>
                                </DropdownMenu>
                            </div>
                        </div>

                        {/* Next run: the load-bearing fact, promoted */}
                        <div className="rounded-lg border border-border/60 bg-background/40 px-3.5 py-2.5">
                            <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                                Next run
                            </p>
                            <div className="mt-0.5 text-sm font-medium">
                                {schedule.enabled ? (
                                    <ScheduleNextRun schedule={schedule} />
                                ) : (
                                    // The identity row already carries the
                                    // "Paused" label + switch; a paused station
                                    // has no next run, so the box reads a
                                    // single quiet marker instead of repeating.
                                    <span className="font-medium text-muted-foreground">
                                        -
                                    </span>
                                )}
                            </div>
                        </div>

                        {/* Send to playout: the per-station arm for THIS environment.
                            Deliberately a separate labelled control rather than a second
                            bare switch beside Active/Paused, because the two mean different
                            things: pausing stops the station generating, while this leaves it
                            generating, reviewing and publishing and keeps the audio off the
                            playout share. Both test and production can run with playout on at
                            the platform level, but only the environment armed for a station
                            may write its file — the file name is identical in both. */}
                        <div className="flex items-center justify-between gap-3 rounded-lg border border-border/60 bg-background/40 px-3.5 py-2.5">
                            <div className="min-w-0">
                                <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                                    Send to playout
                                </p>
                                <p
                                    className={cn(
                                        'mt-0.5 text-xs font-medium',
                                        envPlayoutEnabled &&
                                            schedule.playoutEnabled
                                            ? 'text-muted-foreground'
                                            : 'text-warning'
                                    )}
                                >
                                    {!envPlayoutEnabled
                                        ? 'This environment never sends to playout'
                                        : schedule.playoutEnabled
                                          ? 'On: a published bulletin reaches playout'
                                          : 'Off: generates and publishes, nothing reaches playout'}
                                </p>
                            </div>
                            <Switch
                                checked={schedule.playoutEnabled}
                                onClick={(e) => e.stopPropagation()}
                                onCheckedChange={
                                    outOfScope
                                        ? undefined
                                        : () => onTogglePlayout(schedule)
                                }
                                disabled={outOfScope}
                                aria-label={
                                    outOfScope
                                        ? 'Send to playout (outside your assigned markets)'
                                        : 'Send to playout'
                                }
                            />
                        </div>
                        {/* Coverage preview */}
                        {schedule.coverage ? (
                            <CoverageMapPreview coverage={schedule.coverage} />
                        ) : (
                            <p className="text-xs text-muted-foreground">
                                No coverage area set
                            </p>
                        )}

                        {/* One quiet stats column; detail lives in View Details.
                            A deliberate two-row stack, NOT a `flex-wrap
                            justify-between` row: that pushed the voice glyph
                            hard right on short cron strings and wrapped it on
                            long ones, so this block measured 19px on some cards
                            and 47px on others. Since `mt-auto` below collects
                            all slack into one gap, a 28px content swing became a
                            28px hole above the primary action — the same card,
                            spaced differently per station. Two rows always. */}
                        <div className="flex min-w-0 flex-col gap-1.5 text-xs text-muted-foreground">
                            <CronScheduleGlyph
                                expression={schedule.cronExpression}
                                className="min-w-0"
                            />
                            <span className="inline-flex min-w-0 items-center gap-1">
                                <VoiceModeGlyph
                                    schedule={schedule}
                                    voiceName={voiceName}
                                    className="min-w-0"
                                />
                                <span className="shrink-0">
                                    · {schedule.targetDurationSeconds}s
                                </span>
                            </span>
                        </div>

                        {/* `mt-auto` lives here, not on the button: the divider,
                            the meta line and the action are one bottom-anchored
                            cluster, so a taller sibling card's slack opens up
                            above the divider (where a card breathes) instead of
                            as a hole between the meta line and its own button. */}
                        <div className="mt-auto flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs text-muted-foreground">
                            {operatorNames && operatorNames.length > 0 ? (
                                <Tooltip>
                                    <TooltipTrigger asChild>
                                        <span className="flex min-w-0 items-center gap-1">
                                            <Users
                                                className="size-3 shrink-0 opacity-60"
                                                aria-hidden
                                            />
                                            <span className="truncate">
                                                {operatorNames.join(', ')}
                                            </span>
                                        </span>
                                    </TooltipTrigger>
                                    <TooltipContent>
                                        Operators assigned to this market. An
                                        unscoped operator can access every
                                        market.
                                    </TooltipContent>
                                </Tooltip>
                            ) : (
                                <span />
                            )}
                            <span
                                className="shrink-0"
                                title={formatDateTime(schedule.updatedAt)}
                            >
                                Updated {formatRelativeTime(schedule.updatedAt)}
                            </span>
                        </div>

                        <Button
                            variant="secondary"
                            onClick={(e) => {
                                e.stopPropagation()
                                onTrigger(schedule)
                            }}
                            className="w-full"
                            disabled={outOfScope}
                            title={
                                outOfScope
                                    ? 'Outside your assigned markets'
                                    : schedule.enabled
                                      ? undefined
                                      : 'Generates immediately, even while paused'
                            }
                        >
                            <ICON.urgent data-icon="inline-start" />
                            Trigger now
                        </Button>
                    </div>
                </SpotlightCard>
            </div>
            <ConfirmDialog
                open={isOpen}
                onOpenChange={onOpenChange}
                onConfirm={confirmDelete}
                title="Delete station"
                // Station and name are BOTH non-unique in production — dev runs
                // two `Star 102.7 FM` markets whose names also match — so the
                // marketId is the only thing that tells the operator which
                // record this irreversible action will take.
                description={`Deleting ${pendingItem?.station ?? schedule.station ?? ''} (${pendingItem?.marketId ?? schedule.marketId}) stops automatic bulletins.`}
            />
        </>
    )
}
