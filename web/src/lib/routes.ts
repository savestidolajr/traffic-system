import {
    type LucideIcon,
    RadioTower,
    ClipboardCheck,
    History,
    Settings2,
    ShieldCheck,
    AudioLines,
    MessageSquareText,
    BookText,
    BookOpen,
    Drama,
    ArrowDownUp,
    ScrollText,
} from 'lucide-react'

export type RequiredRole = 'operator' | 'admin'

export interface RouteAccess {
    readonly isOperator: boolean
    readonly isAdmin: boolean
}

interface NavRoute {
    readonly path: string
    readonly label: string
    readonly icon: LucideIcon
    readonly requiredRole: RequiredRole
    /**
     * Trailing key of the two-key "go to" sequence (`g` then this key).
     * The palette advertises the full sequence and useNavShortcuts binds
     * it, so a declared shortcut is always reachable.
     */
    readonly shortcut?: string
}

export const ROUTE_HOME = '/'
export const ROUTE_JOBS = '/jobs'
export const ROUTE_DESK = '/desk'
export const ROUTE_HISTORY = '/history'
export const ROUTE_STATIONS = '/stations'
export const ROUTE_SCHEDULES = '/schedules'
export const ROUTE_TRIGGER = '/trigger'
export const ROUTE_OPERATORS = '/operators'
export const ROUTE_VOICES = '/voices'
export const ROUTE_GUIDANCE = '/guidance'
export const ROUTE_DELIVERY = '/delivery'
export const ROUTE_BOOKENDS = '/bookends'
export const ROUTE_CONFIG_TRANSFER = '/config-transfer'
export const ROUTE_ACTIVITY = '/activity'
export const ROUTE_PRONUNCIATION = '/pronunciation'
export const APP_ROUTES: readonly NavRoute[] = [
    {
        path: '/jobs',
        label: 'On air',
        icon: RadioTower,
        requiredRole: 'operator',
        shortcut: '1',
    },
    {
        path: '/desk',
        label: 'Desk',
        icon: ClipboardCheck,
        // Operators vet road events on the Desk: GET /ledger and POST .../verdict both
        // resolve to Operator in RouteRoleMatrix (verdicts are market-guarded server-side).
        requiredRole: 'operator',
        shortcut: 'd',
    },
    {
        path: '/history',
        label: 'History',
        icon: History,
        requiredRole: 'operator',
        shortcut: '2',
    },
    {
        path: '/stations',
        label: 'Stations',
        icon: Settings2,
        requiredRole: 'operator',
        shortcut: '3',
    },
    {
        path: '/voices',
        label: 'Voices',
        icon: AudioLines,
        // Operators curate the voice library: every /api/voice-profiles route except DELETE
        // resolves to Operator in RouteRoleMatrix, so only the gallery's Delete affordance is
        // admin-gated (VoicesPage `canDelete`).
        requiredRole: 'operator',
        shortcut: '4',
    },
    {
        path: '/delivery',
        label: 'Delivery',
        icon: Drama,
        // Operators curate the delivery library: every /api/delivery-styles route except DELETE
        // resolves to Operator in RouteRoleMatrix, so only the gallery's Delete affordance is
        // admin-gated (DeliveryPage `canDelete`).
        requiredRole: 'operator',
        shortcut: '5',
    },
    {
        path: ROUTE_BOOKENDS,
        label: 'Bookends',
        icon: BookOpen,
        // Operators author bookends: every /api/bookends route except DELETE resolves to
        // Operator in RouteRoleMatrix, so only the gallery's Delete affordance is
        // admin-gated (BookendGalleryPage `canDelete`).
        requiredRole: 'operator',
        shortcut: 'i',
    },
    {
        path: '/pronunciation',
        label: 'Pronunciation',
        icon: BookText,
        // Operators curate the pronunciation library: every /api/pronunciation-dictionaries
        // route except DELETE resolves to Operator in RouteRoleMatrix, so only the gallery's
        // Delete affordance is admin-gated (PronunciationPage `canDelete`).
        requiredRole: 'operator',
        shortcut: '6',
    },
    {
        path: '/operators',
        label: 'Operators',
        icon: ShieldCheck,
        requiredRole: 'admin',
        shortcut: '7',
    },
    {
        path: ROUTE_CONFIG_TRANSFER,
        label: 'Data transfer',
        icon: ArrowDownUp,
        requiredRole: 'admin',
        shortcut: '8',
    },
    {
        path: ROUTE_ACTIVITY,
        label: 'Activity',
        icon: ScrollText,
        // Operators read the audit log: GET /activity is Operator in RouteRoleMatrix,
        // so every library mutation they make is visible back to them here.
        requiredRole: 'operator',
        shortcut: '9',
    },
    {
        path: '/guidance',
        label: 'Bulletin rules',
        icon: MessageSquareText,
        requiredRole: 'operator',
        shortcut: '0',
    },
] as const

/**
 * Prefix key of the two-key "go to" navigation sequence (`g` then a route
 * shortcut), the GitHub/Linear convention. The palette advertises the full
 * sequence and useNavShortcuts binds it; a route's `shortcut` is only ever
 * the trailing key.
 */
export const NAV_SHORTCUT_PREFIX = 'g'

/**
 * Resolve a two-key "go to" sequence to a route path. Returns undefined for
 * any sequence that is not the prefix followed by a declared route shortcut,
 * so the key handler never navigates on a half-typed or unrelated chord.
 */
export function resolveNavShortcut(
    keys: readonly string[]
): string | undefined {
    if (keys.length !== 2 || keys[0] !== NAV_SHORTCUT_PREFIX) return undefined
    return APP_ROUTES.find((route) => route.shortcut === keys[1])?.path
}

export function canAccessRequiredRole(
    requiredRole: RequiredRole,
    access: RouteAccess
): boolean {
    return requiredRole === 'admin'
        ? access.isAdmin
        : access.isOperator || access.isAdmin
}

export function visibleRoutes(access: RouteAccess): readonly NavRoute[] {
    return APP_ROUTES.filter((route) =>
        canAccessRequiredRole(route.requiredRole, access)
    )
}

/**
 * Whether a nav route is active for the current pathname.
 * "On air" (/jobs) also owns the /jobs/:jobId deep links; the bare `/`
 * is the hero landing, owned by the brand link, not a nav item.
 */
export function isRouteActive(path: string, pathname: string): boolean {
    return pathname.startsWith(path)
}
