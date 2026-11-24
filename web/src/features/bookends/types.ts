import { z } from 'zod'
import {
    BookendApiSchema,
    BookendVersionApiSchema,
    BookendsListResponseSchema,
    BookendPreviewResponseSchema,
} from './api/bookend-schemas'

export type Bookend = z.infer<typeof BookendApiSchema>
export type BookendVersion = z.infer<typeof BookendVersionApiSchema>
export type BookendsListResponse = z.infer<typeof BookendsListResponseSchema>
export type BookendPreviewResponse = z.infer<
    typeof BookendPreviewResponseSchema
>

// ---------------------------------------------------------------------------
// Hand-written request bodies.
//
// NOTE: these MUST stay in sync with the PascalCase zod response schemas in
// api/bookend-schemas.ts (same drift hazard as the delivery slice): request
// bodies are sent pascalized via pascalizeKeys, responses are parsed by the
// zod schemas. Change a field here and there together.
// ---------------------------------------------------------------------------

/** Body for creating a new bookend pair (mints version 1). */
export interface CreateBookendRequest {
    readonly name: string
    readonly description: string | null
    readonly introText: string
    readonly outroText: string
    readonly changeNote: string | null
}

/** Body for updating a bookend pair — a text change mints a new version. */
export interface UpdateBookendRequest {
    readonly name: string
    readonly description: string | null
    readonly introText: string
    readonly outroText: string
    readonly changeNote: string | null
}

/** Body for resolving an unsaved pair against a representative station. */
export interface PreviewBookendRequest {
    readonly introText?: string
    readonly outroText?: string
    readonly marketId?: string
    readonly station?: string
    readonly tokens?: Record<string, string>
}
