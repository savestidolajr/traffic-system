import { z } from 'zod'
import {
    ContentSelectionSchema,
    MIN_CORRIDOR_RADIUS_M,
    MAX_CORRIDOR_RADIUS_M,
    type ContentSelection,
} from '@/lib/api/schemas/shared'
import {
    validateCoverageShape,
    type CoverageIssueCode,
} from '@/components/map-picker/coverage-validation'

/** Trigger-form copy is deliberately terser than the schedule form's. */
const COVERAGE_MESSAGES: Partial<Record<CoverageIssueCode, string>> = {
    'circle-too-large': 'Maximum 50 km',
    'bbox-incomplete': 'A bbox needs west, south, east and north',
    'corridor-too-few-points': 'A corridor needs at least 2 points',
}

export type TriggerContentSource = 'traffic' | 'weather' | 'fuel'

/** Flip a trigger content source. Deselecting clears its "Always include"
 * flag because the API rejects a required flag for an unselected source. */
export function toggleTriggerContentSource(
    current: ContentSelection,
    key: TriggerContentSource
): ContentSelection {
    const next = { ...current, [key]: !current[key] }
    if (!next[key] && (key === 'weather' || key === 'fuel')) {
        next[key === 'weather' ? 'weatherRequired' : 'fuelRequired'] = false
    }
    return next
}

/** Summarise selected sources without hiding whether supplements are required. */
export function summariseTriggerContent(
    content: ContentSelection | null | undefined
): string {
    if (!content) return 'Choose sources'
    const parts: string[] = []
    if (content.traffic) parts.push('Traffic')
    if (content.weather) {
        parts.push(content.weatherRequired ? 'Weather (always)' : 'Weather')
    }
    if (content.fuel) {
        parts.push(content.fuelRequired ? 'Fuel (always)' : 'Fuel')
    }
    return parts.length > 0 ? parts.join(' · ') : 'Choose sources'
}

// ---------------------------------------------------------------------------
// Schema — subset of OrchestrateRequest validated client-side
export const triggerSchema = z
    .object({
        targetDurationSec: z
            .number({ error: 'Duration is required' })
            .min(10, 'Minimum 10 seconds')
            .max(600, 'Maximum 600 seconds'),
        voiceProfileId: z.string().min(1, 'Voice is required'),
        voiceId: z.string().min(1, 'Voice is required'),
        voiceName: z.string().optional(),
        voiceModel: z.string().optional(),
        voiceSettings: z.object({
            speed: z.number(),
            stability: z.number(),
            similarityBoost: z.number(),
            style: z.number(),
            useSpeakerBoost: z.boolean(),
        }),
        latitude: z.number().min(-90).max(90),
        longitude: z.number().min(-180).max(180),
        radiusKm: z.number().min(1, 'Minimum 1 km'),
        shapeType: z.enum(['circle', 'bbox', 'corridor', 'polygon']).optional(),
        west: z.number().optional(),
        south: z.number().optional(),
        east: z.number().optional(),
        north: z.number().optional(),
        corridorPoints: z
            .array(z.object({ lat: z.number(), lng: z.number() }))
            .optional(),
        corridorRadiusMeters: z
            .number()
            .min(MIN_CORRIDOR_RADIUS_M)
            .max(MAX_CORRIDOR_RADIUS_M)
            .optional(),
        polygonPoints: z
            .array(z.object({ lat: z.number(), lng: z.number() }))
            .optional(),
        content: ContentSelectionSchema,
    })
    .superRefine((data, ctx) => {
        // Coverage shape validation — shared constraints, local copy
        for (const issue of validateCoverageShape(data)) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: COVERAGE_MESSAGES[issue.code] ?? issue.message,
                path: [issue.path],
            })
        }
        if (
            !data.content.traffic &&
            !data.content.weather &&
            !data.content.fuel
        ) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'At least one content source required',
                path: ['content'],
            })
        }
    })

export type TriggerFormData = z.infer<typeof triggerSchema>

export type StepId = 'voice' | 'coverage'

/** Which form fields belong to which step, for validity dots. Every key of
 *  `triggerSchema` must appear in exactly one step — enforced by test. */
export const STEP_FIELDS: Record<StepId, readonly (keyof TriggerFormData)[]> = {
    voice: [
        'voiceProfileId',
        'voiceId',
        'voiceName',
        'voiceModel',
        'voiceSettings',
        'targetDurationSec',
        'content',
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
    ],
}
