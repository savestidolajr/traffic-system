import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Mic, MapPin, Zap, Route, CloudSun, Fuel } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { Input } from '@/components/ui/input'
import {
    Form,
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import { StudioSubmitButton } from '@/components/studio/StudioSubmitButton'
import { InfoHint } from '@/components/InfoHint'
import {
    StudioDialog,
    StudioBody,
    StudioRail,
    StudioCanvas,
    StudioErrorSummary,
    StudioFooter,
    type StudioStep,
} from '@/components/studio/Studio'
import { toastAction } from '@/lib/toast-action'
import { useTriggerJob } from '@/features/jobs/api/job-hooks'
import { MapPicker } from '@/components/map-picker'
import {
    DEFAULT_VOICE_SETTINGS,
    DEFAULT_COVERAGE,
    DEFAULT_CONTENT_SELECTION,
} from '@/lib/api/schemas/shared'
import { formatCoverageSummary } from '@/components/map-picker/coverage-shapes'
import type { CoverageArea, VoiceSettings } from '@/lib/api/schemas/shared'
import type { MarketSchedule } from '@/features/schedules/types'
import { TriggerVoicePicker } from './TriggerVoicePicker'
import {
    triggerSchema,
    STEP_FIELDS,
    summariseTriggerContent,
    toggleTriggerContentSource,
    type StepId,
    type TriggerFormData,
} from './trigger-studio-helpers'

function buildDefaults(schedule: MarketSchedule): TriggerFormData {
    return {
        targetDurationSec: schedule.targetDurationSeconds,
        voiceProfileId: schedule.voiceProfileId ?? '',
        voiceId: '',
        voiceSettings: DEFAULT_VOICE_SETTINGS,
        latitude: schedule.coverage?.latitude ?? DEFAULT_COVERAGE.latitude,
        longitude: schedule.coverage?.longitude ?? DEFAULT_COVERAGE.longitude,
        radiusKm: schedule.coverage?.radiusKm ?? DEFAULT_COVERAGE.radiusKm,
        shapeType: schedule.coverage?.shapeType,
        west: schedule.coverage?.west,
        south: schedule.coverage?.south,
        east: schedule.coverage?.east,
        north: schedule.coverage?.north,
        corridorPoints: schedule.coverage?.corridorPoints,
        corridorRadiusMeters: schedule.coverage?.corridorRadiusMeters,
        polygonPoints: schedule.coverage?.polygonPoints,
        // Keep older schedule responses safe while preserving every saved
        // selection and Always include flag.
        content: {
            ...DEFAULT_CONTENT_SELECTION,
            ...schedule.content,
        },
    }
}

interface TriggerStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** The station whose schedule prefills the bulletin; coverage is read-only
     *  and the voice defaults to the station's assigned profile. */
    readonly schedule: MarketSchedule
}

/**
 * Trigger-now as a studio: for a single station, pick the voice (from the
 * station's configured library) and confirm the read-only coverage on a
 * full canvas. The footer states the consequence plainly: this generates
 * immediately.
 */
export function TriggerStudio({
    open,
    onOpenChange,
    schedule,
}: TriggerStudioProps) {
    const navigate = useNavigate()
    const triggerMutation = useTriggerJob()
    const [step, setStep] = useState<StepId>('voice')

    const form = useForm<TriggerFormData>({
        resolver: zodResolver(triggerSchema),
        mode: 'onChange',
        defaultValues: buildDefaults(schedule),
    })

    const [
        voiceName,
        targetDurationSec,
        radiusKm,
        shapeType,
        west,
        south,
        east,
        north,
        corridorPoints,
        corridorRadiusMeters,
        polygonPoints,
        content,
    ] = useWatch({
        control: form.control,
        name: [
            'voiceName',
            'targetDurationSec',
            'radiusKm',
            'shapeType',
            'west',
            'south',
            'east',
            'north',
            'corridorPoints',
            'corridorRadiusMeters',
            'polygonPoints',
            'content',
        ],
    })

    const { errors } = form.formState
    const stepInvalid = (id: StepId): boolean =>
        STEP_FIELDS[id].some((field) => field in errors)

    const steps: StudioStep[] = [
        {
            id: 'voice',
            label: 'Voice',
            icon: Mic,
            summary: voiceName
                ? `${voiceName} · ${targetDurationSec ?? '-'}s · ${summariseTriggerContent(content)}`
                : 'Pick a voice',
            invalid: stepInvalid('voice'),
        },
        {
            id: 'coverage',
            label: 'Coverage',
            icon: MapPin,
            summary: radiusKm
                ? formatCoverageSummary({
                      latitude: form.getValues('latitude'),
                      longitude: form.getValues('longitude'),
                      radiusKm,
                      shapeType: shapeType ?? undefined,
                      west: west ?? undefined,
                      south: south ?? undefined,
                      east: east ?? undefined,
                      north: north ?? undefined,
                      corridorPoints: corridorPoints ?? undefined,
                      corridorRadiusMeters: corridorRadiusMeters ?? undefined,
                      polygonPoints: polygonPoints ?? undefined,
                  } as CoverageArea)
                : 'Set the area',
            invalid: stepInvalid('coverage'),
        },
    ]

    const handleTrigger = form.handleSubmit(async (data) => {
        const result = await toastAction(
            () =>
                triggerMutation.mutateAsync({
                    station: schedule.station ?? '',
                    market: schedule.name ?? schedule.marketId,
                    targetDurationSec: data.targetDurationSec,
                    voiceProvider: 'elevenlabs',
                    voiceId: data.voiceId,
                    voiceProfileId: data.voiceProfileId || undefined,
                    voiceModel: data.voiceModel,
                    voiceName: data.voiceName,
                    voiceSettings: data.voiceSettings as VoiceSettings,
                    latitude: data.latitude,
                    longitude: data.longitude,
                    radiusKm: data.radiusKm,
                    shapeType: data.shapeType,
                    west: data.west,
                    south: data.south,
                    east: data.east,
                    north: data.north,
                    corridorPoints: data.corridorPoints,
                    corridorRadiusMeters: data.corridorRadiusMeters,
                    polygonPoints: data.polygonPoints,
                    bulletinType: 'traffic',
                    // Send content only when the operator changed it in this dialog: the form
                    // snapshots the schedule at mount, so always sending it silently overrides a
                    // station's later-saved Always include flags (test 2026-08-20: a stale
                    // dialog's Content without WeatherRequired reset the fixture for that job).
                    content: form.formState.dirtyFields.content
                        ? data.content
                        : undefined,
                }),
            '', // Empty = no success toast; the actionable one is shown below
            'Failed to trigger bulletin'
        )
        if (result && typeof result === 'object' && result.jobId) {
            toast.success('Bulletin triggered', {
                id: `trigger-success-${result.jobId}`,
                action: {
                    label: 'Open',
                    onClick: () => navigate(`/jobs/${result.jobId}`),
                },
            })
            onOpenChange(false)
        }
    })

    const isSubmitting = triggerMutation.isPending
    const disabledReason = !form.formState.isValid
        ? form.formState.isDirty
            ? 'Fix the highlighted steps first'
            : 'No changes yet'
        : undefined

    return (
        <StudioDialog
            open={open}
            onOpenChange={onOpenChange}
            title="Trigger now"
            subject={schedule.station ?? undefined}
        >
            <Form {...form}>
                <form
                    onSubmit={handleTrigger}
                    className="flex min-h-0 flex-1 flex-col"
                >
                    <StudioBody>
                        <StudioRail
                            title="Trigger now"
                            steps={steps}
                            activeId={step}
                            onSelect={(id) => setStep(id as StepId)}
                        />
                        <StudioCanvas stepKey={step}>
                            {step === 'voice' && (
                                <>
                                    <TriggerVoicePicker form={form} />
                                    <FormField
                                        control={form.control}
                                        name="targetDurationSec"
                                        render={({ field }) => (
                                            <FormItem className="max-w-50">
                                                <FormLabel>
                                                    Target duration (seconds)
                                                </FormLabel>
                                                <FormControl>
                                                    <Input
                                                        type="number"
                                                        min={10}
                                                        max={600}
                                                        {...field}
                                                        onChange={(e) => {
                                                            const val =
                                                                e.target
                                                                    .valueAsNumber
                                                            field.onChange(
                                                                isNaN(val)
                                                                    ? undefined
                                                                    : val
                                                            )
                                                        }}
                                                    />
                                                </FormControl>
                                                <FormMessage />
                                            </FormItem>
                                        )}
                                    />
                                    <FormField
                                        control={form.control}
                                        name="content"
                                        render={({ field }) => (
                                            <FormItem>
                                                <div className="flex items-center gap-1.5">
                                                    <FormLabel>
                                                        Content sources
                                                    </FormLabel>
                                                    <InfoHint label="How are selected sources used?">
                                                        <p>
                                                            Selected sources are
                                                            used when the read
                                                            has room.
                                                        </p>
                                                        <p className="mt-1.5">
                                                            Turn on "Always
                                                            include" to require
                                                            that source's
                                                            section whenever its
                                                            data is fetched.
                                                        </p>
                                                        <p className="mt-1.5">
                                                            If a provider is
                                                            down, the bulletin
                                                            still airs without
                                                            that section.
                                                        </p>
                                                    </InfoHint>
                                                </div>
                                                <div className="flex max-w-xl flex-col gap-2.5">
                                                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                                                        {[
                                                            [
                                                                'traffic',
                                                                'Traffic',
                                                                Route,
                                                            ] as const,
                                                            [
                                                                'weather',
                                                                'Weather',
                                                                CloudSun,
                                                            ] as const,
                                                            [
                                                                'fuel',
                                                                'Fuel',
                                                                Fuel,
                                                            ] as const,
                                                        ].map(
                                                            ([
                                                                key,
                                                                label,
                                                                Icon,
                                                            ]) => {
                                                                const selected =
                                                                    field.value[
                                                                        key
                                                                    ]
                                                                return (
                                                                    <button
                                                                        key={
                                                                            key
                                                                        }
                                                                        type="button"
                                                                        aria-pressed={
                                                                            selected
                                                                        }
                                                                        onClick={() => {
                                                                            field.onChange(
                                                                                toggleTriggerContentSource(
                                                                                    field.value,
                                                                                    key
                                                                                )
                                                                            )
                                                                        }}
                                                                        className={`flex h-10 cursor-pointer items-center justify-between rounded-lg border px-3 py-2 text-left text-sm font-medium transition-all duration-150 ${
                                                                            selected
                                                                                ? 'border-primary/60 bg-primary/[0.04] text-foreground ring-1 ring-primary/40'
                                                                                : 'border-border/70 bg-card/40 text-muted-foreground hover:border-border hover:text-foreground'
                                                                        }`}
                                                                    >
                                                                        <span className="flex items-center gap-1.5">
                                                                            <Icon className="size-4 shrink-0 text-primary" />
                                                                            {
                                                                                label
                                                                            }
                                                                        </span>
                                                                        <span
                                                                            className={`size-2 shrink-0 rounded-full transition-colors ${
                                                                                selected
                                                                                    ? 'bg-primary'
                                                                                    : 'bg-muted-foreground/30'
                                                                            }`}
                                                                        />
                                                                    </button>
                                                                )
                                                            }
                                                        )}
                                                    </div>
                                                    {(field.value.weather ||
                                                        field.value.fuel) && (
                                                        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border border-border/50 bg-card/30 px-3 py-2 text-xs">
                                                            <div className="flex items-center gap-1 text-muted-foreground">
                                                                <span className="font-medium text-foreground">
                                                                    Always
                                                                    include
                                                                </span>
                                                                <InfoHint label="What does Always include do?">
                                                                    Must air
                                                                    when that
                                                                    source's
                                                                    data is
                                                                    fetched.
                                                                </InfoHint>
                                                            </div>
                                                            <div className="flex items-center gap-4">
                                                                {field.value
                                                                    .weather && (
                                                                    <label
                                                                        htmlFor="trigger-weather-required"
                                                                        className="flex cursor-pointer items-center gap-1.5 font-medium text-muted-foreground transition-colors hover:text-foreground"
                                                                    >
                                                                        <Switch
                                                                            id="trigger-weather-required"
                                                                            aria-label="Always include Weather"
                                                                            checked={
                                                                                field
                                                                                    .value
                                                                                    .weatherRequired
                                                                            }
                                                                            onCheckedChange={(
                                                                                checked
                                                                            ) =>
                                                                                field.onChange(
                                                                                    {
                                                                                        ...field.value,
                                                                                        weatherRequired:
                                                                                            checked,
                                                                                    }
                                                                                )
                                                                            }
                                                                        />
                                                                        <span>
                                                                            Weather
                                                                        </span>
                                                                    </label>
                                                                )}
                                                                {field.value
                                                                    .fuel && (
                                                                    <label
                                                                        htmlFor="trigger-fuel-required"
                                                                        className="flex cursor-pointer items-center gap-1.5 font-medium text-muted-foreground transition-colors hover:text-foreground"
                                                                    >
                                                                        <Switch
                                                                            id="trigger-fuel-required"
                                                                            aria-label="Always include Fuel"
                                                                            checked={
                                                                                field
                                                                                    .value
                                                                                    .fuelRequired
                                                                            }
                                                                            onCheckedChange={(
                                                                                checked
                                                                            ) =>
                                                                                field.onChange(
                                                                                    {
                                                                                        ...field.value,
                                                                                        fuelRequired:
                                                                                            checked,
                                                                                    }
                                                                                )
                                                                            }
                                                                        />
                                                                        <span>
                                                                            Fuel
                                                                        </span>
                                                                    </label>
                                                                )}
                                                            </div>
                                                        </div>
                                                    )}
                                                </div>
                                                <FormMessage />
                                            </FormItem>
                                        )}
                                    />
                                </>
                            )}
                            {step === 'coverage' && (
                                <div className="flex flex-col gap-4">
                                    <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                                        <MapPin className="size-4" />
                                        Coverage Area
                                    </div>
                                    <p className="text-xs text-muted-foreground">
                                        Read-only, from the station's schedule.
                                    </p>
                                    <MapPicker
                                        value={{
                                            latitude: form.watch('latitude'),
                                            longitude: form.watch('longitude'),
                                            radiusKm: form.watch('radiusKm'),
                                            shapeType: form.watch('shapeType'),
                                            west: form.watch('west'),
                                            south: form.watch('south'),
                                            east: form.watch('east'),
                                            north: form.watch('north'),
                                            corridorPoints:
                                                form.watch('corridorPoints'),
                                            corridorRadiusMeters: form.watch(
                                                'corridorRadiusMeters'
                                            ),
                                            polygonPoints:
                                                form.watch('polygonPoints'),
                                        }}
                                        readOnly
                                        height="480px"
                                        className="w-full"
                                    />
                                </div>
                            )}
                        </StudioCanvas>
                    </StudioBody>

                    <StudioErrorSummary
                        steps={steps}
                        onSelect={(id) => setStep(id as StepId)}
                    />

                    <StudioFooter>
                        <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                            <span className="inline-flex items-center gap-1.5 font-medium text-foreground">
                                <Zap
                                    className="size-3.5 text-primary"
                                    aria-hidden
                                />
                                Generates immediately
                            </span>
                            <span className="tabular-nums">
                                ≈{targetDurationSec ?? '-'}s ·{' '}
                                {voiceName ?? 'no voice'} · {radiusKm ?? '-'} km
                                around {schedule.station ?? '-'}
                            </span>
                            {/* The manual path resolves the matched schedule's delivery style,
                                pronunciation dictionaries and bookends server-side (see
                                AdHocStationConfigResolver). It used to resolve none of them and
                                say nothing, so an operator heard a platform-default read and had
                                no way to know why. */}
                            <span>
                                Uses this station's read style, pronunciation
                                and bookends
                            </span>
                        </div>
                        <div className="flex items-center gap-2">
                            <Button
                                type="button"
                                variant="ghost"
                                onClick={() => onOpenChange(false)}
                                disabled={isSubmitting}
                            >
                                Cancel
                            </Button>
                            <StudioSubmitButton
                                label="Trigger bulletin"
                                isSubmitting={isSubmitting}
                                isValid={form.formState.isValid}
                                disabledReason={disabledReason}
                            />
                        </div>
                    </StudioFooter>
                </form>
            </Form>
        </StudioDialog>
    )
}
