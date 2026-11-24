// =============================================================================
// Air plan — what will actually reach the playout system for the next slot
// =============================================================================
//
// The board answers "is this station covered". It never answered the question an
// operator actually asks before publishing: for the NEXT slot, what audio is
// currently set to go to Zetta, and by when must someone act to change it.
//
// Every rule below mirrors a backend rule, deliberately, so the forecast matches
// what the deadline sweep will really do:
//
//   cutoff            = slot - BroadcastOptions.FallbackDeadlineMinutes (3)
//   re-air candidate  = TableStorageJobRepository.GetLastSuccessfulJobForMarketAsync
//                       (same station, same LOCAL day, status Approved/Published/
//                        FallbackUsed, audio present, newest by CreatedAt)
//   filler cart       = BulletinFallbackService.TryFillerCartAsync, the station's
//                       own-voice cart
//   held back         = BulletinFallbackService returns None for Approved, and
//                       JobWatchdog.IsValidAudio skips a WaitingApproval job that
//                       already has audio. Neither is delivered, and neither gets
//                       covered — the slot goes to air with nothing unless an
//                       operator publishes. That case had no representation in the
//                       UI at all, which is what made a stale publish look safe.

import type { JobSummary } from '@/features/jobs/types'
import {
    AUDIO_STATUSES,
    PIPELINE_STATUSES,
} from '@/features/jobs/config/status-values'
import { parseTimestamp, stationLocalDayKey } from '@/lib/formatting'
import type { StationLane } from './slots'

/** Mirrors backend `BroadcastOptions.FallbackDeadlineMinutes` (3 minutes). */
export const CUTOFF_LEAD_MS = 3 * 60 * 1000

/**
 * Mirrors backend `BroadcastOptions.ApprovedDeliveryLeadSeconds` (90 seconds): how far BEFORE
 * the cutoff the deadline sweep delivers an approved-but-unpublished bulletin. It has to be
 * ahead of the cutoff, not at it — playout refuses a slot whose deadline has passed, so a sweep
 * firing at the deadline could never land (2026-08-13: a prod slot aired nothing for exactly
 * this reason). Used here only to decide when "sending now" becomes true.
 */
export const AUTO_DELIVERY_LEAD_MS = 90 * 1000

/**
 * Mirrors backend `BroadcastOptions.ScheduleLeadTimeMinutes` (15 minutes): the
 * poller starts a bulletin this far before its airtime, so this is the left edge
 * of the window a slot owns on the board.
 */
export const GENERATION_LEAD_MS = 15 * 60 * 1000

const AUDIO = new Set<string>(AUDIO_STATUSES)
const PIPELINE = new Set<string>(PIPELINE_STATUSES)

/**
 * The only statuses the backend will re-air. WAITING_APPROVAL is excluded on
 * purpose: re-airing content nobody approved is a compliance problem, not a
 * convenience (see the repository query's own comment).
 */
const REAIR_STATUSES = new Set<string>([
    'APPROVED',
    'PUBLISHED',
    'FALLBACK_USED',
])

/** How many earlier bulletins to offer as re-air options. */
const MAX_REAIR_CANDIDATES = 3

/** How many of a station's own upcoming airtimes the inline track shows. */
const MAX_UPCOMING_SLOTS = 5

export type AirSource =
    | 'fresh' // this slot's own bulletin: published, or approved and awaiting delivery
    | 'generating' // a bulletin for this slot is being made right now
    | 'notStarted' // the slot is further out than the generation lead: nothing due yet
    | 'reair' // nothing is coming, so an earlier bulletin today covers the slot
    | 'filler' // nothing is coming and nothing to repeat: the standby cart covers it
    | 'none' // nothing available: only reachable in a misconfigured environment
    | 'noSlot' // no further slot today

export interface AirCandidate {
    /** Job id, or the literal `filler` for the cart. */
    readonly id: string
    readonly kind: 'fresh' | 'reair' | 'filler'
    readonly label: string
    readonly detail: string
    /** True for the option that will air if nobody intervenes. */
    readonly current: boolean
}

export interface AirPlanRow {
    readonly marketId: string
    readonly station: string
    readonly market: string
    readonly timezone: string
    /** When the next bulletin airs, or null when the station has no slot left. */
    readonly slotAt: Date | null
    /** The publish-by moment: after this, the deadline sweep decides, not the operator. */
    readonly cutoffAt: Date | null
    /** Negative once the cutoff has passed. Null when there is no slot. */
    readonly secondsToCutoff: number | null
    readonly source: AirSource
    /** This station's own upcoming airtimes, so its cadence is visible inline. */
    readonly upcoming: readonly Date[]
    /** The bulletin that will air, when one is identified. */
    readonly job?: JobSummary
    /** The bulletin generated for this specific slot (even if failed/waiting/unairable). */
    readonly slotJob?: JobSummary
    /**
     * A bulletin for this slot that is still awaiting review. It does NOT air
     * unless someone approves it — the deadline sweep covers the slot with a
     * repeat or the cart instead — but it is what the operator needs to jump to,
     * so the row can offer "Approve" while the forecast says "Repeat".
     */
    readonly pending?: JobSummary
    readonly candidates: readonly AirCandidate[]
}

export interface AirPlanOptions {
    /**
     * Whether a standby cart exists to cover an uncovered slot.
     *
     * This is a NETWORK-level setting, not a per-station one. The backend tries
     * the cart script synthesized in the station's own voice first, and falls
     * back to the evergreen `Broadcast__FillerCartAudioUrl` WAV when that cannot
     * resolve (`BulletinFallbackService.TryFillerCartAsync`). Both are populated
     * in dev, test and prod, so a slot going to air with NOTHING is a
     * misconfiguration rather than an operating state.
     *
     * An earlier version derived this per station from `voiceProfileId != null`,
     * which ignored the evergreen WAV entirely and so reported dead air on a
     * station the backend would have covered — a false alarm, which is worse
     * than no signal.
     */
    readonly standbyCartConfigured: boolean
}

// ---------------------------------------------------------------------------
// Local-day comparison
// ---------------------------------------------------------------------------

// The backend's re-air lookup is keyed by the station-local broadcast day.
// Keep this comparison in one shared formatter so the board and review card
// cannot drift back to the browser's timezone.

// Same cache discipline as the clock formatter below: the plan re-renders on
// a 1Hz tick and every row formats two clock times.
const clockFormatters = new Map<string, Intl.DateTimeFormat>()

/** HH:MM in the station's own timezone — a slot is a local broadcast time. */
export function formatSlotClock(date: Date, timezone: string): string {
    let formatter = clockFormatters.get(timezone)
    if (!formatter) {
        try {
            formatter = new Intl.DateTimeFormat('en-GB', {
                timeZone: timezone,
                hour: '2-digit',
                minute: '2-digit',
                hour12: false,
            })
        } catch {
            formatter = new Intl.DateTimeFormat('en-GB', {
                hour: '2-digit',
                minute: '2-digit',
                hour12: false,
            })
        }
        clockFormatters.set(timezone, formatter)
    }
    return formatter.format(date)
}

// A slot time is the STATION's local clock, so a console watching several
// markets shows 12:40 (Perth) beside 14:36 (Gold Coast) — correct, and
// unreadable without saying which zone each one is. Only foreign zones get a
// label; a station on the viewer's own clock needs no decoration.
const zoneLabels = new Map<string, string>()

function shortZone(date: Date, timezone?: string): string {
    try {
        // en-AU, not en-GB: en-GB renders Australian zones as "GMT+9:30",
        // where the broadcast-side name operators use is "ACST".
        const parts = new Intl.DateTimeFormat('en-AU', {
            ...(timezone ? { timeZone: timezone } : {}),
            timeZoneName: 'short',
        }).formatToParts(date)
        return parts.find((p) => p.type === 'timeZoneName')?.value ?? ''
    } catch {
        return ''
    }
}

/** Short zone name (e.g. `AWST`), or '' when the station shares the viewer's clock. */
export function foreignZoneLabel(timezone: string): string {
    const cached = zoneLabels.get(timezone)
    if (cached !== undefined) return cached
    const now = new Date()
    const station = shortZone(now, timezone)
    const label = station && station !== shortZone(now) ? station : ''
    zoneLabels.set(timezone, label)
    return label
}

// ---------------------------------------------------------------------------
// Resolution
// ---------------------------------------------------------------------------

function hasAudio(job: JobSummary): boolean {
    return AUDIO.has(job.status)
}

/** `in 4:10` before the cutoff, `1:50 over` after it. */
export function cutoffPhrase(secondsToCutoff: number | null): string {
    if (secondsToCutoff === null) return ''
    const abs = Math.abs(secondsToCutoff)
    const minutes = Math.floor(abs / 60)
    const seconds = Math.floor(abs % 60)
    const clock = `${minutes}:${seconds.toString().padStart(2, '0')}`
    return secondsToCutoff < 0 ? `${clock} over` : `in ${clock}`
}

/**
 * Earlier bulletins from the same station and the same LOCAL broadcast day that
 * the backend would accept as a re-air source, newest first.
 *
 * Mirrors `TableStorageJobRepository.GetLastSuccessfulJobForMarketAsync`: same
 * station, same local day, an airable status, audio present. Getting any of
 * those wrong would make the board promise a repeat the sweep would refuse.
 */
function reairCandidates(
    jobs: readonly JobSummary[],
    station: string,
    timezone: string,
    nowMs: number,
    excludeJobId: string | null
): JobSummary[] {
    const today = stationLocalDayKey(nowMs, timezone)
    return jobs
        .filter(
            (job) =>
                job.station === station &&
                job.jobId !== excludeJobId &&
                REAIR_STATUSES.has(job.status) &&
                hasAudio(job) &&
                stationLocalDayKey(parseTimestamp(job.createdAt), timezone) ===
                    today
        )
        .sort(
            (a, b) => parseTimestamp(b.createdAt) - parseTimestamp(a.createdAt)
        )
}

/**
 * What will reach playout for this station's next slot, and what else could.
 *
 * Pure: same inputs, same row. The board renders one of these per station and
 * re-renders on a 1Hz clock, so everything here is cheap and allocation-light.
 */
export function resolveAirPlan(
    lane: StationLane,
    jobs: readonly JobSummary[],
    now: Date,
    standbyCartConfigured: boolean
): AirPlanRow {
    const nowMs = now.getTime()
    const base = {
        marketId: lane.marketId,
        station: lane.station,
        market: lane.market,
        timezone: lane.timezone,
    }

    const nextSlot = lane.slots.find((slot) => slot.at.getTime() >= nowMs)
    if (!nextSlot) {
        return {
            ...base,
            slotAt: null,
            cutoffAt: null,
            secondsToCutoff: null,
            source: 'noSlot',
            upcoming: [],
            candidates: [],
        }
    }

    const slotAt = nextSlot.at
    const cutoffAt = new Date(slotAt.getTime() - CUTOFF_LEAD_MS)
    const secondsToCutoff = Math.floor((cutoffAt.getTime() - nowMs) / 1000)
    const slotJob = nextSlot.job

    const reairs = reairCandidates(
        jobs,
        lane.station,
        lane.timezone,
        nowMs,
        slotJob?.jobId ?? null
    ).slice(0, MAX_REAIR_CANDIDATES)

    const candidates: AirCandidate[] = []

    // A bulletin still WAITING REVIEW does not air: the sweep covers the slot
    // with a repeat or the cart instead, because re-airing content nobody
    // approved is a compliance problem. It is still the thing an operator must
    // act on, so it travels separately from the forecast.
    const pending =
        slotJob && hasAudio(slotJob) && slotJob.status === 'WAITING_APPROVAL'
            ? slotJob
            : undefined

    // Two states used to collapse into "Repeat" wrongly, which made a repeat
    // look like the normal case:
    //
    //   - a bulletin for this slot still in the pipeline. It has no audio yet,
    //     so the old branch fell straight through to the fallback and announced
    //     a repeat while the rail beside it said "Fetching traffic".
    //   - a slot further out than the generation lead. Nothing has been
    //     requested because it is not time; that is an empty diary, not a
    //     fallback.
    const generating =
        slotJob && PIPELINE.has(slotJob.status) ? slotJob : undefined
    const beforeLead = slotAt.getTime() - nowMs > GENERATION_LEAD_MS

    let source: AirSource
    if (
        slotJob &&
        (slotJob.status === 'PUBLISHED' ||
            (slotJob.status === 'APPROVED' && hasAudio(slotJob)))
    ) {
        source = 'fresh'
    } else if (generating) {
        source = 'generating'
    } else if (!slotJob && beforeLead) {
        source = 'notStarted'
    } else if (reairs.length > 0) {
        source = 'reair'
    } else if (standbyCartConfigured) {
        source = 'filler'
    } else {
        source = 'none'
    }

    // Every audio option for the slot, airable or not, because the panel that
    // renders these is where an operator LISTENS. A bulletin awaiting review is
    // included and carries a Review action rather than an Air one: the backend
    // refuses unapproved content, so approving is its only route to air — but
    // hearing it first is the whole point of standing in front of this list.
    if (slotJob && hasAudio(slotJob) && (source === 'fresh' || pending)) {
        candidates.push({
            id: slotJob.jobId,
            kind: 'fresh',
            label: 'New bulletin',
            detail:
                slotJob.status === 'PUBLISHED'
                    ? 'in playout'
                    : slotJob.status === 'APPROVED'
                      ? 'approved, not yet sent'
                      : 'needs approval',
            current: source === 'fresh',
        })
    }

    for (const [index, job] of reairs.entries()) {
        candidates.push({
            id: job.jobId,
            kind: 'reair',
            // Named by the clock it aired on, not by its job id: an operator
            // deciding which repeat to run recognises "the 15:22 one", and a
            // database key tells them nothing about the content.
            label: `Repeat ${formatSlotClock(new Date(parseTimestamp(job.createdAt)), lane.timezone)}`,
            detail: 'aired earlier today',
            current: source === 'reair' && index === 0,
        })
    }

    if (standbyCartConfigured) {
        candidates.push({
            id: 'filler',
            kind: 'filler',
            label: 'Standby cart',
            detail: 'this station\u2019s voice',
            current: source === 'filler',
        })
    }

    const airingJob =
        source === 'fresh' || source === 'generating'
            ? slotJob
            : source === 'reair'
              ? reairs[0]
              : undefined

    return {
        ...base,
        slotAt,
        cutoffAt,
        secondsToCutoff,
        source,
        pending,
        slotJob,
        upcoming: lane.slots
            .filter((slot) => slot.at.getTime() >= nowMs)
            .slice(0, MAX_UPCOMING_SLOTS)
            .map((slot) => slot.at),
        job: airingJob,
        candidates,
    }
}

/**
 * Order: the rows a human can still change, first.
 *
 * Urgency is NOT part of this — the countdown beside each row carries that, and
 * sorting by it made rows swap places under the pointer on the 1Hz tick.
 */
function rowPriority(row: AirPlanRow): number {
    if (row.pending) return 4
    switch (row.source) {
        case 'none':
            return 3
        case 'filler':
            return 2
        case 'reair':
            return 1
        default:
            return 0
    }
}

/**
 * One row per active station, the ones needing a human first.
 *
 * Paused stations are dropped rather than sorted last: a paused station has no
 * slot to forecast, and leaving it in the list made "nothing will air" read as
 * a problem instead of a setting.
 */
export function buildAirPlan(
    lanes: readonly StationLane[],
    jobs: readonly JobSummary[],
    now: Date,
    standbyCartConfigured: boolean
): AirPlanRow[] {
    return lanes
        .filter((lane) => lane.enabled)
        .map((lane) => resolveAirPlan(lane, jobs, now, standbyCartConfigured))
        .sort((a, b) => {
            const byNeed = rowPriority(b) - rowPriority(a)
            if (byNeed !== 0) return byNeed
            const at = a.cutoffAt?.getTime() ?? Infinity
            const bt = b.cutoffAt?.getTime() ?? Infinity
            return at - bt
        })
}

// ---------------------------------------------------------------------------
// Presentation
// ---------------------------------------------------------------------------
//
// One vocabulary, used by the label, the tooltip, the text colour and the bar
// fill, so a row can never say one thing in words and another in colour.
//
// Labels are two or three words, in the tense an operator would use out loud.
// Anything longer stopped being read: at six rows on a 1Hz clock the label is
// scanned, not studied, and a sentence in a table cell is a sentence skipped.
//
//   Not due yet    the slot is outside the generation lead; nothing requested
//   Writing        a bulletin for this slot is mid-pipeline
//   Standby cart   nothing published, so the cart covers the slot
//   Sends by 21:12 this slot's own bulletin, approved, sweep has not sent it yet
//   Sending now    inside the delivery window; the sweep is sending it
//   Deadline passed approved too late to send; nothing will air for this slot
//   In playout     the WAV has been written to the station's drop folder
//   Repeat 17:47   today's last approved bulletin goes again

/**
 * What airs, in two or three words.
 *
 * An APPROVED bulletin is not in the drop yet. `JobWatchdog.PublishApprovedBeforeDeadlineAsync`
 * delivers it on a sweep tick in the window BEFORE its cutoff, so until that window opens the
 * honest phrasing is a bound ("sends by HH:MM" — the exact tick inside the window is not
 * knowable here), inside it "sending now", and "In playout" only once the file has been written.
 * Past the cutoff nothing will send it: playout refuses an expired slot, so promising delivery
 * there would be a promise nothing keeps.
 */
export function airSourceLabel(row: AirPlanRow): string {
    switch (row.source) {
        case 'fresh':
            if (row.job?.status === 'PUBLISHED') return 'In playout'
            if (!row.cutoffAt) return 'Approved'
            if (row.secondsToCutoff === null)
                return `Sends by ${formatSlotClock(row.cutoffAt, row.timezone)}`
            if (row.secondsToCutoff < 0) return 'Deadline passed'
            return row.secondsToCutoff * 1000 <= AUTO_DELIVERY_LEAD_MS
                ? 'Sending now'
                : `Sends by ${formatSlotClock(row.cutoffAt, row.timezone)}`
        case 'generating':
            return 'Writing'
        case 'notStarted':
            return 'Not due yet'
        case 'reair':
            return row.job
                ? `Repeat ${formatSlotClock(new Date(parseTimestamp(row.job.createdAt)), row.timezone)}`
                : 'Repeat'
        case 'filler':
            return 'Standby cart'
        case 'none':
            return 'Nothing to air'
        case 'noSlot':
            return 'Done for today'
    }
}

/**
 * Why that is what airs. Read on hover by someone who does not recognise the
 * label, so it explains the CAUSE the label cannot fit; it never restates the
 * label, and it never runs past one line.
 */
export function airSourceReason(row: AirPlanRow): string {
    switch (row.source) {
        case 'fresh':
            if (row.job?.status === 'PUBLISHED')
                return 'In the station’s drop folder, waiting for playout to collect it.'
            // No delivery deadline means nothing will send it: only the poller sets
            // FallbackDeadlineUtc, so an ad-hoc bulletin waits for a human to publish.
            if (!row.cutoffAt)
                return 'Approved, but this bulletin has no slot deadline — publish it to send it.'
            if (row.secondsToCutoff === null)
                return 'Approved. Nothing is in the drop yet; the sweep sends it before the deadline.'
            if (row.secondsToCutoff < 0)
                return 'Approved after the delivery deadline: playout will not accept it, so this slot airs nothing.'
            return row.secondsToCutoff * 1000 <= AUTO_DELIVERY_LEAD_MS
                ? 'Approved and inside the delivery window: being sent on the next sweep, within a minute.'
                : 'Approved. Nothing is in the drop yet; the sweep sends it shortly before the deadline.'
        case 'generating':
            return 'Being written and voiced now. Needs review before it airs.'
        case 'notStarted':
            return 'Requested 15 minutes before airtime.'
        case 'reair':
            return 'Nothing new published, so today’s last approved bulletin goes again.'
        case 'filler':
            return 'Nothing from today to repeat, so the cart covers the slot.'
        case 'none':
            return 'No bulletin and no cart configured. This is a misconfiguration.'
        case 'noSlot':
            return 'No bulletin scheduled for the rest of today.'
    }
}

/** Text colour: one hue per outcome, matching the words beside it. */
export function airSourceTone(source: AirSource): string {
    switch (source) {
        case 'fresh':
            return 'text-success'
        case 'generating':
            return 'text-primary'
        case 'reair':
            return 'text-info'
        case 'filler':
            return 'text-warning'
        case 'none':
            return 'text-destructive'
        default:
            return 'text-muted-foreground'
    }
}

/** The same outcome as a bar fill and edge on the slot track. */
export function airSourceBar(source: AirSource): string {
    switch (source) {
        case 'fresh':
            return 'bg-success/25 border-success/80'
        case 'generating':
            return 'bg-primary/25 border-primary/80'
        case 'reair':
            return 'bg-info/25 border-info/80'
        case 'filler':
            return 'bg-warning/25 border-warning/80'
        case 'none':
            return 'bg-destructive/25 border-destructive/80'
        default:
            return 'bg-muted/40 border-border'
    }
}
