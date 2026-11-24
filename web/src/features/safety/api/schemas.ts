import { z } from 'zod'
import { camelizeKeys } from '../../../lib/api-utils'

// =============================================================================
// Content-safety term schemas (admin-only banned words + place-name exceptions).
// PascalCase API → camelCase frontend. Mirrors the backend SafetyTermDto.
// Distinct from guidance: a HARD compliance gate, additive (union) composition,
// and no Station scope.
// =============================================================================

export const SAFETY_KINDS = ['Banned', 'Allowed'] as const
export type SafetyKindName = (typeof SAFETY_KINDS)[number]

export const SAFETY_SCOPES = ['Global', 'Market'] as const
export type SafetyScopeName = (typeof SAFETY_SCOPES)[number]

export const SafetyTermSchema = z
    .object({
        Id: z.string(),
        Kind: z.string(),
        Scope: z.string(),
        ScopeId: z.string(),
        Term: z.string(),
        Note: z.string().nullish(),
        Enabled: z.boolean().optional().default(true),
        CreatedBy: z.string(),
        CreatedAtUtc: z.string(),
        UpdatedAtUtc: z.string(),
    })
    .transform(camelizeKeys)
export type SafetyTerm = z.infer<typeof SafetyTermSchema>

export const SafetyTermListSchema = z.array(SafetyTermSchema)
