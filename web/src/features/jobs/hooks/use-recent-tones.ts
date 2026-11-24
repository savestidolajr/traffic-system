import { useCallback, useState } from 'react'

const RECENTS_KEY = 'arn.tone.recents'
const RECENTS_LIMIT = 6

/** Tolerates hand-edited or stale localStorage rather than throwing at render. */
function readRecents(): readonly string[] {
    try {
        const parsed: unknown = JSON.parse(
            localStorage.getItem(RECENTS_KEY) ?? '[]'
        )
        if (!Array.isArray(parsed)) return []
        return parsed.filter((tag): tag is string => typeof tag === 'string')
    } catch {
        return []
    }
}

/**
 * Recently-used delivery tones, per operator, per browser. Deliberately local:
 * a shared history would need market scoping and a migration, and the value
 * here is "the four tones I keep reaching for", which is personal by nature.
 *
 * Held once by the owning surface, not per line, so every picker sees the same
 * list the moment one of them adds to it.
 */
export function useRecentTones(): readonly [
    readonly string[],
    (tag: string) => void,
] {
    const [recents, setRecents] = useState<readonly string[]>(readRecents)

    const remember = useCallback((tag: string) => {
        setRecents((previous) => {
            const next = [tag, ...previous.filter((t) => t !== tag)].slice(
                0,
                RECENTS_LIMIT
            )
            localStorage.setItem(RECENTS_KEY, JSON.stringify(next))
            return next
        })
    }, [])

    return [recents, remember]
}
