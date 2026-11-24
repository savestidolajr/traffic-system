import { z } from 'zod'

// API responses are PascalCase (matching the real backend); transforms map them
// to the camelCase the UI works in -- same convention as delivery-style schemas.

// ---------------------------------------------------------------------------
// ConfigImportItemDto
// ---------------------------------------------------------------------------

const ConfigImportItemShape = z.object({
    Section: z.string(),
    Key: z.string(),
    Name: z.string().nullable().default(null),
    Action: z.enum(['Create', 'Update', 'Unchanged', 'Error']),
    Detail: z.string().nullable().default(null),
    Outcome: z.enum(['Applied', 'Failed']).nullable().default(null),
})

type ConfigImportItemApi = z.infer<typeof ConfigImportItemShape>

const toConfigImportItem = (item: ConfigImportItemApi) => ({
    section: item.Section,
    key: item.Key,
    name: item.Name,
    action: item.Action,
    detail: item.Detail,
    outcome: item.Outcome,
})

export const ConfigImportItemApiSchema =
    ConfigImportItemShape.transform(toConfigImportItem)

// ---------------------------------------------------------------------------
// ConfigImportReportDto
// ---------------------------------------------------------------------------

const ConfigImportReportShape = z.object({
    DryRun: z.boolean(),
    RequiresApproval: z.boolean(),
    Applied: z.boolean(),
    Creates: z.number(),
    Updates: z.number(),
    Unchanged: z.number(),
    Errors: z.number(),
    Items: z.array(ConfigImportItemShape),
})

type ConfigImportReportApi = z.infer<typeof ConfigImportReportShape>

const toConfigImportReport = (report: ConfigImportReportApi) => ({
    dryRun: report.DryRun,
    requiresApproval: report.RequiresApproval,
    applied: report.Applied,
    creates: report.Creates,
    updates: report.Updates,
    unchanged: report.Unchanged,
    errors: report.Errors,
    items: report.Items.map(toConfigImportItem),
})

export const ConfigImportReportApiSchema =
    ConfigImportReportShape.transform(toConfigImportReport)

// ---------------------------------------------------------------------------
// ConfigImportAcceptedDto (202 -- parked for approval)
// ---------------------------------------------------------------------------

export const ConfigImportAcceptedApiSchema = z
    .object({
        PendingImportId: z.string(),
        Report: ConfigImportReportShape,
    })
    .transform((r) => ({
        pendingImportId: r.PendingImportId,
        report: toConfigImportReport(r.Report),
    }))

// ---------------------------------------------------------------------------
// PendingConfigImportDto
// ---------------------------------------------------------------------------

export const PendingConfigImportApiSchema = z
    .object({
        Id: z.string(),
        RequestedBy: z.string(),
        RequestedAtUtc: z.string(),
        Report: ConfigImportReportShape.nullable(),
    })
    .transform((r) => ({
        id: r.Id,
        requestedBy: r.RequestedBy,
        requestedAtUtc: r.RequestedAtUtc,
        report: r.Report === null ? null : toConfigImportReport(r.Report),
    }))

// ---------------------------------------------------------------------------
// PendingConfigImportsListDto
// ---------------------------------------------------------------------------
export const PendingConfigImportsListApiSchema = z
    .object({
        Items: z.array(PendingConfigImportApiSchema),
        Total: z.number(),
    })
    .transform((r) => ({
        items: r.Items,
        total: r.Total,
    }))

// ---------------------------------------------------------------------------
// Inferred types (camelCase, for UI consumption)
// ---------------------------------------------------------------------------

export type ConfigImportItem = z.infer<typeof ConfigImportItemApiSchema>
export type ConfigImportReport = z.infer<typeof ConfigImportReportApiSchema>
export type ConfigImportAccepted = z.infer<typeof ConfigImportAcceptedApiSchema>
export type PendingConfigImport = z.infer<typeof PendingConfigImportApiSchema>
export type PendingConfigImportsList = z.infer<
    typeof PendingConfigImportsListApiSchema
>
