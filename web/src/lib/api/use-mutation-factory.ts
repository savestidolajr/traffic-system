import { ApiError } from '@/lib/api/client'
import {
    useMutation,
    useQueryClient,
    type QueryKey,
} from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
export interface OptimisticMutationOptions<TVars> {
    /** Base key pattern for queries to cancel and snapshot (e.g. ['jobs']) */
    cancelKey: QueryKey
    /** Apply optimistic updates after cancellation */
    apply(vars: TVars, queryClient: QueryClient): void
}

/** Root of the activity-log query tree (`createQueryKeys('activity')`). */
const ACTIVITY_SCOPE: QueryKey = ['activity']
const PRECONDITION_STATUSES = new Set([409, 412, 428])

function isPreconditionFailure(error: unknown): boolean {
    return (
        error instanceof ApiError &&
        error.status !== undefined &&
        PRECONDITION_STATUSES.has(error.status)
    )
}

/**
 * Creates a TanStack Query mutation with consistent invalidation.
 *
 * Two modes:
 * 1. **Simple** — invalidates query keys on success.
 * 2. **Optimistic** — cancels matching queries, snapshots them, applies
 *    optimistic updates via `apply()`, rolls back on error, and invalidates
 *    on settle (success or error).
 *
 * @param mutationFn  The async mutation function
 * @param invalidateKeys  Compute query keys to invalidate (from mutation vars)
 * @param optimistic  Optional — enables optimistic mode with cancel/snapshot/rollback
 */
export function useMutationFactory<TVars, TData = void>(
    mutationFn: (args: TVars) => Promise<TData>,
    invalidateKeys: (vars: TVars) => QueryKey[],
    optimistic?: OptimisticMutationOptions<TVars>
) {
    const queryClient = useQueryClient()

    const invalidate = async (vars: TVars) => {
        for (const key of [...invalidateKeys(vars), ACTIVITY_SCOPE]) {
            await queryClient.invalidateQueries({ queryKey: key })
        }
        // Every mutation here is auditable: the backend appends an activity
        // entry for it. Eight surfaces render that log beside the editor that
        // wrote it (both schedule studios' History step, and the
        // `ScopedActivityHistory` panels under guidance, operators,
        // safety-terms, verified-pronunciations, filler-statements and
        // priority-roads), and no feature hook invalidated it - so with a 30s
        // staleTime an operator saved a change and then read a History list
        // that did not contain it. Invalidating the scope centrally is right
        // for every caller and costs a refetch only where a feed is mounted.
    }

    return useMutation<TData, Error, TVars>({
        mutationFn,
        ...(optimistic
            ? {
                  onMutate: async (vars) => {
                      await queryClient.cancelQueries({
                          queryKey: optimistic.cancelKey,
                      })
                      const snapshot = queryClient.getQueriesData({
                          queryKey: optimistic.cancelKey,
                      })
                      optimistic.apply(vars, queryClient)
                      return { __snapshot: snapshot }
                  },
                  onError: (_e, _v, ctx) => {
                      const c = ctx as
                          { __snapshot: Array<[QueryKey, unknown]> } | undefined
                      if (!c?.__snapshot) return
                      for (const [key, data] of c.__snapshot) {
                          queryClient.setQueryData(key, data)
                      }
                  },
                  onSettled: async (_d, _e, vars) => {
                      // Await the detail refetch so the next ETag is rendered
                      // before an action button is enabled again.
                      await invalidate(vars)
                  },
              }
            : {
                  onSuccess: async (_d, vars) => {
                      // Await the detail refetch so the next ETag is rendered
                      // before an action button is enabled again.
                      await invalidate(vars)
                  },
                  onError: async (error, vars) => {
                      // A failed precondition leaves the captured ETag stale.
                      // Refetch immediately so the retry carries the current
                      // revision instead of repeating 409/412/428.
                      if (isPreconditionFailure(error)) await invalidate(vars)
                  },
              }),
    })
}
