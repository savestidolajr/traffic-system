import { describe, it, expect } from 'vitest'
import { ApiError, isPermanentClientError } from './client'

/**
 * The mutation retry policy is not screenshot-able, so this is its proof.
 *
 * The regression it guards: `mutations: { retry: 1 }` retried every failure,
 * including a permanent 4xx. An approve whose response was lost got retried
 * with the same now-stale etag, the server answered 412, and the operator was
 * told the approval had failed when it had in fact landed. The query branch of
 * the same config object already refused 4xx, so the principle was understood
 * and simply not applied to mutations.
 */
describe('isPermanentClientError', () => {
    it('refuses to retry the precondition failure an approve race produces', () => {
        expect(isPermanentClientError(new ApiError('etag mismatch', 412))).toBe(
            true
        )
        expect(isPermanentClientError(new ApiError('conflict', 409))).toBe(true)
    })

    it('refuses to retry the rest of the 4xx range', () => {
        for (const status of [400, 401, 403, 404, 422, 499]) {
            expect(isPermanentClientError(new ApiError('nope', status))).toBe(
                true
            )
        }
    })

    it('allows a retry for a server fault or a transport failure', () => {
        for (const status of [500, 502, 503, 504]) {
            expect(isPermanentClientError(new ApiError('boom', status))).toBe(
                false
            )
        }
        // A network error never reaches the server, so it carries no status and
        // is exactly the case a single retry is for.
        expect(isPermanentClientError(new ApiError('network down'))).toBe(false)
        expect(isPermanentClientError(new Error('offline'))).toBe(false)
        expect(isPermanentClientError(undefined)).toBe(false)
    })

    it('treats 399 and 500 as outside the permanent band', () => {
        expect(isPermanentClientError(new ApiError('redirect', 399))).toBe(
            false
        )
        expect(isPermanentClientError(new ApiError('server', 500))).toBe(false)
    })
})
