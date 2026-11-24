import type { ReactNode } from 'react'
import { ErrorAlert } from './ErrorAlert'
import type { UseQueryResult } from '@tanstack/react-query'

interface DataBoundaryProps<T, E = Error> {
    readonly query: Pick<
        UseQueryResult<T, E>,
        'isError' | 'error' | 'isLoading' | 'data'
    > & {
        readonly refetch?: () => Promise<unknown> | unknown
    }
    readonly errorFallback: string
    /** Overrides the default retry action (the query's `refetch`). */
    readonly onRetry?: () => void
    readonly loading: ReactNode
    readonly empty: ReactNode
    /** Custom emptiness test for object-wrapped lists (e.g. `{ styles: [] }`). */
    readonly isEmpty?: (data: T) => boolean
    readonly children: ReactNode
}

export function DataBoundary<T, E = Error>({
    query: { isError, error, isLoading, data, refetch },
    errorFallback,
    onRetry,
    loading,
    empty,
    isEmpty,
    children,
}: DataBoundaryProps<T, E>) {
    if (isError)
        return (
            <ErrorAlert
                error={error}
                fallbackMessage={errorFallback}
                onRetry={onRetry ?? (refetch && (() => void refetch()))}
            />
        )
    if (isLoading) return <>{loading}</>
    // No data AND no fetch in flight is a failure state, not an empty one: a
    // query paused by a network failure (or otherwise stalled without ever
    // resolving) renders `empty` here, which silently blanks the surface
    // (bug-bash #25 — a jobs-feed failure emptied the queue, home ops band
    // and history table with no error and no retry). `empty` is only for
    // genuinely-empty results (empty arrays / isEmpty wrappers), which the
    // branch below handles.
    if (!data)
        return (
            <ErrorAlert
                error={error}
                fallbackMessage={errorFallback}
                onRetry={onRetry ?? (refetch && (() => void refetch()))}
            />
        )
    if (isEmpty ? isEmpty(data) : Array.isArray(data) && data.length === 0)
        return <>{empty}</>
    return <>{children}</>
}
