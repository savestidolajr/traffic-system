const DAY = 1440

export interface DayTrackWindow {
    readonly start: string
    readonly end: string
}

export interface DayTrackSegment {
    readonly lo: number
    readonly hi: number
}

/** Parse an HH:MM value into minutes since midnight. */
export function toMin(time: string): number {
    const [hours, minutes] = time.split(':').map(Number)
    return (hours || 0) * 60 + (minutes || 0)
}

/** Serialise minutes since midnight, wrapping into a 24-hour day. */
export function toTimeStr(minutes: number): string {
    const value = ((minutes % DAY) + DAY) % DAY
    return `${Math.floor(value / 60)
        .toString()
        .padStart(2, '0')}:${(value % 60).toString().padStart(2, '0')}`
}

/** Split a window into visual day segments, including midnight wraps. */
export function segmentsOf({ start, end }: DayTrackWindow): DayTrackSegment[] {
    const startMin = toMin(start)
    const endMin = toMin(end)
    if (startMin < endMin) return [{ lo: startMin, hi: endMin }]
    if (startMin > endMin) {
        const segments = [{ lo: startMin, hi: DAY }]
        if (endMin > 0) segments.push({ lo: 0, hi: endMin })
        return segments
    }
    return []
}

export { DAY }

/** The ONE source of `window-N` ids. Both add paths — the editor's "Add window"
 *  button and clicking an empty stretch of the timeline — must mint from here:
 *  when each kept its own counter they both started at 0 and handed two sibling
 *  rows the same React key. */
let nextWindowId = 0
export const createWindowId = () => `window-${++nextWindowId}`
