// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DataBoundary } from './DataBoundary'

afterEach(() => {
    cleanup()
})

const loading = <div>loading-stub</div>
const empty = <div>empty-stub</div>
const content = <div>content-stub</div>

describe('DataBoundary', () => {
    it('renders the loading state while a first fetch is in flight', () => {
        render(
            <DataBoundary
                query={{
                    isError: false,
                    error: null,
                    isLoading: true,
                    data: undefined,
                }}
                errorFallback="Failed to load"
                loading={loading}
                empty={empty}
            >
                {content}
            </DataBoundary>
        )
        expect(screen.getByText('loading-stub')).toBeInTheDocument()
        expect(screen.queryByText('content-stub')).not.toBeInTheDocument()
    })

    it('renders the empty state only for genuinely-empty results (empty array)', () => {
        render(
            <DataBoundary
                query={{
                    isError: false,
                    error: null,
                    isLoading: false,
                    data: [],
                }}
                errorFallback="Failed to load"
                loading={loading}
                empty={empty}
            >
                {content}
            </DataBoundary>
        )
        expect(screen.getByText('empty-stub')).toBeInTheDocument()
    })

    it('renders the empty state for object-wrapped lists via isEmpty', () => {
        render(
            <DataBoundary
                query={{
                    isError: false,
                    error: null,
                    isLoading: false,
                    data: { styles: [] },
                }}
                errorFallback="Failed to load"
                loading={loading}
                empty={empty}
                isEmpty={(d) => d.styles.length === 0}
            >
                {content}
            </DataBoundary>
        )
        expect(screen.getByText('empty-stub')).toBeInTheDocument()
    })

    it('renders children once data is present', () => {
        render(
            <DataBoundary
                query={{
                    isError: false,
                    error: null,
                    isLoading: false,
                    data: [{ id: 1 }],
                }}
                errorFallback="Failed to load"
                loading={loading}
                empty={empty}
            >
                {content}
            </DataBoundary>
        )
        expect(screen.getByText('content-stub')).toBeInTheDocument()
    })

    it('renders an error with a retry affordance when the query is in error state', async () => {
        const user = userEvent.setup()
        const refetch = vi.fn()
        render(
            <DataBoundary
                query={{
                    isError: true,
                    error: new Error('Request failed with status code 500'),
                    isLoading: false,
                    data: undefined,
                    refetch,
                }}
                errorFallback="Failed to load the bulletin queue"
                loading={loading}
                empty={empty}
            >
                {content}
            </DataBoundary>
        )
        const alert = screen.getByRole('alert')
        expect(alert).toHaveTextContent('Request failed with status code 500')
        await user.click(screen.getByRole('button', { name: /retry/i }))
        expect(refetch).toHaveBeenCalledTimes(1)
        expect(screen.queryByText('empty-stub')).not.toBeInTheDocument()
    })

    // bug-bash #25: a jobs-feed failure (500 or a hang) must surface as an
    // error, never as a silent blank. A query that has NO data and NO fetch
    // in flight (paused by a network failure, or stalled without resolving)
    // used to fall through to the `empty` branch — which is `null`/blank on
    // the queue beam and history table, so a backend hiccup silently removed
    // the triage queue with no error and no retry. It is a failure state, not
    // an empty one, and must render the same ErrorAlert + Retry as isError.
    it('renders an error with a retry affordance when data is missing with no fetch in flight', async () => {
        const user = userEvent.setup()
        const refetch = vi.fn()
        render(
            <DataBoundary
                query={{
                    isError: false,
                    error: null,
                    isLoading: false,
                    data: undefined,
                    refetch,
                }}
                errorFallback="Failed to load the bulletin queue"
                loading={loading}
                empty={empty}
            >
                {content}
            </DataBoundary>
        )
        const alert = screen.getByRole('alert')
        expect(alert).toHaveTextContent('Failed to load the bulletin queue')
        await user.click(screen.getByRole('button', { name: /retry/i }))
        expect(refetch).toHaveBeenCalledTimes(1)
        expect(screen.queryByText('empty-stub')).not.toBeInTheDocument()
    })
})
