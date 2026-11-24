import type { JobSummary } from '../../types'
import { stationLocalDayKey } from '@/lib/formatting'

/**
 * Statuses that represent a bulletin that actually went to air, worth surfacing as
 * "earlier today". Mirrors the backend's same-day continuity lookup set
 * (`GetLatestBulletinForMarketTodayAsync`), so the list matches the bulletins the
 * generator actually treats as prior context — an unpublished (waiting-approval,
 * approved-but-not-yet-published, or fallback) job was never heard by listeners, so
 * it is not a valid "earlier today" reference either.
 */
export const EARLIER_TODAY_STATUSES = [
    'PUBLISHED',
] as const satisfies readonly JobSummary['status'][]

/** Membership lookup over {@link EARLIER_TODAY_STATUSES} (small static table). */
const IS_EARLIER_TODAY_STATUS: Record<string, true> = Object.fromEntries(
    EARLIER_TODAY_STATUSES.map((s) => [s, true])
)

/**
 * True when two ISO timestamps fall on the same calendar day in the station's
 * schedule timezone. The browser timezone is unrelated to broadcast-day
 * continuity and must not decide which bulletin is "earlier today".
 */
export function sameLocalDay(
    aIso: string,
    bIso: string,
    timezone: string
): boolean {
    const aMs = Date.parse(aIso)
    const bMs = Date.parse(bIso)
    if (!Number.isFinite(aMs) || !Number.isFinite(bMs)) return false

    const aDay = stationLocalDayKey(aMs, timezone)
    return aDay !== '' && aDay === stationLocalDayKey(bMs, timezone)
}

export interface EarlierTodayAnchor {
    readonly jobId: string
    readonly station: string
    readonly createdAt: string
    readonly timezone: string
}

/**
 * The day's earlier bulletins for the same station as `current`: same station,
 * same local day, created strictly before `current`, in a produced/aired status,
 * excluding `current` itself. Newest first.
 */
export function selectEarlierToday(
    jobs: readonly JobSummary[],
    current: EarlierTodayAnchor
): JobSummary[] {
    const currentMs = Date.parse(current.createdAt)
    return jobs
        .filter(
            (j) =>
                j.jobId !== current.jobId &&
                j.station === current.station &&
                IS_EARLIER_TODAY_STATUS[j.status] === true &&
                sameLocalDay(
                    j.createdAt,
                    current.createdAt,
                    current.timezone
                ) &&
                Date.parse(j.createdAt) < currentMs
        )
        .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
}
