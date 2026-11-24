// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useRejectJob } from './job-hooks'
import { JobSchema } from './schemas'
import type { Job } from '../types'
import { JOB_FIXTURES } from '@/lib/api/mock/fixtures'
import { AsAiredCard } from '../components/review/AsAiredCard'

const detailKey = (id: string) => ['jobs', 'detail', id]

vi.mock('@/lib/api/client', () => ({
    apiClient: { post: vi.fn().mockResolvedValue({ data: {} }), get: vi.fn() },
    ApiError: class ApiError extends Error {},
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

/**
 * A job on its SECOND revise cycle: already rejected once, regenerated, and now
 * back at WAITING_APPROVAL. The backend clears Rejection on regeneration but
 * StatusHistory keeps the earlier REJECTED entry — exactly the shape the
 * optimistic reject then flips to status REJECTED.
 */
function reRejectedCandidate(): Job {
    const base = JOB_FIXTURES.find((f) => f.JobId === 'arn-job-00008')
    if (!base) throw new Error('fixture arn-job-00008 missing')
    const dto = {
        ...base,
        Rejection: null,
        RejectionHistory: null,
        StatusHistory: [
            ...base.StatusHistory,
            {
                Status: 'REJECTED',
                At: base.LastUpdatedAt,
                By: 'operator@arn.com.au',
                Note: '',
            },
        ],
    }
    return JobSchema.parse(dto) as Job
}

describe('optimistic reject — REJECTED variant completeness', () => {
    it('does not install a REJECTED job with no rejection payload', async () => {
        const qc = new QueryClient()
        const prev = reRejectedCandidate()
        qc.setQueryData<Job>(detailKey(prev.jobId), prev)

        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={qc}>{children}</QueryClientProvider>
        )
        const { result } = renderHook(() => useRejectJob(), { wrapper })

        await act(async () => {
            await result.current.mutateAsync({
                jobId: prev.jobId,
                reasonCode: 'RevisionRequested',
                notes: 'Second pass',
            })
        })

        const patched = qc.getQueryData<Job>(detailKey(prev.jobId))
        if (patched?.status === 'REJECTED')
            expect(patched.rejection).toBeDefined()

        // The record card renders the moment the status turns terminal, and it
        // reads the reason under the Rejected history entry.
        expect(() =>
            render(<AsAiredCard job={patched ?? prev} />)
        ).not.toThrow()
    })

    it('renders the record card from the history note when the decision record is missing', () => {
        // Belt to the boundary's braces. JobSchema refuses this payload and the
        // optimistic patch refuses to install it, so reaching the card at all is
        // already a bug — but a bug must not cost the operator the whole
        // workspace, which is what "Cannot read properties of undefined (reading
        // 'reason')" on the error boundary did mid-review.
        const complete = reRejectedCandidate()
        const withoutRecord = {
            ...complete,
            status: 'REJECTED',
            rejection: undefined,
            statusHistory: complete.statusHistory.map((h) =>
                h.status === 'REJECTED'
                    ? { ...h, note: 'Revision requested from review' }
                    : h
            ),
        } as unknown as Job

        const { getByText } = render(<AsAiredCard job={withoutRecord} />)

        expect(getByText(/Revision requested from review/)).toBeInTheDocument()
    })
})
