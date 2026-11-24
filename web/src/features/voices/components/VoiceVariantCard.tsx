import { ICON, ICON_SIZE } from '@/lib/design/icons'
import { Badge } from '@/components/ui/badge'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { formatDateTime, formatRelativeTime } from '@/lib/formatting'
import { SamplePlayButton } from './SamplePlayButton'
import {
    currentVersion,
    wordTargetForDuration,
} from '../lib/voice-profile-utils'
import {
    getModelDisplayName,
    getModelShortLabel,
} from '../lib/model-capabilities'
import type { VoiceProfile } from '../profile-types'

/** Airtime the calibration WPM is reported against on cards. */
const SLOT_SECONDS = 30
/** Pace-meter range (typical broadcast read sits ~150–180 wpm). */
const WPM_MIN = 120
const WPM_MAX = 190
/** Airings-meter reference: this many real broadcasts reads as "established"
 *  on the fill bar. Not a cap — purely a glanceable scale, same convention
 *  as the pace meter's WPM_MIN/WPM_MAX band. */
const ESTABLISHED_RUN_COUNT = 20

interface VoiceVariantCardProps {
    readonly profile: VoiceProfile
    /** Station labels airing with this voice — derived from schedules (the
     *  wire DTO's `assignedStations` is never populated; see
     *  `lib/assignments.ts`). */
    readonly assignedStations: readonly string[]
    readonly onView: (profileId: string) => void
    readonly onEdit: (profileId: string) => void
    readonly onDuplicate: (profile: VoiceProfile) => void
    readonly onDelete: (profile: VoiceProfile) => void
    readonly onArchive: (profile: VoiceProfile) => void
    readonly onUnarchive: (profile: VoiceProfile) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * A thin labelled meter: a caption row (LABEL · value) above a track with a
 * proportional fill. The shared visual unit for this card's stats.
 */
function Meter({
    label,
    value,
    fill,
    tooltip,
}: {
    readonly label: string
    readonly value: string
    /** 0..1 portion of the track to fill. */
    readonly fill: number
    /** When set, the label shows a Tooltip explaining what the meter measures. */
    readonly tooltip?: string
}) {
    return (
        <div className="flex flex-col gap-1">
            <div className="flex items-baseline justify-between gap-2 text-2xs">
                {tooltip ? (
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <span className="cursor-help uppercase tracking-wide text-muted-foreground">
                                {label}
                            </span>
                        </TooltipTrigger>
                        <TooltipContent>{tooltip}</TooltipContent>
                    </Tooltip>
                ) : (
                    <span className="uppercase tracking-wide text-muted-foreground">
                        {label}
                    </span>
                )}
                <span className="font-medium tabular-nums text-foreground">
                    {value}
                </span>
            </div>
            <span className="h-1.5 w-full overflow-hidden rounded-full bg-muted/60">
                <span
                    className="block h-full rounded-full bg-primary/70"
                    style={{
                        width: `${Math.max(0, Math.min(1, fill)) * 100}%`,
                    }}
                />
            </span>
        </div>
    )
}

/**
 * A voice in the shared library. Fixed skeleton so every card is the same
 * height and aligns on a grid: header (audition + identity), a symmetric
 * two-column stats band (pace ▏ proven-on-air), then a single-line footer
 * (assignment ▏ version). Character settings (stability/similarity) are
 * deliberately NOT shown here — every profile starts from the same defaults,
 * so those values are near-identical across cards and carry no real signal;
 * they stay in the editor where tuning them actually matters. The stats band
 * instead answers "does this voice actually work": its measured pace and how
 * proven it is on air. Calibration and assignment have same-footprint empty
 * states so assigned/unassigned and aired/not-yet never shift the layout.
 */
export function VoiceVariantCard({
    profile,
    assignedStations,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: VoiceVariantCardProps) {
    const version = currentVersion(profile)
    const calibration = version.calibration
    const stationCount = assignedStations.length
    const isAssigned = stationCount > 0
    const isArchived = profile.status === 'archived'
    const wpm = calibration ? Math.round(calibration.wordsPerMinute) : null
    const runCount = calibration?.runCount ?? 0
    // A voice is "proven" only once real broadcast runs have measured it. A
    // calibration row with zero runs is an imported/seed value, not airtime,
    // so the never-aired card collapses to a single "Not aired yet" body.
    const hasAired = wpm !== null && runCount > 0

    // Pointer-only enhancement: the whole card opens View details. Keyboard
    // users reach the same action through the (focusable) actions menu, so the
    // wrapper stays non-interactive for a11y (no nested-interactive violation).
    const handleCardClick = () => onView(profile.id)

    return (
        <div onClick={handleCardClick} className="h-full cursor-pointer">
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    {/* Header — audition control + identity */}
                    <div className="flex items-start gap-3">
                        {version.sampleUrl ? (
                            // Guard: audition must never also open the view dialog
                            <span onClick={(e) => e.stopPropagation()}>
                                <SamplePlayButton
                                    url={version.sampleUrl}
                                    label="Play latest sample"
                                    variant="secondary"
                                    className="size-10 shrink-0 rounded-lg"
                                />
                            </span>
                        ) : (
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-muted/30 text-muted-foreground">
                                <ICON.voice
                                    className={ICON_SIZE.badge}
                                    aria-hidden
                                />
                            </div>
                        )}
                        <div className="min-w-0 flex-1">
                            <h2
                                className="flex items-center gap-2 truncate text-base font-semibold leading-tight"
                                title={profile.name}
                            >
                                <span className="truncate">{profile.name}</span>
                                {profile.status === 'archived' && (
                                    <Badge variant="muted">Archived</Badge>
                                )}
                            </h2>
                            <p
                                className="flex items-center gap-1.5 text-xs text-muted-foreground"
                                title={profile.baseVoiceName}
                            >
                                <span
                                    className="shrink-0 rounded bg-primary/10 px-1.5 py-px text-3xs font-semibold uppercase tracking-wide text-primary-on-tint"
                                    title={`ElevenLabs model: ${getModelDisplayName(version.model)}`}
                                >
                                    {getModelShortLabel(version.model)}
                                </span>
                                <span className="truncate">
                                    {profile.baseVoiceName}
                                </span>
                            </p>
                        </div>
                        <EntityActionsMenu
                            onView={() => onView(profile.id)}
                            onEdit={() => onEdit(profile.id)}
                            onDuplicate={() => onDuplicate(profile)}
                            onDelete={
                                canDelete ? () => onDelete(profile) : undefined
                            }
                            onArchive={
                                isArchived
                                    ? undefined
                                    : () => onArchive(profile)
                            }
                            onUnarchive={
                                isArchived
                                    ? () => onUnarchive(profile)
                                    : undefined
                            }
                            ariaLabel="Variant actions"
                            className="-mr-1 shrink-0"
                        />
                    </div>

                    {/* Stats — symmetric: pace ▏ proven-on-air */}
                    {hasAired ? (
                        <div className="grid grid-cols-2 items-start gap-4">
                            <div className="flex flex-col gap-2.5">
                                <div className="flex items-baseline gap-1.5">
                                    <span className="text-2xl font-semibold leading-none tabular-nums text-foreground">
                                        {wpm}
                                    </span>
                                    <span className="text-2xs text-muted-foreground">
                                        wpm
                                    </span>
                                    {version.paceBoostWpm > 0 && (
                                        <Tooltip>
                                            <TooltipTrigger asChild>
                                                <span className="shrink-0 cursor-help rounded-full bg-primary/10 px-1.5 py-px text-3xs font-semibold text-primary-on-tint">
                                                    +{version.paceBoostWpm}
                                                </span>
                                            </TooltipTrigger>
                                            <TooltipContent>
                                                Delivery pace boost: script is
                                                sized {version.paceBoostWpm} wpm
                                                faster, then compressed to fit
                                                the slot
                                            </TooltipContent>
                                        </Tooltip>
                                    )}
                                </div>
                                <Meter
                                    label="Pace"
                                    value={`~${wordTargetForDuration(wpm, SLOT_SECONDS)}/${SLOT_SECONDS}s`}
                                    fill={(wpm - WPM_MIN) / (WPM_MAX - WPM_MIN)}
                                />
                            </div>
                            <div className="flex flex-col gap-2.5">
                                <div className="flex items-baseline gap-1">
                                    <span className="text-2xl font-semibold leading-none tabular-nums text-foreground">
                                        {runCount}
                                    </span>
                                    <span className="text-2xs text-muted-foreground">
                                        airing{runCount === 1 ? '' : 's'}
                                    </span>
                                </div>
                                <Meter
                                    label="Proven"
                                    tooltip="Real broadcasts this voice's pace is measured from. The bar fills toward 20 airings, the point at which a voice reads as established on air."
                                    value={
                                        calibration?.calibratedAt
                                            ? formatRelativeTime(
                                                  calibration.calibratedAt
                                              )
                                            : '-'
                                    }
                                    fill={runCount / ESTABLISHED_RUN_COUNT}
                                />
                            </div>
                        </div>
                    ) : (
                        <div className="flex items-center gap-2 text-sm text-muted-foreground-subtle">
                            Not aired yet
                        </div>
                    )}

                    {/* Footer — assignment ▏ version */}
                    <div className="flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs">
                        <span
                            className="inline-flex items-center gap-1.5"
                            title={
                                isAssigned
                                    ? `On air at: ${assignedStations.join(', ')}`
                                    : 'Not assigned to any station'
                            }
                        >
                            <ICON.broadcast
                                className={cn(
                                    ICON_SIZE.inline,
                                    isAssigned
                                        ? 'text-primary'
                                        : 'text-muted-foreground-subtle'
                                )}
                            />
                            <span
                                className={cn(
                                    isAssigned
                                        ? 'font-medium text-foreground'
                                        : 'text-muted-foreground'
                                )}
                            >
                                {isAssigned
                                    ? `${stationCount} station${stationCount === 1 ? '' : 's'}`
                                    : 'Unassigned'}
                            </span>
                        </span>
                        <span
                            className="text-muted-foreground"
                            title={formatDateTime(profile.updatedAt)}
                        >
                            v{profile.currentVersion} · updated{' '}
                            {formatRelativeTime(profile.updatedAt)}
                        </span>
                    </div>
                </div>
            </SpotlightCard>
        </div>
    )
}
