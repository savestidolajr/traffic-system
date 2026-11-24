// =============================================================================
// Timing & polling constants — single source of truth
// =============================================================================

// ---------------------------------------------------------------------------
// API polling intervals
// ---------------------------------------------------------------------------
/** How often to poll jobs while any are mid-pipeline (ms). */
export const POLL_ACTIVE_MS = 3000
/** How often to poll a structured job progress document while it is active (ms). */
export const PROGRESS_POLL_MS = 2000

/** How often to poll jobs when none are mid-pipeline (ms). Kept tight:
 *  a bulletin can land WAITING_APPROVAL at any time, and every second of
 *  polling lag eats into the operator's 8-minute approval window. */
export const POLL_IDLE_MS = 10000

/** Stale time before a job detail refetches (ms). */
export const STALE_MS = 30000

/** How often to poll schedules (ms). */
export const SCHEDULE_POLL_MS = 30000

// ---------------------------------------------------------------------------
// Slot/broadcast timing
// ---------------------------------------------------------------------------
/** Window for matching a job to its cron slot: 15 min each side (ms). */
export const SLOT_WINDOW_MS = 15 * 60 * 1000

/** How far back the rundown looks (ms). Twenty minutes keeps the slot that just
 *  aired on screen — its bar starts 15 min before its airtime. */
export const LOOKBACK_MS = 20 * 60 * 1000

/** Threshold at which a slot is considered missed (ms). */
export const SLOT_MISS_THRESHOLD_MS = 5 * 60 * 1000

/** How far ahead the rundown board projects slots (ms). One hour, down from two:
 *  a slot is drawn as its whole generate-to-air WINDOW rather than a dot, and at
 *  a 2 h span those windows were ~10 px wide — unreadable, and the far half of
 *  the board carried nothing an operator could act on. Slots beyond the window
 *  are counted at the track's right edge instead of drawn. */
export const BOARD_LOOKAHEAD_MS = 60 * 60 * 1000
export const SLOT_LOOKBACK_WINDOW_MS = 6 * 60 * 60 * 1000
export const SCHEDULE_URGENT_MS = 5 * 60 * 1000
export const QUERY_STALE_TIME_MS = 5 * 60 * 1000
export const QUERY_RETRY_DELAY_CAP_MS = 30000
