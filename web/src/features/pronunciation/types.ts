import { z } from 'zod'
import {
    RuleApiSchema,
    DictionaryVersionApiSchema,
    DictionaryApiSchema,
    DictionariesListResponseSchema,
    PronunciationRulePreviewResponseSchema,
    JobPronunciationRuleResponseSchema,
} from './api/pronunciation-schemas'

export type Rule = z.infer<typeof RuleApiSchema>
export type DictionaryVersion = z.infer<typeof DictionaryVersionApiSchema>
export type Dictionary = z.infer<typeof DictionaryApiSchema>
export type DictionariesListResponse = z.infer<
    typeof DictionariesListResponseSchema
>
export type PronunciationRulePreviewResponse = z.infer<
    typeof PronunciationRulePreviewResponseSchema
>

/** Result of the review cockpit's one-word fix (`POST /jobs/{id}/pronunciation-rules`). */
export type JobPronunciationRuleResult = z.infer<
    typeof JobPronunciationRuleResponseSchema
>

/** A rule as the operator edits it (camelCase, no server-enforced fields yet). */
export interface RuleInput {
    readonly type: 'alias' | 'phoneme'
    readonly stringToReplace: string
    readonly replacement: string
    readonly alphabet: string | null
    readonly caseSensitive: boolean
    readonly wordBoundaries: boolean
    /**
     * Client-side row identity only — assigned when a rule enters the editor
     * and stripped again at the save boundary, so it never reaches the wire.
     * Gives each editable row a stable React key (see PronunciationRuleTable),
     * so per-row state like an in-flight preview stays on its row when rows
     * above it are deleted. Optional because API-parsed rules (e.g. the
     * duplicate action's `dict.rules`) legitimately lack it.
     */
    readonly _uid?: string
}

/** Reach: per station, or platform-wide under every station's own dictionary. */
export type DictionaryScope = Dictionary['scope']

/** Body for creating a new pronunciation dictionary (mints version 1). */
export interface CreateDictionaryRequest {
    readonly name: string
    readonly description: string | null
    readonly rules: readonly RuleInput[]
    readonly changeNote: string | null
    readonly scope?: DictionaryScope
}

/** Body for updating a pronunciation dictionary — a rule change mints a new version. */
export interface UpdateDictionaryRequest {
    readonly name: string
    readonly description: string | null
    readonly rules: readonly RuleInput[]
    readonly changeNote: string | null
    /** ETag returned by the dictionary GET/list this edit was based on. */
    readonly expectedEtag: string | null
    readonly scope?: DictionaryScope
}

/**
 * Body for auditioning ONE rule (saved or not) in a chosen voice. The rule fields mirror the
 * editor's RuleInput; the backend validates them through the same domain construction a save
 * uses, so an unsaved rule previews exactly like a saved one.
 */
export interface PreviewPronunciationRuleRequest {
    readonly type: 'alias' | 'phoneme'
    readonly stringToReplace: string
    readonly replacement: string
    readonly alphabet: string | null
    /** The voice profile's base voice — the voice id the station actually airs with. */
    readonly baseVoiceId: string
    /** The voice profile's current-version model. */
    readonly model: string
}
