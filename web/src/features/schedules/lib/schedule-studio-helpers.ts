import type { ScheduleFormData } from '../types'

export type StepId =
    | 'station'
    | 'schedule'
    | 'alerts'
    | 'voice'
    | 'speech'
    | 'content'
    | 'coverage'
    | 'history'

/** Public alias for the step id; used by ScheduleStudio's `initialStep` prop. */
export type ScheduleStudioStepId = StepId

/** Which form fields belong to which step, for validity dots. Every key of
 *  `ScheduleFormSchema` must appear in exactly one step (or in
 *  `NON_STEP_FIELDS`) — enforced by test. */
export const STEP_FIELDS: Record<StepId, readonly (keyof ScheduleFormData)[]> =
    {
        station: [
            'marketId',
            'name',
            'station',
            'timezone',
            'targetDurationSeconds',
        ],
        schedule: [
            'daysOfWeek',
            'scheduleMode',
            'startTime',
            'endTime',
            'intervalMinutes',
            'specificTimes',
            'calendar',
        ],
        alerts: [
            'notifyBeforePlayoutMinutes',
            'escalationBeforePlayoutMinutes',
        ],
        voice: [
            'voiceProfileId',
            'voiceRotationMode',
            'voiceRotationIds',
            'voiceWindows',
        ],
        speech: [
            'deliveryStyleId',
            'playbookId',
            'pronunciationDictionaryId',
            'bookendId',
            'bookendRotationMode',
            'bookendRotationIds',
            'bookendWindows',
            'bookendTokens',
        ],
        coverage: [
            'latitude',
            'longitude',
            'radiusKm',
            'shapeType',
            'west',
            'south',
            'east',
            'north',
            'corridorPoints',
            'corridorRadiusMeters',
            'polygonPoints',
            'weatherCoverage',
            'fuelCoverage',
        ],
        content: ['content', 'fillOrder'],
        // No form fields -- read-only activity feed, never invalidates.
        history: [],
    }

/** Schema fields deliberately outside the step rail: `enabled` and `playoutEnabled` live
 *  on the footer toggles, `s3Path` is server-managed and never edited here. */
export const NON_STEP_FIELDS: readonly (keyof ScheduleFormData)[] = [
    'enabled',
    'playoutEnabled',
    's3Path',
]
