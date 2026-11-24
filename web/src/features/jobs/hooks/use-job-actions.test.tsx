// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { toast } from 'sonner'
import {
    useJobActions,
    isDeletePreconditionLost,
    DELETE_PRECONDITION_LOST_MESSAGE,
} from './use-job-actions'
import { apiClient, ApiError } from '@/lib/api/client'

vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn().mockResolvedValue({ data: {} }),
        post: vi.fn().mockResolvedValue({ data: {} }),
        delete: vi.fn().mockResolvedValue({ data: {} }),
    },
    ApiError: class ApiError extends Error {
        readonly status: number | undefined
        constructor(message: string, status?: number) {
            super(message)
            this.status = status
        }
    },
}))

afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.clearAllMocks()
})

function renderActions(
    jobId = 'job-x',
    market = 'melbourne',
    etag: string | null = 'etag-1'
) {
    const queryClient = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
    return renderHook(() => useJobActions(jobId, market, etag), { wrapper })
}

describe('useJobActions — delete carries the revision precondition (bug-bash #12)', () => {
    it('sends the job etag as the DELETE body, through the real mutation chain', async () => {
        const { result } = renderActions()
        await act(async () => {
            await result.current.handleDelete()
        })
        expect(apiClient.delete).toHaveBeenCalledWith('/jobs/job-x', {
            data: { expectedEtag: 'etag-1' },
        })
    })

    it('sends no body when no etag is known (the server then rejects with 428)', async () => {
        const { result } = renderActions('job-x', 'melbourne', null)
        await act(async () => {
            await result.current.handleDelete()
        })
        expect(apiClient.delete).toHaveBeenCalledWith('/jobs/job-x', {
            data: undefined,
        })
    })

    it('surfaces a reload message when the delete loses its precondition (409)', async () => {
        const toastError = vi.spyOn(toast, 'error')
        vi.mocked(apiClient.delete).mockRejectedValueOnce(
            new ApiError('conflict', 409)
        )
        const { result } = renderActions()
        await act(async () => {
            await result.current.handleDelete()
        })
        expect(toastError).toHaveBeenCalledWith(
            DELETE_PRECONDITION_LOST_MESSAGE
        )
    })

    it('keeps the 403 not-assigned message for operators outside their market', async () => {
        const toastError = vi.spyOn(toast, 'error')
        vi.mocked(apiClient.delete).mockRejectedValueOnce(
            new ApiError('forbidden', 403)
        )
        const { result } = renderActions('job-x', 'melbourne', 'etag-1')
        await act(async () => {
            await result.current.handleDelete()
        })
        expect(toastError).toHaveBeenCalledWith(
            "You're not assigned to melbourne"
        )
    })
})
describe('useJobActions — fallback delivery carries the reviewed revision', () => {
    it('sends the current job etag with the reject-to-fallback command', async () => {
        const { result } = renderActions()
        await act(async () => {
            await result.current.handleRejectToFallback({
                reasonCode: 'RevisionRequested',
                notes: 'Use the approved standby cover.',
            })
        })
        expect(apiClient.post).toHaveBeenCalledWith(
            '/jobs/job-x/reject-to-fallback',
            {
                reasonCode: 'RevisionRequested',
                notes: 'Use the approved standby cover.',
                expectedEtag: 'etag-1',
            }
        )
    })
})

describe('useJobActions rejection dialog lifecycle', () => {
    it('closes the dialog when a void reject mutation succeeds', async () => {
        const { result } = renderActions()

        act(() => {
            result.current.setIsRejectOpen(true)
        })
        expect(result.current.isRejectOpen).toBe(true)

        await act(async () => {
            await result.current.handleReject({
                reasonCode: 'RevisionRequested',
            })
        })

        expect(result.current.isRejectOpen).toBe(false)
    })

    it('keeps the dialog open when the reject mutation fails', async () => {
        vi.mocked(apiClient.post).mockRejectedValueOnce(
            new ApiError('reject failed', 500)
        )
        const { result } = renderActions()

        act(() => {
            result.current.setIsRejectOpen(true)
        })
        await act(async () => {
            await result.current.handleReject({
                reasonCode: 'RevisionRequested',
            })
        })

        expect(result.current.isRejectOpen).toBe(true)
    })
})

describe('isDeletePreconditionLost', () => {
    it('is true for 409 (lost race) and 428 (missing/stale etag) ApiErrors', () => {
        expect(isDeletePreconditionLost(new ApiError('x', 409))).toBe(true)
        expect(isDeletePreconditionLost(new ApiError('x', 428))).toBe(true)
    })

    it('is false for other failures', () => {
        expect(isDeletePreconditionLost(new ApiError('x', 403))).toBe(false)
        expect(isDeletePreconditionLost(new Error('boom'))).toBe(false)
        expect(isDeletePreconditionLost(null)).toBe(false)
    })
})
