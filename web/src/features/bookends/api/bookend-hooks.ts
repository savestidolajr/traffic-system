import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { pascalizeKeys } from '@/lib/api-utils'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    BookendApiSchema,
    BookendsListResponseSchema,
    BookendPreviewResponseSchema,
} from './bookend-schemas'
import {
    BOOKENDS,
    BOOKEND_PREVIEW,
    bookendDetail,
    bookendVersionRestore,
    bookendArchive,
    bookendUnarchive,
} from '@/lib/api/paths'
import type {
    Bookend,
    BookendsListResponse,
    BookendPreviewResponse,
    CreateBookendRequest,
    UpdateBookendRequest,
    PreviewBookendRequest,
} from '../types'

// One query-key scope for the single bookend-pair library.
const BOOKEND_QUERY_KEYS = createQueryKeys('bookends')

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------
const fetchBookends = async (): Promise<BookendsListResponse> => {
    const { data } = await apiClient.get(BOOKENDS)
    return BookendsListResponseSchema.parse(data)
}

const fetchBookend = async (id: string): Promise<Bookend> => {
    const { data } = await apiClient.get(bookendDetail(id))
    return BookendApiSchema.parse(data)
}

const createBookend = async (body: CreateBookendRequest): Promise<Bookend> => {
    const { data } = await apiClient.post(BOOKENDS, pascalizeKeys(body))
    return BookendApiSchema.parse(data)
}

const updateBookend = async ({
    id,
    body,
}: {
    id: string
    body: UpdateBookendRequest
}): Promise<Bookend> => {
    const { data } = await apiClient.put(bookendDetail(id), pascalizeKeys(body))
    return BookendApiSchema.parse(data)
}

const deleteBookend = async (id: string): Promise<void> => {
    await apiClient.delete(bookendDetail(id))
}

const restoreBookendVersion = async ({
    id,
    version,
}: {
    id: string
    version: number
}): Promise<Bookend> => {
    const { data } = await apiClient.post(bookendVersionRestore(id, version))
    return BookendApiSchema.parse(data)
}

const archiveBookend = async (id: string): Promise<Bookend> => {
    const { data } = await apiClient.post(bookendArchive(id))
    return BookendApiSchema.parse(data)
}

const unarchiveBookend = async (id: string): Promise<Bookend> => {
    const { data } = await apiClient.post(bookendUnarchive(id))
    return BookendApiSchema.parse(data)
}

const previewBookend = async (
    body: PreviewBookendRequest
): Promise<BookendPreviewResponse> => {
    const { data } = await apiClient.post(BOOKEND_PREVIEW, pascalizeKeys(body))
    return BookendPreviewResponseSchema.parse(data)
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------
export const useBookends = (options?: { readonly enabled?: boolean }) =>
    useQueryFactory(BOOKEND_QUERY_KEYS.lists(), () => fetchBookends(), {
        staleTime: 1000 * 60,
        enabled: options?.enabled ?? true,
    })

export const useBookend = (id: string | null) =>
    useQueryFactory(
        BOOKEND_QUERY_KEYS.detail(id ?? ''),
        () => fetchBookend(id as string),
        { enabled: Boolean(id) }
    )

export const useCreateBookend = () =>
    useMutationFactory(
        (body: CreateBookendRequest) => createBookend(body),
        () => [BOOKEND_QUERY_KEYS.lists()]
    )

export const useUpdateBookend = () =>
    useMutationFactory(
        ({ id, body }: { id: string; body: UpdateBookendRequest }) =>
            updateBookend({ id, body }),
        (vars) => [
            BOOKEND_QUERY_KEYS.lists(),
            BOOKEND_QUERY_KEYS.detail(vars.id),
        ]
    )

export const useDeleteBookend = () =>
    useMutationFactory(
        (id: string) => deleteBookend(id),
        () => [BOOKEND_QUERY_KEYS.lists()]
    )

export const useRestoreBookendVersion = () =>
    useMutationFactory(
        ({ id, version }: { id: string; version: number }) =>
            restoreBookendVersion({ id, version }),
        (vars) => [
            BOOKEND_QUERY_KEYS.lists(),
            BOOKEND_QUERY_KEYS.detail(vars.id),
        ]
    )

export const useArchiveBookend = () =>
    useMutationFactory(
        (id: string) => archiveBookend(id),
        (id) => [BOOKEND_QUERY_KEYS.lists(), BOOKEND_QUERY_KEYS.detail(id)]
    )

export const useUnarchiveBookend = () =>
    useMutationFactory(
        (id: string) => unarchiveBookend(id),
        (id) => [BOOKEND_QUERY_KEYS.lists(), BOOKEND_QUERY_KEYS.detail(id)]
    )

/** Resolve an unsaved pair against a representative station (debounced by the caller). */
export const useBookendPreview = () =>
    useMutationFactory(
        (body: PreviewBookendRequest) => previewBookend(body),
        () => []
    )
