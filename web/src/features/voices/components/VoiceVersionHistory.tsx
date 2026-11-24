import { useState } from 'react'
import { toastAction } from '@/lib/toast-action'
import { ICON } from '@/lib/design/icons'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { SamplePlayButton } from './SamplePlayButton'
import { formatRelativeTime } from '@/lib/formatting'
import {
    diffVoiceSettings,
    summarizeVoiceSettings,
} from '../lib/voice-profile-utils'
import { useRestoreVoiceProfileVersion } from '../api/voice-profile-hooks'
import type { VoiceProfile, VoiceProfileVersion } from '../profile-types'

/** On-air samples shown before the "+N more" fold. */
const AIRED_PREVIEW_LIMIT = 4

interface VoiceVersionHistoryProps {
    readonly profile: VoiceProfile
    readonly onRestored: (profile: VoiceProfile) => void
}

export function VoiceVersionHistory({
    profile,
    onRestored,
}: VoiceVersionHistoryProps) {
    const restore = useRestoreVoiceProfileVersion()
    // Newest first; pair each with its predecessor for the settings diff.
    const ordered = [...profile.versions].sort((a, b) => b.version - a.version)
    const byVersion = new Map(profile.versions.map((v) => [v.version, v]))

    const handleRestore = async (version: number) => {
        const updated = await toastAction(
            () => restore.mutateAsync({ id: profile.id, version }),
            `Restored settings from version ${version}`,
            'Could not restore that version'
        )
        if (updated && typeof updated === 'object') onRestored(updated)
    }

    return (
        <ol className="flex flex-col">
            {ordered.map((version, index) => (
                <VersionTimelineItem
                    key={version.version}
                    version={version}
                    previous={byVersion.get(version.version - 1) ?? null}
                    isCurrent={version.version === profile.currentVersion}
                    isLast={index === ordered.length - 1}
                    onRestore={() => handleRestore(version.version)}
                    isRestoring={restore.isPending}
                />
            ))}
        </ol>
    )
}

interface VersionTimelineItemProps {
    readonly version: VoiceProfileVersion
    readonly previous: VoiceProfileVersion | null
    readonly isCurrent: boolean
    readonly isLast: boolean
    readonly onRestore: () => void
    readonly isRestoring: boolean
}

function VersionTimelineItem({
    version,
    previous,
    isCurrent,
    isLast,
    onRestore,
    isRestoring,
}: VersionTimelineItemProps) {
    const [airedOpen, setAiredOpen] = useState(false)
    const changes = previous
        ? diffVoiceSettings(previous.settings, version.settings)
        : []
    const calibration = version.calibration
    const aired = version.runSamples
    const paceBoostChanged =
        previous !== null && previous.paceBoostWpm !== version.paceBoostWpm

    return (
        <li className="flex gap-3">
            {/* Timeline rail */}
            <div className="flex flex-col items-center pt-1.5">
                <span
                    className={cn(
                        'size-2.5 shrink-0 rounded-full ring-4 ring-background',
                        isCurrent ? 'bg-primary' : 'bg-muted-foreground/40'
                    )}
                />
                {!isLast && <span className="w-px flex-1 bg-border" />}
            </div>

            {/* Entry */}
            <div className={cn('min-w-0 flex-1', isLast ? 'pb-0' : 'pb-5')}>
                <div className="flex items-center justify-between gap-2">
                    <div className="flex min-w-0 items-center gap-2">
                        <span className="text-sm font-semibold">
                            v{version.version}
                        </span>
                        {isCurrent && <Badge>Current</Badge>}
                        <span className="truncate text-xs text-muted-foreground">
                            {formatRelativeTime(version.createdAt)}
                            {version.createdBy && ` · ${version.createdBy}`}
                        </span>
                    </div>
                    {!isCurrent && (
                        <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={onRestore}
                            disabled={isRestoring}
                            className="h-7 shrink-0 gap-1.5 text-xs"
                        >
                            <ICON.reset className="size-3.5" />
                            Restore as new version
                        </Button>
                    )}
                </div>

                {version.changeNote && (
                    <p className="mt-1 text-sm text-foreground/90">
                        {version.changeNote}
                    </p>
                )}

                {/* What changed — the scannable focus of each entry */}
                <div className="mt-2 flex flex-wrap gap-1.5">
                    {!previous ? (
                        <span className="text-xs text-muted-foreground">
                            Initial settings ·{' '}
                            {summarizeVoiceSettings(version.settings)}
                            {version.paceBoostWpm > 0 &&
                                ` · pace +${version.paceBoostWpm} wpm`}
                        </span>
                    ) : changes.length === 0 && !paceBoostChanged ? (
                        <span className="text-xs text-muted-foreground">
                            Metadata only. No setting changes.
                        </span>
                    ) : (
                        <>
                            {changes.map((change) => (
                                <span
                                    key={change.key}
                                    className="inline-flex items-center gap-1 rounded bg-muted px-1.5 py-0.5 text-xs"
                                >
                                    <span className="font-medium">
                                        {change.label}
                                    </span>
                                    <span className="text-muted-foreground">
                                        {change.from}
                                    </span>
                                    <ICON.forward className="size-3 text-muted-foreground" />
                                    <span>{change.to}</span>
                                </span>
                            ))}
                            {paceBoostChanged && (
                                <span className="inline-flex items-center gap-1 rounded bg-muted px-1.5 py-0.5 text-xs">
                                    <span className="font-medium">
                                        Pace boost
                                    </span>
                                    <span className="text-muted-foreground">
                                        +{previous.paceBoostWpm}
                                    </span>
                                    <ICON.forward className="size-3 text-muted-foreground" />
                                    <span>+{version.paceBoostWpm}</span>
                                </span>
                            )}
                        </>
                    )}
                </div>

                {/* Compact metric + sample controls on one line */}
                <div className="mt-2.5 flex flex-wrap items-center gap-x-3 gap-y-2 text-xs">
                    {calibration ? (
                        <span className="inline-flex items-center gap-1 text-muted-foreground">
                            <ICON.speed className="size-3.5 text-primary" />
                            <span className="font-medium text-foreground">
                                {Math.round(calibration.wordsPerMinute)} wpm
                            </span>
                            · {calibration.runCount} runs
                        </span>
                    ) : (
                        <span className="text-muted-foreground">
                            Not aired yet
                        </span>
                    )}

                    {version.sampleUrl && (
                        <SamplePlayButton
                            url={version.sampleUrl}
                            size="sm"
                            variant="outline"
                            label="Preview"
                            className="h-7"
                        />
                    )}

                    {aired.length > 0 && (
                        <button
                            type="button"
                            onClick={() => setAiredOpen((o) => !o)}
                            className="inline-flex items-center gap-1 text-muted-foreground transition-colors hover:text-foreground"
                            aria-expanded={airedOpen}
                        >
                            <ICON.broadcast className="size-3.5" />
                            {aired.length} on-air
                            <ICON.expand
                                className={cn(
                                    'size-3.5 transition-transform',
                                    airedOpen && 'rotate-180'
                                )}
                            />
                        </button>
                    )}
                </div>

                {/* On-air samples — collapsed by default so this never becomes
                    a wall of rows as a variant airs hundreds of times. */}
                {airedOpen && aired.length > 0 && (
                    <ul className="mt-2 flex flex-col gap-1 rounded-md border bg-muted/20 p-2">
                        {aired.slice(0, AIRED_PREVIEW_LIMIT).map((sample) => (
                            <li
                                key={sample.jobId}
                                className="flex items-center justify-between gap-2 text-xs"
                            >
                                <span className="truncate text-muted-foreground">
                                    {sample.station ?? sample.jobId} ·{' '}
                                    {sample.measuredDurationSec}s ·{' '}
                                    {formatRelativeTime(sample.airedAt)}
                                </span>
                                <SamplePlayButton
                                    url={sample.audioUrl}
                                    size="icon"
                                    variant="ghost"
                                    label="Play on-air sample"
                                />
                            </li>
                        ))}
                        {aired.length > AIRED_PREVIEW_LIMIT && (
                            <li className="px-1 pt-0.5 text-xs text-muted-foreground">
                                +{aired.length - AIRED_PREVIEW_LIMIT} more aired
                            </li>
                        )}
                    </ul>
                )}
            </div>
        </li>
    )
}
