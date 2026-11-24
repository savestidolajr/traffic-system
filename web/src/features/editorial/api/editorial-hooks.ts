import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { pascalizeKeys } from '@/lib/api-utils'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    EditorialSettingsApiSchema,
    EffectiveEditorialSettingsApiSchema,
} from './editorial-schemas'
import type {
    EditorialScope,
    EditorialSettings,
    EffectiveEditorialSettings,
    UpsertEditorialSettingsRequest,
} from '../types'
import {
    EDITORIAL_SETTINGS,
    EDITORIAL_SETTINGS_EFFECTIVE,
} from '@/lib/api/paths'

const EDITORIAL_QUERY_KEYS = createQueryKeys('editorial')

/** Scope is always sent explicitly: a missing one must never fall back to network-wide. */
const scopeParams = (scope: EditorialScope, scopeId: string) =>
    scope === 'Global' ? { scope } : { scope, scopeId }

const fetchEditorialSettings = async (
    scope: EditorialScope,
    scopeId: string
): Promise<EditorialSettings> => {
    const { data } = await apiClient.get(EDITORIAL_SETTINGS, {
        params: scopeParams(scope, scopeId),
    })
    return EditorialSettingsApiSchema.parse(data)
}

const fetchEffectiveEditorialSettings = async (
    market: string,
    station: string | null
): Promise<EffectiveEditorialSettings> => {
    const { data } = await apiClient.get(EDITORIAL_SETTINGS_EFFECTIVE, {
        params: station ? { market, station } : { market },
    })
    return EffectiveEditorialSettingsApiSchema.parse(data)
}

const upsertEditorialSettings = async (
    body: UpsertEditorialSettingsRequest
): Promise<EditorialSettings> => {
    const { data } = await apiClient.put(
        EDITORIAL_SETTINGS,
        pascalizeKeys(body)
    )
    return EditorialSettingsApiSchema.parse(data)
}

const deleteEditorialSettings = async (input: {
    readonly scope: EditorialScope
    readonly scopeId: string
}): Promise<void> => {
    await apiClient.delete(EDITORIAL_SETTINGS, {
        params: scopeParams(input.scope, input.scopeId),
    })
}

export const useEditorialSettings = (scope: EditorialScope, scopeId: string) =>
    useQueryFactory(
        [...EDITORIAL_QUERY_KEYS.all, 'settings', scope, scopeId],
        () => fetchEditorialSettings(scope, scopeId),
        { staleTime: 1000 * 30 }
    )

export const useEffectiveEditorialSettings = (
    market: string,
    station: string | null
) =>
    useQueryFactory(
        [
            ...EDITORIAL_QUERY_KEYS.all,
            'settings',
            'effective',
            market,
            station ?? '',
        ],
        () => fetchEffectiveEditorialSettings(market, station),
        { enabled: market.length > 0, staleTime: 1000 * 30 }
    )

export const useUpsertEditorialSettings = () =>
    useMutationFactory(upsertEditorialSettings, () => [
        EDITORIAL_QUERY_KEYS.all,
    ])

export const useDeleteEditorialSettings = () =>
    useMutationFactory(deleteEditorialSettings, () => [
        EDITORIAL_QUERY_KEYS.all,
    ])
