import { z } from 'zod'
import { createMarketResourceHooks } from '@/features/schedules/api/create-market-resource-hooks'
import {
    marketVerifiedPronunciations,
    VERIFIED_PRONUNCIATIONS_INVENTORY,
} from '@/lib/api/paths'

// =============================================================================
// Per-market verified pronunciations: words an operator listened to and
// confirmed air correctly, so the audio pre-review stops flagging them on every
// bulletin.
//
// This is a PRESENTATION LENS, not a generation input. The backend keeps
// flagging everything and `job.preReview.suspectWords` keeps the raw list, so
// un-verifying a word re-exposes it on every bulletin ever generated rather
// than only on future ones. See the 2026-07-28 decision.
//
// Backend caps: 200 words, 80 chars each, 200-char notes.
// =============================================================================

export const VERIFIED_PRONUNCIATIONS_MAX = 200
export const VERIFIED_PRONUNCIATION_MAX_LENGTH = 80
export const VERIFIED_PRONUNCIATION_NOTE_MAX_LENGTH = 200

export interface VerifiedPronunciationEntry {
    readonly word: string
    readonly note: string | null
    readonly enabled: boolean
    readonly verifiedBy: string | null
    /** ISO 8601, or null on a legacy row written before provenance was stamped. */
    readonly verifiedAt: string | null
}

const VerifiedPronunciationEntrySchema = z
    .object({
        Word: z.string(),
        Note: z.string().nullish(),
        Enabled: z
            .boolean()
            .nullish()
            .transform((v) => v ?? true),
        VerifiedBy: z.string().nullish(),
        VerifiedAt: z.string().nullish(),
    })
    .transform((r): VerifiedPronunciationEntry => ({
        word: r.Word,
        note: r.Note ?? null,
        enabled: r.Enabled,
        verifiedBy: r.VerifiedBy ?? null,
        verifiedAt: r.VerifiedAt ?? null,
    }))

const VerifiedPronunciationsSchema = z
    .object({
        Market: z.string(),
        Words: z.array(VerifiedPronunciationEntrySchema),
    })
    .transform((api) => ({ market: api.Market, words: api.Words }))

export type VerifiedPronunciations = z.infer<
    typeof VerifiedPronunciationsSchema
>

export interface MarketVerifiedPronunciations {
    readonly marketId: string
    readonly words: readonly VerifiedPronunciationEntry[]
}

export interface SetVerifiedPronunciationsRequest {
    readonly marketId: string
    readonly words: readonly VerifiedPronunciationEntry[]
}

const hooks = createMarketResourceHooks<
    VerifiedPronunciationEntry,
    VerifiedPronunciations,
    MarketVerifiedPronunciations,
    SetVerifiedPronunciationsRequest
>({
    key: 'verifiedPronunciations',
    singlePath: marketVerifiedPronunciations,
    inventoryPath: VERIFIED_PRONUNCIATIONS_INVENTORY,
    schema: VerifiedPronunciationsSchema,
    marketId: (resource) => resource.market,
    items: (resource) => resource.words,
    mutationItems: (request) => request.words,
    toMarketResource: (marketId, words) => ({ marketId, words }),
    itemsField: 'words',
})

export const useVerifiedPronunciations = hooks.useResource
export const useSetVerifiedPronunciations = hooks.useSetResource

/** Verified pronunciations across every accessible market in ONE request. */
export const useAllVerifiedPronunciations = hooks.useAllResources

/**
 * Case- and punctuation-insensitive match key. MUST stay byte-identical to the
 * backend's `VerifiedPronunciation.NormalizeWord` — the two sides independently
 * key the same verdict, so any drift means a verified word keeps flagging.
 * Suspect words routinely carry possessives ("Gate's", "Unleaded's").
 */
export function pronunciationKey(word: string): string {
    return word.replace(/[^\p{L}\p{N}]+/gu, '').toLowerCase()
}
