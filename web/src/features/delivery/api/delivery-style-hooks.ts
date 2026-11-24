import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { pascalizeKeys } from '@/lib/api-utils'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    DeliveryStyleApiSchema,
    DeliveryStylesListResponseSchema,
    DeliveryStylePreviewResponseSchema,
} from './delivery-schemas'
import type {
    DeliveryStyle,
    DeliveryStylesListResponse,
    DeliveryStylePreviewResponse,
    CreateDeliveryStyleRequest,
    UpdateDeliveryStyleRequest,
    PreviewDeliveryStyleRequest,
} from '../types'
import {
    DELIVERY_STYLES,
    DELIVERY_STYLE_PREVIEW,
    deliveryStyleDetail,
    deliveryStyleDelete,
    deliveryStyleArchive,
    deliveryStyleUnarchive,
} from '@/lib/api/paths'

const DELIVERY_STYLE_QUERY_KEYS = createQueryKeys('delivery-styles')

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------
const fetchDeliveryStyles = async (): Promise<DeliveryStylesListResponse> => {
    const { data } = await apiClient.get(DELIVERY_STYLES)
    return DeliveryStylesListResponseSchema.parse(data)
}

const fetchDeliveryStyle = async (id: string): Promise<DeliveryStyle> => {
    const { data } = await apiClient.get(deliveryStyleDetail(id))
    return DeliveryStyleApiSchema.parse(data)
}

const createDeliveryStyle = async (
    body: CreateDeliveryStyleRequest
): Promise<DeliveryStyle> => {
    const { data } = await apiClient.post(DELIVERY_STYLES, pascalizeKeys(body))
    return DeliveryStyleApiSchema.parse(data)
}

const updateDeliveryStyle = async ({
    id,
    body,
}: {
    id: string
    body: UpdateDeliveryStyleRequest
}): Promise<DeliveryStyle> => {
    const { data } = await apiClient.put(
        deliveryStyleDetail(id),
        pascalizeKeys(body)
    )
    return DeliveryStyleApiSchema.parse(data)
}

const deleteDeliveryStyle = async (id: string): Promise<void> => {
    await apiClient.delete(deliveryStyleDelete(id))
}

const archiveDeliveryStyle = async (id: string): Promise<DeliveryStyle> => {
    const { data } = await apiClient.post(deliveryStyleArchive(id))
    return DeliveryStyleApiSchema.parse(data)
}

const unarchiveDeliveryStyle = async (id: string): Promise<DeliveryStyle> => {
    const { data } = await apiClient.post(deliveryStyleUnarchive(id))
    return DeliveryStyleApiSchema.parse(data)
}

const previewDeliveryStyle = async (
    body: PreviewDeliveryStyleRequest
): Promise<DeliveryStylePreviewResponse> => {
    const { data } = await apiClient.post(
        DELIVERY_STYLE_PREVIEW,
        pascalizeKeys(body)
    )
    return DeliveryStylePreviewResponseSchema.parse(data)
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------
export const useDeliveryStyles = (options?: { readonly enabled?: boolean }) =>
    useQueryFactory(DELIVERY_STYLE_QUERY_KEYS.lists(), fetchDeliveryStyles, {
        staleTime: 1000 * 60,
        enabled: options?.enabled ?? true,
    })

export const useDeliveryStyle = (id: string | null) =>
    useQueryFactory(
        DELIVERY_STYLE_QUERY_KEYS.detail(id ?? ''),
        () => fetchDeliveryStyle(id as string),
        { enabled: Boolean(id) }
    )

export const useCreateDeliveryStyle = () =>
    useMutationFactory(createDeliveryStyle, () => [
        DELIVERY_STYLE_QUERY_KEYS.lists(),
    ])

export const useUpdateDeliveryStyle = () =>
    useMutationFactory(updateDeliveryStyle, (vars) => [
        DELIVERY_STYLE_QUERY_KEYS.lists(),
        DELIVERY_STYLE_QUERY_KEYS.detail(vars.id),
    ])

export const usePreviewDeliveryStyle = () =>
    useMutationFactory(previewDeliveryStyle, () => [])

export const useDeleteDeliveryStyle = () =>
    useMutationFactory(deleteDeliveryStyle, () => [
        DELIVERY_STYLE_QUERY_KEYS.lists(),
    ])

export const useArchiveDeliveryStyle = () =>
    useMutationFactory(archiveDeliveryStyle, (id) => [
        DELIVERY_STYLE_QUERY_KEYS.lists(),
        DELIVERY_STYLE_QUERY_KEYS.detail(id),
    ])

export const useUnarchiveDeliveryStyle = () =>
    useMutationFactory(unarchiveDeliveryStyle, (id) => [
        DELIVERY_STYLE_QUERY_KEYS.lists(),
        DELIVERY_STYLE_QUERY_KEYS.detail(id),
    ])
