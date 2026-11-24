import { Mic, Repeat, Clock } from 'lucide-react'
import { cn } from '@/lib/utils'
import { voiceColor, buildVoiceColorMap } from '../lib/voice-colors'
import { segmentsOf, DAY } from '../lib/day-track'
import type { MarketSchedule } from '../types'

interface VoiceModeGlyphProps {
    readonly schedule: MarketSchedule
    readonly voiceName: string
    readonly className?: string
}

/**
 * Compact at-a-glance voice indicator for station cards, mirroring
 * CronScheduleGlyph: single → mic + name; rotate → repeat + count;
 * time-of-day → clock + a mini colour-segmented day bar + window count.
 */
export function VoiceModeGlyph({
    schedule,
    voiceName,
    className,
}: VoiceModeGlyphProps) {
    const rot = schedule.voiceRotation

    if (rot?.mode === 'roundRobin') {
        const n = rot.voiceProfileIds.length
        return (
            <span className={cn('inline-flex items-center gap-1', className)}>
                <Repeat className="size-3 shrink-0 opacity-60" aria-hidden />
                {n} voice{n === 1 ? '' : 's'} · rotating
            </span>
        )
    }

    if (rot?.mode === 'timeOfDay') {
        const n = rot.windows.length
        const colors = buildVoiceColorMap(
            rot.windows.map((w) => w.voiceProfileId)
        )
        return (
            <span className={cn('inline-flex items-center gap-1.5', className)}>
                <Clock className="size-3 shrink-0 opacity-60" aria-hidden />
                <span
                    className="relative h-1.5 w-14 overflow-hidden rounded-full bg-muted"
                    aria-hidden
                >
                    {rot.windows.flatMap((w, i) =>
                        segmentsOf(w).map((seg, si) => (
                            <span
                                key={`${i}-${si}`}
                                className={cn(
                                    'absolute inset-y-0',
                                    voiceColor(
                                        colors.get(w.voiceProfileId) ?? 0
                                    ).dot
                                )}
                                style={{
                                    left: `${(seg.lo / DAY) * 100}%`,
                                    width: `${((seg.hi - seg.lo) / DAY) * 100}%`,
                                }}
                            />
                        ))
                    )}
                </span>
                {n} window{n === 1 ? '' : 's'}
            </span>
        )
    }

    return (
        <span
            className={cn('inline-flex min-w-0 items-center gap-1', className)}
        >
            <Mic className="size-3 shrink-0 opacity-60" aria-hidden />
            {/* Truncate, never wrap: a long variant name would put this glyph
                on a second line and reintroduce the per-card height variance
                the station card's two-row stats stack exists to remove. */}
            <span className="truncate">{voiceName}</span>
        </span>
    )
}
