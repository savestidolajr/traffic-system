import { useEffect, useRef, useState } from 'react'
import { cn } from '@/lib/utils'
import { voiceColor } from '../lib/voice-colors'
import {
    segmentsOf,
    toMin,
    toTimeStr,
    DAY,
    createWindowId,
} from '../lib/day-track'

const SNAP = 15
const LABEL_HOURS = [0, 3, 6, 9, 12, 15, 18, 21]

interface Window {
    start: string
    end: string
    voiceProfileId: string
    id?: string
}

interface VoiceTimelineProps {
    readonly windows: readonly Window[]
    readonly defaultVoiceId: string
    readonly nameOf: (id: string) => string
    readonly colorIndexOf: (id: string) => number
    readonly selectedIndex: number | null
    readonly onSelectWindow: (index: number | null) => void
    readonly onChange: (windows: Window[]) => void
}

const pad = (n: number) => n.toString().padStart(2, '0')
const snap = (min: number) =>
    Math.max(0, Math.min(DAY, Math.round(min / SNAP) * SNAP))
const pct = (min: number) => (min / DAY) * 100

/** Uncovered minute ranges across all windows → "default voice" bands. */
function gapsOf(windows: readonly Window[]): Array<[number, number]> {
    const covered = windows
        .flatMap(segmentsOf)
        .map((s) => [s.lo, s.hi] as [number, number])
        .filter(([lo, hi]) => hi > lo)
        .sort((a, b) => a[0] - b[0])
    const merged: Array<[number, number]> = []
    for (const [lo, hi] of covered) {
        const last = merged[merged.length - 1]
        if (last && lo <= last[1]) last[1] = Math.max(last[1], hi)
        else merged.push([lo, hi])
    }
    const gaps: Array<[number, number]> = []
    let cursor = 0
    for (const [lo, hi] of merged) {
        if (lo > cursor) gaps.push([cursor, lo])
        cursor = Math.max(cursor, hi)
    }
    if (cursor < DAY) gaps.push([cursor, DAY])
    return gaps
}

/** True when window i's segments intersect any other window's segments. */
function overlaps(windows: readonly Window[], i: number): boolean {
    const mine = segmentsOf(windows[i])
    return windows.some((w, j) => {
        if (j === i) return false
        return segmentsOf(w).some((o) =>
            mine.some((m) => m.lo < o.hi && o.lo < m.hi)
        )
    })
}

/** How far window i can grow before hitting a neighbour (same-day windows only). */
function neighbourBounds(
    windows: readonly Window[],
    i: number
): [number, number] {
    const s = toMin(windows[i].start)
    const e = toMin(windows[i].end)
    let lo = 0
    let hi = DAY
    windows.forEach((w, j) => {
        if (j === i) return
        for (const seg of segmentsOf(w)) {
            if (seg.hi <= s) lo = Math.max(lo, seg.hi)
            if (seg.lo >= e) hi = Math.min(hi, seg.lo)
        }
    })
    return [lo, hi]
}

const isSameDay = (w: Window) => toMin(w.start) < toMin(w.end)

/**
 * The station day as a 24-hour voice track: coloured segment per window,
 * muted bands for the default voice in gaps, overlaps ringed in red. Click an
 * empty stretch to add a window; click a segment to select it; drag a same-day
 * segment's edges to resize (15-minute snap). The row editor below is the
 * exact, keyboard-complete path.
 */
export function VoiceTimeline({
    windows,
    defaultVoiceId,
    nameOf,
    colorIndexOf,
    selectedIndex,
    onSelectWindow,
    onChange,
}: VoiceTimelineProps) {
    const trackRef = useRef<HTMLDivElement>(null)
    const [drag, setDrag] = useState<{
        index: number
        edge: 'start' | 'end'
    } | null>(null)

    const minAtClientX = (clientX: number): number | null => {
        const rect = trackRef.current?.getBoundingClientRect()
        if (!rect || rect.width === 0) return null
        const ratio = Math.min(
            Math.max((clientX - rect.left) / rect.width, 0),
            1
        )
        return snap(ratio * DAY)
    }

    useEffect(() => {
        if (!drag) return
        const onMove = (e: PointerEvent) => {
            const min = minAtClientX(e.clientX)
            if (min === null) return
            const w = windows[drag.index]
            if (!w || !isSameDay(w)) return
            const [lo, hi] = neighbourBounds(windows, drag.index)
            const s = toMin(w.start)
            const en = toMin(w.end)
            const next = [...windows]
            if (drag.edge === 'start') {
                const ns = Math.max(lo, Math.min(min, en - SNAP))
                next[drag.index] = { ...w, start: toTimeStr(ns) }
            } else {
                const ne = Math.min(hi, Math.max(min, s + SNAP))
                next[drag.index] = { ...w, end: toTimeStr(ne) }
            }
            onChange(next)
        }
        const onUp = () => setDrag(null)
        window.addEventListener('pointermove', onMove)
        window.addEventListener('pointerup', onUp)
        return () => {
            window.removeEventListener('pointermove', onMove)
            window.removeEventListener('pointerup', onUp)
        }
    }, [drag, windows, onChange])

    const gaps = gapsOf(windows)
    const addWindowAt = (min: number) => {
        const gap = gaps.find(([lo, hi]) => min >= lo && min < hi)
        const [lo, hi] = gap ?? [snap(min), Math.min(snap(min) + 120, DAY)]
        const start = snap(Math.max(lo, Math.min(min, hi - SNAP)))
        const end = Math.min(start + 120, hi)
        onChange([
            ...windows,
            {
                id: createWindowId(),
                start: toTimeStr(start),
                end: toTimeStr(end),
                voiceProfileId: '',
            },
        ])
        onSelectWindow(windows.length)
    }

    const handleTrackClick = (e: React.MouseEvent) => {
        const min = minAtClientX(e.clientX)
        if (min === null) return
        addWindowAt(min)
    }

    const handleTrackKeyDown = (e: React.KeyboardEvent) => {
        if (e.key !== 'Enter' && e.key !== ' ') return
        e.preventDefault()
        addWindowAt(gaps[0]?.[0] ?? 0)
    }

    return (
        <div className="flex flex-col gap-1">
            <div
                ref={trackRef}
                onClick={handleTrackClick}
                onKeyDown={handleTrackKeyDown}
                role="button"
                tabIndex={0}
                aria-label="Voice day track: click an empty stretch to add a window"
                className="relative h-14 cursor-copy overflow-hidden rounded-lg border bg-muted/20 outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
                {Array.from({ length: 23 }, (_, i) => i + 1).map((h) => (
                    <div
                        key={h}
                        aria-hidden
                        className={cn(
                            'absolute inset-y-0 w-px',
                            h % 3 === 0 ? 'bg-border/50' : 'bg-border/20'
                        )}
                        style={{ left: `${pct(h * 60)}%` }}
                    />
                ))}

                {gaps.map(([lo, hi]) => (
                    <div
                        key={`gap-${lo}-${hi}`}
                        aria-hidden
                        title={`Default voice: ${nameOf(defaultVoiceId)}`}
                        className="absolute inset-y-0 flex items-center justify-center bg-[repeating-linear-gradient(45deg,hsl(var(--muted-foreground)/0.07)_0_6px,transparent_6px_12px)]"
                        style={{
                            left: `${pct(lo)}%`,
                            width: `${pct(hi - lo)}%`,
                        }}
                    >
                        {hi - lo >= 180 && (
                            <span className="truncate px-1 text-3xs text-muted-foreground-subtle">
                                {nameOf(defaultVoiceId) || 'Default'}
                            </span>
                        )}
                    </div>
                ))}

                {windows.map((w, i) =>
                    segmentsOf(w).map((seg, si) => {
                        const colorIdx = colorIndexOf(w.voiceProfileId)
                        const hasVoice =
                            w.voiceProfileId !== '' && colorIdx >= 0
                        const color = hasVoice ? voiceColor(colorIdx) : null
                        const bad = overlaps(windows, i)
                        const selected = selectedIndex === i
                        const draggable = isSameDay(w)
                        return (
                            <button
                                type="button"
                                key={`w-${w.id ?? i}-${si}`}
                                onClick={(e) => {
                                    e.stopPropagation()
                                    onSelectWindow(i)
                                }}
                                aria-label={`${w.start} to ${w.end}, ${
                                    hasVoice
                                        ? nameOf(w.voiceProfileId)
                                        : 'no voice'
                                }, select to edit`}
                                className={cn(
                                    'group absolute inset-y-1 flex items-center justify-center overflow-hidden rounded-md px-1 text-3xs font-medium ring-offset-1 ring-offset-background transition-shadow',
                                    color?.bar ??
                                        'border border-dashed border-muted-foreground/50 bg-muted/40 text-muted-foreground',
                                    selected && 'ring-2 ring-primary',
                                    bad && 'ring-2 ring-destructive'
                                )}
                                style={{
                                    left: `${pct(seg.lo)}%`,
                                    width: `${pct(seg.hi - seg.lo)}%`,
                                }}
                            >
                                <span className="pointer-events-none truncate">
                                    {hasVoice
                                        ? nameOf(w.voiceProfileId)
                                        : 'Pick voice'}
                                </span>
                                {draggable && (
                                    <>
                                        <span
                                            role="presentation"
                                            onPointerDown={(e) => {
                                                e.stopPropagation()
                                                setDrag({
                                                    index: i,
                                                    edge: 'start',
                                                })
                                            }}
                                            className="absolute inset-y-0 left-0 w-2 cursor-ew-resize bg-black/15 opacity-0 group-hover:opacity-100"
                                        />
                                        <span
                                            role="presentation"
                                            onPointerDown={(e) => {
                                                e.stopPropagation()
                                                setDrag({
                                                    index: i,
                                                    edge: 'end',
                                                })
                                            }}
                                            className="absolute inset-y-0 right-0 w-2 cursor-ew-resize bg-black/15 opacity-0 group-hover:opacity-100"
                                        />
                                    </>
                                )}
                            </button>
                        )
                    })
                )}
            </div>

            <div className="relative h-4" aria-hidden>
                {LABEL_HOURS.map((h) => (
                    <span
                        key={h}
                        className={cn(
                            'absolute font-mono text-3xs tabular-nums text-muted-foreground-subtle',
                            // Midnight sits on the track's left edge: centring
                            // it would hang the label outside the content
                            // column, exactly as 24:00 is pinned flush right.
                            h > 0 && '-translate-x-1/2'
                        )}
                        style={{ left: `${pct(h * 60)}%` }}
                    >
                        {pad(h)}:00
                    </span>
                ))}
                <span className="absolute right-0 font-mono text-3xs tabular-nums text-muted-foreground-subtle">
                    24:00
                </span>
            </div>
        </div>
    )
}
