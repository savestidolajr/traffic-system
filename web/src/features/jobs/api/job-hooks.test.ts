import { describe, it, expect } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import type { Query, QueryKey } from '@tanstack/react-query'
import type { Job } from '../types'
import { JOB_PROGRESS_QUERY_KEYS } from './query-keys'
import { POLL_ACTIVE_MS, POLL_IDLE_MS } from '@/lib/constants/timing'
import { jobDetailRefetchInterval } from './job-hooks'

describe('jobDetailRefetchInterval', () => {
    it('returns POLL_ACTIVE_MS when data is null/undefined', () => {
        const query = { state: { data: undefined } } as unknown as Query<
            Job,
            Error,
            Job,
            QueryKey
        >
        expect(jobDetailRefetchInterval(query)).toBe(POLL_ACTIVE_MS)
    })

    it('returns false for terminal status jobs', () => {
        const query = {
            state: {
                data: { status: 'PUBLISHED' } as Job,
            },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query)).toBe(false)
    })
    it('keeps polling actively while durable delivery is pending', () => {
        const query = {
            state: {
                data: { status: 'DELIVERY_PENDING' } as Job,
            },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query)).toBe(POLL_ACTIVE_MS)
    })

    it('returns POLL_IDLE_MS for non-terminal jobs with audio', () => {
        const query = {
            state: {
                data: { status: 'WAITING_APPROVAL' } as Job,
            },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query)).toBe(POLL_IDLE_MS)
    })

    it('returns POLL_ACTIVE_MS for non-terminal jobs without audio', () => {
        const query = {
            state: {
                data: { status: 'INCIDENTS_SELECTED' } as Job,
            },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query)).toBe(POLL_ACTIVE_MS)
    })
    it('stops detail polling when an active progress document is cached', () => {
        const queryClient = new QueryClient()
        queryClient.setQueryData(JOB_PROGRESS_QUERY_KEYS.detail('job-1'), {
            jobId: 'job-1',
            status: 'INCIDENTS_SELECTED',
            stage: 'Evidence',
            stageStartedAt: '2026-09-02T00:00:00Z',
            attempt: 1,
            maxAttempts: 3,
            updatedAt: '2026-09-02T00:00:00Z',
            version: 1,
            events: [],
            typicalSeconds: null,
        })
        const query = {
            state: { data: { status: 'DELIVERY_PENDING' } as Job },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query, queryClient, 'job-1')).toBe(
            false
        )
    })

    it('keeps the 3 second fallback when progress is unavailable', () => {
        const queryClient = new QueryClient()
        queryClient.setQueryData(JOB_PROGRESS_QUERY_KEYS.detail('job-1'), {
            unavailable: true,
        })
        const query = {
            state: { data: { status: 'INCIDENTS_SELECTED' } as Job },
        } as unknown as Query<Job, Error, Job, QueryKey>
        expect(jobDetailRefetchInterval(query, queryClient, 'job-1')).toBe(
            POLL_ACTIVE_MS
        )
    })
})
