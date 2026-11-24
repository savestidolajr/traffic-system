// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TooltipProvider } from '@/components/ui/tooltip'
import { EditorialSettingsPanel } from './EditorialSettingsPanel'

const saveMock = vi.hoisted(() => vi.fn())
const storedMock = vi.hoisted(() => vi.fn())
const effectiveMock = vi.hoisted(() => vi.fn())

vi.mock('../api/editorial-hooks', () => ({
    useEditorialSettings: () => storedMock(),
    useEffectiveEditorialSettings: () => effectiveMock(),
    useUpsertEditorialSettings: () => ({ mutate: saveMock, isPending: false }),
    useDeleteEditorialSettings: () => ({ mutate: vi.fn(), isPending: false }),
}))

class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

const ALL_SOURCES = [
    'livetraffic-nsw',
    'qldtraffic',
    'victraffic',
    'mainroads-wa',
    'trafficsa',
    'tasalert',
]

function givenStored(disabled: string[] | null) {
    storedMock.mockReturnValue({
        data: { DisabledTrafficSources: disabled },
    })
    effectiveMock.mockReturnValue({
        data: {
            ConfiguredByArn: [],
            EnabledTrafficSources: ALL_SOURCES.filter(
                (id) => !(disabled ?? []).includes(id)
            ),
        },
    })
}

function renderPanel() {
    return render(
        <TooltipProvider>
            <EditorialSettingsPanel
                scope="Station"
                scopeId="kiis-90s"
                market="sydney"
                station="kiis-90s"
            />
        </TooltipProvider>
    )
}

/** The saved body from the last save click. */
function savedBody(): Record<string, unknown> {
    expect(saveMock).toHaveBeenCalled()
    return saveMock.mock.calls[0]![0] as Record<string, unknown>
}

/**
 * Guard for the two knobs that resolve onto a bulletin but are consumed by nothing (every
 * production read is the deployed SpeechDurationOptions value, not the EditorialSettings member).
 * They are hidden until that is wired, because these four decide whether a short read is padded or
 * sent to the standby cart — an operator who set the pad and believed they had stopped dead air
 * would be wrong. Re-add them only in the change that threads the overrides through
 * IAudioPipelineService down to AudioDurationFineTuner.
 */
describe('EditorialSettingsPanel inert slot-fit knobs', () => {
    it('offers the two slot-fit controls that are wired', () => {
        givenStored(null)
        renderPanel()

        expect(
            screen.getByRole('slider', { name: 'Stretch a short read' })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('slider', { name: 'Compress a long read' })
        ).toBeInTheDocument()
    })

    it('offers no input for the two that nothing consumes', () => {
        givenStored(null)
        renderPanel()

        expect(
            screen.queryByRole('slider', { name: /silence pad/i })
        ).toBeNull()
        expect(
            screen.queryByRole('slider', { name: /overrun trim/i })
        ).toBeNull()
        expect(screen.queryByText(/Maximum silence pad/)).toBeNull()
        expect(screen.queryByText(/Maximum safe overrun trim/)).toBeNull()
    })

    it('a save clears any value a previous build stored for them', async () => {
        const user = userEvent.setup()
        givenStored(null)
        renderPanel()

        await user.click(screen.getByRole('button', { name: 'Save settings' }))

        // Absent from the whole-record upsert => the backend stores null, so a value written by an
        // earlier build cannot linger and keep reading as ARN-configured on the effective endpoint.
        expect(savedBody().maxSilencePadSeconds).toBeUndefined()
        expect(savedBody().maxSafeOverrunTrimSeconds).toBeUndefined()
    })
})

describe('EditorialSettingsPanel traffic sources', () => {
    it('shows every authority feed as consulted when nothing is disabled', () => {
        givenStored(null)
        renderPanel()

        const sa = screen.getByRole('switch', { name: /Traffic SA/ })
        expect(sa).toBeChecked()
        expect(
            screen.getByRole('switch', { name: /Live Traffic NSW/ })
        ).toBeChecked()
    })

    it('shows a stored denylist as unchecked and labels it', () => {
        givenStored(['trafficsa'])
        renderPanel()

        expect(
            screen.getByRole('switch', { name: /Traffic SA/ })
        ).not.toBeChecked()
        expect(
            screen.getByRole('switch', { name: /Live Traffic NSW/ })
        ).toBeChecked()
        // An off source greys its own label rather than adding a "not consulted" caption to
        // every row — same signal, one element instead of two.
        expect(screen.getByText('Traffic SA')).toHaveClass(
            'text-muted-foreground'
        )
    })

    it('sends the source as disabled after the operator unticks it', async () => {
        const user = userEvent.setup()
        givenStored(null)
        renderPanel()

        await user.click(screen.getByRole('switch', { name: /Traffic SA/ }))
        await user.click(screen.getByRole('button', { name: 'Save settings' }))

        expect(savedBody().disabledTrafficSources).toEqual(['trafficsa'])
    })

    it('re-enabling a source sends an empty list, not null, so a market denylist is overridden', async () => {
        const user = userEvent.setup()
        givenStored(['trafficsa'])
        renderPanel()

        await user.click(screen.getByRole('switch', { name: /Traffic SA/ }))
        await user.click(screen.getByRole('button', { name: 'Save settings' }))

        expect(savedBody().disabledTrafficSources).toEqual([])
    })

    /**
     * The whole-record upsert means an untouched denylist has to be RESENT. Editing only a numeric
     * cap and sending null would silently clear the scope's sources — the same shape as the
     * 2026-08-20 Trigger dialog incident, where a stale mount-time snapshot reset a station's
     * always-include flags.
     */
    it('resends an untouched denylist when only a cap is edited', async () => {
        const user = userEvent.setup()
        givenStored(['trafficsa'])
        renderPanel()

        await user.click(screen.getByRole('button', { name: 'Save settings' }))

        expect(savedBody().disabledTrafficSources).toEqual(['trafficsa'])
    })

    it('does not claim the sources are yours when the scope stores no denylist', () => {
        givenStored(null)
        renderPanel()

        const heading = screen.getByRole('heading', {
            name: 'Traffic data sources',
        })
        expect(heading.parentElement).not.toHaveTextContent('Yours')
    })

    it('marks the sources as yours when this scope stores a denylist', () => {
        givenStored([])
        renderPanel()

        const heading = screen.getByRole('heading', {
            name: 'Traffic data sources',
        })
        expect(heading.parentElement).toHaveTextContent('Yours')
    })
})
