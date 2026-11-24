import { z } from 'zod'

// API responses are PascalCase (matching the real backend); transforms map them
// to the camelCase the UI works in — same convention as voice-profile schemas.

export const DeliveryStylePolicyApiSchema = z
    .object({
        ToneDescription: z.string().default(''),
        AllowedTags: z.array(z.string()).optional().default([]),
        Intensity: z.number().default(0),
    })
    .transform((p) => ({
        toneDescription: p.ToneDescription,
        allowedTags: p.AllowedTags,
        intensity: p.Intensity,
    }))

export const DeliveryStyleVersionApiSchema = z
    .object({
        Version: z.number(),
        Policy: DeliveryStylePolicyApiSchema,
        ChangeNote: z.string().nullable().default(null),
        CreatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
    })
    .transform((v) => ({
        version: v.Version,
        policy: v.Policy,
        changeNote: v.ChangeNote,
        createdAt: v.CreatedAt,
        createdBy: v.CreatedBy,
    }))

export const DELIVERY_STYLE_STATUSES = ['active', 'archived'] as const

export const DeliveryStyleApiSchema = z
    .object({
        Id: z.string(),
        Name: z.string(),
        Description: z.string().nullable().default(null),
        Status: z.enum(DELIVERY_STYLE_STATUSES).default('active'),
        CurrentVersion: z.number(),
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
        AssignedStations: z.array(z.string()).optional().default([]),
        Versions: z.array(DeliveryStyleVersionApiSchema),
    })
    .transform((s) => ({
        id: s.Id,
        name: s.Name,
        description: s.Description,
        status: s.Status,
        currentVersion: s.CurrentVersion,
        createdAt: s.CreatedAt,
        updatedAt: s.UpdatedAt,
        createdBy: s.CreatedBy,
        assignedStations: s.AssignedStations,
        versions: s.Versions,
    }))

export const DeliveryStylesListResponseSchema = z
    .object({
        // Defensive, matching the jobs list convention: a missing/empty/legacy
        // envelope degrades to an empty library instead of throwing a parse error.
        Items: z.array(DeliveryStyleApiSchema).nullish(),
        items: z.array(DeliveryStyleApiSchema).nullish(),
        Total: z.number().nullish(),
    })
    .transform((api) => ({
        styles: api.Items ?? api.items ?? [],
        total: api.Total ?? 0,
    }))

export const DeliveryStylePreviewResponseSchema = z
    .object({
        Url: z.string(),
        EstimatedDurationSec: z.number(),
        TagCount: z.number(),
        TagsSupported: z.boolean(),
    })
    .transform((api) => ({
        url: api.Url,
        estimatedDurationSec: api.EstimatedDurationSec,
        tagCount: api.TagCount,
        tagsSupported: api.TagsSupported,
    }))
