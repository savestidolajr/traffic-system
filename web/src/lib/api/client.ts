import axios, { AxiosHeaders } from 'axios'

export class ApiError extends Error {
    readonly status: number | undefined
    /**
     * The server's parsed response body, verbatim.
     *
     * The interceptor below turns every axios failure into an `ApiError`, so
     * `error.response` is gone by the time a caller sees it — yet callers kept
     * reaching for `err.response?.status` / `err.response?.data`, which is
     * always `undefined` here. That silently discarded every structured error
     * body the backend sends: the config-import dry-run's per-row error report,
     * and the approve gate's 403 ("a different admin must approve") and 409
     * ("the plan changed since it was submitted…") messages. Whatever needs
     * more than a message reads `status` and `body`.
     */
    readonly body: unknown
    readonly originalError: unknown

    constructor(
        message: string,
        status?: number,
        originalError?: unknown,
        body?: unknown
    ) {
        super(message)
        this.name = 'ApiError'
        this.status = status
        this.body = body
        this.originalError = originalError
    }
}

/**
 * A 4xx the server will answer identically on a retry: a validation failure, a
 * precondition (etag) mismatch, a missing row, a permission denial.
 *
 * Retrying one of these is never merely wasteful. On an approve, whose response
 * was lost in transit, the retry carries the same now-stale etag, the server
 * answers 412, and the operator is told the approval FAILED when it landed. The
 * query retry policy already refused 4xx; mutations shared none of that logic,
 * which is what this predicate exists to close.
 */
export function isPermanentClientError(error: unknown): boolean {
    return (
        error instanceof ApiError &&
        error.status !== undefined &&
        error.status >= 400 &&
        error.status < 500
    )
}

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'
export const API_TOKEN = import.meta.env.VITE_API_TOKEN ?? null
const USE_DEV_AUTH =
    import.meta.env.VITE_USE_DEV_AUTH === 'true' || import.meta.env.DEV

/** True when the app is running against simulated data instead of a backend. */
export const isMockMode = import.meta.env.VITE_USE_MOCKS === 'true'

/**
 * Resolve a job audio URL to something an `<audio>` element can load.
 * Absolute URLs (SAS, external filler carts, mock blobs) pass through untouched;
 * a relative proxy path (`jobs/{id}/audio`) is joined onto the API base so it
 * rides the SWA linked-backend proxy with the auth cookie (audio elements
 * cannot attach bearer headers).
 */
export function resolveAudioSrc(url: string): string {
    if (!url || /^(https?:|blob:|data:|local:)/i.test(url)) return url
    // Static mock assets under public/mock/ are served directly by Vite in dev
    // and bundled into the SWA in prod. They MUST NOT be joined onto the API base
    // (which routes through the /api proxy to the Functions backend), because in
    // mock mode there is no backend — the request would 502. Pass them through
    // so mock mode makes zero network requests that fail.
    if (url.startsWith('/mock/')) return url
    return `${API_BASE_URL}/${url.replace(/^\/+/, '')}`
}

export const apiClient = axios.create({
    baseURL: API_BASE_URL,
    headers: {
        'Content-Type': 'application/json',
    },
})

// =============================================================================
// Mock Adapter — activated by VITE_USE_MOCKS=true, no backend needed
// =============================================================================
if (isMockMode) {
    const { createMockAdapter } = await import('./mock')
    apiClient.defaults.adapter = createMockAdapter()
}

apiClient.interceptors.request.use((config) => {
    // The SWA linked backend injects X-MS-CLIENT-PRINCIPAL on every /api/* request,
    // which the Functions AuthorizationMiddleware authenticates. No bearer token is
    // needed in production (the backend ignores Authorization), so we avoid a
    // per-request /.auth/me round trip. Local dev may attach an optional API token
    // for direct-to-Functions calls.
    if (USE_DEV_AUTH && API_TOKEN) {
        const headers = new AxiosHeaders(config.headers)
        headers.set('Authorization', `Bearer ${API_TOKEN}`)
        config.headers = headers
    }
    return config
})

apiClient.interceptors.response.use(
    (response) => response,
    (error) => {
        // Backend ErrorResponse serializes PascalCase ({ Error, Detail, Status }); accept both
        // casings so the server's message (e.g. ElevenLabs validation text) reaches the UI
        // instead of axios's generic "Request failed with status code N".
        const data = error.response?.data
        const message =
            data?.error ||
            data?.Error ||
            data?.message ||
            data?.Message ||
            error.message ||
            'An unexpected error occurred'
        const status = error.response?.status

        return Promise.reject(new ApiError(message, status, error, data))
    }
)
