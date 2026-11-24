import { z } from 'zod'
import { createMarketResourceHooks } from './create-market-resource-hooks'
import {
    marketFillerStatements,
    FILLER_STATEMENTS_INVENTORY,
} from '@/lib/api/paths'

// =============================================================================
// Per-market filler statements (BFN-002): short editorial fillers used when
// traffic incidents alone can't reach the target bulletin duration. Separate
// resource from the schedule document — own GET/PUT.
// Backend caps: 50 statements, 500 chars each (mirrored in the studio editor).
// A statement may carry an optional Availability window (station-local date
// range, days of week, time of day); null = always eligible. Dates are
// yyyy-MM-dd, times HH:mm, days .NET day names (Sunday..Saturday) — the exact
// wire strings the backend maps to FillerAvailability.
// =============================================================================

export const FILLER_STATEMENTS_MAX = 50
export const FILLER_STATEMENT_MAX_LENGTH = 500

export interface FillerAvailabilityEntry {
    readonly startDate: string | null
    readonly endDate: string | null
    readonly days: readonly string[] | null
    readonly startLocal: string | null
    readonly endLocal: string | null
}

export interface FillerStatementEntry {
    readonly id: string
    readonly text: string
    readonly enabled: boolean
    /** Null = always eligible (the default — keeps existing rows one-line). */
    readonly availability: FillerAvailabilityEntry | null
}

const FillerAvailabilitySchema = z
    .object({
        StartDate: z.string().nullish(),
        EndDate: z.string().nullish(),
        Days: z.array(z.string()).nullish(),
        StartLocal: z.string().nullish(),
        EndLocal: z.string().nullish(),
    })
    .transform((r): FillerAvailabilityEntry => ({
        startDate: r.StartDate ?? null,
        endDate: r.EndDate ?? null,
        days: r.Days ?? null,
        startLocal: r.StartLocal ?? null,
        endLocal: r.EndLocal ?? null,
    }))

const FillerStatementEntrySchema = z
    .object({
        Id: z.string(),
        Text: z.string(),
        Enabled: z
            .boolean()
            .nullish()
            .transform((v) => v ?? true),
        Availability: FillerAvailabilitySchema.nullish(),
    })
    .transform((r): FillerStatementEntry => ({
        id: r.Id,
        text: r.Text,
        enabled: r.Enabled,
        availability: r.Availability ?? null,
    }))

const FillerStatementsSchema = z
    .object({
        Market: z.string(),
        Statements: z.array(FillerStatementEntrySchema),
    })
    .transform((api) => ({ market: api.Market, statements: api.Statements }))

export type FillerStatements = z.infer<typeof FillerStatementsSchema>

export interface MarketStatements {
    readonly marketId: string
    readonly statements: readonly FillerStatementEntry[]
}

export interface SetFillerStatementsRequest {
    readonly marketId: string
    readonly statements: readonly FillerStatementEntry[]
}

const fillerStatementsHooks = createMarketResourceHooks<
    FillerStatementEntry,
    FillerStatements,
    MarketStatements,
    SetFillerStatementsRequest
>({
    key: 'fillerStatements',
    singlePath: marketFillerStatements,
    inventoryPath: FILLER_STATEMENTS_INVENTORY,
    schema: FillerStatementsSchema,
    marketId: (resource) => resource.market,
    items: (resource) => resource.statements,
    mutationItems: (request) => request.statements,
    toMarketResource: (marketId, statements) => ({ marketId, statements }),
    itemsField: 'statements',
})

export const useFillerStatements = fillerStatementsHooks.useResource
export const useSetFillerStatements = fillerStatementsHooks.useSetResource

/**
 * Aggregate filler statements across every accessible market in ONE request. The backend
 * loops the requested markets server-side and enforces scoping (markets the caller can't
 * access are dropped), replacing the former per-market client fan-out. Returns one entry
 * per requested market, in input order; a market the backend omitted yields empty statements.
 */
export const useAllFillerStatements = fillerStatementsHooks.useAllResources
