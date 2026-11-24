import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { VoicesListResponseSchema } from './schemas'
import type { VoicesListResponse } from '../types'
import { VOICES } from '@/lib/api/paths'

const VOICE_QUERY_KEYS = createQueryKeys('voices')

// API CALLS
const fetchVoices = async (): Promise<VoicesListResponse> => {
    const { data } = await apiClient.get(VOICES)
    return VoicesListResponseSchema.parse(data)
}

export const useVoices = () => {
    return useQueryFactory(VOICE_QUERY_KEYS.lists(), fetchVoices, {
        staleTime: 1000 * 60 * 5,
    })
}
