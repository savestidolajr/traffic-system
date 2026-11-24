import { z } from 'zod'
import { camelizeKeys } from '@/lib/api-utils'

// =============================================================================
// Operator directory + market-scoping schemas
// PascalCase API → camelCase frontend, consistent with the other features.
// =============================================================================

/**
 * The calling operator's own scope. `scoped` false = unrestricted admin.
 * A non-admin without an assignment is represented by `markets: []`.
 */
export const OperatorScopeSchema = z
    .object({
        Scoped: z.boolean(),
        Markets: z.array(z.string()).nullish(),
    })
    .transform((api) => ({
        scoped: api.Scoped,
        markets: api.Markets ?? null,
    }))

export type OperatorScope = z.infer<typeof OperatorScopeSchema>

/** A persisted market assignment returned by mutation/me endpoints. */
export const OperatorAssignmentSchema = z
    .object({
        ObjectId: z.string(),
        DisplayName: z.string(),
        Markets: z.array(z.string()),
        UpdatedAt: z.string(),
        UpdatedBy: z.string(),
    })
    .transform(camelizeKeys)

export type OperatorAssignment = z.infer<typeof OperatorAssignmentSchema>

/**
 * A live Entra role holder merged with its optional persisted market
 * assignment. Directory entries with no assignment have an empty `markets`
 * list; admins are unrestricted regardless of that list.
 */
export const OperatorDirectoryEntrySchema = z
    .object({
        ObjectId: z.string(),
        DisplayName: z.string(),
        UserPrincipalName: z.string().nullable(),
        Roles: z.array(z.string()),
        Source: z.string(),
        Markets: z.array(z.string()),
        IsAdmin: z.boolean(),
        UpdatedAtUtc: z.string().nullable(),
        UpdatedBy: z.string().nullable(),
    })
    .transform(camelizeKeys)

export type OperatorDirectoryEntry = z.infer<
    typeof OperatorDirectoryEntrySchema
>

export const OperatorDirectorySchema = z
    .object({
        Operators: z.array(OperatorDirectoryEntrySchema),
        DirectoryStatus: z.enum(['Ok', 'Unavailable']),
        DirectoryError: z.string().nullable(),
    })
    .transform((api) => ({
        operators: api.Operators,
        directoryStatus: api.DirectoryStatus,
        directoryError: api.DirectoryError,
    }))

export type OperatorDirectory = z.infer<typeof OperatorDirectorySchema>
