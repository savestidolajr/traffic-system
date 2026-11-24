import { STALE_MS } from '@/lib/constants/timing'
import {
    useQuery,
    useInfiniteQuery,
    type Query,
    type QueryKey,
    type InfiniteData,
} from '@tanstack/react-query'

export interface QueryFactoryOptions<TData> {
    readonly staleTime?: number
    readonly refetchInterval?:
        | number
        | false
        | ((
              query: Query<TData, Error, TData, QueryKey>
          ) => number | false | undefined)
    readonly enabled?: boolean
    readonly retry?: number | false
}

export function useQueryFactory<TData>(
    queryKey: QueryKey,
    queryFn: (signal?: AbortSignal) => Promise<TData>,
    opts?: QueryFactoryOptions<TData>
) {
    return useQuery<TData>({
        queryKey,
        queryFn: ({ signal }) => queryFn(signal),
        staleTime: opts?.staleTime ?? STALE_MS,
        refetchInterval: opts?.refetchInterval ?? false,
        enabled: opts?.enabled ?? true,
        retry: opts?.retry ?? 1,
    })
}

export interface InfiniteQueryFactoryOptions<TPage> {
    readonly staleTime?: number
    readonly enabled?: boolean
    readonly refetchInterval?:
        | number
        | false
        | ((
              query: Query<
                  TPage,
                  Error,
                  InfiniteData<TPage, string | undefined>,
                  QueryKey
              >
          ) => number | false | undefined)
    readonly refetchIntervalInBackground?: boolean
    /** Returns the next page's cursor, or `undefined` when there is nothing older to load. */
    readonly getNextPageParam: (lastPage: TPage) => string | undefined
}

/**
 * "Load older" pagination: pages accumulate oldest-last, each one's cursor derived from the
 * previous page's own data (a `to` timestamp — the last item's own key field) rather than an
 * offset, matching how the backend's date-range-cursor endpoints (Activity, Jobs history) page.
 */
export function useInfiniteQueryFactory<TPage>(
    queryKey: QueryKey,
    queryFn: (
        cursor: string | undefined,
        signal?: AbortSignal
    ) => Promise<TPage>,
    opts: InfiniteQueryFactoryOptions<TPage>
) {
    return useInfiniteQuery<
        TPage,
        Error,
        InfiniteData<TPage, string | undefined>,
        QueryKey,
        string | undefined
    >({
        queryKey,
        queryFn: ({ pageParam, signal }) => queryFn(pageParam, signal),
        initialPageParam: undefined,
        getNextPageParam: (lastPage) => opts.getNextPageParam(lastPage),
        staleTime: opts.staleTime ?? STALE_MS,
        refetchInterval: opts.refetchInterval ?? false,
        refetchIntervalInBackground: opts.refetchIntervalInBackground ?? false,
        enabled: opts.enabled ?? true,
    })
}
