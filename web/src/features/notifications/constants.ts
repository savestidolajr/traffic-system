// =============================================================================
// Notification constants
// =============================================================================

import { NOTIFICATION_TRIGGER_STATUSES } from '@/features/jobs/config/status-values'
export { NOTIFICATION_TRIGGER_STATUSES }

// ---------------------------------------------------------------------------
// Chime configuration
// ---------------------------------------------------------------------------
export interface ChimeConfig {
    readonly freq1: number
    readonly dur1: number
    readonly freq2: number
    readonly dur2: number
}

/** Two-tone ascending chime for alarm notification. */
export const ALARM_CHIME: ChimeConfig = {
    freq1: 880,
    dur1: 120,
    freq2: 1175,
    dur2: 180,
}

/** Two-tone descending chime for silent/background notification. */
export const SILENT_CHIME: ChimeConfig = {
    freq1: 440,
    dur1: 150,
    freq2: 660,
    dur2: 120,
}

/** Master chime volume gain (0-1). */
export const CHIME_VOLUME = 0.3
