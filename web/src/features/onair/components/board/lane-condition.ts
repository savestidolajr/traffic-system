import {
    AlarmClock,
    CircleCheck,
    Clock,
    Loader2,
    AlertTriangle,
    CircleOff,
    PauseCircle,
} from 'lucide-react'
import type { JobSummary } from '@/features/jobs/types'
import { slotStateForJob, type StationLane } from '@/features/onair/lib/slots'
import { getSecondsRemaining } from '@/features/jobs/lib/job-meta/urgency'

/** The single most important fact about a station, ranked by operator need. */
export type LaneCondition =
    | { readonly kind: 'paused' }
    | { readonly kind: 'needsReview'; readonly secs: number | null }
    | { readonly kind: 'atRisk'; readonly count: number }
    | { readonly kind: 'late'; readonly count: number }
    | { readonly kind: 'missed'; readonly count: number }
    | { readonly kind: 'generating'; readonly count: number }
    | { readonly kind: 'clear'; readonly nextSlotAt: Date | null }

export function laneCondition(lane: StationLane, now: number): LaneCondition {
    if (!lane.enabled) return { kind: 'paused' }

    const waiting: JobSummary[] = []
    let atRisk = 0
    let late = 0
    let missed = 0
    let generating = 0

    for (const slot of lane.slots) {
        if (slot.state === 'needsReview' && slot.job) waiting.push(slot.job)
        if (slot.state === 'atRisk') atRisk++
        if (slot.state === 'late') late++
        if (slot.state === 'missed') missed++
        if (slot.state === 'generating') generating++
    }
    for (const job of lane.adHoc) {
        // Ad-hoc bulletins belong to no cron slot, so there is no slot to miss.
        const state = slotStateForJob(job, null, now)
        if (state === 'needsReview') waiting.push(job)
        if (state === 'atRisk') atRisk++
        if (state === 'generating') generating++
    }

    if (waiting.length > 0) {
        let min = Infinity
        for (const job of waiting) {
            const secs = getSecondsRemaining(job, now)
            if (secs !== null && secs < min) min = secs
        }
        return {
            kind: 'needsReview',
            secs: Number.isFinite(min) ? min : null,
        }
    }
    if (atRisk > 0) return { kind: 'atRisk', count: atRisk }
    // `late` outranks `missed`: an overdue bulletin the operator can still
    // approve is a live fact, a settled miss is not.
    if (late > 0) return { kind: 'late', count: late }
    if (missed > 0) return { kind: 'missed', count: missed }
    if (generating > 0) return { kind: 'generating', count: generating }

    const next = lane.slots.find(
        (s) => s.state === 'scheduled' && s.at.getTime() > now
    )
    return { kind: 'clear', nextSlotAt: next?.at ?? null }
}

// Colors come from the semantic theme tokens (destructive/warning/success/
// status-waiting) — the same vocabulary as RUNDOWN_DOT and STATUS_CONFIG, so
// a palette change happens once in the Tailwind theme, never per-map.
export const CONDITION_META: Record<
    LaneCondition['kind'],
    {
        readonly icon: typeof Clock
        readonly phrase: string
        /** Icon tile treatment on station coverage cards. */
        readonly tileClass: string
        readonly textClass: string
    }
> = {
    needsReview: {
        icon: Clock,
        phrase: 'Needs review',
        tileClass:
            'border-status-waiting/40 bg-status-waiting/10 text-status-waiting',
        textClass: 'text-status-waiting',
    },
    atRisk: {
        icon: AlertTriangle,
        phrase: 'Slot at risk',
        tileClass:
            'border-destructive/40 bg-destructive/10 text-destructive-on-tint',
        textClass: 'text-destructive-on-tint',
    },
    late: {
        icon: AlarmClock,
        phrase: 'Late',
        tileClass:
            'border-destructive/40 bg-destructive/10 text-destructive-on-tint',
        textClass: 'text-destructive-on-tint',
    },
    missed: {
        icon: CircleOff,
        phrase: 'Slot missed',
        tileClass:
            'border-border/50 bg-transparent text-muted-foreground-subtle',
        textClass: 'text-muted-foreground-subtle',
    },
    generating: {
        icon: Loader2,
        phrase: 'Generating',
        tileClass: 'border-border bg-muted/40 text-muted-foreground',
        textClass: 'text-muted-foreground',
    },
    clear: {
        icon: CircleCheck,
        phrase: 'All clear',
        tileClass: 'border-success/30 bg-success/10 text-success',
        textClass: 'text-muted-foreground',
    },
    paused: {
        icon: PauseCircle,
        phrase: 'Paused',
        tileClass:
            'border-border/50 bg-transparent text-muted-foreground-subtle',
        textClass: 'text-muted-foreground-subtle',
    },
}
