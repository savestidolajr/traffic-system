// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { Form } from '@/components/ui/form'
import { TooltipProvider } from '@/components/ui/tooltip'
import { SchedulingSection } from './SchedulingSection'
import type { ScheduleFormData } from '../../types'
import { DEFAULT_CONTENT_SELECTION } from '../../types'

afterEach(cleanup)

/**
 * Shape of a real metro schedule (Sydney KIIS 90s on dev): the same minute
 * recurs on the weekday cron line AND the Sat/Sun lines, so a `specificTimes`
 * field can arrive carrying repeats. Duplicate React keys used to orphan chip
 * DOM nodes, and every click on an orphan was inert — removal stopped working
 * after the first click.
 */
const DUPLICATED_TIMES = [
    '05:59',
    '05:59',
    '06:29',
    '07:59',
    '07:59',
    '07:59',
    '08:59',
    '08:59',
]

function Harness({ times }: { readonly times: string[] }) {
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
            specificTimes: times,
            content: { ...DEFAULT_CONTENT_SELECTION },
        } as ScheduleFormData,
    })
    return (
        <TooltipProvider>
            <Form {...form}>
                <SchedulingSection form={form} />
                {/* The studio gates Save on `isDirty`; mirror that here so a
                    change the gate cannot see fails the test. */}
                <output data-testid="dirty">
                    {form.formState.isDirty ? 'dirty' : 'clean'}
                </output>
            </Form>
        </TooltipProvider>
    )
}

const removeButtons = () =>
    screen
        .queryAllByRole('button', { name: /^Remove \d{2}:\d{2}$/ })
        .map((b) => b.getAttribute('aria-label')!.replace('Remove ', ''))

describe('SchedulingSection · specific times', () => {
    it('shows one chip per distinct time when the parsed cron repeats a time', () => {
        render(<Harness times={DUPLICATED_TIMES} />)
        expect(removeButtons()).toEqual(['05:59', '06:29', '07:59', '08:59'])
    })

    it('empties the list one click at a time when times arrived duplicated', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        for (let remaining = 4; remaining > 0; remaining--) {
            expect(removeButtons()).toHaveLength(remaining)
            await user.click(
                screen.getAllByRole('button', {
                    name: /^Remove \d{2}:\d{2}$/,
                })[0]
            )
        }

        expect(removeButtons()).toEqual([])
    })

    it('retires only the clicked time', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        await user.click(screen.getByRole('button', { name: 'Remove 07:59' }))

        expect(removeButtons()).toEqual(['05:59', '06:29', '08:59'])
    })

    it('places one track marker per distinct time', () => {
        render(<Harness times={DUPLICATED_TIMES} />)
        expect(
            screen.getAllByRole('button', {
                name: /^Run at .*click to remove$/,
            })
        ).toHaveLength(4)
    })

    it('rejects a custom time that is already scheduled', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        await user.type(screen.getByLabelText('Custom time'), '7:59')
        await user.click(screen.getByRole('button', { name: /^Add$/ }))

        expect(
            screen.getByText('This time is already added')
        ).toBeInTheDocument()
        expect(removeButtons()).toHaveLength(4)
    })
})

describe('SchedulingSection · the studio save gate sees the edit', () => {
    /**
     * `form.setValue` does NOT recompute dirtiness unless told to, and the
     * studio's footer is `saveable = form.formState.isDirty && ...`. Every
     * mutation below changed the value while leaving the form "clean", so a
     * specific-times schedule could be edited on screen and never saved.
     */
    it.each([
        [
            'removing a time',
            async (user: UserEvent) =>
                user.click(
                    screen.getByRole('button', { name: 'Remove 07:59' })
                ),
        ],
        [
            'adding a custom time',
            async (user: UserEvent) => {
                await user.type(screen.getByLabelText('Custom time'), '10:15')
                await user.click(screen.getByRole('button', { name: /^Add$/ }))
            },
        ],
        [
            'toggling a single day',
            async (user: UserEvent) =>
                user.click(screen.getByRole('button', { name: 'Sat' })),
        ],
        [
            'toggling a day group',
            async (user: UserEvent) =>
                user.click(screen.getByRole('button', { name: 'Wknd' })),
        ],
        [
            'switching to interval mode',
            async (user: UserEvent) =>
                user.click(screen.getByRole('tab', { name: /Interval/ })),
        ],
    ])('marks the form dirty when %s', async (_label, act) => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        expect(screen.getByTestId('dirty')).toHaveTextContent('clean')
        await act(user)
        expect(screen.getByTestId('dirty')).toHaveTextContent('dirty')
    })
})

describe('SchedulingSection · weekly and year are peer layers', () => {
    /**
     * The year calendar is a LAYER over the weekly clock, so it is a peer tab of
     * "Weekly clock", not a third sibling of "Specific times"/"Interval", which are
     * two spellings of one weekly pattern. Pin the nesting: a regression that flattens
     * them into one tab bar is exactly the IA mistake this replaced.
     */
    it('nests the weekly mode tabs inside the weekly layer, not beside the year layer', () => {
        render(<Harness times={DUPLICATED_TIMES} />)

        const layers = screen.getAllByRole('tablist')[0]!
        const layerTabs = within(layers)
            .getAllByRole('tab')
            .map((t) => t.textContent)

        expect(layerTabs).toHaveLength(2)
        expect(layerTabs[0]).toContain('Every week')
        expect(layerTabs[1]).toContain('Specific dates')
        expect(
            screen.getByRole('tab', { name: /Specific times/ })
        ).toBeInTheDocument()
    })

    /**
     * One screen: the palette doubles as the legend, and applying a day type is a
     * click on a date. There is no sub-tab bar, no "add dates" form and no table
     * repeating what the grid shows.
     */
    it('shows one screen with a palette and no sub-tabs or composer', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        await user.click(screen.getByRole('tab', { name: /Specific dates/ }))

        expect(
            screen.getByRole('button', { name: /Weekly clock/ })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: /Off air/ })
        ).toBeInTheDocument()
        // The only tablist left is the two layers.
        expect(screen.getAllByRole('tablist')).toHaveLength(1)
        expect(screen.queryByLabelText(/^From$/)).not.toBeInTheDocument()
        // Looking at the tab writes nothing.
        expect(screen.getByTestId('dirty')).toHaveTextContent('clean')
    })

    it('marks the form dirty when a day type is added, so the studio Save sees it', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        await user.click(screen.getByRole('tab', { name: /Specific dates/ }))
        await user.click(screen.getByRole('button', { name: /New day type/ }))

        expect(screen.getByTestId('dirty')).toHaveTextContent('dirty')
        // Seeded from the station's own clock rather than blank, so the operator
        // trims down from the real week.
        expect(screen.getByLabelText('Name')).toHaveValue('Day type 1')
    })

    it('applies a day type by clicking a date, and clears it on a second click', async () => {
        const user = userEvent.setup()
        render(<Harness times={DUPLICATED_TIMES} />)

        await user.click(screen.getByRole('tab', { name: /Specific dates/ }))
        await user.click(screen.getByRole('button', { name: /Off air/ }))

        const dates = screen.getAllByRole('button', {
            name: /^\d{4}-\d{2}-\d{2},/,
        })
        const target = dates[0]!
        const iso = target.getAttribute('aria-label')!.slice(0, 10)

        await user.click(target)
        expect(
            screen.getByRole('button', { name: new RegExp(`^${iso}, off air`) })
        ).toBeInTheDocument()

        await user.click(
            screen.getByRole('button', { name: new RegExp(`^${iso}, off air`) })
        )
        expect(
            screen.queryByRole('button', {
                name: new RegExp(`^${iso}, off air`),
            })
        ).not.toBeInTheDocument()
    })
})
