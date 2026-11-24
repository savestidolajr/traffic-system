import { z } from 'zod'

// API responses are PascalCase (matching the real backend); transforms map them
// to the camelCase the UI works in — same convention as delivery schemas.

export const RULE_TYPES = ['alias', 'phoneme'] as const
export const PHONEME_ALPHABETS = ['ipa', 'cmu'] as const
export const DICTIONARY_STATUSES = ['active', 'archived'] as const

/**
 * Reach. A `Global` dictionary applies to every station UNDER whatever that station carries: the
 * station layer is sent to ElevenLabs first, so it wins any word both layers define. Operator-facing
 * copy says "platform-wide", never "global".
 */
export const DICTIONARY_SCOPES = ['Station', 'Global'] as const

/** An ElevenLabs pronunciation rule — alias (word→respelling) or phoneme (IPA/CMU). */
export const RuleApiSchema = z
    .object({
        Type: z.enum(RULE_TYPES),
        StringToReplace: z.string(),
        Replacement: z.string(),
        Alphabet: z.string().nullable().default(null),
        CaseSensitive: z.boolean().default(false),
        WordBoundaries: z.boolean().default(true),
        AppliesOnAllModels: z.boolean().default(true),
    })
    .transform((r) => ({
        type: r.Type,
        stringToReplace: r.StringToReplace,
        replacement: r.Replacement,
        alphabet: r.Alphabet,
        caseSensitive: r.CaseSensitive,
        wordBoundaries: r.WordBoundaries,
        appliesOnAllModels: r.AppliesOnAllModels,
    }))

export const DictionaryVersionApiSchema = z
    .object({
        Version: z.number(),
        ElevenLabsVersionId: z.string(),
        Rules: z.array(RuleApiSchema),
        ChangeNote: z.string().nullable().default(null),
        CreatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
    })
    .transform((v) => ({
        version: v.Version,
        elevenLabsVersionId: v.ElevenLabsVersionId,
        rules: v.Rules,
        changeNote: v.ChangeNote,
        createdAt: v.CreatedAt,
        createdBy: v.CreatedBy,
    }))

export const DictionaryApiSchema = z
    .object({
        Id: z.string(),
        DictionaryId: z.string(),
        Name: z.string(),
        Description: z.string().nullable().default(null),
        Status: z.enum(DICTIONARY_STATUSES).default('active'),
        // Absent on rows written before the platform-wide layer existed; those were per station.
        Scope: z.enum(DICTIONARY_SCOPES).nullish(),
        CurrentVersion: z.number(),
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        Etag: z.string().nullable().default(null),
        // Accept either API acronym spelling while clients roll out the ETag field.
        ETag: z.string().nullable().optional(),
        CreatedBy: z.string().nullable().default(null),
        AssignedStations: z.array(z.string()).optional().default([]),
        Rules: z.array(RuleApiSchema),
        Versions: z.array(DictionaryVersionApiSchema),
    })
    .transform((d) => ({
        id: d.Id,
        dictionaryId: d.DictionaryId,
        name: d.Name,
        description: d.Description,
        status: d.Status,
        scope: d.Scope ?? 'Station',
        currentVersion: d.CurrentVersion,
        createdAt: d.CreatedAt,
        updatedAt: d.UpdatedAt,
        etag: d.Etag ?? d.ETag ?? null,
        createdBy: d.CreatedBy,
        assignedStations: d.AssignedStations,
        rules: d.Rules,
        versions: d.Versions,
    }))

export const DictionariesListResponseSchema = z
    .object({
        // Defensive, matching the delivery styles list convention
        Items: z.array(DictionaryApiSchema).nullish(),
        items: z.array(DictionaryApiSchema).nullish(),
        Total: z.number().nullish(),
    })
    .transform((api) => ({
        items: api.Items ?? api.items ?? [],
        total: api.Total ?? 0,
    }))

/**
 * `POST /jobs/{id}/pronunciation-rules` — the cockpit's one-word fix. `Outcome` says what the
 * server's read-modify-write actually did, so the toast can be honest when the rule was already
 * there (the operator's click is then a no-op, not a new version).
 */
export const JobPronunciationRuleResponseSchema = z
    .object({
        Outcome: z.enum(['added', 'changed', 'alreadyCorrect']),
        Dictionary: DictionaryApiSchema,
    })
    .transform((api) => ({
        outcome: api.Outcome,
        dictionary: api.Dictionary,
    }))

/**
 * `POST /pronunciation-dictionaries/preview-rule` — audition ONE candidate rule (saved or
 * not) in a chosen voice. `spokenText` is the exact text that was synthesized (an alias rule
 * reads its replacement verbatim; a phoneme rule carries the inline tag), and `phonemeApplied`
 * is true only when the model actually interpreted a phoneme tag.
 */
export const PronunciationRulePreviewResponseSchema = z
    .object({
        Url: z.string(),
        DurationSeconds: z.number(),
        SpokenText: z.string(),
        PhonemeApplied: z.boolean(),
    })
    .transform((api) => ({
        url: api.Url,
        durationSeconds: api.DurationSeconds,
        spokenText: api.SpokenText,
        phonemeApplied: api.PhonemeApplied,
    }))
