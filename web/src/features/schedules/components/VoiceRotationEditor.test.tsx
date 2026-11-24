// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { Form } from '@/components/ui/form'
import { TooltipProvider } from '@/components/ui/tooltip'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'
import { VoiceRotationEditor } from './VoiceRotationEditor'
import type { ScheduleFormData } from '../types'

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({ data: { profiles: [] } }),
}))

afterEach(cleanup)

/**
 * Windows exactly as `VoiceWindowApiSchema` hands them back from the wire:
 * start/end/voiceProfileId but NO `id` — the id is a client-side React key,
 * never persisted. Every station using "By time of day" loads its saved
 * rotation into the studio in this shape.
 */
function Harness({
    windows,
}: {
    readonly windows: ReadonlyArray<{
        start: string
        end: string
        voiceProfileId: string
    }>
}) {
    const form = useForm<ScheduleFormData>({
        defaultValues: {
            marketId: 'sydney',
            name: 'Sydney',
            station: 'KIIS 90s',
            timezone: 'Australia/Sydney',
            enabled: false,
            scheduleMode: 'specific',
            daysOfWeek: [1, 2, 3, 4, 5],
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 60,
            specificTimes: ['08:00'],
            content: { ...DEFAULT_CONTENT_SELECTION },
            voiceRotationMode: 'timeOfDay',
            voiceRotationIds: [],
            voiceWindows: windows.map((w) => ({
                ...w,
            })) as ScheduleFormData['voiceWindows'],
            voiceProfileId: 'vp-default',
        } as ScheduleFormData,
    })
    return (
        <TooltipProvider>
            <Form {...form}>
                <VoiceRotationEditor form={form} />
            </Form>
        </TooltipProvider>
    )
}

describe('VoiceRotationEditor · time-of-day windows loaded from the wire', () => {
    it('renders duplicate loaded windows without duplicate React keys', () => {
        // A stored rotation could carry two identical windows (same start,
        // end and voice) — the wire schema does not forbid it. Their
        // start|end|voice composite was used as the row key, so both rows
        // rendered with the same key, which is exactly the duplicate-key
        // shape that orphaned chip DOM nodes in the reference bug.
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                ]}
            />
        )

        expect(
            errorSpy.mock.calls.some((call) =>
                String(call[0]).includes('same key')
            )
        ).toBe(false)
        errorSpy.mockRestore()

        // Both rows are present and independently removable.
        expect(screen.getAllByLabelText('Window 1 start')).toHaveLength(1)
        expect(screen.getAllByLabelText('Window 2 start')).toHaveLength(1)
        expect(
            screen.getAllByRole('button', { name: 'Remove window' })
        ).toHaveLength(2)
    })

    it('keeps focus while editing a window whose row key is a duplicate', () => {
        // With duplicate keys React reuses the wrong fiber on update: the
        // focused input is torn down mid-edit, so the operator can only type
        // one character (or one stepper click) before focus drops.
        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                ]}
            />
        )

        const start = screen.getByLabelText('Window 1 start')
        start.focus()
        fireEvent.change(start, { target: { value: '07:00' } })

        // Editing a duplicate-keyed row must not orphan the old DOM node —
        // the reference bug's signature is a ghost row that never unmounts.
        expect(screen.getAllByLabelText(/^Window \d start$/)).toHaveLength(2)
        expect(start).toHaveFocus()
        expect(screen.getByLabelText('Window 1 start')).toHaveValue('07:00')
    })

    it('removes one duplicate window without losing the other', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                    { start: '06:00', end: '10:00', voiceProfileId: 'vp-1' },
                ]}
            />
        )

        await user.click(
            screen.getAllByRole('button', { name: 'Remove window' })[1]
        )

        expect(
            screen.getAllByRole('button', { name: 'Remove window' })
        ).toHaveLength(1)
        expect(screen.getByLabelText('Window 1 start')).toHaveValue('06:00')
    })
})
