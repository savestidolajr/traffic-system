import { lazy, Suspense, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toast } from 'sonner'
import {
    Radio,
    CalendarClock,
    Bell,
    Mic,
    AudioLines,
    MapPin,
    Layers,
    History,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Form } from '@/components/ui/form'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { StudioSubmitButton } from '@/components/studio/StudioSubmitButton'
import {
    StudioDialog,
    StudioBody,
    StudioRail,
    StudioCanvas,
    StudioErrorSummary,
    StudioFooter,
    type StudioStep,
} from '@/components/studio/Studio'
import { useStudioStepGate } from '@/components/studio/use-studio-step-gate'
import { VoiceRotationEditor } from './VoiceRotationEditor'
import { SpeechSection } from './sections/SpeechSection'
import { PlaybookSelector } from '@/features/playbooks/components/PlaybookSelector'
import { useCoarseNow } from '@/hooks/use-second-tick'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { useActivityLog } from '@/features/activity/api/activity-hooks'
import { ActivityFeed } from '@/features/activity/components/ActivityFeed'
import { useIsAdmin } from '@/hooks/use-current-user'
import {
    DEFAULT_COVERAGE,
    DEFAULT_CONTENT_SELECTION,
    DEFAULT_FILL_ORDER,
} from '@/lib/api/schemas/shared'
import { formatCoverageSummary } from '@/components/map-picker/coverage-shapes'
import type { MarketSchedule, ScheduleFormData } from '../types'
import { ScheduleFormSchema } from '../form-schemas'
import { parseCronToCalendar } from '../lib/cron-calendar'
import {
    BasicInfoSection,
    SchedulingSection,
    AlertTimesSection,
    CoverageSection,
    ContentSection,
} from './sections'
import {
    type ScheduleStudioStepId,
    type StepId,
} from '../lib/schedule-studio-helpers'
import type { FlattenCronConfirmState } from '../hooks/use-schedule-actions'
import {
    formatBookendBlocker,
    useBookendTokenBlockers,
} from '../lib/bookend-token-gate'
import { useScheduleStudioSummaries } from './useScheduleStudioSummaries'

// Lazy + render-on-open: keeps the Radix alert-dialog out of the /stations initial
// bundle and off the first render (it only loads when a discard is actually requested),
// which restores the route's total-blocking-time under the perf budget.
const ConfirmDialog = lazy(() =>
    import('@/components/ConfirmDialog').then((m) => ({
        default: m.ConfirmDialog,
    }))
)

// ---------------------------------------------------------------------------
// Default form values
// ---------------------------------------------------------------------------
const DEFAULT_TARGET_DURATION_SEC = 30
const DEFAULT_NOTIFY_MINUTES = 15
const DEFAULT_ESCALATION_MINUTES = 10
const DEFAULT_ENABLED = true

const DAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

function summariseDays(days: readonly number[]): string {
    if (days.length === 0) return 'No days'
    if (days.length === 7) return 'Every day'
    const sorted = [...days].sort((a, b) => a - b)
    if (sorted.join(',') === '1,2,3,4,5') return 'Mon-Fri'
    if (sorted.join(',') === '0,6') return 'Weekends'
    return sorted.map((d) => DAY_LABELS[d] ?? String(d)).join(' ')
}

interface ScheduleStudioProps {
    readonly schedule: MarketSchedule | null
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly onSubmit: (data: ScheduleFormData) => Promise<void>
    readonly isSubmitting: boolean
    /** Step to open on first render — e.g. jump straight to voice/coverage. */
    readonly initialStep?: ScheduleStudioStepId
    /**
     * Pending confirmation for writing a flattened cron over a day-specific
     * stored one, from `useScheduleActions`. Rendered as a destructive-save
     * confirm; declining leaves the form untouched.
     */
    readonly flattenConfirm?: FlattenCronConfirmState | null
}

/**
 * The schedule editor as a studio: four decisions, four instruments —
 * station identity, the day×time pattern, the voice, and the coverage
 * map (full canvas). The footer carries the live consequences: the next
 * runs the current form values would produce.
 */
export function ScheduleStudio({
    schedule,
    open,
    onOpenChange,
    onSubmit,
    isSubmitting,
    initialStep,
    flattenConfirm,
}: ScheduleStudioProps) {
    const initialCalendar = parseCronToCalendar(
        schedule?.cronExpression ?? null
    )
    const [step, setStep] = useState<StepId>(initialStep ?? 'station')
    const rotation = schedule?.voiceRotation ?? null
    const bookendRotation = schedule?.bookendRotation ?? null

    // Mirrors ScheduleDetailStudio's history query: 366 days is the backend's
    // own max range, approximating "since the beginning". Only fires for an
    // existing station while the dialog is open -- a "New station" has no
    // activity yet, and the query must not fire on an unmounted/closed studio.
    // GET /api/activity is Admin-only in RouteRoleMatrix, and this studio is now operator-
    // reachable (Operators own PUT /schedules/{marketId}). Firing it for an Operator returns
    // 403, which the feed renders as "No activity recorded" -- a 403 disguised as absence, so
    // the step is withheld instead.
    const isAdmin = useIsAdmin()
    const coarseNowMs = useCoarseNow(300)
    const { data: activityData } = useActivityLog(
        {
            resource: 'schedules',
            entityId: schedule?.marketId,
            toUtc: new Date(coarseNowMs).toISOString(),
            fromUtc: new Date(
                coarseNowMs - 366 * 24 * 60 * 60 * 1000
            ).toISOString(),
        },
        open && !!schedule && isAdmin
    )

    const form = useForm<ScheduleFormData>({
        resolver: zodResolver(ScheduleFormSchema),
        mode: 'onChange',
        defaultValues: {
            marketId: schedule?.marketId ?? '',
            name: schedule?.name ?? '',
            station: schedule?.station ?? '',
            timezone: schedule?.timezone ?? 'Australia/Sydney',
            enabled: schedule?.enabled ?? DEFAULT_ENABLED,
            // Armed by default, matching the backend: an operator standing a station up in
            // a held-back environment turns this off explicitly.
            playoutEnabled: schedule?.playoutEnabled ?? true,
            latitude: schedule?.coverage?.latitude ?? DEFAULT_COVERAGE.latitude,
            longitude:
                schedule?.coverage?.longitude ?? DEFAULT_COVERAGE.longitude,
            radiusKm: schedule?.coverage?.radiusKm ?? DEFAULT_COVERAGE.radiusKm,
            shapeType: schedule?.coverage?.shapeType,
            west: schedule?.coverage?.west,
            south: schedule?.coverage?.south,
            east: schedule?.coverage?.east,
            north: schedule?.coverage?.north,
            corridorPoints: schedule?.coverage?.corridorPoints,
            corridorRadiusMeters: schedule?.coverage?.corridorRadiusMeters,
            polygonPoints: schedule?.coverage?.polygonPoints,
            weatherCoverage: schedule?.coverage?.weatherCoverage,
            fuelCoverage: schedule?.coverage?.fuelCoverage,
            targetDurationSeconds:
                schedule?.targetDurationSeconds ?? DEFAULT_TARGET_DURATION_SEC,
            notifyBeforePlayoutMinutes:
                schedule?.notifyBeforePlayoutMinutes ?? DEFAULT_NOTIFY_MINUTES,
            escalationBeforePlayoutMinutes:
                schedule?.escalationBeforePlayoutMinutes ??
                DEFAULT_ESCALATION_MINUTES,
            voiceProfileId: schedule?.voiceProfileId ?? '',
            deliveryStyleId: schedule?.deliveryStyleId ?? '',
            playbookId: schedule?.playbookId ?? '',
            pronunciationDictionaryId:
                schedule?.pronunciationDictionaryId ?? '',
            bookendId: schedule?.bookendId ?? '',
            bookendRotationMode: bookendRotation?.mode ?? 'single',
            bookendRotationIds:
                bookendRotation?.mode === 'roundRobin'
                    ? [...bookendRotation.bookendIds]
                    : [],
            bookendWindows:
                bookendRotation?.mode === 'timeOfDay'
                    ? bookendRotation.windows.map((w) => ({ ...w }))
                    : [],
            bookendTokens: schedule?.bookendTokens
                ? Object.entries(schedule.bookendTokens).map(
                      ([key, value]) => ({
                          key,
                          value,
                      })
                  )
                : [],
            daysOfWeek: initialCalendar.daysOfWeek,
            scheduleMode: initialCalendar.scheduleMode,
            startTime: initialCalendar.startTime,
            endTime: initialCalendar.endTime,
            intervalMinutes: initialCalendar.intervalMinutes,
            specificTimes: initialCalendar.specificTimes,
            calendar: schedule?.calendar ?? null,
            content: schedule?.content ?? {
                ...DEFAULT_CONTENT_SELECTION,
            },
            fillOrder: schedule?.fillOrder
                ? [...schedule.fillOrder]
                : [...DEFAULT_FILL_ORDER],
            voiceRotationMode: rotation?.mode ?? 'single',
            voiceRotationIds:
                rotation?.mode === 'roundRobin'
                    ? [...rotation.voiceProfileIds]
                    : [],
            voiceWindows:
                rotation?.mode === 'timeOfDay'
                    ? rotation.windows.map((w) => ({ ...w }))
                    : [],
        },
    })

    const {
        values,
        contentSummary,
        voiceSummary,
        speechSummary,
        timesSummary,
        calendarSummary,
        coverageForSummary,
        stepInvalid,
    } = useScheduleStudioSummaries(form, schedule)
    // The backend refuses a save when ANY referenced bookend (primary pair or
    // rotation member) uses a token with no value for this station — the token
    // would be silently dropped at airtime. Gate Save here so the refusal is
    // never discovered from the 400.
    const bookendTokenBlockers = useBookendTokenBlockers(form)
    const {
        enabled,
        playoutEnabled,
        station,
        name,
        daysOfWeek,
        radiusKm,
        notifyBeforePlayoutMinutes: notifyMins,
        escalationBeforePlayoutMinutes: escalationMins,
    } = values

    const steps: StudioStep[] = [
        {
            id: 'station',
            label: 'Station',
            icon: Radio,
            summary: station
                ? `${station}${name ? ` \u00B7 ${name}` : ''}`
                : 'Who is this for',
            invalid: stepInvalid('station'),
        },
        {
            id: 'schedule',
            label: 'Schedule',
            icon: CalendarClock,
            summary: `${summariseDays(daysOfWeek ?? [])} \u00B7 ${timesSummary}${calendarSummary}`,
            invalid: stepInvalid('schedule'),
        },
        {
            id: 'alerts',
            label: 'Alerts',
            icon: Bell,
            summary: `notify ${notifyMins ?? '-'}m \u00B7 escalate ${escalationMins ?? '-'}m${!schedule ? ' (default)' : ''}`,
            invalid: stepInvalid('alerts'),
        },
        {
            id: 'voice',
            label: 'Voice',
            icon: Mic,
            summary: voiceSummary,
            invalid: stepInvalid('voice'),
        },
        {
            id: 'speech',
            label: 'Speech',
            icon: AudioLines,
            summary: speechSummary,
            invalid: stepInvalid('speech') || bookendTokenBlockers.length > 0,
        },
        {
            id: 'content',
            label: 'Content',
            icon: Layers,
            summary: contentSummary,
            invalid: stepInvalid('content'),
        },
        {
            id: 'coverage',
            label: 'Coverage',
            icon: MapPin,
            summary: radiusKm
                ? formatCoverageSummary(coverageForSummary)
                : 'Set the area',
            invalid: stepInvalid('coverage'),
        },
        ...(schedule && isAdmin
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: History,
                      summary: activityData?.length
                          ? `${activityData.length} change${activityData.length === 1 ? '' : 's'}`
                          : 'No changes',
                  } satisfies StudioStep,
              ]
            : []),
    ]

    const hasUnfillableTokens = bookendTokenBlockers.length > 0
    // The gate message names the bookend and the token(s), mirroring the
    // backend's refusal. With several blocked bookends the first is named and
    // the rest are counted — the Speech step warnings list every one.
    const tokenBlockReason =
        bookendTokenBlockers.length === 0
            ? undefined
            : bookendTokenBlockers.length === 1
              ? formatBookendBlocker(bookendTokenBlockers[0])
              : `Save is refused: Bookend '${bookendTokenBlockers[0].bookendName}' uses token(s) {${bookendTokenBlockers[0].unresolvedTokens.join('}, {')}} with no value for this station, and ${bookendTokenBlockers.length - 1} more bookend${bookendTokenBlockers.length === 2 ? '' : 's'} with unfillable tokens. Add them in BookendTokens.`

    // Validation indicators stay hidden until a step is visited or a save is
    // attempted — a fresh studio does not scold before the operator has
    // looked at anything.
    const gate = useStudioStepGate(steps, step, open)

    const handleSave = form.handleSubmit(
        async (data) => {
            gate.onSubmitAttempt()
            if (hasUnfillableTokens) {
                toast.error(tokenBlockReason)
                return
            }
            try {
                await onSubmit(data)
            } catch {
                // Keep the studio open so the operator can retry
                return
            }
            onOpenChange(false)
        },
        (errors) => {
            // Surface hidden-field failures instead of swallowing the submit
            gate.onSubmitAttempt()
            const first = Object.values(errors).find((e) => e?.message)
            if (first?.message) toast.error(String(first.message))
        }
    )

    const hasChanges = form.formState.isDirty
    const invalidSteps = steps.filter((s) => s.invalid)
    const hasInvalidSteps = invalidSteps.length > 0
    const saveable =
        hasChanges && !hasInvalidSteps && !hasUnfillableTokens && !isSubmitting
    // Name the offending steps. A station can arrive already invalid — a row
    // stored before a rule existed, e.g. adelaide's notify 5 / escalate 5 which
    // predates the alert-ordering rule — and then Save is disabled on a studio
    // the operator has not even touched. "Fix the highlighted steps first" left
    // them hunting through eight steps for a highlight they had no reason to
    // expect.
    const disabledReason = tokenBlockReason
        ? tokenBlockReason
        : hasInvalidSteps
          ? `Fix ${invalidSteps.map((s) => s.label).join(' and ')} first`
          : !hasChanges
            ? 'No changes yet'
            : undefined

    const discardConfirm = useConfirmDialog()
    // Any close attempt — the Cancel button, ESC, or a backdrop click —
    // must confirm first when there are unsaved edits, so they're never
    // silently discarded.
    const requestClose = (nextOpen: boolean) => {
        if (!nextOpen && hasChanges) {
            discardConfirm.open()
            return
        }
        onOpenChange(nextOpen)
    }
    return (
        <>
            <StudioDialog
                open={open}
                onOpenChange={requestClose}
                title={schedule ? 'Edit station' : 'New station'}
                subject={
                    schedule
                        ? (schedule.station ?? schedule.marketId)
                        : undefined
                }
                description="Automatic bulletin generation."
            >
                <Form {...form}>
                    <form
                        onSubmit={handleSave}
                        className="flex min-h-0 flex-1 flex-col"
                    >
                        <StudioBody>
                            <StudioRail
                                title={
                                    schedule ? 'Edit station' : 'New station'
                                }
                                steps={gate.steps}
                                activeId={step}
                                onSelect={(id) => {
                                    gate.onSelectStep(id)
                                    setStep(id as StepId)
                                }}
                            />
                            <StudioCanvas stepKey={step}>
                                {step === 'station' && (
                                    <BasicInfoSection form={form} />
                                )}
                                {step === 'schedule' && (
                                    <SchedulingSection form={form} />
                                )}
                                {step === 'alerts' && (
                                    <AlertTimesSection form={form} />
                                )}
                                {step === 'voice' && (
                                    <VoiceRotationEditor form={form} />
                                )}
                                {step === 'speech' && (
                                    <div className="space-y-5">
                                        <SpeechSection form={form} />
                                        <PlaybookSelector form={form} />
                                    </div>
                                )}
                                {step === 'content' && (
                                    <ContentSection form={form} />
                                )}
                                {step === 'coverage' && (
                                    <CoverageSection
                                        form={form}
                                        mapHeight="480px"
                                    />
                                )}
                                {step === 'history' && schedule && (
                                    <ActivityFeed
                                        entries={activityData ?? []}
                                        diffable
                                    />
                                )}
                            </StudioCanvas>
                        </StudioBody>

                        <StudioErrorSummary
                            steps={gate.steps}
                            onSelect={(id) => setStep(id as StepId)}
                        />

                        <StudioFooter>
                            <div className="flex flex-wrap items-center gap-x-5 gap-y-2 sm:flex-1">
                                <div className="flex items-center gap-2">
                                    <Switch
                                        id="schedule-active"
                                        checked={enabled}
                                        onCheckedChange={(checked) =>
                                            form.setValue('enabled', checked, {
                                                shouldDirty: true,
                                            })
                                        }
                                    />
                                    <Label
                                        htmlFor="schedule-active"
                                        className="cursor-pointer font-medium"
                                    >
                                        Active
                                    </Label>
                                </div>

                                {/* Separate from Active on purpose: Active decides whether the
                                    station generates at all, this decides whether a published
                                    bulletin leaves for the playout share from THIS environment. */}
                                <div className="flex items-center gap-2">
                                    <Switch
                                        id="schedule-playout"
                                        checked={playoutEnabled}
                                        onCheckedChange={(checked) =>
                                            form.setValue(
                                                'playoutEnabled',
                                                checked,
                                                { shouldDirty: true }
                                            )
                                        }
                                    />
                                    <Label
                                        htmlFor="schedule-playout"
                                        className="cursor-pointer font-medium"
                                    >
                                        Send to playout
                                    </Label>
                                </div>
                            </div>

                            <div className="flex items-center gap-2">
                                <Button
                                    type="button"
                                    variant="ghost"
                                    onClick={() => requestClose(false)}
                                    disabled={isSubmitting}
                                >
                                    Cancel
                                </Button>
                                <StudioSubmitButton
                                    label={
                                        schedule
                                            ? 'Save changes'
                                            : 'Create station'
                                    }
                                    isSubmitting={isSubmitting}
                                    isValid={saveable}
                                    disabledReason={disabledReason}
                                />
                            </div>
                        </StudioFooter>
                    </form>
                </Form>
            </StudioDialog>
            {discardConfirm.isOpen && (
                <Suspense fallback={null}>
                    <ConfirmDialog
                        open={discardConfirm.isOpen}
                        onOpenChange={discardConfirm.onOpenChange}
                        onConfirm={() => onOpenChange(false)}
                        title="Discard unsaved changes?"
                        description="Unsaved edits will be lost."
                        confirmLabel="Discard"
                        destructive
                    />
                </Suspense>
            )}
            {flattenConfirm?.open && (
                <Suspense fallback={null}>
                    <ConfirmDialog
                        open={flattenConfirm.open}
                        onOpenChange={(open) => {
                            if (!open) flattenConfirm.onCancel()
                        }}
                        onConfirm={flattenConfirm.onConfirm}
                        title="Replace day-specific times?"
                        description={flattenConfirm.description}
                        confirmLabel="Replace times"
                        cancelLabel="Keep editing"
                        destructive
                    />
                </Suspense>
            )}
        </>
    )
}
