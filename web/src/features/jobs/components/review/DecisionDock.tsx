import { useSecondTick } from '@/hooks/use-second-tick'
import { ICON } from '@/lib/design/icons'
import { Spinner } from '@/components/ui/spinner'
import type { Job } from '../../types'
import {
    resolveBindingDeadline,
    getUrgencyBand,
    urgencyBandMeta,
    formatOverdue,
} from '../../lib/job-meta/urgency'
import { formatClockTimeFromDate, formatDuration } from '@/lib/formatting'
import { cn } from '@/lib/utils'

interface DecisionDockProps {
    readonly job: Job
    /** Opens the Revise dialog — the single "not good enough" surface. */
    readonly onRevise: () => void
    readonly onApprove?: () => void
    readonly isApproving?: boolean
    readonly disabled: boolean
    readonly reviseLabel?: string
    /** Queued changes count, badged on the Revise button. */
    readonly changesCount: number
    /**
     * The bulletin's slot on its station's schedule, from `findAirSlot`. Null
     * for an ad-hoc bulletin or a station with no enabled schedule, in which
     * case only the approval window is available to count down.
     */
    readonly airSlotAt: Date | null
}

/**
 * The decision, as a floating dock: countdown, Revise, and Approve in one
 * pill pinned to the bottom of the workspace. Revise (also `R`) opens the
 * consolidated regenerate/reject surface; Approve (also `A`) opens the approve
 * confirmation — neither key reaches a mutation, so one stray keystroke can
 * never put a bulletin to air. Renders only for WAITING_APPROVAL.
 *
 * The countdown is the EARLIER of the air slot and the approval window, and it
 * says which one it is. Counting the approval window alone told the operator
 * "+2:00 over, was due 8:58 am" for a bulletin whose 8:45 slot had already
 * passed, so the number on screen was both alarming and about the wrong thing.
 */
export function DecisionDock({
    job,
    onRevise,
    onApprove,
    isApproving = false,
    disabled,
    reviseLabel,
    changesCount,
    airSlotAt,
}: DecisionDockProps) {
    useSecondTick()

    const deadline = resolveBindingDeadline(job, airSlotAt)
    if (deadline === null) return null

    const secs = deadline.secondsRemaining
    const band = getUrgencyBand(secs)
    const deadlineTime = formatClockTimeFromDate(deadline.at)

    const abs = Math.abs(secs)
    // showHours is REQUIRED here: formatDuration defaults it false, which renders anything past an
    // hour as unbounded minutes — a bulletin due in two hours read "120:00", which an operator
    // naturally reads as 120 hours. The overdue branch never had the bug because formatOverdue
    // handles hours itself.
    const clock =
        secs < 0 ? formatOverdue(abs) : formatDuration(abs, { showHours: true })
    // A missed slot is not late paperwork, it is a station that aired nothing.
    // Say that, rather than a signed number the operator has to interpret.
    const countdown = deadline.slotMissed
        ? 'Slot missed'
        : secs < 0
          ? `+${clock} over`
          : clock
    const subLabel = deadline.slotMissed
        ? `${deadlineTime} slot, ${formatOverdue(abs)} ago`
        : deadline.kind === 'air'
          ? secs < 0
              ? `was on air ${deadlineTime}`
              : `on air ${deadlineTime}`
          : secs < 0
            ? `approval was due ${deadlineTime}`
            : `approve by ${deadlineTime}`

    // Final-seconds escalation: the dock itself turns hot so the deadline
    // is unmissable even in peripheral vision.
    const meta = urgencyBandMeta(band)
    const escalated = band === 'critical' || band === 'overdue'

    return (
        <div className="pointer-events-none fixed inset-x-0 bottom-0 z-30 flex justify-center bg-linear-to-t from-background via-background/70 to-transparent px-4 pb-5 pt-10">
            {/* fixed, not sticky: centred on the screen, not the pane column */}
            <div
                role="group"
                aria-label="Bulletin decision"
                className={cn(
                    'pointer-events-auto flex items-stretch overflow-hidden rounded-full border bg-popover shadow-md transition-[transform,box-shadow] duration-200 hover:-translate-y-0.5 hover:shadow-lg motion-reduce:transition-none motion-reduce:hover:translate-y-0 animate-in slide-in-from-bottom-2 fade-in-0 motion-reduce:animate-none',
                    escalated &&
                        'border-destructive/50 shadow-[0_0_28px_-6px_hsl(var(--destructive)/0.5)]'
                )}
            >
                <div
                    role="timer"
                    aria-label={
                        deadline.slotMissed
                            ? 'Air slot missed'
                            : deadline.kind === 'air'
                              ? 'Time remaining until this bulletin airs'
                              : 'Time remaining to approve'
                    }
                    className={cn(
                        'flex items-center gap-2 py-1.5 pl-4 pr-3',
                        meta.bgClass,
                        escalated && meta.textClass
                    )}
                >
                    <ICON.time
                        className={cn(
                            'size-4 shrink-0',
                            escalated && 'motion-safe:animate-pulse'
                        )}
                        aria-hidden
                    />
                    <span className="flex flex-col leading-tight">
                        <span
                            className={cn(
                                'font-mono text-sm font-semibold tabular-nums',
                                escalated && meta.textClass
                            )}
                        >
                            {countdown}
                        </span>
                        <span className="text-2xs">{subLabel}</span>
                    </span>
                </div>
                <button
                    type="button"
                    onClick={onRevise}
                    disabled={disabled}
                    className="flex items-center gap-1.5 border-l px-4 text-sm font-medium text-foreground transition-colors hover:bg-muted/60 focus-visible:bg-muted/60 disabled:pointer-events-none disabled:opacity-50"
                >
                    <ICON.revise className="size-4 text-primary" aria-hidden />
                    {reviseLabel ?? 'Revise'}
                    {changesCount > 0 && (
                        <span className="inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary/20 px-1 font-mono text-3xs tabular-nums text-primary-on-tint">
                            {changesCount}
                        </span>
                    )}
                    <kbd className="ml-0.5 rounded border border-border/60 bg-muted/60 px-1 font-mono text-3xs text-muted-foreground">
                        R
                    </kbd>
                </button>
                {onApprove && (
                    <button
                        type="button"
                        onClick={onApprove}
                        disabled={disabled}
                        /* The visible kbd is decorative: hidden from AT so the
                           accessible name stays "Approve", with the shortcut
                           exposed properly via aria-keyshortcuts. */
                        aria-keyshortcuts="a"
                        className="flex items-center gap-1.5 bg-primary py-2 pl-4 pr-5 text-sm font-semibold text-primary-foreground transition-colors hover:bg-primary/90 focus-visible:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
                    >
                        {isApproving ? (
                            <Spinner className="size-4" />
                        ) : (
                            <ICON.approve className="size-4" aria-hidden />
                        )}
                        {isApproving ? 'Approving…' : 'Approve'}
                        <kbd
                            aria-hidden
                            className="ml-0.5 rounded border border-primary-foreground/30 bg-primary-foreground/15 px-1 font-mono text-3xs text-primary-foreground"
                        >
                            A
                        </kbd>
                    </button>
                )}
            </div>
        </div>
    )
}
