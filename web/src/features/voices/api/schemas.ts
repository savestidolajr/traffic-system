import { z } from 'zod'

// Manual per-field mapping (not camelizeKeys) because Labels is
// Record<string,string> — camelizeKeys would lower-case label keys from the API,
// altering their values. All other fields would be identical under camelizeKeys.
export const ElevenLabsVoiceSchema = z
    .object({
        VoiceId: z.string(),
        Name: z.string(),
        Category: z.string().nullable(),
        Description: z.string().nullable(),
        Labels: z.record(z.string(), z.string()).optional().default({}),
        ModelIds: z.array(z.string()).optional().default([]),
        ResolvedModelId: z.string().optional().default(''),
    })
    .transform((api) => ({
        voiceId: api.VoiceId,
        name: api.Name,
        category: api.Category,
        description: api.Description,
        labels: api.Labels,
        modelIds: api.ModelIds,
        resolvedModelId: api.ResolvedModelId,
    }))

export const VoicesListResponseSchema = z
    .object({
        Voices: z.array(ElevenLabsVoiceSchema),
    })
    .transform((api) => ({
        voices: api.Voices,
    }))
