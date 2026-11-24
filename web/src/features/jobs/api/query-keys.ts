import { createQueryKeys } from '@/lib/api/create-query-keys'
import type { JobFilters } from '../types'

const BASE_KEYS = createQueryKeys('jobs')

/** Shared cache keys for job detail, list, and progress observers. */
export const JOB_QUERY_KEYS = {
    ...BASE_KEYS,
    list: (filters?: JobFilters) =>
        [...BASE_KEYS.lists(), { filters }] as const,
}

/** Progress has a separate document and polling lifecycle from job detail. */
export const JOB_PROGRESS_QUERY_KEYS = {
    detail: (id: string) => [...BASE_KEYS.all, 'progress', id] as const,
}
