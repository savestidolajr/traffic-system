import { useQuery } from '@tanstack/react-query'
import { AUTH_ME } from '@/lib/api/paths'

// ── Auth query key (shared cache) ──────────────────────────────
export const AUTH_QUERY_KEY = ['auth', 'me', 'session'] as const

// ── Dev auth configuration ─────────────────────────────────────
// Mock mode implies simulated auth: a mock bundle has no SWA in front of it,
// so `/.auth/me` can never succeed — without this, a static mock build
// (vite build + preview, e.g. the UX-audit harness) walls every page behind
// "Sign in required" while `vite dev` (import.meta.env.DEV) sails through.
const USE_DEV_AUTH =
    import.meta.env.VITE_USE_DEV_AUTH === 'true' ||
    import.meta.env.VITE_USE_MOCKS === 'true' ||
    import.meta.env.DEV
const DEV_USER_NAME = import.meta.env.VITE_DEV_USER_NAME ?? 'Dev User'
const DEV_USER_ROLES = import.meta.env.VITE_DEV_USER_ROLES?.split(',').filter(
    Boolean
) ?? ['admin']

// ── SWA response shapes ───────────────────────────────────────
interface SwaClientPrincipal {
    userDetails: string
    userRoles: string[]
    [key: string]: unknown
}

interface SwaArrayItem {
    clientPrincipal?: SwaClientPrincipal
    accessToken?: string
    idToken?: string
}

export interface AuthData {
    userName: string | null
    userRoles: string[]
    accessToken: string | null
}

/**
 * Parse SWA `/.auth/me` responses that can be either:
 *
 * - **Object format** (classic SWA):
 *   `{ clientPrincipal: { userDetails, userRoles, ... } }`
 *
 * - **Array format** (SWA EasyAuth / static-web-apps-config):
 *   `[ { clientPrincipal?, accessToken?, idToken? } ]`
 */
export function parseAuthPayload(data: unknown): AuthData {
    // Array format
    if (Array.isArray(data) && data.length > 0) {
        const item = data[0] as SwaArrayItem
        return {
            userName: item.clientPrincipal?.userDetails ?? null,
            userRoles: item.clientPrincipal?.userRoles ?? [],
            accessToken: item.accessToken ?? item.idToken ?? null,
        }
    }

    // Object format
    const obj = data as Record<string, unknown> | null | undefined
    const principal = obj?.clientPrincipal as SwaClientPrincipal | undefined
    return {
        userName: principal?.userDetails ?? null,
        userRoles: principal?.userRoles ?? [],
        accessToken: null,
    }
}

async function fetchAuthData(): Promise<AuthData> {
    if (USE_DEV_AUTH) {
        return {
            userName: DEV_USER_NAME,
            userRoles: DEV_USER_ROLES,
            accessToken: null,
        }
    }

    try {
        const res = await fetch(AUTH_ME)
        if (!res.ok) {
            return { userName: null, userRoles: [], accessToken: null }
        }
        return parseAuthPayload(await res.json())
    } catch {
        return { userName: null, userRoles: [], accessToken: null }
    }
}

/**
 * Shared auth hook backed by TanStack Query.
 * Every caller (`useCurrentUser`, `useRoles`, `useIsAdmin`, etc.)
 * shares one query cache entry and one network request.
 */
export function useAuthData() {
    return useQuery({
        queryKey: AUTH_QUERY_KEY,
        queryFn: fetchAuthData,
        staleTime: 5 * 60 * 1000,
        retry: false,
    })
}
