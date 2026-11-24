import type { EditedIncidentPayload } from '../../api/job-hooks'

/**
 * Build the edited-incident payload from included/available key lists,
 * as used by the bulletin-review workflow.
 */
export function toReviewIncidentPayload(
    included: readonly string[],
    available: readonly string[]
): EditedIncidentPayload[] {
    return [
        ...included.map((incidentId, i) => ({
            incidentId,
            included: true,
            priority: i + 1,
        })),
        ...available.map((incidentId) => ({
            incidentId,
            included: false,
            priority: 0,
        })),
    ]
}
