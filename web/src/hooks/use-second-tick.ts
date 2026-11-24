import { useSyncExternalStore } from 'react'

// One module-level clock shared by every consumer: countdowns across the
// queue, dock, and board all flip in the same frame (no per-component
// intervals drifting out of phase), and the interval only runs while at
// least one component is subscribed.
let tick = 0
let now = Date.now()
const listeners = new Set<() => void>()
let intervalId: ReturnType<typeof setInterval> | null = null

function subscribe(listener: () => void): () => void {
    listeners.add(listener)
    intervalId ??= setInterval(() => {
        tick += 1
        now = Date.now()
        for (const notify of listeners) notify()
    }, 1000)
    return () => {
        listeners.delete(listener)
        if (listeners.size === 0 && intervalId !== null) {
            clearInterval(intervalId)
            intervalId = null
        }
    }
}

const getSnapshot = () => tick
const getNowSnapshot = () => now

/**
 * Returns a monotonically incrementing counter that updates every ~1000ms.
 * Use it for re-rendering countdown/urgency displays that need a second tick.
 */
export function useSecondTick(): number {
    return useSyncExternalStore(subscribe, getSnapshot, getSnapshot)
}

/**
 * Current wall-clock time in ms, refreshed every ~1000ms in lockstep with the
 * shared tick. Pure to read during render — prefer this over calling `Date.now()`
 * in a component/memo body (which react-hooks/purity rightly flags) for any live
 * age or countdown display.
 */
export function useNow(): number {
    return useSyncExternalStore(subscribe, getNowSnapshot, getNowSnapshot)
}

/**
 * Wall-clock time refreshed every ~N seconds (default 5) instead of every
 * second — for displays where per-second freshness is imperceptible (e.g. a
 * multi-hour rundown-board timeline where a second of drift moves a beam by
 * a fraction of a pixel) but a full parent-tree re-render on every tick is
 * not free. Shares the SAME underlying 1s interval as `useSecondTick`/
 * `useNow` (no extra timer), just re-renders its subscribers less often.
 * Do NOT use this for anything needing true per-second display (countdowns,
 * deadlines) — use `useNow`/`useSecondTick` for those.
 */
export function useCoarseNow(everyNTicks = 5): number {
    const boundaryMs = everyNTicks * 1000
    const getCoarseSnapshot = () => {
        // Floor `now` to the nearest boundary — depends only on wall-clock
        // value, not on counting ticks since subscribe, so setInterval
        // jitter (a few ms per tick, inevitable under CPU throttling) can
        // never produce a "changed" snapshot mid-window. A modular
        // tick-counting approach (`now - (tick % N) * 1000`) looked
        // equivalent but isn't: `now` still advances by the actual
        // (jittered) elapsed ms every tick, so the subtraction never lands
        // on the exact same value twice and useSyncExternalStore never
        // bails out — measured and confirmed re-rendering every tick.
        return Math.floor(now / boundaryMs) * boundaryMs
    }
    return useSyncExternalStore(subscribe, getCoarseSnapshot, getCoarseSnapshot)
}
