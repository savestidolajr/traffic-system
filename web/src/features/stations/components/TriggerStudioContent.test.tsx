// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { TooltipProvider } from '@/components/ui/tooltip'
import { TriggerStudio } from './TriggerStudio'
import type { MarketSchedule } from '@/features/schedules/types'
import type { VoiceProfile } from '@/features/voices/profile-types'

/**
 * Test 2026-08-20 incident: an already-open Trigger dialog sent its mount-time content
 * snapshot, silently overriding the station's later-saved "Always include" weather flag
 * for that job. The submit now omits `content` unless the operator changed it in the
 * dialog, so the station's saved selection wins by default.
 */

const triggerMutation = vi.hoisted(() => ({
    mutateAsync: vi.fn(async () => ({ jobId: 'job-1' })),
    isPending: false,
}))

vi.mock('@/features/jobs/api/job-hooks', () => ({
    useTriggerJob: () => triggerMutation,
}))

const profile = {
    id: 'vp-1',
    name: 'Test Voice',
    status: 'active',
    baseVoiceId: 'voice-1',
    baseVoiceName: 'Base Voice',
    currentVersion: 1,
    versions: [
        {
            version: 1,
            model: 'eleven_v3',
            settings: {
                speed: 1,
                stability: 0.7,
                similarityBoost: 0.7,
                style: 0,
                useSpeakerBoost: false,
            },
        },
    ],
} as unknown as VoiceProfile

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({
        data: { profiles: [profile] },
        isLoading: false,
    }),
}))

// MapPicker pulls in Leaflet, which does not belong in a jsdom unit test.
vi.mock('@/components/map-picker', () => ({
    MapPicker: () => <div data-testid="map-picker" />,
}))

afterEach(() => {
    cleanup()
    triggerMutation.mutateAsync.mockClear()
})

const schedule: MarketSchedule = {
    marketId: 'sydney',
    name: 'Sydney KIIS 90s Traffic',
    station: 'KIIS 90s',
    timezone: 'Australia/Sydney',
    cronExpression: '33 1 * * *',
    enabled: true,
    playoutEnabled: false,
    targetDurationSeconds: 22,
    notifyBeforePlayoutMinutes: 15,
    escalationBeforePlayoutMinutes: 5,
    voiceProfileId: 'vp-1',
    coverage: {
        latitude: -33.86,
        longitude: 151.2,
        radiusKm: 30,
        shapeType: 'circle',
    },
    content: {
        traffic: true,
        weather: true,
        fuel: false,
        weatherRequired: true,
        fuelRequired: false,
    },
    fillOrder: ['weather', 'fuel', 'minorTraffic', 'filler'],
    voiceRotation: null,
    bookendRotation: null,
    deliveryStyleId: '',
    pronunciationDictionaryId: '',
    bookendId: '',
    bookendTokens: null,
    bookendFitWarning: null,
    calendar: null,
    curationEnabled: false,
    nextRunAt: null,
    currentJobId: null,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-20T00:00:00Z',
} as MarketSchedule

function renderStudio() {
    const client = new QueryClient()
    return render(
        <QueryClientProvider client={client}>
            <TooltipProvider>
                <MemoryRouter>
                    <TriggerStudio
                        open
                        onOpenChange={() => {}}
                        schedule={schedule}
                    />
                </MemoryRouter>
            </TooltipProvider>
        </QueryClientProvider>
    )
}

describe('TriggerStudio content submission', () => {
    it('omits content when the operator never touched it, so the station default wins', async () => {
        renderStudio()

        // Wait for the voice prefill effect to settle the form, then submit through
        // the form element: the validity-gated button is a jsdom/RHF timing concern,
        // while handleSubmit still runs the schema validation this test relies on.
        await waitFor(
            () =>
                expect(
                    screen.getByText(/Test Voice · 22s/)
                ).toBeInTheDocument(),
            { timeout: 5000 }
        )
        const form = document.querySelector('form')
        expect(form).not.toBeNull()
        form!.requestSubmit()

        await waitFor(
            () => expect(triggerMutation.mutateAsync).toHaveBeenCalledTimes(1),
            { timeout: 5000 }
        )
        const payload = triggerMutation.mutateAsync.mock.calls[0][0] as {
            content?: unknown
        }
        expect(payload.content).toBeUndefined()
    })

    it('sends content when the operator changed it in the dialog', async () => {
        renderStudio()

        await waitFor(
            () =>
                expect(
                    screen.getByText(/Test Voice · 22s/)
                ).toBeInTheDocument(),
            { timeout: 5000 }
        )
        // Turning Fuel on is an operator change, so the payload must carry the
        // complete ContentSelection, including saved Always include flags.
        await userEvent.click(screen.getByRole('button', { name: /^fuel$/i }))

        document.querySelector('form')!.requestSubmit()

        await waitFor(
            () => expect(triggerMutation.mutateAsync).toHaveBeenCalledTimes(1),
            { timeout: 5000 }
        )
        const payload = triggerMutation.mutateAsync.mock.calls[0][0] as {
            content?: unknown
        }
        expect(payload.content).toEqual({
            traffic: true,
            weather: true,
            fuel: true,
            weatherRequired: true,
            fuelRequired: false,
        })
    })

    it('initialises saved requirements and clears them when a source is deselected', async () => {
        renderStudio()

        await waitFor(() =>
            expect(
                screen.getByRole('switch', {
                    name: /always include weather/i,
                })
            ).toBeChecked()
        )

        await userEvent.click(
            screen.getByRole('button', { name: /^weather$/i })
        )
        expect(
            screen.queryByRole('switch', { name: /always include weather/i })
        ).not.toBeInTheDocument()

        await userEvent.click(
            screen.getByRole('button', { name: /^weather$/i })
        )
        expect(
            screen.getByRole('switch', { name: /always include weather/i })
        ).not.toBeChecked()
    })

    it('sends independently changed Always include flags in the complete content block', async () => {
        renderStudio()

        await waitFor(() =>
            expect(
                screen.getByRole('switch', {
                    name: /always include weather/i,
                })
            ).toBeChecked()
        )
        await userEvent.click(
            screen.getByRole('switch', { name: /always include weather/i })
        )
        await userEvent.click(screen.getByRole('button', { name: /^fuel$/i }))
        await userEvent.click(
            screen.getByRole('switch', { name: /always include fuel/i })
        )

        document.querySelector('form')!.requestSubmit()

        await waitFor(() =>
            expect(triggerMutation.mutateAsync).toHaveBeenCalledTimes(1)
        )
        const payload = triggerMutation.mutateAsync.mock.calls[0][0] as {
            content?: unknown
        }
        expect(payload.content).toEqual({
            traffic: true,
            weather: true,
            fuel: true,
            weatherRequired: false,
            fuelRequired: true,
        })
    })
})
