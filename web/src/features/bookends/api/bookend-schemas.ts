import { z } from 'zod'

// API responses are PascalCase (matching the real backend); transforms map them
// to the camelCase the UI works in — same convention as delivery-style schemas.

export const BookendVersionApiSchema = z
    .object({
        Version: z.number(),
        IntroText: z.string(),
        OutroText: z.string(),
        ChangeNote: z.string().nullable().default(null),
        CreatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
    })
    .transform((v) => ({
        version: v.Version,
        introText: v.IntroText,
        outroText: v.OutroText,
        changeNote: v.ChangeNote,
        createdAt: v.CreatedAt,
        createdBy: v.CreatedBy,
    }))

export const BOOKEND_STATUSES = ['active', 'archived'] as const

export const BookendApiSchema = z
    .object({
        Id: z.string(),
        Name: z.string(),
        Description: z.string().nullable().default(null),
        Status: z.enum(BOOKEND_STATUSES).default('active'),
        IntroText: z.string(),
        OutroText: z.string(),
        CurrentVersion: z.number(),
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
        AssignedStations: z.array(z.string()).optional().default([]),
        Tokens: z.array(z.string()).optional().default([]),
        Versions: z.array(BookendVersionApiSchema),
    })
    .transform((b) => ({
        id: b.Id,
        name: b.Name,
        description: b.Description,
        status: b.Status,
        introText: b.IntroText,
        outroText: b.OutroText,
        currentVersion: b.CurrentVersion,
        createdAt: b.CreatedAt,
        updatedAt: b.UpdatedAt,
        createdBy: b.CreatedBy,
        assignedStations: b.AssignedStations,
        tokens: b.Tokens,
        versions: b.Versions,
    }))

export const BookendsListResponseSchema = z
    .object({
        // Defensive, matching the jobs/delivery convention: a missing/empty/legacy
        // envelope degrades to an empty library instead of throwing a parse error.
        Items: z.array(BookendApiSchema).nullish(),
        items: z.array(BookendApiSchema).nullish(),
        Total: z.number().nullish(),
    })
    .transform((api) => ({
        bookends: api.Items ?? api.items ?? [],
        total: api.Total ?? 0,
    }))

export const BookendPreviewResponseSchema = z
    .object({
        ResolvedIntro: z.string().default(''),
        ResolvedOutro: z.string().default(''),
        UnresolvedTokens: z.array(z.string()).optional().default([]),
        Values: z.record(z.string(), z.string()).optional().default({}),
    })
    .transform((api) => ({
        resolvedIntro: api.ResolvedIntro,
        resolvedOutro: api.ResolvedOutro,
        unresolvedTokens: api.UnresolvedTokens,
        values: api.Values,
    }))
