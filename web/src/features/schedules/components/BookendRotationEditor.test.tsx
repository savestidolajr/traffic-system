// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { Form } from '@/components/ui/form'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'
import { BookendRotationEditor } from './BookendRotationEditor'
import type { ScheduleFormData } from '../types'

vi.mock('@/features/bookends/api/bookend-hooks', () => ({
    useBookends: () => ({ data: { bookends: [] } }),
}))

afterEach(cleanup)

function Harness({
    windows,
}: {
    readonly windows: ReadonlyArray<{
        start: string
        end: string
        bookendId: string
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
            bookendRotationMode: 'timeOfDay',
            bookendRotationIds: [],
            bookendWindows: windows.map((w) => ({
                ...w,
            })) as ScheduleFormData['bookendWindows'],
        } as ScheduleFormData,
    })
    return (
        <Form {...form}>
            <BookendRotationEditor form={form} />
        </Form>
    )
}

describe('BookendRotationEditor · time-of-day windows loaded from the wire', () => {
    it('renders duplicate loaded windows without duplicate React keys', () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
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
        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                ]}
            />
        )

        const start = screen.getByLabelText('Window 1 start')
        start.focus()
        fireEvent.change(start, { target: { value: '07:00' } })

        // Editing must not orphan the old DOM node — the reference bug's
        // signature is a ghost row that never unmounts.
        expect(screen.getAllByLabelText(/^Window \d start$/)).toHaveLength(2)
        expect(start).toHaveFocus()
        expect(screen.getByLabelText('Window 1 start')).toHaveValue('07:00')
    })

    it('removes one duplicate window without losing the other', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                windows={[
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
                    { start: '06:00', end: '10:00', bookendId: 'bk-1' },
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
