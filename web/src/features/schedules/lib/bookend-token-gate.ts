import { useMemo } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { useBookends } from '@/features/bookends/api/bookend-hooks'
import { extractTokensAcross } from '@/features/bookends/lib/tokens'
import type { Bookend } from '@/features/bookends/types'
import type {
    BookendSelectionMode,
    BookendWindow,
} from '@/lib/api/schemas/shared'
import type { ScheduleFormData } from '../types'

/**
 * Client-side mirror of the backend's bookend-token save gate
 * (ScheduleHttpTrigger.ValidateBookendTokens / BookendTokenResolver):
 * a station must never be saved into a state that would air a bookend with an
 * unfillable token, because the resolver SILENTLY drops an unknown token at
 * airtime. The backend REFUSES the save with a 400; this module computes the
 * same refusal client-side so the studio can gate Save up front — including
 * for rotation members, whose unfillable tokens the preview never showed.
 *
 * The mirror is deliberately conservative about what it can know:
 * `city`/`market` resolve from the backend's KnownMarketNames table (the
 * authoritative market set, not a web copy), so they count as fillable here;
 * a brand-new market id the table does not know yet still gets the backend's
 * clear 400. Every CUSTOM token (anything not built-in) is unfillable unless
 * the operator supplies an override row — which is exactly the case that used
 * to surface only as a post-save rejection.
 */

/** A referenced bookend whose template carries a token with no value for this station. */
export interface BookendTokenBlocker {
    readonly bookendId: string
    readonly bookendName: string
    readonly unresolvedTokens: string[]
}

const BUILT_IN: Record<string, true> = {
    city: true,
    market: true,
    station: true,
}

/**
 * Every bookend id that could air in a slot: the primary pair plus any rotation
 * member. Mirrors ScheduleHttpTrigger's construction of `bookendIds` — a
 * TimeOfDay rotation falls back to the primary, so the primary is referenced
 * even when a rotation is configured.
 */
export function referencedBookendIds(
    bookendId: string,
    rotationMode: BookendSelectionMode,
    rotationIds: readonly string[],
    windows: readonly BookendWindow[]
): string[] {
    const ids = new Set<string>()
    const primary = bookendId.trim()
    if (primary) ids.add(primary)
    if (rotationMode === 'roundRobin') {
        for (const id of rotationIds) {
            const trimmed = id.trim()
            if (trimmed) ids.add(trimmed)
        }
    } else if (rotationMode === 'timeOfDay') {
        for (const window of windows) {
            const id = window.bookendId.trim()
            if (id) ids.add(id)
        }
    }
    return [...ids]
}

/**
 * The unfillable tokens for one referenced bookend, mirroring
 * BookendTokenResolver.BuildValues + ExtractTokens: built-ins first, then
 * overrides applied on top; a token with no value is unfillable.
 */
export function computeBookendTokenBlockers(params: {
    readonly bookends: readonly Bookend[]
    readonly bookendId: string
    readonly bookendRotationMode: BookendSelectionMode
    readonly bookendRotationIds: readonly string[]
    readonly bookendWindows: readonly BookendWindow[]
    readonly station: string
    readonly tokenRows: readonly { key: string; value: string }[]
}): BookendTokenBlocker[] {
    const {
        bookends,
        bookendId,
        bookendRotationMode,
        bookendRotationIds,
        bookendWindows,
        station,
        tokenRows,
    } = params

    const referenced = referencedBookendIds(
        bookendId,
        bookendRotationMode,
        bookendRotationIds,
        bookendWindows
    )
    if (referenced.length === 0) return []

    // Override values, case-insensitively keyed — mirrors the backend's
    // per-station overrides (blank rows are dropped by the payload builder).
    const overrides = new Map<string, string>()
    for (const row of tokenRows) {
        const key = row.key.trim()
        const value = row.value.trim()
        if (key && value) overrides.set(key.toLowerCase(), value)
    }
    const stationValue = station.trim()

    const blockers: BookendTokenBlocker[] = []
    for (const id of referenced) {
        const bookend = bookends.find(
            (b) => b.id.toLowerCase() === id.toLowerCase()
        )
        // The selectors only offer active bookends, so a referenced id is
        // always present; referential integrity is their job, not this gate's.
        if (!bookend) continue
        const unresolved = extractTokensAcross([
            bookend.introText,
            bookend.outroText,
        ]).filter((token) => {
            const lower = token.toLowerCase()
            if (overrides.has(lower)) return false
            if (lower === 'station') return !stationValue
            // city/market derive from the market's known place name server-side.
            return !BUILT_IN[lower]
        })
        if (unresolved.length > 0) {
            blockers.push({
                bookendId: id,
                bookendName: bookend.name,
                unresolvedTokens: unresolved,
            })
        }
    }
    return blockers
}

/** The named-token refusal message, shown at the Save gate and under the tokens editor. */
export function formatBookendBlocker(blocker: BookendTokenBlocker): string {
    return (
        `Bookend '${blocker.bookendName}' uses token(s) ` +
        `{${blocker.unresolvedTokens.join('}, {')}} with no value for this station. ` +
        'Save will be refused. Add them in BookendTokens, or remove them from the template.'
    )
}

/**
 * Live blockers for the schedule form: every referenced bookend (primary plus
 * rotation members) whose template carries a token with no value for this
 * station. Shared by the Speech step's warnings and the studio's Save gate so
 * the gate message and the visible warning can never disagree.
 */
export function useBookendTokenBlockers(
    form: UseFormReturn<ScheduleFormData>
): BookendTokenBlocker[] {
    const bookendId = useWatch({ control: form.control, name: 'bookendId' })
    const bookendRotationMode = useWatch({
        control: form.control,
        name: 'bookendRotationMode',
    })
    const bookendRotationIds = useWatch({
        control: form.control,
        name: 'bookendRotationIds',
    })
    const bookendWindows = useWatch({
        control: form.control,
        name: 'bookendWindows',
    })
    const station = useWatch({ control: form.control, name: 'station' })
    const tokenRows = useWatch({
        control: form.control,
        name: 'bookendTokens',
    })
    const { data: bookendsData } = useBookends()

    return useMemo(
        () =>
            computeBookendTokenBlockers({
                bookends: bookendsData?.bookends ?? [],
                bookendId,
                bookendRotationMode,
                bookendRotationIds: bookendRotationIds ?? [],
                bookendWindows: bookendWindows ?? [],
                station: station ?? '',
                tokenRows: tokenRows ?? [],
            }),
        [
            bookendsData,
            bookendId,
            bookendRotationMode,
            bookendRotationIds,
            bookendWindows,
            station,
            tokenRows,
        ]
    )
}
