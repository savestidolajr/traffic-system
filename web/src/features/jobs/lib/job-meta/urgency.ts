import { parseTimestamp } from '@/lib/formatting'
import { SLOT_MISS_THRESHOLD_MS as SLOT_MISS_GRACE_MS } from '@/lib/constants/timing'
import type { JobSummary } from '../../types'

const URGENCY_CRITICAL_SECONDS = 60
const URGENCY_WARNING_SECONDS = 180
/** Cap for the overdue display — jobs more than 24h overdue show "24h+". */
export const OVERDUE_CAP_SECS = 24 * 60 * 60

/** Format overdue seconds as mm:ss (under 1h) or Xh Ym (1h+), capped at 24h. */
export function formatOverdue(totalSeconds: number): string {
    const abs = Math.abs(totalSeconds)
    if (abs >= OVERDUE_CAP_SECS) return '24h+'
    if (abs < 3600) {
        const m = Math.floor(abs / 60)
        const s = abs % 60
        return `${m}:${s.toString().padStart(2, '0')}`
    }
    const hrs = Math.floor(abs / 3600)
    const mins = Math.floor((abs % 3600) / 60)
    return mins > 0 ? `${hrs}h ${mins}m` : `${hrs}h`
}

export interface UrgencyMeta {
    readonly variant: 'destructive' | 'warning' | 'success' | 'secondary'
    readonly label: string
    readonly textClass: string
    readonly bgClass: string
    readonly strokeClass: string
}

/** Map an urgency band to all visual presentation properties. */
export function urgencyBandMeta(band: UrgencyBand): UrgencyMeta {
    switch (band) {
        case 'overdue':
            return {
                variant: 'destructive',
                label: 'Overdue',
                textClass: 'text-destructive',
                bgClass: 'bg-destructive/10',
                strokeClass: 'stroke-destructive',
            }
        case 'critical':
            return {
                variant: 'destructive',
                label: 'Critical',
                textClass: 'text-destructive',
                bgClass: 'bg-destructive/10',
                strokeClass: 'stroke-destructive',
            }
        case 'warning':
            return {
                variant: 'warning',
                label: 'Warning',
                textClass: 'text-warning',
                bgClass: 'bg-warning/10',
                strokeClass: 'stroke-warning',
            }
        case 'ok':
            return {
                variant: 'success',
                label: 'On track',
                textClass: 'text-success',
                bgClass: 'bg-success/10',
                strokeClass: 'stroke-success',
            }
        case 'none':
            return {
                variant: 'secondary',
                label: '',
                textClass: 'text-muted-foreground',
                bgClass: '',
                strokeClass: 'stroke-muted-foreground',
            }
    }
}

export type UrgencyBand = 'none' | 'overdue' | 'critical' | 'warning' | 'ok'

export function getUrgencyBand(secondsRemaining: number | null): UrgencyBand {
    if (secondsRemaining === null) return 'none'
    if (secondsRemaining < 0) return 'overdue'
    if (secondsRemaining <= URGENCY_CRITICAL_SECONDS) return 'critical'
    if (secondsRemaining <= URGENCY_WARNING_SECONDS) return 'warning'
    return 'ok'
}
export const APPROVAL_WINDOW_MINUTES = 8

/** Returns the approval deadline Date for WAITING_APPROVAL jobs, null otherwise. */
export function getApprovalDeadline(
    job: Pick<JobSummary, 'status' | 'lastUpdatedAt'>
): Date | null {
    if (job.status !== 'WAITING_APPROVAL') return null
    return new Date(
        parseTimestamp(job.lastUpdatedAt) + APPROVAL_WINDOW_MINUTES * 60_000
    )
}

/** Seconds until deadline (negative = overdue), null if not WAITING_APPROVAL. */
export function getSecondsRemaining(
    job: Pick<JobSummary, 'status' | 'lastUpdatedAt'>,
    now: number = Date.now()
): number | null {
    if (job.status !== 'WAITING_APPROVAL') return null
    const deadline = new Date(
        parseTimestamp(job.lastUpdatedAt) + APPROVAL_WINDOW_MINUTES * 60_000
    )
    return Math.floor((deadline.getTime() - now) / 1000)
}

// ---------------------------------------------------------------------------
// The deadline that actually binds
// ---------------------------------------------------------------------------

/**
 * Which clock is running out.
 *
 * `approval` is the 8-minute dwell SLA above. `air` is the bulletin's slot on
 * its station's schedule, resolved by `findAirSlot`. They are unrelated: the
 * SLA is measured from `lastUpdatedAt`, so revising a bulletin RESETS it while
 * the slot does not move. Before this resolver existed the cockpit counted down
 * the SLA and rendered the slot as a static grey label, which is compatible
 * with both "approve now, it airs in ten minutes" and "the slot passed ten
 * minutes ago and the station aired nothing".
 */
export type DeadlineKind = 'air' | 'approval'

export interface BindingDeadline {
    readonly kind: DeadlineKind
    readonly at: Date
    /** Negative once the deadline has passed. */
    readonly secondsRemaining: number
    /** True when an air slot exists and has passed by more than the grace threshold. */
    readonly slotMissed: boolean
}

/**
 * The earlier of the air slot and the approval window, for a bulletin awaiting
 * a decision. Returns null when the job is not awaiting approval.
 *
 * `airSlotAt` is nullable on purpose: an ad-hoc bulletin, or one whose station
 * has no enabled schedule, has no slot, and then the SLA is all there is.
 */
export function resolveBindingDeadline(
    job: Pick<JobSummary, 'status' | 'lastUpdatedAt'>,
    airSlotAt: Date | null,
    now: number = Date.now()
): BindingDeadline | null {
    if (job.status !== 'WAITING_APPROVAL') return null
    const approvalAt = getApprovalDeadline(job)
    if (!approvalAt) return null

    const slotMissed =
        airSlotAt !== null && airSlotAt.getTime() < now - SLOT_MISS_GRACE_MS
    const at =
        airSlotAt !== null && airSlotAt.getTime() <= approvalAt.getTime()
            ? airSlotAt
            : approvalAt
    return {
        kind: at === airSlotAt ? 'air' : 'approval',
        at,
        secondsRemaining: Math.floor((at.getTime() - now) / 1000),
        slotMissed,
    }
}

/**
 * True when the bulletin's OWN scheduled air slot is behind us.
 *
 * Deliberately separate from `resolveBindingDeadline`, which answers a narrower
 * question and returns null for every status except WAITING_APPROVAL — so reading
 * `resolveBindingDeadline(...)?.slotMissed` on a terminal job silently yields `false`,
 * i.e. "the slot is fine", for a bulletin whose slot went weeks ago. That trap cost a
 * gate on FALLBACK_USED that never fired.
 *
 * The source is the job's own `input.timeWindow.to` (`ScheduledForUtc` on the wire),
 * NOT the schedule-derived `useAirSlot`. That is the value the backend itself measures
 * a covered slot's delivery cutoff against (`JobWatchdog.PublishApprovedBeforeDeadlineAsync`),
 * and unlike a cron lookup it is present on the job whether or not the station still has
 * an enabled schedule. Same `SLOT_MISS_GRACE_MS` grace as `slotMissed`, so the two agree
 * on the boundary.
 *
 * Unparseable or absent returns false: never hide an action because a timestamp is
 * missing.
 */
export function isAirSlotPassed(
    job: {
        readonly input?: { readonly timeWindow?: { readonly to?: string } }
    },
    now: number = Date.now()
): boolean {
    const slot = job.input?.timeWindow?.to
    if (!slot) return false
    const at = parseTimestamp(slot)
    if (!Number.isFinite(at) || at <= 0) return false
    return at < now - SLOT_MISS_GRACE_MS
}
