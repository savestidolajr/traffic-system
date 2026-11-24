import { useState } from 'react'
import {
    Radio,
    CalendarClock,
    Bell,
    Mic,
    MapPin,
    Pencil,
    Pause,
    Play,
    Layers,
    Route,
    CloudSun,
    Fuel,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ConfigRow } from '@/components/ConfigRow'
import {
    StudioDialog,
    StudioBody,
    StudioRail,
    StudioCanvas,
    StudioFooter,
    type StudioStep,
} from '@/components/studio/Studio'
import { MapPicker } from '@/components/map-picker'
import { formatCoverageSummary } from '@/components/map-picker/coverage-shapes'
import { DayTimeline } from './sections/SchedulingSection'
import { ICON } from '@/lib/design/icons'
import { DEFAULT_CONTENT_SELECTION } from '@/lib/api/schemas/shared'
import type { MarketSchedule } from '../types'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import {
    voiceCharacterItems,
    currentVersion,
} from '@/features/voices/lib/voice-profile-utils'
import { usePriorityRoads } from '../api/priority-roads-hooks'
import { useCoarseNow } from '@/hooks/use-second-tick'
import { useActivityLog } from '@/features/activity/api/activity-hooks'
import { ActivityFeed } from '@/features/activity/components/ActivityFeed'
import { useIsAdmin } from '@/hooks/use-current-user'
import { ScheduleStatusBadge } from './ScheduleStatusBadge'
import { ScheduleNextRun } from './ScheduleNextRun'
import { CronHumanReadable } from './CronHumanReadable'
import { parseCronToCalendar } from '../lib/cron-calendar'
import { getNextCronOccurrences } from '../lib/cron-occurrences'
import { summariseContentSources } from '../lib/content-summary'
import {
    formatDateTime,
    formatRelativeTime,
    formatRunTime,
} from '@/lib/formatting'
type StepId =
    | 'station'
    | 'schedule'
    | 'alerts'
    | 'voice'
    | 'content'
    | 'coverage'
    | 'priority-roads'
    | 'history'

interface ScheduleDetailStudioProps {
    readonly schedule: MarketSchedule
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
}

/** Format a camelCase fill-source key into a human label (e.g. "minorTraffic" → "Minor traffic"). */
function fillSourceLabel(source: string): string {
    return source
        .replace(/([a-z])([A-Z])/g, '$1 $2')
        .replace(/^./, (c) => c.toUpperCase())
}

/**
 * Station details in the same studio shell as create/edit: the step rail
 * headers show read-only summaries, and each step's canvas shows detail.
 */
export function ScheduleDetailStudio({
    schedule,
    open,
    onOpenChange,
    onEdit,
    onToggle,
}: ScheduleDetailStudioProps) {
    const [step, setStep] = useState<StepId>('station')
    const { data: voiceProfilesData } = useVoiceProfiles()
    const { data: priorityRoadsData } = usePriorityRoads(
        schedule.marketId,
        open
    )
    // 366 days is the backend's own max range, used here to approximate
    // "since the beginning" for this station. useCoarseNow reads the shared
    // clock via useSyncExternalStore -- pure to read during render, unlike
    // calling Date.now() directly (react-hooks/purity rightly flags that).
    // 5-minute granularity is plenty for a modal that's open briefly; the
    // query itself only fires while `open`.
    // Admin-only endpoint (RouteRoleMatrix): withheld from Operators, who now reach this sheet
    // from /stations. A 403 here renders as "No activity recorded", which is a lie.
    const isAdmin = useIsAdmin()
    const coarseNowMs = useCoarseNow(300)
    const { data: activityData } = useActivityLog(
        {
            resource: 'schedules',
            entityId: schedule.marketId,
            toUtc: new Date(coarseNowMs).toISOString(),
            fromUtc: new Date(
                coarseNowMs - 366 * 24 * 60 * 60 * 1000
            ).toISOString(),
        },
        open && isAdmin
    )

    const calendar = parseCronToCalendar(schedule.cronExpression)
    const profile = voiceProfilesData?.profiles.find(
        (p) => p.id === schedule.voiceProfileId
    )
    const voiceName = profile?.name
    const voiceVersion = profile ? currentVersion(profile) : undefined
    const vs = voiceVersion?.settings
    const cov = schedule.coverage
    const upcomingRuns =
        schedule.enabled && schedule.cronExpression
            ? getNextCronOccurrences(
                  schedule.cronExpression,
                  schedule.timezone ?? 'UTC',
                  3
              )
            : []
    const content = schedule.content ?? { ...DEFAULT_CONTENT_SELECTION }
    const roads = priorityRoadsData?.roads ?? null

    const steps: StudioStep[] = [
        {
            id: 'station',
            label: 'Station',
            icon: Radio,
            summary: `${schedule.station ?? schedule.marketId}${schedule.name ? ` · ${schedule.name}` : ''}`,
        },
        {
            id: 'schedule',
            label: 'Schedule',
            icon: CalendarClock,
            summary: (
                <CronHumanReadable
                    expression={schedule.cronExpression}
                    abbreviated
                    className="font-sans text-2xs"
                />
            ),
        },
        {
            id: 'alerts',
            label: 'Alerts',
            icon: Bell,
            summary: `notify ${schedule.notifyBeforePlayoutMinutes}m · escalate ${schedule.escalationBeforePlayoutMinutes}m`,
        },
        {
            id: 'voice',
            label: 'Voice',
            icon: Mic,
            summary: voiceName
                ? `${voiceName} \u00B7 ${schedule.targetDurationSeconds}s`
                : 'Not configured',
        },
        {
            id: 'content',
            label: 'Content',
            icon: Layers,
            summary: summariseContentSources(content),
        },
        {
            id: 'coverage',
            label: 'Coverage',
            icon: MapPin,
            summary: cov ? formatCoverageSummary(cov) : 'No coverage area set',
        },
        {
            id: 'priority-roads',
            label: 'Priority Roads',
            icon: Route,
            summary: roads
                ? `${roads.length} road${roads.length === 1 ? '' : 's'}`
                : 'None',
        },
        ...(isAdmin
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: ICON.history,
                      summary: activityData?.length
                          ? `${activityData.length} change${activityData.length === 1 ? '' : 's'}`
                          : 'No changes',
                  } satisfies StudioStep,
              ]
            : []),
    ]

    return (
        <StudioDialog
            open={open}
            onOpenChange={onOpenChange}
            title="Station details"
            subject={schedule.station ?? schedule.marketId}
            description="Read-only station profile."
        >
            <StudioBody>
                <StudioRail
                    title="Station details"
                    steps={steps}
                    activeId={step}
                    onSelect={(id) => setStep(id as StepId)}
                />
                <StudioCanvas stepKey={step}>
                    {step === 'station' && (
                        <dl className="max-w-md">
                            <ConfigRow label="Status">
                                <ScheduleStatusBadge schedule={schedule} />
                            </ConfigRow>
                            <ConfigRow label="Station">
                                {schedule.station ?? '-'}
                            </ConfigRow>
                            <ConfigRow label="Name">
                                {schedule.name ?? '-'}
                            </ConfigRow>
                            <ConfigRow label="Market ID">
                                <code className="font-mono text-xs">
                                    {schedule.marketId}
                                </code>
                            </ConfigRow>
                            <ConfigRow label="Timezone">
                                {schedule.timezone ?? '-'}
                            </ConfigRow>
                            <ConfigRow label="Duration">
                                {schedule.targetDurationSeconds} seconds
                            </ConfigRow>
                            <ConfigRow label="Created">
                                {formatDateTime(schedule.createdAt)}{' '}
                                <span className="text-xs text-muted-foreground">
                                    ({formatRelativeTime(schedule.createdAt)})
                                </span>
                            </ConfigRow>
                            <ConfigRow label="Updated">
                                {formatDateTime(schedule.updatedAt)}{' '}
                                <span className="text-xs text-muted-foreground">
                                    ({formatRelativeTime(schedule.updatedAt)})
                                </span>
                            </ConfigRow>
                        </dl>
                    )}
                    {step === 'schedule' && (
                        <div className="flex flex-col gap-5">
                            {/* The week's pattern on the day track,
                                read-only */}
                            <div className="pointer-events-none">
                                <DayTimeline
                                    mode={calendar.scheduleMode}
                                    times={calendar.specificTimes}
                                    startTime={calendar.startTime}
                                    endTime={calendar.endTime}
                                    intervalMinutes={calendar.intervalMinutes}
                                    onAddTime={() => {}}
                                    onRemoveTime={() => {}}
                                />
                            </div>
                            <dl className="max-w-md">
                                <ConfigRow label="Schedule">
                                    <CronHumanReadable
                                        expression={schedule.cronExpression}
                                        abbreviated
                                        className="font-sans text-sm"
                                    />
                                </ConfigRow>
                                <ConfigRow label="Cron">
                                    <code className="rounded bg-muted/50 px-1.5 py-0.5 font-mono text-xs">
                                        {schedule.cronExpression ?? '-'}
                                    </code>
                                </ConfigRow>
                                <ConfigRow label="Upcoming">
                                    {upcomingRuns.length > 0 ? (
                                        <span className="flex flex-col gap-0.5">
                                            {upcomingRuns.map((d, i) => (
                                                <span
                                                    key={i}
                                                    className="whitespace-nowrap tabular-nums"
                                                >
                                                    {formatRunTime(
                                                        d,
                                                        schedule.timezone
                                                    )}
                                                </span>
                                            ))}
                                        </span>
                                    ) : (
                                        <span className="italic text-muted-foreground">
                                            -
                                        </span>
                                    )}
                                </ConfigRow>
                            </dl>
                        </div>
                    )}
                    {step === 'alerts' && (
                        <dl className="max-w-md">
                            <ConfigRow label="Alert">
                                {schedule.notifyBeforePlayoutMinutes} min before
                                playout
                            </ConfigRow>
                            <ConfigRow label="Urgent">
                                {schedule.escalationBeforePlayoutMinutes} min
                                before playout
                            </ConfigRow>
                        </dl>
                    )}
                    {step === 'voice' && (
                        <dl className="max-w-md">
                            <ConfigRow label="Voice">
                                {schedule.voiceProfileId ? (
                                    (voiceName ?? schedule.voiceProfileId)
                                ) : (
                                    <span className="italic text-muted-foreground">
                                        Not configured
                                    </span>
                                )}
                            </ConfigRow>
                            {vs && voiceVersion ? (
                                <>
                                    {voiceCharacterItems(
                                        vs,
                                        voiceVersion.model
                                    ).map((item) => (
                                        <ConfigRow
                                            key={item.key}
                                            label={item.label}
                                        >
                                            {item.value}
                                        </ConfigRow>
                                    ))}
                                </>
                            ) : (
                                <ConfigRow label="Settings">
                                    <span className="italic text-muted-foreground">
                                        Default
                                    </span>
                                </ConfigRow>
                            )}
                        </dl>
                    )}
                    {step === 'content' && (
                        <dl className="max-w-md">
                            <ConfigRow label="Content sources">
                                <span
                                    role="img"
                                    className="inline-flex flex-wrap gap-2"
                                    aria-label={summariseContentSources(
                                        content
                                    )}
                                >
                                    {(
                                        [
                                            {
                                                key: 'traffic' as const,
                                                icon: Route,
                                                label: 'Traffic',
                                            },
                                            {
                                                key: 'weather' as const,
                                                icon: CloudSun,
                                                label: 'Weather',
                                            },
                                            {
                                                key: 'fuel' as const,
                                                icon: Fuel,
                                                label: 'Fuel',
                                            },
                                        ] as const
                                    )
                                        .filter((s) => content[s.key])
                                        .map((s) => (
                                            <span
                                                key={s.key}
                                                className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-background/60 px-2.5 py-1 text-xs font-medium"
                                            >
                                                <s.icon className="size-3.5" />
                                                {s.label}
                                            </span>
                                        ))}
                                </span>
                            </ConfigRow>
                            <ConfigRow label="Always include">
                                {content.weatherRequired ||
                                content.fuelRequired ? (
                                    <span className="inline-flex flex-wrap gap-1.5">
                                        {content.weatherRequired && (
                                            <span className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-background/60 px-2.5 py-1 text-xs font-medium">
                                                <CloudSun className="size-3.5" />
                                                Weather
                                            </span>
                                        )}
                                        {content.fuelRequired && (
                                            <span className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-background/60 px-2.5 py-1 text-xs font-medium">
                                                <Fuel className="size-3.5" />
                                                Fuel
                                            </span>
                                        )}
                                    </span>
                                ) : (
                                    <span className="italic text-muted-foreground">
                                        None
                                    </span>
                                )}
                            </ConfigRow>
                            <ConfigRow label="Fill priority">
                                {schedule.fillOrder &&
                                schedule.fillOrder.length > 0 ? (
                                    <span className="inline-flex flex-wrap items-center gap-1.5">
                                        {schedule.fillOrder.map((source, i) => (
                                            <span
                                                key={source}
                                                className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-background/60 px-2.5 py-1 text-xs font-medium"
                                            >
                                                <span className="font-mono text-2xs text-muted-foreground">
                                                    {i + 1}
                                                </span>
                                                {fillSourceLabel(source)}
                                            </span>
                                        ))}
                                    </span>
                                ) : (
                                    <span className="italic text-muted-foreground">
                                        Default (weather → fuel → minor traffic
                                        → filler)
                                    </span>
                                )}
                            </ConfigRow>
                        </dl>
                    )}
                    {step === 'coverage' &&
                        (cov ? (
                            <MapPicker
                                readOnly
                                value={cov}
                                height="460px"
                                className="w-full"
                            />
                        ) : (
                            <p className="text-sm italic text-muted-foreground">
                                No coverage configured.
                            </p>
                        ))}
                    {step === 'priority-roads' && (
                        <dl className="max-w-md">
                            <ConfigRow label="Priority roads">
                                {roads && roads.length > 0 ? (
                                    <span className="inline-flex flex-wrap gap-1.5">
                                        {roads.map((road) => (
                                            <span
                                                key={road.name}
                                                className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-background/60 px-2.5 py-1 text-xs font-medium"
                                            >
                                                {road.name}
                                            </span>
                                        ))}
                                    </span>
                                ) : (
                                    <span className="italic text-muted-foreground">
                                        None
                                    </span>
                                )}
                            </ConfigRow>
                        </dl>
                    )}
                    {step === 'history' && (
                        <ActivityFeed entries={activityData ?? []} diffable />
                    )}
                </StudioCanvas>
            </StudioBody>

            <StudioFooter>
                <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                    <span className="font-medium text-foreground">
                        Next run
                    </span>
                    <ScheduleNextRun schedule={schedule} />
                </div>
                <div className="flex items-center gap-2">
                    <Button
                        variant="outline"
                        size="sm"
                        onClick={() => onToggle(schedule)}
                    >
                        {schedule.enabled ? (
                            <>
                                <Pause data-icon="inline-start" />
                                Pause
                            </>
                        ) : (
                            <>
                                <Play data-icon="inline-start" />
                                Start
                            </>
                        )}
                    </Button>
                    <Button size="sm" onClick={() => onEdit(schedule)}>
                        <Pencil data-icon="inline-start" />
                        Edit station
                    </Button>
                </div>
            </StudioFooter>
        </StudioDialog>
    )
}
