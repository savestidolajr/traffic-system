// @vitest-environment jsdom
import type { ReactNode } from 'react'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from './client'
import { useMutationFactory } from './use-mutation-factory'

function makeQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    })
}

function wrapperFor(queryClient: QueryClient) {
    return ({ children }: { readonly children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
}

function deferred<T>() {
    let resolve!: (value: T) => void
    const promise = new Promise<T>((nextResolve) => {
        resolve = nextResolve
    })
    return { promise, resolve }
}

afterEach(() => {
    cleanup()
})

describe('useMutationFactory ETag invalidation', () => {
    it('keeps a mutation pending until the successful detail refetch settles', async () => {
        const queryClient = makeQueryClient()
        const invalidation = deferred<void>()
        const invalidateQueries = vi
            .spyOn(queryClient, 'invalidateQueries')
            .mockImplementation(() => invalidation.promise)
        const { result } = renderHook(
            () =>
                useMutationFactory<void>(
                    async () => undefined,
                    () => [['jobs', 'detail', 'job-1']]
                ),
            { wrapper: wrapperFor(queryClient) }
        )

        let mutationPromise!: Promise<void>
        act(() => {
            mutationPromise = result.current.mutateAsync()
        })
        await waitFor(() => expect(result.current.isPending).toBe(true))
        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: ['jobs', 'detail', 'job-1'],
        })

        await act(async () => {
            invalidation.resolve()
            await mutationPromise
        })

        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: ['activity'],
        })
        await waitFor(() => expect(result.current.isPending).toBe(false))
    })

    it('refetches the detail after an ETag precondition failure, but not other errors', async () => {
        const queryClient = makeQueryClient()
        const invalidateQueries = vi
            .spyOn(queryClient, 'invalidateQueries')
            .mockResolvedValue()
        const preconditionError = new ApiError('stale revision', 412)
        const { result } = renderHook(
            () =>
                useMutationFactory<void>(
                    async () => {
                        throw preconditionError
                    },
                    () => [['jobs', 'detail', 'job-1']]
                ),
            { wrapper: wrapperFor(queryClient) }
        )

        await act(async () => {
            await expect(result.current.mutateAsync()).rejects.toBe(
                preconditionError
            )
        })

        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: ['jobs', 'detail', 'job-1'],
        })
        expect(invalidateQueries).toHaveBeenCalledWith({
            queryKey: ['activity'],
        })

        invalidateQueries.mockClear()
        const ordinaryError = new ApiError('validation failed', 422)
        const ordinary = renderHook(
            () =>
                useMutationFactory<void>(
                    async () => {
                        throw ordinaryError
                    },
                    () => [['jobs', 'detail', 'job-1']]
                ),
            { wrapper: wrapperFor(queryClient) }
        )

        await act(async () => {
            await expect(ordinary.result.current.mutateAsync()).rejects.toBe(
                ordinaryError
            )
        })
        expect(invalidateQueries).not.toHaveBeenCalled()
        ordinary.unmount()
    })
})
