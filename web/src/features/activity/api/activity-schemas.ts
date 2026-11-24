import { z } from 'zod'
import { camelizeKeys } from '@/lib/api-utils'

// API responses are PascalCase (matching the real backend); camelizeKeys maps
// them to the camelCase the UI works in -- same convention as
// StatusHistoryItemSchema in features/jobs/api/schemas.ts.

// ---------------------------------------------------------------------------
// ActivityLogEntryDto
// ---------------------------------------------------------------------------

export const ActivityLogEntryApiSchema = z
    .object({
        AtUtc: z.string(),
        ActorName: z.string(),
        ActorObjectId: z.string(),
        Method: z.string(),
        Path: z.string(),
        Resource: z.string(),
        EntityId: z.string().nullable(),
        StatusCode: z.number(),
        RequestBodyJson: z.string().nullable(),
    })
    .transform(camelizeKeys)

// ---------------------------------------------------------------------------
// ActivityLogListResponseDto
// ---------------------------------------------------------------------------

export const ActivityLogListResponseApiSchema = z
    .object({
        Items: z.array(ActivityLogEntryApiSchema),
        Total: z.number(),
        HasMore: z.boolean().nullish(),
    })
    .transform((r) => ({
        items: r.Items,
        total: r.Total,
        hasMore: r.HasMore ?? false,
    }))

// ---------------------------------------------------------------------------
// Inferred types (camelCase, for UI consumption)
// ---------------------------------------------------------------------------

export type ActivityLogEntry = z.infer<typeof ActivityLogEntryApiSchema>
export type ActivityLogListResponse = z.infer<
    typeof ActivityLogListResponseApiSchema
>
