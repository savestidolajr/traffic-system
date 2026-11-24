import { useState } from 'react'
import { toast } from 'sonner'
import { toastAction } from '@/lib/toast-action'
import { useUpdateSchedule, useDeleteSchedule } from '../api/schedule-hooks'
import {
    BookendRotationPayload,
    MarketSchedule,
    ScheduleFormData,
    VoiceRotationPayload,
} from '../types'
import {
    buildCronFromCalendar,
    detectCronLoss,
    parseCronToCalendar,
    type CronLossReport,
} from '../lib/cron-calendar'

/**
 * Builds the voice-rotation payload from the form. Single is sent explicitly (not omitted)
 * so switching a station back to a single voice clears any previous rotation server-side.
 */
function buildVoiceRotationPayload(
    formData: ScheduleFormData
): VoiceRotationPayload {
    if (formData.voiceRotationMode === 'roundRobin') {
        return {
            mode: 'roundRobin',
            voiceProfileIds: formData.voiceRotationIds,
        }
    }
    if (formData.voiceRotationMode === 'timeOfDay') {
        return {
            mode: 'timeOfDay',
            windows: formData.voiceWindows.map((w) => ({
                start: w.start,
                end: w.end,
                // Re-send the ids the editor cannot show alongside the one it
                // can. This PUT replaces the whole policy, so sending only
                // `voiceProfileId` silently deleted the rest of a window's
                // rotation set on any unrelated edit.
                voiceProfileIds: [
                    w.voiceProfileId,
                    ...(w.extraVoiceProfileIds ?? []),
                ],
            })),
        }
    }
    return { mode: 'single' }
}

/**
 * Builds the bookend-rotation payload from the form. Single is sent explicitly
 * (not omitted) so switching a station back to a single bookend clears any
 * previous rotation server-side — same reasoning as the voice one.
 */
function buildBookendRotationPayload(
    formData: ScheduleFormData
): BookendRotationPayload {
    const mode = formData.bookendRotationMode
    if (mode === 'roundRobin') {
        return { mode: 'roundRobin', bookendIds: formData.bookendRotationIds }
    }
    if (mode === 'timeOfDay') {
        return {
            mode: 'timeOfDay',
            windows: formData.bookendWindows.map((w) => ({
                start: w.start,
                end: w.end,
                // Re-send the ids the editor cannot show alongside the one it
                // can — the PUT replaces the whole policy (see voice above).
                bookendIds: [w.bookendId, ...(w.extraBookendIds ?? [])],
            })),
        }
    }
    return { mode: 'single' }
}

/**
 * Builds the token-override record, trimming both sides and dropping blank
 * rows. The backend skips empty overrides too, so this is belt-and-braces.
 */
function buildBookendTokensPayload(
    formData: ScheduleFormData
): Record<string, string> {
    const record: Record<string, string> = {}
    for (const row of formData.bookendTokens) {
        const key = row.key.trim()
        const value = row.value.trim()
        if (key && value) record[key] = value
    }
    return record
}

/**
 * The cron a save would produce from the schedule as loaded — i.e. the value the
 * studio rebuilds when the operator never touches the day/time step.
 *
 * `parseCronToCalendar` is lossy for the per-day compound expressions ARN actually
 * runs (dev stores day-specific schedules with distinct weekday and weekend times:
 * adelaide 45 lines, brisbane 44, perth 43), while the calendar model is one
 * time SET plus the UNION of days. Rebuilding it unconditionally therefore rewrote a
 * pattern nobody edited, and the flattened result fires Saturday's 06:58 alongside the
 * weekday 06:59 — a 1-minute gap the backend's 2-minute floor rejects, so editing only
 * the target duration, voice or bookends failed outright with
 * "Cron must not schedule bulletins more often than every 2 minutes"
 * (reported on dev 2026-08-10; the stored expression's real tightest gap is 10 minutes).
 */
function loadedPatternCron(schedule: MarketSchedule | null): string | null {
    if (!schedule?.cronExpression) return null
    return (
        buildCronFromCalendar(parseCronToCalendar(schedule.cronExpression))
            .cronExpression ?? null
    )
}

export interface FlattenCronConfirmState {
    readonly open: boolean
    readonly description: string
    readonly onConfirm: () => Promise<void>
    readonly onCancel: () => void
}

export function useScheduleActions() {
    const updateSchedule = useUpdateSchedule()
    const deleteSchedule = useDeleteSchedule()

    const [editingSchedule, setEditingSchedule] =
        useState<MarketSchedule | null>(null)
    const [viewingSchedule, setViewingSchedule] =
        useState<MarketSchedule | null>(null)
    const [isCreating, setIsCreating] = useState(false)

    // A save awaiting the operator's explicit OK to write a flattened
    // expression over a day-specific stored cron. Holds the form data so the
    // PUT can run unchanged once confirmed.
    const [pendingFlattenConfirm, setPendingFlattenConfirm] = useState<{
        report: CronLossReport
        formData: ScheduleFormData
    } | null>(null)

    const handleView = (schedule: MarketSchedule) => {
        setViewingSchedule(schedule)
        setEditingSchedule(null)
        setIsCreating(false)
    }

    const handleEdit = (schedule: MarketSchedule) => {
        setEditingSchedule(schedule)
        setViewingSchedule(null)
        setIsCreating(false)
    }

    const handleDuplicate = (schedule: MarketSchedule) => {
        setEditingSchedule({
            ...schedule,
            marketId: '',
            name: schedule.name + ' (Copy)',
            enabled: false,
            // A duplicate starts held back as well as paused: a copy inherits the original's
            // market codes and file name, so arming it by accident would have two stations
            // writing one playout file.
            playoutEnabled: false,
        })
        setViewingSchedule(null)
        setIsCreating(true)
    }

    const handleCreate = () => {
        setIsCreating(true)
        setEditingSchedule(null)
    }

    const handleCancel = () => {
        setEditingSchedule(null)
        setViewingSchedule(null)
        setIsCreating(false)
    }

    const handleSubmit = async (
        formData: ScheduleFormData,
        flattenConfirmed = false
    ) => {
        const cronResult = buildCronFromCalendar({
            scheduleMode: formData.scheduleMode,
            daysOfWeek: formData.daysOfWeek,
            startTime: formData.startTime,
            endTime: formData.endTime,
            intervalMinutes: formData.intervalMinutes,
            specificTimes: formData.specificTimes,
        })
        if (!cronResult.cronExpression) {
            // THROW, never return: `ScheduleStudio.handleSave` only keeps the
            // studio open when `onSubmit` rejects. Returning here resolved
            // normally, so the studio closed exactly as it does on success
            // while nothing had been sent — the operator's whole edit vanished.
            // The form schema now rejects an inexpressible calendar up front, so
            // this is the backstop for any future path that yields a null cron.
            const message =
                cronResult.error ?? 'Unable to generate a valid schedule cron'
            toast.error(message)
            throw new Error(message)
        }

        // Omit the pattern ONLY when this request is a true PATCH of the record
        // it was loaded from. `PUT /schedules/{marketId}` upserts: if the id has
        // no stored schedule the backend falls through to its create path, which
        // REQUIRES a cron and otherwise fails with "Schedule does not exist and
        // create data is missing." Market ID is an editable field, so an operator
        // renaming it turns the save into a create at the new id — that must
        // still carry the cron.
        const isPatchOfLoadedRecord =
            !isCreating &&
            editingSchedule !== null &&
            formData.marketId === editingSchedule.marketId
        const storedCron = editingSchedule?.cronExpression ?? null
        const patternMoved =
            !isPatchOfLoadedRecord ||
            cronResult.cronExpression !== loadedPatternCron(editingSchedule)

        // Day-specific guard: the calendar model is a time SET x a UNION of
        // days, so a schedule-step edit over a stored per-day compound would
        // silently flatten it into every time on every day. The flattened cron
        // still passes the backend's 2-minute floor, so the save would succeed
        // and the loss would be permanent with no undo. Require an explicit
        // confirmation before the PUT. Non-schedule edits never reach here:
        // their rebuilt cron equals the loaded one, so `patternMoved` is false
        // and no cron is sent at all.
        if (
            patternMoved &&
            isPatchOfLoadedRecord &&
            storedCron &&
            !flattenConfirmed
        ) {
            const report = detectCronLoss(storedCron)
            if (report.lossy) {
                setPendingFlattenConfirm({ report, formData })
                // Reject WITHOUT a toast: the studio keeps the form open (same
                // contract as the null-cron THROW above) and the confirmation
                // dialog explains the consequence. Resolving here would close
                // the studio with nothing sent, discarding the operator's edit.
                throw new Error('FLATTEN_CONFIRM_REQUIRED')
            }
        }

        const result = await toastAction(
            () =>
                updateSchedule.mutateAsync({
                    marketId: formData.marketId,
                    schedule: {
                        name: formData.name,
                        station: formData.station,
                        timezone: formData.timezone,
                        ...(patternMoved
                            ? { cronExpression: cronResult.cronExpression! }
                            : {}),
                        enabled: formData.enabled,
                        playoutEnabled: formData.playoutEnabled,
                        coverage: {
                            latitude: formData.latitude,
                            longitude: formData.longitude,
                            radiusKm: formData.radiusKm,
                            shapeType: formData.shapeType,
                            west: formData.west,
                            south: formData.south,
                            east: formData.east,
                            north: formData.north,
                            corridorPoints: formData.corridorPoints,
                            corridorRadiusMeters: formData.corridorRadiusMeters,
                            polygonPoints: formData.polygonPoints,
                            weatherCoverage: formData.weatherCoverage,
                            fuelCoverage: formData.fuelCoverage,
                        },
                        content: formData.content,
                        fillOrder: formData.fillOrder,
                        // Always sent, including as an EMPTY calendar: that is how an
                        // operator clears every day type and date back to the plain
                        // weekly clock. Omitting it would make the clear a no-op.
                        calendar: formData.calendar ?? {
                            patterns: [],
                            assignments: [],
                        },
                        targetDurationSeconds: formData.targetDurationSeconds,
                        notifyBeforePlayoutMinutes:
                            formData.notifyBeforePlayoutMinutes,
                        escalationBeforePlayoutMinutes:
                            formData.escalationBeforePlayoutMinutes,
                        voiceProfileId: formData.voiceProfileId || undefined,
                        deliveryStyleId: formData.deliveryStyleId,
                        playbookId: formData.playbookId ?? '',
                        pronunciationDictionaryId:
                            formData.pronunciationDictionaryId,
                        bookendId: formData.bookendId,
                        bookendTokens: buildBookendTokensPayload(formData),
                        bookendRotation: buildBookendRotationPayload(formData),
                        voiceRotation: buildVoiceRotationPayload(formData),
                    },
                }),
            editingSchedule ? 'Schedule updated' : 'Schedule created',
            'Failed to save schedule'
        )
        if (result !== undefined) handleCancel()
    }

    /** Re-runs the pending save with the flatten guard satisfied. */
    const handleConfirmFlatten = async () => {
        if (!pendingFlattenConfirm) return
        const { formData } = pendingFlattenConfirm
        setPendingFlattenConfirm(null)
        await handleSubmit(formData, true)
    }

    /** Declines the flatten: no PUT, the studio keeps the operator's edits. */
    const handleCancelFlatten = () => setPendingFlattenConfirm(null)

    const handleDelete = (marketId: string) =>
        toastAction(
            () => deleteSchedule.mutateAsync(marketId),
            'Schedule deleted',
            'Failed to delete schedule'
        )

    const handleToggleEnabled = async (
        schedule: MarketSchedule
    ): Promise<void> => {
        await toastAction(
            () =>
                updateSchedule.mutateAsync({
                    marketId: schedule.marketId,
                    schedule: { enabled: !schedule.enabled },
                }),
            schedule.enabled ? 'Schedule paused' : 'Schedule enabled',
            'Failed to toggle schedule'
        )
    }

    /**
     * Arms or holds back this station's playout in THIS environment. Deliberately separate
     * from {@link handleToggleEnabled}: pausing a station stops it generating, whereas this
     * leaves it generating and publishing while nothing reaches the playout share. That is
     * what lets two environments run side by side with only one of them airing a station.
     */
    const handleTogglePlayout = async (
        schedule: MarketSchedule
    ): Promise<void> => {
        await toastAction(
            () =>
                updateSchedule.mutateAsync({
                    marketId: schedule.marketId,
                    schedule: { playoutEnabled: !schedule.playoutEnabled },
                }),
            schedule.playoutEnabled
                ? 'Send to playout off — this station will not reach playout from here'
                : 'Send to playout on — published bulletins will reach playout',
            'Failed to toggle send to playout'
        )
    }

    return {
        editingSchedule,
        viewingSchedule,
        isCreating,
        showEditSheet: isCreating || !!editingSchedule,
        showDetailSheet: !!viewingSchedule,
        isSubmitting: updateSchedule.isPending,
        handleView,
        handleEdit,
        handleDuplicate,
        handleCreate,
        handleCancel,
        handleSubmit,
        handleDelete,
        handleToggleEnabled,
        handleTogglePlayout,
        // Pending confirmation for writing a flattened cron over a
        // day-specific stored one. Non-null only while a save waits on the
        // operator's explicit OK; ScheduleStudio renders the dialog.
        flattenConfirm: pendingFlattenConfirm
            ? {
                  open: true,
                  description: pendingFlattenConfirm.report.summary,
                  onConfirm: handleConfirmFlatten,
                  onCancel: handleCancelFlatten,
              }
            : null,
    }
}
