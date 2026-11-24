// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useNavShortcuts } from './use-nav-shortcuts'
import { NAV_SHORTCUT_PREFIX } from '@/lib/routes'

// ---------------------------------------------------------------------------
// `g` then a route key navigates. This hook was written, deployed, and then
// lost with its routes.ts helpers — recoverable only from a dangling git blob —
// which left the command palette advertising 12 chords nothing implemented.
// The resolver is unit-tested in routes.test.ts; what was never pinned is that
// a real keystroke sequence reaches `navigate`, so that is what this covers.
//
// The timer tests need synthetic events, dispatched on document.body so
// `event.target` is a real Element exactly as a browser delivers a keydown.
// Dispatching on `window` yields a target with no `closest()`, which no real
// key event ever does.
// ---------------------------------------------------------------------------

const { navigateSpy } = vi.hoisted(() => ({ navigateSpy: vi.fn() }))
vi.mock('react-router', () => ({
    useNavigate: () => navigateSpy,
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
    vi.useRealTimers()
})

function Harness() {
    useNavShortcuts()
    return <input aria-label="field" />
}

describe('useNavShortcuts — `g` then a route key', () => {
    it('navigates on the full sequence', async () => {
        const user = userEvent.setup()
        render(<Harness />)

        await user.keyboard(`${NAV_SHORTCUT_PREFIX}1`)

        expect(navigateSpy).toHaveBeenCalledExactlyOnceWith('/jobs')
    })

    it('does not navigate on the prefix alone', async () => {
        const user = userEvent.setup()
        render(<Harness />)

        await user.keyboard(NAV_SHORTCUT_PREFIX)

        expect(navigateSpy).not.toHaveBeenCalled()
    })

    it('ignores a second key that names no route', async () => {
        const user = userEvent.setup()
        render(<Harness />)

        await user.keyboard(`${NAV_SHORTCUT_PREFIX}x`)

        expect(navigateSpy).not.toHaveBeenCalled()
    })

    it('does not fire from inside a form field', async () => {
        const user = userEvent.setup()
        const { getByLabelText } = render(<Harness />)

        await user.click(getByLabelText('field'))
        await user.keyboard(`${NAV_SHORTCUT_PREFIX}1`)

        expect(navigateSpy).not.toHaveBeenCalled()
    })

    it('expires an armed prefix after the timeout, so a stray `g` never swallows a later key', () => {
        vi.useFakeTimers()
        render(<Harness />)

        document.body.dispatchEvent(
            new KeyboardEvent('keydown', {
                key: NAV_SHORTCUT_PREFIX,
                bubbles: true,
            })
        )
        vi.advanceTimersByTime(1600)
        document.body.dispatchEvent(
            new KeyboardEvent('keydown', { key: '1', bubbles: true })
        )

        expect(navigateSpy).not.toHaveBeenCalled()
    })

    it('navigates while the prefix is still armed', () => {
        vi.useFakeTimers()
        render(<Harness />)

        document.body.dispatchEvent(
            new KeyboardEvent('keydown', {
                key: NAV_SHORTCUT_PREFIX,
                bubbles: true,
            })
        )
        vi.advanceTimersByTime(500)
        document.body.dispatchEvent(
            new KeyboardEvent('keydown', { key: '1', bubbles: true })
        )

        expect(navigateSpy).toHaveBeenCalledExactlyOnceWith('/jobs')
    })
})
