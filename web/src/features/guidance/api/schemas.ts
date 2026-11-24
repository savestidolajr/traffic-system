import { z } from 'zod'
import { camelizeKeys } from '../../../lib/api-utils'

// =============================================================================
// Self-service guidance schemas. PascalCase API → camelCase frontend, matching
// the other features. Mirrors the backend GuidanceDto / GuidanceCandidateDto.
// =============================================================================

export const GUIDANCE_SCOPES = ['Global', 'Market', 'Station'] as const
export type GuidanceScopeName = (typeof GUIDANCE_SCOPES)[number]

/** Editorial directive types that compose into the <guidance> block (+ HouseStyle preamble). */
export const GUIDANCE_TYPES = [
    'Style',
    'Editorial',
    'Lexicon',
    'Prohibition',
    'HouseStyle',
] as const
export type GuidanceTypeName = (typeof GUIDANCE_TYPES)[number]

export const GuidanceSchema = z
    .object({
        Id: z.string(),
        Scope: z.string(),
        ScopeId: z.string(),
        Type: z.string(),
        Text: z.string(),
        Enabled: z.boolean(),
        CreatedBy: z.string(),
        CreatedAtUtc: z.string(),
        UpdatedAtUtc: z.string(),
        Source: z.string().nullish(),
        ReviewAfterUtc: z.string().nullish(),
    })
    .transform(camelizeKeys)

export type Guidance = z.infer<typeof GuidanceSchema>

export const GuidanceListSchema = z.array(GuidanceSchema)

/** Composed "rules in effect" preview (already camelCase on the wire). */
export const ComposedGuidanceSchema = z.object({
    inEffect: GuidanceListSchema,
    droppedCount: z.number(),
})

/** One stored future lesson (operator-taught or inferred) awaiting governance. */
export const GuidanceCandidateSchema = z
    .object({
        Id: z.string(),
        Station: z.string(),
        JobId: z.string(),
        Category: z.string(),
        Text: z.string(),
        CreatedAtUtc: z.string(),
        LastSeenAtUtc: z.string(),
        OccurrenceCount: z.number(),
        ExpiresAtUtc: z.string(),
        Promoted: z.boolean().default(false),
        Status: z.string().default('Active'),
        Origin: z.string().default('Operator'),
        ConflictWithLessonId: z.string().nullable().default(null),
    })
    .transform(camelizeKeys)

export type ComposedGuidance = z.infer<typeof ComposedGuidanceSchema>

export type GuidanceCandidate = z.infer<typeof GuidanceCandidateSchema>

export const GuidanceCandidateListSchema = z.array(GuidanceCandidateSchema)
