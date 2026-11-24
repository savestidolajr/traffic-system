// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest'
import type { ReactNode } from 'react'
import { act, cleanup, renderHook } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useScheduleActions } from './use-schedule-actions'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'
import type { MarketSchedule, ScheduleFormData } from '../types'
import { parseCronToCalendar } from '../lib/cron-calendar'

const updateSchedule = vi.hoisted(() => ({
    mutateAsync: vi.fn(async () => ({})),
    isPending: false,
}))
const deleteSchedule = vi.hoisted(() => ({
    mutateAsync: vi.fn(async () => {}),
}))

vi.mock('../api/schedule-hooks', () => ({
    useUpdateSchedule: () => updateSchedule,
    useDeleteSchedule: () => deleteSchedule,
}))

function baseForm(overrides: Partial<ScheduleFormData> = {}): ScheduleFormData {
    return {
        marketId: 'goldcoast',
        name: 'GC',
        station: 'HOT 91.1',
        timezone: 'Australia/Brisbane',
        enabled: true,
        latitude: -28,
        longitude: 153.4,
        radiusKm: 40,
        targetDurationSeconds: 30,
        notifyBeforePlayoutMinutes: 15,
        escalationBeforePlayoutMinutes: 10,
        voiceProfileId: 'vp-1',
        deliveryStyleId: '',
        pronunciationDictionaryId: '',
        bookendId: '',
        bookendRotationMode: 'single',
        bookendRotationIds: [],
        bookendWindows: [],
        bookendTokens: [],
        intervalMinutes: 15,
        daysOfWeek: [1],
        scheduleMode: 'specific',
        startTime: '06:00',
        endTime: '09:00',
        shapeType: 'circle',
        specificTimes: ['08:00'],
        content: { ...DEFAULT_CONTENT_SELECTION },
        fillOrder: ['weather', 'fuel', 'filler'],
        voiceRotationMode: 'single',
        voiceRotationIds: [],
        voiceWindows: [],
        ...overrides,
    }
}

function renderActions() {
    const queryClient = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
    return renderHook(() => useScheduleActions(), { wrapper })
}

async function submit(formData: ScheduleFormData) {
    const { result } = renderActions()
    await act(async () => {
        await result.current.handleSubmit(formData)
    })
    return updateSchedule.mutateAsync.mock.calls[0][0] as {
        schedule: Record<string, unknown>
    }
}

/** dev's KIIS 90s: one line per (day, time), tightest real gap 10 minutes. */
const PER_DAY_CRON = [
    '59 5 * * 1-5',
    '59 6 * * 1-5',
    '59 8 * * 1-5',
    '29 9 * * 1-5',
    '45 9 * * 1-5',
    '58 6 * * 6',
    '58 8 * * 6',
    '30 9 * * 6',
    '46 9 * * 0',
].join('\n')

function existingSchedule(cronExpression: string): MarketSchedule {
    return {
        marketId: 'sydney',
        name: 'Sydney KIIS 90s Traffic',
        station: 'KIIS 90s',
        timezone: 'Australia/Sydney',
        cronExpression,
        enabled: false,
        targetDurationSeconds: 30,
    } as MarketSchedule
}

/** Submits against an existing station, as the studio does after Edit. */
async function submitEdit(
    schedule: MarketSchedule,
    formData: ScheduleFormData
) {
    const { result } = renderActions()
    act(() => {
        result.current.handleEdit(schedule)
    })
    await act(async () => {
        await result.current.handleSubmit(formData)
    })
    return updateSchedule.mutateAsync.mock.calls[0][0] as {
        schedule: Record<string, unknown>
    }
}

/** The form values the studio loads for `cron`, plus any edits. */
function formForCron(
    cron: string,
    overrides: Partial<ScheduleFormData> = {}
): ScheduleFormData {
    const calendar = parseCronToCalendar(cron)
    return baseForm({
        marketId: 'sydney',
        timezone: 'Australia/Sydney',
        scheduleMode: calendar.scheduleMode,
        daysOfWeek: calendar.daysOfWeek,
        startTime: calendar.startTime,
        endTime: calendar.endTime,
        intervalMinutes: calendar.intervalMinutes,
        specificTimes: calendar.specificTimes,
        ...overrides,
    })
}

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

describe('useScheduleActions bookend submit payload', () => {
    it('converts the token override rows into a record, dropping blank rows', async () => {
        const { schedule } = await submit(
            baseForm({
                bookendId: 'bk-1',
                bookendTokens: [
                    { key: 'city', value: 'Gold Coast' },
                    { key: '  suburb  ', value: '  Southport  ' },
                    { key: '', value: '' },
                    { key: 'empty', value: '' },
                ],
            })
        )

        expect(schedule.bookendTokens).toEqual({
            city: 'Gold Coast',
            suburb: 'Southport',
        })
    })

    it('sends the bookend id and a single-mode rotation', async () => {
        const { schedule } = await submit(baseForm({ bookendId: 'bk-1' }))

        expect(schedule.bookendId).toBe('bk-1')
        // Single is sent explicitly so an earlier rotation is cleared server-side
        expect(schedule.bookendRotation).toEqual({ mode: 'single' })
    })

    it('sends a round-robin set', async () => {
        const { schedule } = await submit(
            baseForm({
                bookendRotationMode: 'roundRobin',
                bookendRotationIds: ['bk-1', 'bk-2'],
            })
        )

        expect(schedule.bookendRotation).toEqual({
            mode: 'roundRobin',
            bookendIds: ['bk-1', 'bk-2'],
        })
    })

    it('carries extra window ids through a time-of-day payload', async () => {
        const { schedule } = await submit(
            baseForm({
                bookendRotationMode: 'timeOfDay',
                bookendWindows: [
                    {
                        start: '06:00',
                        end: '10:00',
                        bookendId: 'bk-1',
                        extraBookendIds: ['bk-2', 'bk-3'],
                    },
                ],
            })
        )

        expect(schedule.bookendRotation).toEqual({
            mode: 'timeOfDay',
            windows: [
                {
                    start: '06:00',
                    end: '10:00',
                    bookendIds: ['bk-1', 'bk-2', 'bk-3'],
                },
            ],
        })
    })
})

describe('useScheduleActions cron preservation', () => {
    it('omits the cron when the operator only changed the target duration', async () => {
        const { schedule } = await submitEdit(
            existingSchedule(PER_DAY_CRON),
            formForCron(PER_DAY_CRON, { targetDurationSeconds: 22 })
        )

        // The calendar model cannot express one time set per weekday, so rebuilding
        // it would merge Saturday's 06:58 into the weekday days and fire it 1 minute
        // before 06:59 — rejected by the backend's 2-minute floor.
        expect(schedule).not.toHaveProperty('cronExpression')
        expect(schedule.targetDurationSeconds).toBe(22)
    })

    it('sends the rebuilt cron when the operator changes the times', async () => {
        const { schedule } = await submitEdit(
            existingSchedule('0 7,8 * * 1,2,3,4,5'),
            formForCron('0 7,8 * * 1,2,3,4,5', {
                scheduleMode: 'specific',
                daysOfWeek: [1, 2, 3, 4, 5],
                specificTimes: ['07:00', '08:00', '09:00'],
            })
        )

        expect(schedule.cronExpression).toBe('0 7,8,9 * * 1,2,3,4,5')
    })

    it('sends the cron for a brand-new station', async () => {
        const { schedule } = await submit(baseForm())

        expect(schedule.cronExpression).toBe('0 8 * * 1')
    })

    it('sends the cron when the operator renames the market id, because that save is a create', async () => {
        // `PUT /schedules/{marketId}` upserts. Renaming the (editable) Market ID
        // targets an id with no stored schedule, so the backend takes its create
        // path — omitting the cron there failed the whole save with "Schedule
        // does not exist and create data is missing."
        const { schedule } = await submitEdit(
            existingSchedule(PER_DAY_CRON),
            formForCron(PER_DAY_CRON, { marketId: 'sydney-kiis90s' })
        )

        expect(schedule).toHaveProperty('cronExpression')
        expect(typeof schedule.cronExpression).toBe('string')
    })

    it('still omits the cron when only the friendly name changed', async () => {
        const { schedule } = await submitEdit(
            existingSchedule(PER_DAY_CRON),
            formForCron(PER_DAY_CRON, { name: 'Sydney KIIS 90s Drive' })
        )

        expect(schedule).not.toHaveProperty('cronExpression')
        expect(schedule.name).toBe('Sydney KIIS 90s Drive')
    })
})

describe('useScheduleActions flatten confirmation', () => {
    /** A schedule-step edit over the lossy PER_DAY_CRON: remove one time. */
    const flatteningForm = () =>
        formForCron(PER_DAY_CRON, {
            scheduleMode: 'specific',
            daysOfWeek: [1, 2, 3, 4, 5],
            specificTimes: ['07:00', '08:00'],
        })

    /** Starts an edit of the lossy schedule and submits the given form. */
    async function submitLossyEdit(formData: ScheduleFormData) {
        const { result } = renderActions()
        act(() => {
            result.current.handleEdit(existingSchedule(PER_DAY_CRON))
        })
        let error: unknown
        await act(async () => {
            try {
                await result.current.handleSubmit(formData)
            } catch (e) {
                error = e
            }
        })
        return { result, error }
    }

    it('does not PUT until confirmed when a schedule-step edit would flatten a day-specific cron', async () => {
        const { result, error } = await submitLossyEdit(flatteningForm())

        // Rejected so the studio keeps the form open, but no cron was sent.
        expect(error).toBeInstanceOf(Error)
        expect(updateSchedule.mutateAsync).not.toHaveBeenCalled()
        expect(result.current.flattenConfirm?.open).toBe(true)
        expect(result.current.flattenConfirm?.description).toContain(
            'cannot be undone'
        )
    })

    it('PUTs the flattened cron after the operator confirms', async () => {
        const { result } = await submitLossyEdit(flatteningForm())
        expect(result.current.flattenConfirm?.open).toBe(true)

        await act(async () => {
            await result.current.flattenConfirm!.onConfirm()
        })

        expect(updateSchedule.mutateAsync).toHaveBeenCalledTimes(1)
        const { schedule } = updateSchedule.mutateAsync.mock.calls[0][0]
        expect(schedule.cronExpression).toBe('0 7,8 * * 1,2,3,4,5')
        expect(result.current.flattenConfirm).toBeNull()
    })

    it('sends nothing and leaves the form as-is when the operator declines', async () => {
        const { result } = await submitLossyEdit(flatteningForm())

        act(() => {
            result.current.flattenConfirm!.onCancel()
        })

        expect(updateSchedule.mutateAsync).not.toHaveBeenCalled()
        expect(result.current.flattenConfirm).toBeNull()
        // The studio is still open on the loaded record with its edits intact.
        expect(result.current.editingSchedule?.marketId).toBe('sydney')
        expect(result.current.showEditSheet).toBe(true)
    })

    it('shows no confirmation and sends no cron for a non-schedule edit', async () => {
        const { result } = await submitLossyEdit(
            formForCron(PER_DAY_CRON, { targetDurationSeconds: 22 })
        )

        expect(result.current.flattenConfirm).toBeNull()
        expect(updateSchedule.mutateAsync).toHaveBeenCalledTimes(1)
        const { schedule } = updateSchedule.mutateAsync.mock.calls[0][0]
        expect(schedule).not.toHaveProperty('cronExpression')
        expect(schedule.targetDurationSeconds).toBe(22)
    })

    it('shows no confirmation for a schedule-step edit over a representable cron', async () => {
        const { result } = renderActions()
        act(() => {
            result.current.handleEdit(existingSchedule('0 7,8 * * 1,2,3,4,5'))
        })
        await act(async () => {
            await result.current.handleSubmit(
                formForCron('0 7,8 * * 1,2,3,4,5', {
                    scheduleMode: 'specific',
                    daysOfWeek: [1, 2, 3, 4, 5],
                    specificTimes: ['07:00', '08:00', '09:00'],
                })
            )
        })

        expect(result.current.flattenConfirm).toBeNull()
        expect(updateSchedule.mutateAsync).toHaveBeenCalledTimes(1)
    })
})
