// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { ScheduleStudio } from './ScheduleStudio'
import { ScheduleFormSchema } from '../form-schemas'
import { NON_STEP_FIELDS, STEP_FIELDS } from '../lib/schedule-studio-helpers'
import type { MarketSchedule } from '../types'
import { apiClient } from '@/lib/api/client'

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({ data: undefined }),
}))

vi.mock('@/features/delivery/api/delivery-style-hooks', () => ({
    useDeliveryStyles: () => ({ data: undefined, isLoading: false }),
}))

vi.mock('@/features/pronunciation/api/pronunciation-hooks', () => ({
    usePronunciationDictionaries: () => ({ data: undefined, isLoading: false }),
}))

// The bookend hooks (WebLibraries' slice) run for REAL against this fake
// transport, so the tests exercise the full hook → schema → component chain
// against the documented wire contract rather than a mocked-away hook.
vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn(),
        post: vi.fn(),
        put: vi.fn(),
        delete: vi.fn(),
    },
    ApiError: class ApiError extends Error {},
    API_BASE_URL: '/api',
    API_TOKEN: null,
    isMockMode: false,
    resolveAudioSrc: (url: string) => url,
}))

const bookendLibrary = vi.hoisted(() => ({
    pairs: [] as Array<Record<string, unknown>>,
    preview: {} as Record<string, unknown>,
}))

function wireBookendTransport() {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
        if (url.includes('/bookends')) {
            return {
                data: {
                    Items: bookendLibrary.pairs,
                    Total: bookendLibrary.pairs.length,
                },
            }
        }
        return { data: { Items: [], Total: 0 } }
    })
    vi.mocked(apiClient.post).mockImplementation(async () => ({
        data: bookendLibrary.preview,
    }))
}

/** Wire payloads per the documented BookendDto contract (PascalCase pair). */
function bookendDto(
    id: string,
    name: string,
    introText: string,
    outroText: string,
    status: 'active' | 'archived' = 'active'
): Record<string, unknown> {
    return {
        Id: id,
        Name: name,
        Description: null,
        Status: status,
        IntroText: introText,
        OutroText: outroText,
        CurrentVersion: 1,
        CreatedAt: '2026-01-01T00:00:00Z',
        UpdatedAt: '2026-01-01T00:00:00Z',
        CreatedBy: null,
        AssignedStations: [],
        Tokens: [],
        Versions: [],
    }
}

class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)

/**
 * STEP_FIELDS drives the per-step validity dots: a schema field missing from
 * the map silently never invalidates its step. These tests force every new
 * schema field to be assigned to a step (or explicitly to NON_STEP_FIELDS)
 * before it can ship.
 */
describe('ScheduleStudio STEP_FIELDS', () => {
    const schemaKeys = Object.keys(ScheduleFormSchema.shape).sort()
    const mappedKeys = [
        ...new Set([...Object.values(STEP_FIELDS).flat(), ...NON_STEP_FIELDS]),
    ].sort()

    it('covers every field of ScheduleFormSchema', () => {
        const unassigned = schemaKeys.filter(
            (k) => !mappedKeys.includes(k as (typeof mappedKeys)[number])
        )
        expect(
            unassigned,
            'schema fields not assigned to any studio step — add them to STEP_FIELDS (or NON_STEP_FIELDS if deliberately outside the rail)'
        ).toEqual([])
    })

    it('maps no field that the schema does not define', () => {
        const phantom = mappedKeys.filter(
            (k) => !schemaKeys.includes(k as string)
        )
        expect(
            phantom,
            'STEP_FIELDS references fields missing from ScheduleFormSchema'
        ).toEqual([])
    })

    it('assigns each field to at most one step', () => {
        const all = [...Object.values(STEP_FIELDS).flat(), ...NON_STEP_FIELDS]
        const duplicates = all.filter((k, i) => all.indexOf(k) !== i)
        expect(duplicates).toEqual([])
    })
})

function renderStudio(onOpenChange = vi.fn(), initialStep?: 'speech') {
    const queryClient = new QueryClient()
    render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <ScheduleStudio
                    schedule={null}
                    open
                    onOpenChange={onOpenChange}
                    onSubmit={vi.fn()}
                    isSubmitting={false}
                    initialStep={initialStep}
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
    return { onOpenChange }
}

/** Renders the studio over a STORED schedule, so the form starts from persisted
 *  values rather than create-mode defaults. */
function renderStudioWithSchedule(overrides: Partial<MarketSchedule> = {}) {
    const queryClient = new QueryClient()
    const schedule: MarketSchedule = {
        marketId: 'adelaide',
        name: 'Adelaide Drive Traffic',
        station: 'KIIS 102.3',
        timezone: 'Australia/Adelaide',
        cronExpression: '0 8 * * *',
        enabled: false,
        targetDurationSeconds: 30,
        notifyBeforePlayoutMinutes: 15,
        escalationBeforePlayoutMinutes: 5,
        voiceProfileId: 'vp-1',
        deliveryStyleId: null,
        pronunciationDictionaryId: null,
        bookendId: null,
        nextRunAt: null,
        currentJobId: null,
        createdAt: '2026-08-01T00:00:00Z',
        updatedAt: '2026-08-01T00:00:00Z',
        coverage: {
            latitude: -34.93,
            longitude: 138.6,
            radiusKm: 25,
        } as MarketSchedule['coverage'],
        content: null,
        voiceRotation: null,
        bookendRotation: null,
        bookendTokens: null,
        fillOrder: null,
        ...overrides,
    }
    render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <ScheduleStudio
                    schedule={schedule}
                    open
                    onOpenChange={vi.fn()}
                    onSubmit={vi.fn()}
                    isSubmitting={false}
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
    return { schedule }
}

describe('ScheduleStudio Cancel — unsaved changes', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('closes immediately when the form has no edits', async () => {
        const user = userEvent.setup()
        const { onOpenChange } = renderStudio()

        await user.click(screen.getByRole('button', { name: 'Cancel' }))

        expect(onOpenChange).toHaveBeenCalledWith(false)
        expect(
            screen.queryByText('Discard unsaved changes?')
        ).not.toBeInTheDocument()
    })

    it('prompts a discard confirmation instead of closing when a field is dirty', async () => {
        const user = userEvent.setup()
        const { onOpenChange } = renderStudio()

        await user.type(screen.getByLabelText(/Station name/), 'KIIS 106.5')
        await user.click(screen.getByRole('button', { name: 'Cancel' }))

        expect(onOpenChange).not.toHaveBeenCalled()
        // ConfirmDialog is lazy-loaded (perf), so it resolves asynchronously.
        expect(
            await screen.findByText('Discard unsaved changes?')
        ).toBeInTheDocument()

        await user.click(screen.getByRole('button', { name: 'Discard' }))
        expect(onOpenChange).toHaveBeenCalledWith(false)
    })
})

describe('ScheduleStudio bookend selector', () => {
    beforeEach(() => {
        bookendLibrary.pairs = [
            bookendDto(
                'bk-active',
                'Welcome pair',
                'Welcome to {city} traffic.',
                'That is all for {market}.'
            ),
            bookendDto(
                'bk-archived',
                'Archived pair',
                'Old line',
                'Old close',
                'archived'
            ),
        ]
        bookendLibrary.preview = {}
        wireBookendTransport()
    })

    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('shows only active bookends in the single picker', async () => {
        const user = userEvent.setup()
        renderStudio(undefined, 'speech')

        await user.click(
            await screen.findByRole('combobox', { name: 'Bookend' })
        )

        expect(
            await screen.findByRole('option', { name: /Welcome pair/ })
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('option', { name: /Archived pair/ })
        ).not.toBeInTheDocument()
    })
})

describe('ScheduleStudio bookend preview + token gate', () => {
    beforeEach(() => {
        bookendLibrary.pairs = [
            bookendDto(
                'bk-active',
                'Welcome pair',
                'Welcome to {city} traffic.',
                'Thanks for joining us, {suburb}.'
            ),
        ]
        wireBookendTransport()
    })

    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('resolves the pair, names an unfillable custom token and blocks save', async () => {
        const user = userEvent.setup()
        renderStudio(undefined, 'speech')

        bookendLibrary.preview = {
            ResolvedIntro: 'Welcome to Gold Coast traffic.',
            ResolvedOutro: 'Thanks for joining us, .',
            UnresolvedTokens: ['suburb'],
            Values: { city: 'Gold Coast', market: 'goldcoast' },
        }

        await user.click(
            await screen.findByRole('combobox', { name: 'Bookend' })
        )
        await user.click(
            await screen.findByRole('option', { name: /Welcome pair/ })
        )

        // The debounce fires ~400ms after the selection settles; findBy* waits.
        expect(
            await screen.findByText(/Welcome to Gold Coast traffic/)
        ).toBeInTheDocument()
        // The resolved value chips make the derived default visible
        expect(screen.getByText(/city → Gold Coast/)).toBeInTheDocument()
        // The client-side gate names the bookend and the token, and states the
        // refusal — never "dropped on air".
        expect(
            screen.getByText(
                /Bookend 'Welcome pair' uses token\(s\) \{suburb\}/
            )
        ).toBeInTheDocument()
        expect(screen.getByText(/Save will be refused/)).toBeInTheDocument()
        // Save is gated while any referenced bookend has an unfillable token.
        expect(
            screen.getByRole('button', { name: 'Create station' })
        ).toBeDisabled()
    })

    // Two Radix Select round-trips plus 16 typed characters make this the slowest test in the
    // file: 4.2s standalone, but ~2.7x that under verify.sh's five parallel lanes, where the 5s
    // default timed it out intermittently. An explicit budget instead of raising testTimeout
    // globally, which would hide a real hang everywhere else.
    it('unblocks save once the missing token gets an override value', async () => {
        const user = userEvent.setup()
        renderStudio(undefined, 'speech')

        bookendLibrary.preview = {
            ResolvedIntro: 'Welcome to Gold Coast traffic.',
            ResolvedOutro: 'Thanks for joining us, Southport.',
            UnresolvedTokens: [],
            Values: {
                city: 'Gold Coast',
                market: 'goldcoast',
                suburb: 'Southport',
            },
        }

        await user.click(
            await screen.findByRole('combobox', { name: 'Bookend' })
        )
        await user.click(
            await screen.findByRole('option', { name: /Welcome pair/ })
        )
        await screen.findByText(/Welcome to Gold Coast traffic/)

        // The warning pointed at BookendTokens; add the override row it needs.
        await user.click(screen.getByRole('button', { name: 'Add override' }))
        const keyInput = screen.getByLabelText(/Override token key 1/)
        const valueInput = screen.getByLabelText(/Override token value 1/)
        await user.type(keyInput, 'suburb')
        await user.type(valueInput, 'Southport')

        // The named-token warning is the gate's message: once the token has a
        // value, the block lifts (the button stays disabled here only because
        // the rest of this new-station form is still empty).
        expect(
            screen.queryByText(/Save will be refused/)
        ).not.toBeInTheDocument()
        expect(
            screen.queryByText(/Bookend 'Welcome pair' uses token/)
        ).not.toBeInTheDocument()
    }, 20_000)
})

describe('ScheduleStudio rotation-member token gate', () => {
    beforeEach(() => {
        // The primary pair is fully fillable; only the ROTATION MEMBER carries
        // an unfillable custom token — the case the preview used to hide.
        bookendLibrary.pairs = [
            bookendDto(
                'bk-active',
                'Welcome pair',
                'Welcome to {city} traffic.',
                'That is all for {market}.'
            ),
            bookendDto('bk-rotate', 'Rotate pair', 'Up next, {suburb}.', ''),
        ]
        bookendLibrary.preview = {}
        wireBookendTransport()
    })

    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('flags the rotation member and blocks save before the backend 400', async () => {
        const user = userEvent.setup()
        renderStudio(undefined, 'speech')

        await user.click(
            await screen.findByRole('combobox', { name: 'Bookend' })
        )
        await user.click(
            await screen.findByRole('option', { name: /Welcome pair/ })
        )

        // Primary pair resolves clean — no warning for it.
        expect(
            screen.queryByText(/Save will be refused/)
        ).not.toBeInTheDocument()

        // Add the rotation member whose template needs a custom token.
        await user.click(screen.getByRole('tab', { name: /Rotate/ }))
        await user.click(
            await screen.findByRole('combobox', {
                name: 'Add a bookend to the rotation',
            })
        )
        await user.click(
            await screen.findByRole('option', { name: /Rotate pair/ })
        )

        // The gate names the MEMBER bookend and its unfillable token.
        expect(
            screen.getByText(/Bookend 'Rotate pair' uses token\(s\) \{suburb\}/)
        ).toBeInTheDocument()
        expect(screen.getByText(/Save will be refused/)).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: 'Create station' })
        ).toBeDisabled()
    })
})

/**
 * The validation gate: a freshly opened studio must not show amber step
 * warnings before the operator has looked at anything, and the warnings must
 * return exactly as before once a step is visited or a save is attempted.
 */
describe('ScheduleStudio validation gate', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('shows no step warnings on a fresh open', () => {
        renderStudio()

        expect(screen.queryByText(/Fix issues in/)).not.toBeInTheDocument()
        expect(
            screen.queryByLabelText('Step needs attention')
        ).not.toBeInTheDocument()
    })

    it('reveals the warnings once a save is attempted with missing fields', async () => {
        renderStudio()

        // The submit button is disabled while the form is invalid, so the
        // attempt is the form's own submit path (Enter/implicit submission
        // and tests both land here). The dialog portal mounts in
        // document.body, so query the document rather than a render container.
        fireEvent.submit(document.querySelector('form')!)

        expect(await screen.findByText(/Fix issues in/)).toBeInTheDocument()
        expect(
            await screen.findAllByLabelText('Step needs attention')
        ).not.toHaveLength(0)
    })

    it('reveals a warning once the invalid step itself is visited', async () => {
        const user = userEvent.setup()
        renderStudio()

        // The Voice step is invalid on a fresh create (no voice picked);
        // visiting it reveals its warning without any submit attempt.
        await user.click(screen.getByRole('button', { name: 'Voice' }))

        expect(
            await screen.findAllByLabelText('Step needs attention')
        ).not.toHaveLength(0)
        expect(await screen.findByText(/Fix issues in/)).toBeInTheDocument()
    })

    // A station can arrive ALREADY invalid: adelaide on dev stored notify 5 /
    // escalate 5, written before the alert-ordering rule existed, so its Save
    // was disabled on an untouched studio and the only explanation was the
    // generic "Fix the highlighted steps first". The reason has to name the
    // step, or the operator has eight to search.
    it('names the blocking step when a stored schedule arrives invalid', async () => {
        const user = userEvent.setup()
        renderStudioWithSchedule({
            notifyBeforePlayoutMinutes: 5,
            escalationBeforePlayoutMinutes: 5,
        })

        const save = screen.getByRole('button', { name: 'Save changes' })
        expect(save).toBeDisabled()

        // The reason rides a tooltip on the focusable wrapper, since a disabled
        // button swallows pointer events.
        await user.hover(save.parentElement!)

        expect(await screen.findByText('Fix Alerts first')).toBeInTheDocument()
    })
})

/**
 * The flatten confirmation is driven by `useScheduleActions` (see its hook
 * tests for the no-PUT-until-confirmed contract); here the studio just
 * renders the dialog the hook hands it and routes confirm/cancel back.
 */
describe('ScheduleStudio flatten confirmation', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    const description =
        'This station runs day-specific times: weekdays 5 times (05:59, 06:59, 08:59, 09:29, 09:45); Saturdays 3 times (06:58, 08:58, 09:30); Sundays 1 time (09:46). The editor shows one set of times for all days, so saving replaces the day-specific pattern with one that runs every selected time on every selected day: every selected day 9 times (05:59, 06:58, 06:59, 08:58, 08:59, and 4 more). The day-specific pattern is replaced permanently and cannot be undone.'

    function renderWithFlatten(flattenConfirm: {
        open: boolean
        description: string
        onConfirm: () => Promise<void>
        onCancel: () => void
    }) {
        const queryClient = new QueryClient()
        const studio = (withConfirm: boolean) => (
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ScheduleStudio
                        schedule={null}
                        open
                        onOpenChange={vi.fn()}
                        onSubmit={vi.fn()}
                        isSubmitting={false}
                        flattenConfirm={
                            withConfirm ? flattenConfirm : undefined
                        }
                    />
                </TooltipProvider>
            </QueryClientProvider>
        )
        const { rerender } = render(studio(false))
        // The save attempt opened the confirmation (hook state), so the
        // confirm mounts on a later render — exactly how the discard dialog
        // and the production flow behave.
        rerender(studio(true))
        return { flattenConfirm }
    }

    it('renders the confirmation with the loss summary when a flatten is pending', async () => {
        const flattenConfirm = {
            open: true,
            description,
            onConfirm: vi.fn(async () => {}),
            onCancel: vi.fn(),
        }
        renderWithFlatten(flattenConfirm)

        // ConfirmDialog is lazy-loaded, so it resolves asynchronously.
        expect(
            await screen.findByText('Replace day-specific times?')
        ).toBeInTheDocument()
        expect(screen.getByText(description)).toBeInTheDocument()
    })

    it('confirms via the Replace times action', async () => {
        const user = userEvent.setup()
        const flattenConfirm = {
            open: true,
            description,
            onConfirm: vi.fn(async () => {}),
            onCancel: vi.fn(),
        }
        renderWithFlatten(flattenConfirm)

        await screen.findByText('Replace day-specific times?')
        await user.click(screen.getByRole('button', { name: 'Replace times' }))

        expect(flattenConfirm.onConfirm).toHaveBeenCalledTimes(1)
    })

    it('declines via Keep editing without confirming', async () => {
        const user = userEvent.setup()
        const flattenConfirm = {
            open: true,
            description,
            onConfirm: vi.fn(async () => {}),
            onCancel: vi.fn(),
        }
        renderWithFlatten(flattenConfirm)

        await screen.findByText('Replace day-specific times?')
        await user.click(screen.getByRole('button', { name: 'Keep editing' }))

        expect(flattenConfirm.onCancel).toHaveBeenCalledTimes(1)
        expect(flattenConfirm.onConfirm).not.toHaveBeenCalled()
    })
})
