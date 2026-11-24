import type { Status } from '../../types'

/**
 * What the pipeline was DOING when it failed — the same in-flight activity
 * vocabulary the pipeline stepper and audit timeline use, so "Synthesizing
 * audio" in the stepper and on the failure badge are recognisably one stage.
 */
export const FAILURE_STAGE_LABELS: Record<string, string> = {
    REQUESTING: 'Requesting',
    FETCHING_TRAFFIC: 'Fetching traffic',
    GENERATING_SCRIPT: 'Generating script',
    SYNTHESIZING_AUDIO: 'Synthesizing audio',
    PUBLISHING: 'Publishing',
    UNKNOWN: 'Unknown stage',
}

/**
 * The in-flight name for each pipeline milestone -- used by the audit
 * timeline when a row's own note is still describing work in progress
 * (see resolveStageTitle in SystemAuditTab.tsx), so a milestone stamped at
 * phase START ("HereFetched" + "Fetching traffic...") doesn't read as
 * self-contradictory ("Traffic Fetched" + "Fetching traffic...").
 */
export const IN_PROGRESS_STAGE_LABELS: Partial<Record<Status, string>> = {
    REQUESTED: 'Requesting',
    HERE_FETCHED: 'Fetching traffic',
    INCIDENTS_SELECTED: 'Generating script',
    SCRIPT_READY: 'Finalizing audio',
    SYNTHESIZING_AUDIO: 'Synthesizing audio',
    AUDIO_READY: 'Finalizing audio',
}
