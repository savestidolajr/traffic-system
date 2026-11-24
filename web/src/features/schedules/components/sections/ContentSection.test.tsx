// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { Form } from '@/components/ui/form'
import { ContentSection } from './ContentSection'
import type { ScheduleFormData } from '../../types'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'

afterEach(cleanup)

function Harness({
    content,
}: {
    readonly content: ScheduleFormData['content']
}) {
    const form = useForm<ScheduleFormData>({
        defaultValues: {
            marketId: 'sydney',
            name: 'Sydney',
            station: 'KIIS 90s',
            timezone: 'Australia/Sydney',
            enabled: false,
            scheduleMode: 'specific',
            daysOfWeek: [1],
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 60,
            specificTimes: ['08:00'],
            content: { ...content },
            fillOrder: ['weather', 'fuel', 'filler'],
        } as ScheduleFormData,
    })
    return (
        <Form {...form}>
            <ContentSection form={form} />
            <output data-testid="content-json">
                {JSON.stringify(form.watch('content'))}
            </output>
        </Form>
    )
}

const contentJson = (): ScheduleFormData['content'] =>
    JSON.parse(screen.getByTestId('content-json').textContent ?? '{}')

const weatherCardButton = () =>
    screen.getByRole('button', { name: /^WeatherCurrent conditions/ })
const fuelCardButton = () =>
    screen.getByRole('button', { name: /^FuelCheapest unleaded/ })

describe('ContentSection always include and running order', () => {
    it('shows always-include switches for supplementary sources and a traffic order switch', async () => {
        const user = userEvent.setup()
        render(<Harness content={{ ...DEFAULT_CONTENT_SELECTION }} />)

        // Traffic has its running-order control; supplementary sources do not.
        expect(screen.getAllByRole('switch')).toHaveLength(1)

        await user.click(weatherCardButton())
        expect(screen.getAllByRole('switch')).toHaveLength(2)

        await user.click(fuelCardButton())
        expect(screen.getAllByRole('switch')).toHaveLength(3)

        // Deselecting weather hides its switch and clears its required flag.
        await user.click(weatherCardButton())
        expect(screen.getAllByRole('switch')).toHaveLength(2)
        expect(contentJson().weather).toBe(false)
        expect(contentJson().weatherRequired).toBe(false)
    })

    it('clicking the switch does not toggle the source', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                content={{
                    traffic: false,
                    weather: true,
                    fuel: false,
                    weatherRequired: false,
                    fuelRequired: false,
                }}
            />
        )

        const toggle = screen.getByRole('switch', { name: 'Always include' })
        expect(toggle).not.toBeChecked()

        await user.click(toggle)
        expect(toggle).toBeChecked()
        expect(contentJson().weather).toBe(true)
        expect(contentJson().weatherRequired).toBe(true)

        await user.click(toggle)
        expect(toggle).not.toBeChecked()
        expect(contentJson().weather).toBe(true)
        expect(contentJson().weatherRequired).toBe(false)
    })

    it('deselecting a source clears its always-include flag', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                content={{
                    traffic: false,
                    weather: true,
                    fuel: false,
                    weatherRequired: true,
                    fuelRequired: false,
                }}
            />
        )
        expect(screen.getByRole('switch')).toBeChecked()

        await user.click(weatherCardButton())

        expect(contentJson().weather).toBe(false)
        expect(contentJson().weatherRequired).toBe(false)
    })

    it('the always-include switch is keyboard reachable', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                content={{
                    traffic: false,
                    weather: true,
                    fuel: false,
                    weatherRequired: false,
                    fuelRequired: false,
                }}
            />
        )

        // Tab order: "If a source is down" trigger, traffic card, weather
        // card, then the weather card's switch.
        for (let i = 0; i < 4; i++) await user.tab()
        expect(screen.getByRole('switch')).toHaveFocus()
    })
    it('reorders enabled segments and omits disabled segments from the list', async () => {
        const user = userEvent.setup()
        render(
            <Harness
                content={{
                    traffic: true,
                    weather: true,
                    fuel: true,
                    weatherRequired: false,
                    fuelRequired: false,
                    segmentOrder: ['Traffic', 'Weather', 'Fuel'],
                }}
            />
        )

        const runningOrder = screen
            .getByRole('heading', { name: 'Running order' })
            .closest('section')
        if (!runningOrder) throw new Error('Running order section not found')
        await user.click(
            within(runningOrder).getByRole('button', {
                name: 'Move Weather up',
            })
        )
        expect(contentJson().segmentOrder).toEqual([
            'Weather',
            'Traffic',
            'Fuel',
        ])

        cleanup()
        render(
            <Harness
                content={{
                    traffic: true,
                    weather: false,
                    fuel: true,
                    weatherRequired: false,
                    fuelRequired: false,
                    segmentOrder: ['Weather', 'Traffic', 'Fuel'],
                }}
            />
        )
        const secondRunningOrder = screen
            .getByRole('heading', { name: 'Running order' })
            .closest('section')
        if (!secondRunningOrder)
            throw new Error('Running order section not found')
        expect(
            within(secondRunningOrder).queryByRole('button', {
                name: /Move Weather/,
            })
        ).toBeNull()
    })
})
