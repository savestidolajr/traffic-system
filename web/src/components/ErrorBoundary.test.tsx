// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { useEffect } from 'react'
import ErrorBoundary from './ErrorBoundary'

/**
 * App.tsx renders `<ErrorBoundary key={location.pathname}>` around `<Routes>`.
 * These tests lock the two behaviors that key is there to provide:
 *  1. a pathname change (navigation) remounts the subtree and therefore resets
 *     a caught error instead of stranding the operator on the crash screen;
 *  2. a query-string-only change (e.g. `/stations?open=A` -> `/stations?open=B`)
 *     does NOT remount the subtree, so per-page state survives param changes.
 */

let mountCount = 0
const flags = { shouldThrow: false }

function Probe() {
    // Count real MOUNTS, not renders: a parent re-render on a query change
    // re-renders the subtree (normal React) but must not remount it. A mount
    // effect with no deps fires exactly once per mounted instance.
    useEffect(() => {
        mountCount += 1
    }, [])
    if (flags.shouldThrow) throw new Error('probe boom')
    return <div>probe content</div>
}

/** The exact App.tsx construct: boundary keyed by pathname, wrapping routes. */
function KeyedBoundaryPattern() {
    const location = useLocation()
    return (
        <ErrorBoundary key={location.pathname}>
            <Probe />
        </ErrorBoundary>
    )
}

function NavButtons() {
    const navigate = useNavigate()
    return (
        <div>
            <button type="button" onClick={() => navigate('/a?x=1')}>
                add query
            </button>
            <button type="button" onClick={() => navigate('/a?x=2')}>
                change query
            </button>
            <button type="button" onClick={() => navigate('/b')}>
                navigate
            </button>
        </div>
    )
}

function renderHarness() {
    return render(
        <MemoryRouter initialEntries={['/a']}>
            <NavButtons />
            <KeyedBoundaryPattern />
        </MemoryRouter>
    )
}

beforeEach(() => {
    mountCount = 0
    flags.shouldThrow = false
})

afterEach(cleanup)

describe('ErrorBoundary keyed by pathname (App.tsx pattern)', () => {
    it('does not remount the route subtree on a query-string-only change', async () => {
        const user = userEvent.setup()
        renderHarness()
        expect(screen.getByText('probe content')).toBeInTheDocument()
        const mountsAfterInitial = mountCount

        await user.click(screen.getByRole('button', { name: 'add query' }))
        expect(mountCount).toBe(mountsAfterInitial)

        await user.click(screen.getByRole('button', { name: 'change query' }))
        expect(mountCount).toBe(mountsAfterInitial)
    })

    it('resets a caught error on pathname navigation', async () => {
        const user = userEvent.setup()
        flags.shouldThrow = true
        renderHarness()

        // A render error is caught; the boundary shows its fallback.
        expect(screen.getByText('Something went wrong')).toBeInTheDocument()
        expect(screen.queryByText('probe content')).not.toBeInTheDocument()
        const mountsAfterError = mountCount

        // Same pathname, new query: the boundary is NOT remounted, so the
        // crash screen persists (per-page state survives; see first test).
        await user.click(screen.getByRole('button', { name: 'change query' }))
        expect(mountCount).toBe(mountsAfterError)
        expect(screen.getByText('Something went wrong')).toBeInTheDocument()

        // Pathname change remounts the boundary: the error is cleared and the
        // route subtree renders again from scratch. Flipping the flag does not
        // itself trigger a render — only the key change does.
        flags.shouldThrow = false
        await user.click(screen.getByRole('button', { name: 'navigate' }))
        expect(screen.getByText('probe content')).toBeInTheDocument()
        expect(
            screen.queryByText('Something went wrong')
        ).not.toBeInTheDocument()
        expect(mountCount).toBeGreaterThan(mountsAfterError)
    })
})
