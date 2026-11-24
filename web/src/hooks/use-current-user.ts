import { useAuthData } from '@/lib/auth'

const ADMIN_ROLES: Record<string, true> = {
    admin: true,
    administrator: true,
}
const OPERATOR_ROLES: Record<string, true> = {
    operator: true,
}

export function hasAdminRole(userRoles: readonly string[]): boolean {
    return userRoles.some((r) => ADMIN_ROLES[r.toLowerCase()] === true)
}

export function hasOperatorRole(userRoles: readonly string[]): boolean {
    return userRoles.some((r) => OPERATOR_ROLES[r.toLowerCase()] === true)
}

/** SWA stamps these on every authenticated principal; they are not app roles. */
const SWA_PSEUDO_ROLES = new Set(['anonymous', 'authenticated'])

/**
 * True when the caller is SWA-authenticated (carries the `authenticated`
 * pseudo-role) but no custom role source is wired up (dev's built-in
 * provider, or a test/prod custom-provider caller with no app role
 * assigned yet). In this state the frontend cannot see Operator/Admin at
 * all, so per-route/UI gates must defer entirely to backend RBAC rather
 * than blocking on a role SWA never forwards.
 */
function isAuthenticatedWithoutCustomRoles(
    userRoles: readonly string[]
): boolean {
    const normalized = userRoles.map((r) => r.toLowerCase())
    if (!normalized.includes('authenticated')) return false
    return normalized.every((r) => SWA_PSEUDO_ROLES.has(r))
}

/**
 * True when the caller carries an explicit app role (Admin/Operator) OR
 * is authenticated with no custom role source configured. In the latter
 * case the backend is the sole access authority — the frontend passes
 * through and lets API-level RBAC decide.
 */
export function hasAppAccess(userRoles: readonly string[]): boolean {
    if (hasAdminRole(userRoles) || hasOperatorRole(userRoles)) return true
    return isAuthenticatedWithoutCustomRoles(userRoles)
}

/**
 * Returns the current SWA-authenticated user.
 * Backed by TanStack Query — multiple callers share one cache entry.
 */
export function useCurrentUser() {
    const { data, isLoading } = useAuthData()
    return {
        userName: data?.userName ?? null,
        userRoles: data?.userRoles ?? [],
        isLoading,
    }
}

/**
 * Admin/Operator gates defer to backend RBAC (see `AuthorizationMiddleware`)
 * whenever SWA forwards no custom role — otherwise every admin/operator
 * route and control 403s in dev (built-in provider) and for any test/prod
 * caller SWA hasn't assigned a role to yet, even when the backend would
 * authorise them (dev's `Auth__DevFallbackRole`, or a correctly-assigned
 * caller whose role claim didn't make it through SWA).
 */
export function useIsAdmin(): boolean {
    const { userRoles } = useCurrentUser()
    return (
        hasAdminRole(userRoles) || isAuthenticatedWithoutCustomRoles(userRoles)
    )
}

export function useIsOperator(): boolean {
    const { userRoles } = useCurrentUser()
    return (
        hasOperatorRole(userRoles) ||
        isAuthenticatedWithoutCustomRoles(userRoles)
    )
}

export function useHasAppAccess(): boolean {
    const { userRoles } = useCurrentUser()
    return hasAppAccess(userRoles)
}

/**
 * True when SWA considers the caller signed in at all (carries the
 * `authenticated` pseudo-role). `/.auth/me` returns `{ clientPrincipal: null }`
 * — zero roles — for a genuinely anonymous visitor; this distinguishes that
 * case from "signed in but assigned a role the app doesn't recognise".
 */
export function useIsAuthenticated(): boolean {
    const { userRoles } = useCurrentUser()
    return userRoles.some((r) => r.toLowerCase() === 'authenticated')
}
