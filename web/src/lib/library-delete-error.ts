import { ApiError } from '@/lib/api/client'

/**
 * The four versioned per-station libraries share the backend's delete guard:
 * deleting an entity a schedule still references fails with a 409
 * `library.in_use`, and the API message names the stations holding it. This
 * formats that refusal for `toastAction` — surfacing the API's reason verbatim
 * instead of the generic "Could not delete" every gallery would otherwise
 * show.
 */
export function inUseDeleteError(noun: string) {
    return (err: unknown): string | undefined =>
        err instanceof ApiError && err.status === 409
            ? `Cannot delete this ${noun}: ${
                  err.message || 'it is still assigned to stations'
              }`
            : undefined
}
