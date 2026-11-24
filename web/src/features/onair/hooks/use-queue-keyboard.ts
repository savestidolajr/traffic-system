import { useCallback } from 'react'
import { useGlobalKeydown } from '@/hooks/use-global-keydown'
import type { JobSummary } from '@/features/jobs/types'

/**
 * Global keyboard navigation for the queue rail.
 * j / ArrowDown  → next item
 * k / ArrowUp    → previous item
 * Enter          → open selected item
 *
 * Dialog/form-field checks are handled by useGlobalKeydown.
 */
export function useQueueKeyboard({
    items,
    selectedJobId,
    onSelect,
}: {
    readonly items: readonly JobSummary[]
    readonly selectedJobId: string | null
    readonly onSelect: (jobId: string) => void
}): void {
    const handleEnter = useCallback(() => {
        if (selectedJobId) onSelect(selectedJobId)
    }, [selectedJobId, onSelect])

    useGlobalKeydown('Enter', handleEnter)

    const currentIndex = selectedJobId
        ? items.findIndex((j) => j.jobId === selectedJobId)
        : -1

    const handleJ = useCallback(() => {
        const nextIndex = currentIndex < items.length - 1 ? currentIndex + 1 : 0
        const next = items[nextIndex]
        if (next) onSelect(next.jobId)
    }, [currentIndex, items, onSelect])

    const handleK = useCallback(() => {
        const nextIndex = currentIndex > 0 ? currentIndex - 1 : items.length - 1
        const next = items[nextIndex]
        if (next) onSelect(next.jobId)
    }, [currentIndex, items, onSelect])

    useGlobalKeydown('j', handleJ)
    useGlobalKeydown('k', handleK)
}
