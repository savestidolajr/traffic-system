import { apiClient } from '../../../lib/api/client'
import { createQueryKeys } from '../../../lib/api/create-query-keys'
import { useQueryFactory } from '../../../lib/api/use-query-factory'
import { useMutationFactory } from '../../../lib/api/use-mutation-factory'
import { SAFETY_TERMS, safetyTermDetail } from '@/lib/api/paths'
import {
    SafetyTermListSchema,
    type SafetyTerm,
    type SafetyKindName,
    type SafetyScopeName,
} from './schemas'

const SAFETY_KEYS = createQueryKeys('safety-terms')
const INVALIDATE = () => [SAFETY_KEYS.all]

const fetchSafetyTerms = async (): Promise<SafetyTerm[]> => {
    const { data } = await apiClient.get(SAFETY_TERMS)
    return SafetyTermListSchema.parse(data)
}

/** All safety terms across scopes (Admin-only at the API). */
export const useSafetyTerms = () =>
    useQueryFactory<SafetyTerm[]>(SAFETY_KEYS.all, fetchSafetyTerms)

export interface UpsertSafetyTermVars {
    id?: string
    kind: SafetyKindName
    scope: SafetyScopeName
    scopeId?: string
    term: string
    note?: string
    enabled?: boolean
}
const upsertSafetyTerm = async (v: UpsertSafetyTermVars): Promise<void> => {
    await apiClient.put(SAFETY_TERMS, {
        id: v.id,
        kind: v.kind,
        scope: v.scope,
        scopeId: v.scopeId,
        term: v.term,
        note: v.note,
        enabled: v.enabled,
    })
}

export const useUpsertSafetyTerm = () =>
    useMutationFactory(upsertSafetyTerm, INVALIDATE)

export interface DeleteSafetyTermVars {
    id: string
    scope: SafetyScopeName
    scopeId: string
}

const deleteSafetyTerm = async (v: DeleteSafetyTermVars): Promise<void> => {
    const params = new URLSearchParams({ scope: v.scope, scopeId: v.scopeId })
    await apiClient.delete(`${safetyTermDetail(v.id)}?${params.toString()}`)
}

export const useDeleteSafetyTerm = () =>
    useMutationFactory(deleteSafetyTerm, INVALIDATE)
