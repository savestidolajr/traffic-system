import { z } from 'zod'
import { camelizeKeys } from '@/lib/api-utils'

// Mirrors PlaybookSection in Domain. A section missing here fails the whole payload, not just its
// own block, so the studio goes blank rather than degrading: keep this in step with the enum.
const PlaybookSectionSchema = z.enum([
    'role',
    'material',
    'selection',
    'voice',
    'length',
    'output',
])

const PlaybookSectionApiSchema = z
    .string()
    .transform((value) => value.toLowerCase())
    .pipe(PlaybookSectionSchema)

export const PlaybookBlockApiSchema = z
    .object({
        Id: z.string(),
        Section: PlaybookSectionApiSchema,
        Body: z.string(),
        Order: z.number(),
        Locked: z.boolean().default(false),
        Enabled: z.boolean().default(true),
    })
    .transform(camelizeKeys)

export const PlaybookOverlayApiSchema = z
    .object({
        Rewrite: z.record(z.string(), z.string()).optional().default({}),
        Disable: z.array(z.string()).optional().default([]),
        Reorder: z.record(z.string(), z.number()).optional().default({}),
        Add: z.array(PlaybookBlockApiSchema).optional().default([]),
    })
    .transform(camelizeKeys)

export const BulletinPlaybookVersionApiSchema = z
    .object({
        Version: z.number(),
        Overlay: PlaybookOverlayApiSchema,
        ChangeNote: z.string().nullable().default(null),
        CreatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
    })
    .transform(camelizeKeys)

export const BULLETIN_PLAYBOOK_STATUSES = ['active', 'archived'] as const

export const BulletinPlaybookApiSchema = z
    .object({
        Id: z.string(),
        Name: z.string(),
        Description: z.string().nullable().default(null),
        Role: z.string(),
        Status: z.enum(BULLETIN_PLAYBOOK_STATUSES).default('active'),
        CurrentVersion: z.number(),
        CurrentOverlay: PlaybookOverlayApiSchema,
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
        AssignedStations: z.array(z.string()).optional().default([]),
        Versions: z.array(BulletinPlaybookVersionApiSchema).default([]),
    })
    .transform(camelizeKeys)

export const BulletinPlaybooksListResponseSchema = z
    .object({
        Items: z.array(BulletinPlaybookApiSchema).nullish(),
        items: z.array(BulletinPlaybookApiSchema).nullish(),
        Total: z.number().nullish(),
    })
    .transform((api) => ({
        playbooks: api.Items ?? api.items ?? [],
        total: api.Total ?? 0,
    }))

export const DefaultPlaybookResponseSchema = z
    .object({
        Role: z.string(),
        DefaultAvailable: z.boolean().default(true),
        Blocks: z.array(PlaybookBlockApiSchema).optional().default([]),
    })
    .transform(camelizeKeys)
export const PlaybookPreviewApiSchema = z
    .object({
        Script: z.string(),
        BaselineScript: z.string().nullable().default(null),
        Refused: z.array(z.string()).default([]),
        Notes: z.array(z.string()).default([]),
        PredictedSeconds: z.number().nullable().default(null),
    })
    .transform((api) => ({
        script: api.Script,
        baselineScript: api.BaselineScript,
        refused: api.Refused,
        notes: api.Notes,
        predictedSeconds: api.PredictedSeconds,
    }))
