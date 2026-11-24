import { useCallback, useState } from 'react'

/** How an entity-list page renders its items: the card gallery or a table. */
export type ViewMode = 'cards' | 'table'

const STORAGE_PREFIX = 'arn:view-mode:'

function isViewMode(value: string | null): value is ViewMode {
    return value === 'cards' || value === 'table'
}

function readStored(key: string): ViewMode | null {
    try {
        const stored = window.localStorage.getItem(STORAGE_PREFIX + key)
        return isViewMode(stored) ? stored : null
    } catch {
        // Private mode / disabled storage — fall back to the in-memory default.
        return null
    }
}

/**
 * Card/table view-mode toggle state, persisted per page under a stable `key`
 * so the operator's choice survives reloads and navigation. Mirrors the shape
 * of `useState` (value + setter) so pages read like local state.
 */
export function useViewMode(
    key: string,
    fallback: ViewMode = 'cards'
): readonly [ViewMode, (mode: ViewMode) => void] {
    const [mode, setMode] = useState<ViewMode>(
        () => readStored(key) ?? fallback
    )

    const update = useCallback(
        (next: ViewMode) => {
            setMode(next)
            try {
                window.localStorage.setItem(STORAGE_PREFIX + key, next)
            } catch {
                // Ignore write failures (quota / private mode); state still updates.
            }
        },
        [key]
    )

    return [mode, update] as const
}
