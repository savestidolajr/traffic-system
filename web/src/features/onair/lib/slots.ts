// =============================================================================
// Station Slot Computation — blanket recomposition
// =============================================================================

import type { MarketSchedule } from '@/features/schedules/types'
import type { JobSummary } from '@/features/jobs/types'
import {
    PIPELINE_STATUSES as PIPELINE_STATUSES_LIST,
    COVERED_STATUSES as COVERED_STATUSES_LIST,
    AT_RISK_STATUSES as AT_RISK_STATUSES_LIST,
} from '@/features/jobs/config/status-values'
import { getNextCronOccurrences } from '@/features/schedules/lib/cron-occurrences'
import { nextOccurrence } from '@/features/schedules/lib/schedule-calendar'
import { parseTimestamp } from '@/lib/formatting'
import { GENERATION_LEAD_MS } from './air-plan'
import {
    SLOT_MISS_THRESHOLD_MS,
    SLOT_LOOKBACK_WINDOW_MS,
} from '@/lib/constants/timing'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type SlotState =
    | 'covered'
    | 'needsReview'
    | 'generating'
    | 'atRisk'
    | 'late'
    | 'missed'
    | 'scheduled'

export interface Slot {
    readonly at: Date
    readonly state: SlotState
    readonly job?: JobSummary
}

export interface StationLane {
    readonly marketId: string
    readonly station: string
    readonly market: string
    readonly timezone: string
    readonly enabled: boolean
    readonly slots: Slot[]
    readonly adHoc: JobSummary[]
}

const PIPELINE_STATUSES = new Set<string>(PIPELINE_STATUSES_LIST)
const COVERED_STATUSES = new Set<string>(COVERED_STATUSES_LIST)
const AT_RISK_STATUSES = new Set<string>(AT_RISK_STATUSES_LIST)

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Normalize a 6-field cron to 5 fields by stripping the leading seconds
 * field, since the cron-occurrences lib expects 5-field input.
 */
function normaliseCron(expression: string | null): string | null {
    if (!expression) return null
    const parts = expression.trim().split(/\s+/)
    return parts.length === 6 ? parts.slice(1).join(' ') : expression
}

// ---------------------------------------------------------------------------
// Slot State Machine
// ---------------------------------------------------------------------------

/**
 * Map a job's status to the slot state it represents.
 *
 * `slotTime` is the cron occurrence this job was generated for, or null for an
 * ad-hoc bulletin that belongs to no slot. The past-slot states split by
 * actionability: a bulletin still awaiting a decision (or still generating)
 * once its slot has passed reads `late` - the operator can still approve it,
 * and until the expiry sweep marks it missed the bulletin is genuinely still
 * pending. `missed` is reserved for settled facts - a terminal `MISSED` job, or
 * a slot that never got a bulletin at all - where nothing remains to act on.
 * The old code collapsed both onto `missed`, so a station that had already
 * aired nothing was indistinguishable from one whose slot the operator could
 * still save.
 */
export function slotStateForJob(
    job: JobSummary,
    slotTime: number | null,
    now: number
): SlotState {
    // A terminal MISSED job is a settled miss and must win over every status-set
    // check below (MISSED is deliberately in none of them) or the board would
    // keep re-litigating a slot that already aired nothing.
    if (job.status === 'MISSED') return 'missed'
    if (COVERED_STATUSES.has(job.status)) return 'covered'
    const slotPassed =
        slotTime !== null && slotTime < now - SLOT_MISS_THRESHOLD_MS
    if (job.status === 'WAITING_APPROVAL')
        return slotPassed ? 'late' : 'needsReview'
    if (AT_RISK_STATUSES.has(job.status)) return 'atRisk'
    if (PIPELINE_STATUSES.has(job.status))
        return slotPassed ? 'late' : 'generating'
    return 'scheduled'
}

/** Determine slot state when no job occupies it. */
function slotStateNoJob(slotTime: number, now: number): SlotState {
    return slotTime < now - SLOT_MISS_THRESHOLD_MS ? 'missed' : 'scheduled'
}

// ---------------------------------------------------------------------------
// Job–Slot Matching
// ---------------------------------------------------------------------------

/**
 * Match jobs to the slot they were GENERATED FOR, not the slot nearest their
 * creation time.
 *
 * The poller creates a bulletin `ScheduleLeadTimeMinutes` (15) before its
 * airtime, so a raw `createdAt` comparison sits 15 minutes to the left of the
 * slot it belongs to. For a cadence C, that put it 15 minutes from its OWN slot
 * and C-15 from the previous one — so its own slot only won when C exceeded 30
 * minutes. Every station on a tighter clock than that (both pilot stations, and
 * every drive-time clock in the dev corpus) had its bulletins attributed to the
 * slot they FOLLOWED, or thrown out as ad-hoc and parked on the now-beam.
 *
 * Shifting by the lead makes a poller bulletin land on its own slot exactly, so
 * the tolerance can also tighten: a manual trigger fires at the moment it is
 * requested with no lead at all, and must stay ad-hoc rather than being pulled
 * onto whichever slot happens to sit 15 minutes ahead of it.
 *
 * The real fix is the backend's own `ScheduledForUtc` reaching the console, at
 * which point this heuristic can be deleted outright.
 */
const SLOT_MATCH_TOLERANCE_MS = 7 * 60 * 1000

function matchJobsToSlots(
    jobs: readonly JobSummary[],
    slots: Date[],
    station: string
): { matched: Map<number, JobSummary>; adHoc: JobSummary[] } {
    const matched = new Map<number, JobSummary>()
    const unmatched: JobSummary[] = []

    for (const job of jobs) {
        if (job.station !== station) continue

        const expectedAirtime =
            parseTimestamp(job.createdAt) + GENERATION_LEAD_MS
        let bestSlot: number | null = null
        let bestDist = Infinity

        for (const slot of slots) {
            const dist = Math.abs(slot.getTime() - expectedAirtime)
            if (dist <= SLOT_MATCH_TOLERANCE_MS && dist < bestDist) {
                bestSlot = slot.getTime()
                bestDist = dist
            }
        }

        if (bestSlot !== null && !matched.has(bestSlot)) {
            matched.set(bestSlot, job)
        } else {
            unmatched.push(job)
        }
    }

    const adHoc = unmatched
        .slice()
        .sort(
            (a, b) => parseTimestamp(b.createdAt) - parseTimestamp(a.createdAt)
        )
        .slice(0, 3)

    return { matched, adHoc }
}

// ---------------------------------------------------------------------------
// Slot enumeration helpers
// ---------------------------------------------------------------------------

/**
 * Upper bound on occurrences enumerated across the 6h lookback window. One
 * per minute is the finest a 5-field cron can express, so this covers any
 * schedule; `getNextCronOccurrences` has its own step cap underneath.
 */
const MAX_LOOKBACK_OCCURRENCES = Math.ceil(SLOT_LOOKBACK_WINDOW_MS / 60_000)

/**
 * Collect past and future cron occurrences around `now`.
 * Past slots: enumerate forward from `lookbackStart` (6h before now), keep
 * those at or before `now`, and take the most recent `pastCount`.
 * Future slots: enumerate `futureCount` occurrences from `now`.
 */
function enumerateSlots(
    cronExpression: string,
    timezone: string,
    now: Date,
    pastCount: number,
    futureCount: number
): Date[] {
    const nowMs = now.getTime()
    const all: Date[] = []

    if (pastCount > 0) {
        const lookback = new Date(nowMs - SLOT_LOOKBACK_WINDOW_MS)
        // The request has to span the WHOLE lookback window, not a small
        // multiple of pastCount. Asking for `pastCount * 3 + futureCount`
        // silently truncated any dense schedule: a station running every 15
        // minutes through a 6h window has ~24 occurrences before `now`, so a
        // 10-occurrence request stopped at 08:15 and the 08:45 slot the
        // operator was actually racing never existed. Every past slot, and
        // therefore every `missed` state, was unreachable for the busiest
        // stations. Enumerate the window and take the tail.
        const raw = getNextCronOccurrences(
            cronExpression,
            timezone,
            MAX_LOOKBACK_OCCURRENCES,
            lookback,
            now
        )
        const past = raw.filter((d) => d.getTime() <= nowMs).slice(-pastCount)
        all.push(...past)
    }

    if (futureCount > 0) {
        const future = getNextCronOccurrences(
            cronExpression,
            timezone,
            futureCount,
            now
        )
        all.push(...future)
    }

    all.sort((a, b) => a.getTime() - b.getTime())
    return all
}

/**
 * Calendar schedules cannot be enumerated by one compound cron expression:
 * each station-local date may be suppressed or may resolve to a different
 * pattern clock. Walk the same calendar-aware resolver used by the schedule
 * editor and poller, retaining only the bounded lookback tail.
 */
function enumerateCalendarSlots(
    cronExpression: string,
    timezone: string,
    calendar: NonNullable<MarketSchedule['calendar']>,
    now: Date,
    pastCount: number,
    futureCount: number
): Date[] {
    const schedule = { cronExpression, timezone, calendar }
    const nowMs = now.getTime()
    const all: Date[] = []

    if (pastCount > 0) {
        const past: Date[] = []
        let cursor = new Date(nowMs - SLOT_LOOKBACK_WINDOW_MS)
        for (let count = 0; count < MAX_LOOKBACK_OCCURRENCES; count++) {
            const occurrence = nextOccurrence(schedule, cursor)
            if (!occurrence || occurrence.getTime() > nowMs) break
            past.push(occurrence)
            cursor = occurrence
        }
        all.push(...past.slice(-pastCount))
    }

    if (futureCount > 0) {
        let cursor = now
        for (let count = 0; count < futureCount; count++) {
            const occurrence = nextOccurrence(schedule, cursor)
            if (!occurrence) break
            all.push(occurrence)
            cursor = occurrence
        }
    }

    all.sort((a, b) => a.getTime() - b.getTime())
    return all
}

export function computeStationLanes(
    schedules: readonly MarketSchedule[],
    jobs: readonly JobSummary[],
    now: Date,
    opts?: { past?: number; future?: number }
): StationLane[] {
    const pastCount = opts?.past ?? 2
    const futureCount = opts?.future ?? 4
    const nowMs = now.getTime()

    return schedules.map((schedule) => {
        const stationName = schedule.station ?? schedule.marketId
        const tz = schedule.timezone ?? 'UTC'

        const normalisedCron = normaliseCron(schedule.cronExpression)

        let allSlots: Date[] = []
        if (schedule.enabled && normalisedCron) {
            allSlots = schedule.calendar
                ? enumerateCalendarSlots(
                      normalisedCron,
                      tz,
                      schedule.calendar,
                      now,
                      pastCount,
                      futureCount
                  )
                : enumerateSlots(
                      normalisedCron,
                      tz,
                      now,
                      pastCount,
                      futureCount
                  )
        }

        // A disabled schedule has no active cadence — its jobs are history,
        // not "happening now". Skip matching entirely so old jobs don't
        // fall into the ad-hoc bucket and get drawn parked on the now-beam.
        const { matched, adHoc } = schedule.enabled
            ? matchJobsToSlots(
                  jobs.filter((j) => j.station === stationName),
                  allSlots,
                  stationName
              )
            : { matched: new Map<number, JobSummary>(), adHoc: [] }

        const slots: Slot[] = allSlots.map((slot) => {
            const job = matched.get(slot.getTime())
            if (job) {
                return {
                    at: slot,
                    state: slotStateForJob(job, slot.getTime(), nowMs),
                    job,
                }
            }
            return { at: slot, state: slotStateNoJob(slot.getTime(), nowMs) }
        })

        return {
            marketId: schedule.marketId,
            station: stationName,
            market: schedule.name ?? stationName,
            timezone: tz,
            enabled: schedule.enabled,
            slots,
            adHoc,
        }
    })
}

/**
 * Resolve the air slot for one bulletin: the cron occurrence on its
 * station's schedule that the job was generated for, or null when the
 * job is ad-hoc or the station has no enabled schedule.
 */
export function findAirSlot(
    schedules: readonly MarketSchedule[],
    job: JobSummary,
    now: Date = new Date()
): Date | null {
    const schedule = schedules.find(
        (s) => s.enabled && s.station === job.station
    )
    if (!schedule) return null
    const lanes = computeStationLanes([schedule], [job], now)
    for (const slot of lanes[0]?.slots ?? []) {
        if (slot.job?.jobId === job.jobId) return slot.at
    }
    return null
}
