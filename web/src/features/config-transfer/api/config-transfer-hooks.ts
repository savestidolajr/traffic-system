import { ApiError, apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    ConfigImportReportApiSchema,
    ConfigImportAcceptedApiSchema,
    PendingConfigImportsListApiSchema,
} from './config-transfer-schemas'
import type {
    ConfigImportReport,
    ConfigImportAccepted,
    PendingConfigImportsList,
} from './config-transfer-schemas'
import {
    CONFIG_EXPORT,
    CONFIG_SCHEMA,
    CONFIG_IMPORT,
    CONFIG_IMPORTS_PENDING,
} from '@/lib/api/paths'
import { useMemo } from 'react'
import { getProp, isRecord } from '@/lib/json'
import { SECTIONS } from '../lib/section-meta'

const CONFIG_TRANSFER_KEYS = createQueryKeys('config-transfer')

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------

/**
 * Fetch the full config bundle as raw JSON (for download).
 * The bundle itself is not modeled in zod -- it's opaque JSON.
 */
export const fetchExportConfig = async (
    sections?: string[]
): Promise<unknown> => {
    const params = sections?.length ? `?sections=${sections.join(',')}` : ''
    const { data } = await apiClient.get(`${CONFIG_EXPORT}${params}`)
    return data
}

/** Fetch the JSON Schema for the config bundle. */
export const fetchConfigSchema = async (): Promise<unknown> => {
    const { data } = await apiClient.get(CONFIG_SCHEMA)
    return data
}

/** POST import bundle for dry-run or apply. */
export const postImportConfig = async (args: {
    bundle: unknown
    dryRun: boolean
}): Promise<ConfigImportReport | ConfigImportAccepted> => {
    const params = args.dryRun ? '?dryRun=true' : '?dryRun=false'
    const { data } = await apiClient.post(
        `${CONFIG_IMPORT}${params}`,
        args.bundle as unknown as Record<string, unknown>
    )
    // The backend returns 200 (ConfigImportReportDto) or 202 (ConfigImportAcceptedDto)
    // depending on approval settings. We distinguish by checking for PendingImportId.
    if (data && typeof data === 'object' && 'PendingImportId' in data) {
        return ConfigImportAcceptedApiSchema.parse(data)
    }
    return ConfigImportReportApiSchema.parse(data)
}

/** Fetch the pending imports list. */
export const fetchPendingImports = async (
    signal?: AbortSignal
): Promise<PendingConfigImportsList> => {
    const { data } = await apiClient.get(CONFIG_IMPORTS_PENDING, { signal })
    return PendingConfigImportsListApiSchema.parse(data)
}

/** Approve a pending import. */
export const approveImport = async (
    id: string
): Promise<ConfigImportReport> => {
    const { data } = await apiClient.post(
        `/config/imports/pending/${id}/approve`
    )
    return ConfigImportReportApiSchema.parse(data)
}

/** Reject a pending import. */
export const rejectImport = async (id: string): Promise<void> => {
    await apiClient.post(`/config/imports/pending/${id}/reject`)
}

/**
 * Runs an import request and normalizes both failure shapes: a 400
 * (validation failed — nothing applied) carries the exact same
 * `ConfigImportReportDto` shape as a 200/202 success, so the caller always
 * ends up with a report to render, and a real network/unexpected error is
 * kept distinct from "the bundle didn't validate".
 */
export type ImportOutcome =
    | { ok: true; result: ConfigImportReport | ConfigImportAccepted }
    | { ok: false; report: ConfigImportReport }
    | { ok: false; error: unknown }

export const tryImportConfig = async (args: {
    bundle: unknown
    dryRun: boolean
}): Promise<ImportOutcome> => {
    try {
        return { ok: true, result: await postImportConfig(args) }
    } catch (err) {
        // A 400 from this endpoint is not a bare failure: the body IS the dry-run
        // report, carrying one Error row per offending entity. This used to read
        // `err.response`, which the client's interceptor has already stripped —
        // so an invalid bundle showed a single toast and the operator never saw
        // WHICH entities failed or why.
        if (err instanceof ApiError && err.status === 400) {
            const parsed = ConfigImportReportApiSchema.safeParse(err.body)
            if (parsed.success) return { ok: false, report: parsed.data }
        }
        return { ok: false, error: err }
    }
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

export const usePendingImports = () =>
    useQueryFactory(CONFIG_TRANSFER_KEYS.lists(), fetchPendingImports, {
        staleTime: 1000 * 10,
        refetchInterval: 1000 * 30,
    })

/**
 * Full current bundle, for the export section picker's live counts and as
 * the "before" side of the fresh-import diff review.
 */
export const useConfigExportPreview = () =>
    useQueryFactory(
        CONFIG_TRANSFER_KEYS.detail('export-preview'),
        () => fetchExportConfig(),
        {
            staleTime: 1000 * 30,
        }
    )

/**
 * The sections this SERVER actually offers, read from the published JSON
 * Schema (`GET /config/schema`) rather than from a list compiled into the SPA.
 *
 * `ConfigSections.All` has grown before, and every time it did the picker kept
 * exporting the old set: a bundle downloaded from the UI silently omitted the
 * new section, so "download the whole config" was not a whole config. Reading
 * the contract makes a new backend section appear here with no frontend
 * release — `sectionLabel`/`sectionIcon` humanise anything this build cannot
 * name. Falls back to the compiled list when the schema cannot be read, so the
 * picker is never empty.
 *
 * Array-typed properties are the sections; the four scalars (`Schema`,
 * `SchemaVersion`, `ExportedAtUtc`, `Source`) are bundle metadata.
 */
export const useConfigSections = () => {
    const query = useQueryFactory(
        CONFIG_TRANSFER_KEYS.detail('schema-sections'),
        fetchConfigSchema,
        { staleTime: Infinity }
    )
    const sections = useMemo(
        () => sectionsFromSchema(query.data) ?? SECTIONS,
        [query.data]
    )
    return { ...query, sections }
}

/** Array-typed top-level schema properties, in declaration order. */
export function sectionsFromSchema(schema: unknown): string[] | null {
    if (!isRecord(schema)) return null
    const properties = getProp(schema, 'properties')
    if (!isRecord(properties)) return null
    const sections = Object.entries(properties)
        .filter(([, spec]) => {
            if (!isRecord(spec)) return false
            const type = spec.type
            return Array.isArray(type)
                ? type.includes('array')
                : type === 'array'
        })
        .map(([name]) => name)
    return sections.length > 0 ? sections : null
}

/**
 * Query-key roots of every feature an approved bundle rewrites — one per
 * BACKEND section (`ConfigSections.All`, twelve of them), plus `voices` (the
 * provider voices a profile resolves against). NOT one per `SECTION_LABELS`
 * entry: the picker can export a partial bundle, so keying this list off the
 * picker would leave the caches of the omitted sections stale after an
 * approval. The audit log is invalidated centrally by `useMutationFactory`,
 * so it is deliberately absent here.
 *
 * Approving an import applies the bundle server-side, so leaving these cached
 * showed the operator the PRE-import configuration on every other page: the
 * voice/delivery/pronunciation/bookend lists hold a 60s staleTime and no
 * refetch interval, so a station could be re-pointed at a new voice profile
 * while the Voices page still listed the old set.
 *
 * `VoiceCalibrations` has no query of its own — the speech-rate table is read
 * server-side during generation — so it has no cache to invalidate.
 */
const APPLIED_SCOPES: readonly (readonly unknown[])[] = [
    ['voice-profiles'],
    ['voices'],
    ['delivery-styles'],
    ['pronunciation-dictionaries'],
    ['verifiedPronunciations'],
    ['safety-terms'],
    ['guidance'],
    ['priorityRoads'],
    ['fillerStatements'],
    ['bookends'],
    ['operators'],
    ['schedules'],
]

export const useApproveImport = () =>
    useMutationFactory(
        (id: string) => approveImport(id),
        () => [CONFIG_TRANSFER_KEYS.lists(), ...APPLIED_SCOPES]
    )

export const useRejectImport = () =>
    // A rejected bundle is never applied — only the queue changes.
    useMutationFactory(
        (id: string) => rejectImport(id),
        () => [CONFIG_TRANSFER_KEYS.lists()]
    )
