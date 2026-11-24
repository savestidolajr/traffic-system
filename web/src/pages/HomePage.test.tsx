// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { HomePage } from './HomePage'
import { useJobs } from '@/features/jobs/api/job-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useIsAdmin } from '@/hooks/use-current-user'
import type { JobsListResponse } from '@/features/jobs/types'
import type { SchedulesListResponse } from '@/features/schedules/types'
import type { Mock } from 'vitest'

vi.mock('@/features/jobs/api/job-hooks', () => ({
    useJobs: vi.fn<() => JobsFeedQueryState>(),
}))
vi.mock('@/features/schedules/api/schedule-hooks', () => ({
    useSchedules: vi.fn<() => SchedulesFeedQueryState>(),
}))
vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: vi.fn<() => boolean>(),
}))

/** The slice of the jobs query result HomePage reads. */
interface JobsFeedQueryState {
    data: JobsListResponse | undefined
    isLoading: boolean
    isError: boolean
    error: Error | null
    refetch: () => void
}

/** The slice of the schedules query result HomePage reads. */
interface SchedulesFeedQueryState {
    data: SchedulesListResponse | undefined
    isLoading: boolean
    isError: boolean
    error: Error | null
    refetch: () => void
}

// The module mocks are typed as the component's consumed slice, not the full
// react-query result, so mockReturnValue stays honest about what HomePage reads.
const useJobsMock = useJobs as unknown as Mock<() => JobsFeedQueryState>
const useSchedulesMock = useSchedules as unknown as Mock<
    () => SchedulesFeedQueryState
>
const useIsAdminMock = useIsAdmin as unknown as Mock<() => boolean>

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

const jobsRefetch = vi.fn()
const schedulesRefetch = vi.fn()

function mockQueries({
    jobsError = false,
    jobsErrorDetail,
    jobsData,
    jobsLoading = false,
    schedulesData,
}: {
    jobsError?: boolean
    jobsErrorDetail?: Error
    jobsData?: JobsListResponse
    jobsLoading?: boolean
    schedulesData?: SchedulesListResponse
} = {}) {
    useJobsMock.mockReturnValue({
        data: jobsData,
        isLoading: jobsLoading,
        isError: jobsError,
        error: jobsErrorDetail ?? null,
        refetch: jobsRefetch,
    })
    useSchedulesMock.mockReturnValue({
        data: schedulesData,
        isLoading: false,
        isError: false,
        error: null,
        refetch: schedulesRefetch,
    })
}

describe('HomePage — ops band failure surfacing', () => {
    beforeEach(() => {
        // Default: the happy path, and an Admin viewer — the hero copy stays
        // platform-wide for admins.
        useIsAdminMock.mockReturnValue(true)
        mockQueries({
            jobsData: { jobs: [], total: 0, hasMore: false },
            schedulesData: { schedules: [], total: 0 },
        })
    })

    it('renders the live ops band when the feeds are healthy', () => {
        render(
            <MemoryRouter>
                <HomePage />
            </MemoryRouter>
        )
        expect(screen.getByText('Waiting on you')).toBeInTheDocument()
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    // bug-bash #25: the home ops band used to vanish with NO message when the
    // jobs feed failed (`opsReady` silently dropped the band). It must now
    // surface the same ErrorAlert + Retry the other console surfaces use.
    it('renders an error with a retry instead of silently dropping the band when the jobs feed errors', async () => {
        const user = userEvent.setup()
        mockQueries({
            jobsError: true,
            jobsErrorDetail: new Error('Request failed with status code 500'),
        })
        render(
            <MemoryRouter>
                <HomePage />
            </MemoryRouter>
        )
        const alert = screen.getByRole('alert')
        // ErrorAlert leads with the query error's own message; the fallback is
        // reserved for failures that carry no Error object.
        expect(alert).toHaveTextContent('Request failed with status code 500')
        expect(screen.queryByText('Waiting on you')).not.toBeInTheDocument()
        await user.click(screen.getByRole('button', { name: /retry/i }))
        expect(jobsRefetch).toHaveBeenCalledTimes(1)
        expect(schedulesRefetch).toHaveBeenCalledTimes(1)
    })

    // A jobs query with no data and no fetch in flight (paused/stalled) is a
    // failure state too — the band must not silently vanish for it either.
    it('renders an error with a retry when the jobs query has no data and is not loading', async () => {
        const user = userEvent.setup()
        mockQueries({ jobsData: undefined })
        render(
            <MemoryRouter>
                <HomePage />
            </MemoryRouter>
        )
        const alert = screen.getByRole('alert')
        expect(alert).toHaveTextContent('Failed to load live ops data')
        await user.click(screen.getByRole('button', { name: /retry/i }))
        expect(jobsRefetch).toHaveBeenCalledTimes(1)
    })
})

describe('HomePage — scope-honest ops copy (MS-UI-3)', () => {
    beforeEach(() => {
        mockQueries({
            jobsData: { jobs: [], total: 0, hasMore: false },
            schedulesData: { schedules: [], total: 0 },
        })
    })

    // The pulse line is one <p> with the counts in child spans, so it must be
    // matched on the parent's textContent, not a single text node.
    const findPulse = () =>
        screen.getByText(
            (_, element) =>
                element?.tagName === 'P' &&
                (element.textContent ?? '').includes('generating now')
        )

    it('keeps the platform-wide wording for admins', () => {
        useIsAdminMock.mockReturnValue(true)
        render(
            <MemoryRouter>
                <HomePage />
            </MemoryRouter>
        )
        expect(findPulse()).toHaveTextContent(/0 stations on air/)
        expect(screen.getByText(/every station is paused/)).toBeInTheDocument()
        // The scoped phrasing must not appear for an admin.
        expect(findPulse()).not.toHaveTextContent(/of your stations on air/)
    })

    it('scopes the hero counts to the caller for operators', () => {
        // The feeds are server-filtered to the operator's markets, so the
        // counts describe THEIR stations — never the whole network.
        useIsAdminMock.mockReturnValue(false)
        render(
            <MemoryRouter>
                <HomePage />
            </MemoryRouter>
        )
        expect(findPulse()).toHaveTextContent(/0 of your stations on air/)
        expect(
            screen.getByText(/your stations are all paused/)
        ).toBeInTheDocument()
        expect(findPulse()).not.toHaveTextContent(/0 stations on air/)
        expect(
            screen.queryByText(/every station is paused/)
        ).not.toBeInTheDocument()
    })
})
