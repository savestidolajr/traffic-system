// =============================================================================
// Single source of truth for all job status values and pipeline stage sets
// =============================================================================

/** Every job status value, ordered as the pipeline progresses. */
export const STATUS_VALUES = [
    'REQUESTED',
    'HERE_FETCHED',
    'INCIDENTS_SELECTED',
    'AWAITING_CURATION',
    'SCRIPT_READY',
    'SYNTHESIZING_AUDIO',
    'AUDIO_READY',
    'WAITING_APPROVAL',
    'APPROVED',
    'DELIVERY_PENDING',
    'REJECTED',
    'PUBLISHED',
    'FALLBACK_USED',
    'FAILED',
    'MISSED',
    'CANCELLED',
] as const satisfies readonly string[]

/** Statuses that are mid-pipeline (not terminal, not approved/published). */
export const PIPELINE_STATUSES = [
    'REQUESTED',
    'HERE_FETCHED',
    'INCIDENTS_SELECTED',
    'AWAITING_CURATION',
    'SCRIPT_READY',
    'SYNTHESIZING_AUDIO',
    'AUDIO_READY',
    'DELIVERY_PENDING',
] as const

/** Statuses that represent a successfully completed/reached state. */
export const COVERED_STATUSES = ['APPROVED', 'PUBLISHED'] as const

/** Statuses that represent a failed or rejected outcome. MISSED is deliberately
 *  absent: AT_RISK drives the on-air board's `atRisk` slot state, and a missed
 *  slot is a settled past fact, not a live risk. */
export const AT_RISK_STATUSES = ['REJECTED', 'FALLBACK_USED', 'FAILED'] as const

/** Statuses eligible for notification triggers (waiting for approval, failed). */
export const NOTIFICATION_TRIGGER_STATUSES = new Set<string>([
    'WAITING_APPROVAL',
    'FAILED',
])

/** Statuses that have script data. */
export const SCRIPT_STATUSES = [
    'SCRIPT_READY',
    'SYNTHESIZING_AUDIO',
    'AUDIO_READY',
    'WAITING_APPROVAL',
    'APPROVED',
    'DELIVERY_PENDING',
    'REJECTED',
    'PUBLISHED',
    'FALLBACK_USED',
    'MISSED',
] as const

/** Statuses that have audio data. */
export const AUDIO_STATUSES = [
    'AUDIO_READY',
    'WAITING_APPROVAL',
    'APPROVED',
    'DELIVERY_PENDING',
    'REJECTED',
    'PUBLISHED',
    'FALLBACK_USED',
    'MISSED',
] as const

/** Statuses that have traffic data. */
export const TRAFFIC_STATUSES = [
    'HERE_FETCHED',
    'INCIDENTS_SELECTED',
    'AWAITING_CURATION',
    'SCRIPT_READY',
    'SYNTHESIZING_AUDIO',
    'AUDIO_READY',
    'WAITING_APPROVAL',
    'APPROVED',
    'DELIVERY_PENDING',
    'REJECTED',
    'PUBLISHED',
    'FALLBACK_USED',
    'MISSED',
] as const

/** Terminal statuses (published, rejected, failed, fallback, missed). A missed
 *  bulletin is settled for good: it can never air or regenerate, so it must be
 *  terminal but is deliberately NOT at-risk (see AT_RISK_STATUSES).
 *
 *  This list DELIBERATELY differs from the backend's `Job.IsTerminal`, which omits
 *  REJECTED. The two answer different questions and both are right: the backend asks
 *  "may this job still be failed or covered?" (a rejected job may, so it is not
 *  terminal there), while this asks "has this job stopped changing on its own?" — which
 *  is what drives polling (`job-hooks` stops the detail poll), the as-aired record card,
 *  and suppressing live sentence verification. A rejected bulletin sits still until a
 *  human acts, so it belongs here. Do not "align" the two lists; they are not the same
 *  predicate, and moving REJECTED out would restart a poll that has nothing to watch. */
export const TERMINAL_STATUSES = [
    'PUBLISHED',
    'REJECTED',
    'CANCELLED',
    'FAILED',
    'FALLBACK_USED',
    'MISSED',
] as const
/** Statuses shown in as-aired/record view. Literal-typed (`as const`, no
 *  widening annotation) so lookups keyed by the status union stay sound. */
export const RECORD_STATUSES = [
    'WAITING_APPROVAL',
    'APPROVED',
    'PUBLISHED',
    'REJECTED',
    'CANCELLED',
    'FALLBACK_USED',
    'FAILED',
    'MISSED',
] as const satisfies readonly (typeof STATUS_VALUES)[number][]

/**
 * All failure stage values the backend can actually report. Mirrors
 * `JobMappingExtensions.ToFailureStage`, which maps Requested -> REQUESTING and
 * IncidentsSelected -> GENERATING_SCRIPT, so neither REQUESTED nor
 * SELECTING_INCIDENTS is ever on the wire. The schema catches unknowns as UNKNOWN.
 */
export const FAILURE_STAGE_VALUES = [
    'REQUESTING',
    'FETCHING_TRAFFIC',
    'GENERATING_SCRIPT',
    'SYNTHESIZING_AUDIO',
    'FAILED',
    'PUBLISHING',
    'REGENERATING',
    'UNKNOWN',
] as const satisfies readonly string[]
