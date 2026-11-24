import { z } from 'zod'
import { camelizeKeys } from '@/lib/api-utils'

/** Wire names emitted by ProgressStage in the backend contract. */
export const PROGRESS_STAGE_VALUES = [
    'Evidence',
    'Select',
    'Write',
    'Compose',
    'FitWords',
    'Edit',
    'Gates',
    'Voice',
    'FitAudio',
    'Ready',
    'Failed',
] as const

/** Wire names emitted by ProgressEventKind in the backend contract. */
export const PROGRESS_EVENT_KIND_VALUES = [
    'StageStarted',
    'StageCompleted',
    'SubStep',
    'AttemptStarted',
    'AttemptFailed',
    'LlmCall',
    'Note',
] as const

/** Unknown enum values remain visible as a safe sentinel when the backend grows. */
const ProgressStageSchema = z
    .enum([...PROGRESS_STAGE_VALUES, 'Unknown'] as const)
    .catch('Unknown')
const ProgressEventKindSchema = z
    .enum([...PROGRESS_EVENT_KIND_VALUES, 'Unknown'] as const)
    .catch('Unknown')

const CountsSchema = z.record(z.string(), z.number()).nullable().optional()

export const JobProgressEventSchema = z
    .object({
        At: z.string(),
        Attempt: z.number().int(),
        Stage: ProgressStageSchema,
        Kind: ProgressEventKindSchema,
        Detail: z.string(),
        Counts: CountsSchema,
        DurationMs: z.number().int().nullable().optional(),
        Model: z.string().nullable().optional(),
        Route: z.string().nullable().optional(),
    })
    .transform(camelizeKeys)

export const JobProgressSchema = z
    .object({
        JobId: z.string(),
        Status: z.string(),
        Stage: ProgressStageSchema,
        StageStartedAt: z.string(),
        Attempt: z.number().int(),
        MaxAttempts: z.number().int(),
        UpdatedAt: z.string(),
        Version: z.number().int(),
        Events: z.array(JobProgressEventSchema),
        TypicalSeconds: z.record(z.string(), z.number()).nullable().optional(),
    })
    .transform((value) => ({
        ...camelizeKeys(value),
        // Stage names are enum keys, not DTO property names. Keep their
        // PascalCase so callers can index by ProgressStage without guessing.
        typicalSeconds: value.TypicalSeconds,
    }))

export type ProgressStage = z.output<typeof ProgressStageSchema>
export type ProgressEventKind = z.output<typeof ProgressEventKindSchema>
export type JobProgressEvent = z.output<typeof JobProgressEventSchema>
export type JobProgress = z.output<typeof JobProgressSchema>

/** A 404 is a supported legacy path, not a query error. */
export interface JobProgressUnavailable {
    readonly unavailable: true
}

export type JobProgressResult = JobProgress | JobProgressUnavailable

export const isJobProgressUnavailable = (
    value: JobProgressResult | undefined
): value is JobProgressUnavailable =>
    value !== undefined && 'unavailable' in value && value.unavailable === true
