import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { pascalizeKeys } from '@/lib/api-utils'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    DictionaryApiSchema,
    DictionariesListResponseSchema,
    PronunciationRulePreviewResponseSchema,
    JobPronunciationRuleResponseSchema,
} from './pronunciation-schemas'
import type {
    Dictionary,
    DictionariesListResponse,
    PronunciationRulePreviewResponse,
    CreateDictionaryRequest,
    UpdateDictionaryRequest,
    PreviewPronunciationRuleRequest,
    JobPronunciationRuleResult,
} from '../types'
import {
    PRONUNCIATION_DICTIONARIES,
    PRONUNCIATION_RULE_PREVIEW,
    pronunciationDictionaryDetail,
    pronunciationDictionaryArchive,
    pronunciationDictionaryUnarchive,
    pronunciationDictionaryDelete,
    jobPronunciationRules,
} from '@/lib/api/paths'

const PRONUNCIATION_QUERY_KEYS = createQueryKeys('pronunciation-dictionaries')

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------
const fetchDictionaries = async (): Promise<DictionariesListResponse> => {
    const { data } = await apiClient.get(PRONUNCIATION_DICTIONARIES)
    return DictionariesListResponseSchema.parse(data)
}

const fetchDictionary = async (id: string): Promise<Dictionary> => {
    const { data } = await apiClient.get(pronunciationDictionaryDetail(id))
    return DictionaryApiSchema.parse(data)
}

const createDictionary = async (
    body: CreateDictionaryRequest
): Promise<Dictionary> => {
    const { data } = await apiClient.post(
        PRONUNCIATION_DICTIONARIES,
        pascalizeKeys(body)
    )
    return DictionaryApiSchema.parse(data)
}

const updateDictionary = async ({
    id,
    body,
}: {
    id: string
    body: UpdateDictionaryRequest
}): Promise<Dictionary> => {
    // `ExpectedEtag` is part of the whole-list precondition; pascalization keeps
    // it on the wire alongside the replacement rule list.
    const payload = pascalizeKeys(body)
    const { data } = await apiClient.put(
        pronunciationDictionaryDetail(id),
        payload
    )
    return DictionaryApiSchema.parse(data)
}

/**
 * The review cockpit's one-word fix, addressed by JOB rather than by dictionary: the route
 * inherits the job's market guard and replays the edit against the dictionary the bulletin
 * aired with, while the library-wide PUT is Operator as a shared-config write defended by
 * versioning + change note + activity log (like the voice and delivery libraries). The server
 * resolves the dictionary the bulletin aired with and replays the edit against the current
 * rule set, so there is no ETag to carry and no conflict to retry here.
 */
const addJobPronunciationRule = async ({
    jobId,
    word,
    replacement,
}: {
    jobId: string
    word: string
    replacement: string
}): Promise<JobPronunciationRuleResult> => {
    const { data } = await apiClient.post(
        jobPronunciationRules(jobId),
        pascalizeKeys({ word, replacement })
    )
    return JobPronunciationRuleResponseSchema.parse(data)
}

const deleteDictionary = async (id: string): Promise<void> => {
    await apiClient.delete(pronunciationDictionaryDelete(id))
}

const archiveDictionary = async (id: string): Promise<Dictionary> => {
    const { data } = await apiClient.post(pronunciationDictionaryArchive(id))
    return DictionaryApiSchema.parse(data)
}

const unarchiveDictionary = async (id: string): Promise<Dictionary> => {
    const { data } = await apiClient.post(pronunciationDictionaryUnarchive(id))
    return DictionaryApiSchema.parse(data)
}

/**
 * Auditions ONE candidate rule (saved or not) in the chosen voice. The route carries no
 * dictionary id — the backend validates the candidate through the same domain construction a
 * save uses, so this works on rules the operator has just typed.
 */
const previewPronunciationRule = async (
    body: PreviewPronunciationRuleRequest
): Promise<PronunciationRulePreviewResponse> => {
    const { data } = await apiClient.post(
        PRONUNCIATION_RULE_PREVIEW,
        pascalizeKeys(body)
    )
    return PronunciationRulePreviewResponseSchema.parse(data)
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------
export const usePronunciationDictionaries = (options?: {
    readonly enabled?: boolean
}) =>
    useQueryFactory(PRONUNCIATION_QUERY_KEYS.lists(), fetchDictionaries, {
        staleTime: 1000 * 60,
        enabled: options?.enabled ?? true,
    })

export const usePronunciationDictionary = (id: string | null) =>
    useQueryFactory(
        PRONUNCIATION_QUERY_KEYS.detail(id ?? ''),
        () => fetchDictionary(id as string),
        { enabled: Boolean(id) }
    )

export const useCreatePronunciationDictionary = () =>
    useMutationFactory(createDictionary, () => [
        PRONUNCIATION_QUERY_KEYS.lists(),
    ])

export const useUpdatePronunciationDictionary = () =>
    useMutationFactory(updateDictionary, (vars) => [
        PRONUNCIATION_QUERY_KEYS.lists(),
        PRONUNCIATION_QUERY_KEYS.detail(vars.id),
    ])

/**
 * Invalidates the LIST only: the caller identifies a job, not a dictionary, and the cockpit reads
 * the aired dictionary out of the list (matching on the provider id the bulletin recorded).
 */
export const useAddJobPronunciationRule = () =>
    useMutationFactory(addJobPronunciationRule, () => [
        PRONUNCIATION_QUERY_KEYS.lists(),
    ])

export const usePreviewPronunciationRule = () =>
    useMutationFactory(previewPronunciationRule, () => [])

export const useDeletePronunciationDictionary = () =>
    useMutationFactory(deleteDictionary, () => [
        PRONUNCIATION_QUERY_KEYS.lists(),
    ])

export const useArchivePronunciationDictionary = () =>
    useMutationFactory(archiveDictionary, (id) => [
        PRONUNCIATION_QUERY_KEYS.lists(),
        PRONUNCIATION_QUERY_KEYS.detail(id),
    ])

export const useUnarchivePronunciationDictionary = () =>
    useMutationFactory(unarchiveDictionary, (id) => [
        PRONUNCIATION_QUERY_KEYS.lists(),
        PRONUNCIATION_QUERY_KEYS.detail(id),
    ])
