import { describe, expect, it } from 'vitest'
import {
    APP_ROUTES,
    NAV_SHORTCUT_PREFIX,
    canAccessRequiredRole,
    resolveNavShortcut,
    visibleRoutes,
} from './routes'

describe('route access gating', () => {
    it('hides every app tab from authenticated users without app roles', () => {
        expect(
            visibleRoutes({ isAdmin: false, isOperator: false }).map(
                (route) => route.label
            )
        ).toEqual([])
    })

    it('shows operator tabs only to operators', () => {
        // Stations is operator-visible: GET /schedules and PUT /schedules/{marketId} are both
        // Operator server-side (market-guarded), so the station a person is assigned to is theirs
        // to read and configure. Create/duplicate/delete/trigger stay admin, inside the page.
        // Voices, Delivery and Pronunciation are operator-visible AND operator-writable: every
        // route except DELETE resolves to Operator in RouteRoleMatrix, so each page gates the
        // Delete affordance alone (VoicesPage / DeliveryPage / PronunciationPage `canDelete`).
        // Bookends is operator-visible AND operator-writable for the same reason — only the
        // gallery's Delete affordance is admin-gated (BookendGalleryPage `canDelete`).
        // Activity is operator-readable: GET /activity is Operator server-side, so operators
        // see the audit trail their own library writes leave behind.
        expect(
            visibleRoutes({ isAdmin: false, isOperator: true }).map(
                (route) => route.label
            )
        ).toEqual([
            'On air',
            'Desk',
            'History',
            'Stations',
            'Voices',
            'Delivery',
            'Bookends',
            'Pronunciation',
            'Activity',
            'Bulletin rules',
        ])
    })

    it('shows all tabs to admins', () => {
        expect(
            visibleRoutes({ isAdmin: true, isOperator: false }).map(
                (route) => route.label
            )
        ).toEqual([
            'On air',
            'Desk',
            'History',
            'Stations',
            'Voices',
            'Delivery',
            'Bookends',
            'Pronunciation',
            'Operators',
            'Data transfer',
            'Activity',
            'Bulletin rules',
        ])
    })

    it('lets admins into operator routes but not operators into admin routes', () => {
        expect(
            canAccessRequiredRole('operator', {
                isAdmin: true,
                isOperator: false,
            })
        ).toBe(true)
        expect(
            canAccessRequiredRole('admin', {
                isAdmin: false,
                isOperator: true,
            })
        ).toBe(false)
    })
})

describe('nav key uniqueness', () => {
    // The sidebar keys nav rows by `item.path`, the mobile sheet keys them by
    // `item.label`, and the command palette keys its page items by `item.path`.
    // A duplicate label or path would orphan one row's DOM node and kill its
    // click handler (the same class of bug as the duplicated cron-time chips).
    it('gives every route a unique path', () => {
        const paths = APP_ROUTES.map((route) => route.path)
        expect(new Set(paths).size).toBe(paths.length)
    })

    it('gives every route a unique label', () => {
        const labels = APP_ROUTES.map((route) => route.label)
        expect(new Set(labels).size).toBe(labels.length)
    })
})

describe('route shortcut hints', () => {
    it('stores bare keys, never a hardcoded Mac ⌘ glyph', () => {
        for (const route of APP_ROUTES) {
            if (!route.shortcut) continue
            expect(route.shortcut).not.toMatch(/⌘/)
            expect(route.shortcut).toMatch(/^[0-9A-Za-z]$/)
        }
    })

    it('gives every route a unique shortcut', () => {
        const shortcuts = APP_ROUTES.map((route) => route.shortcut).filter(
            (s): s is string => Boolean(s)
        )
        expect(new Set(shortcuts).size).toBe(shortcuts.length)
    })

    // The palette advertises `g` + shortcut and useNavShortcuts navigates on
    // the same sequence, so a declared shortcut with no resolvable path would
    // be a lie in the UI. This keeps the advertised chord and the bound chord
    // in lockstep — they went out of sync once already, leaving the palette
    // showing 12 modifier+digit chords no handler implemented.
    it('resolves every declared shortcut to its own path', () => {
        for (const route of APP_ROUTES) {
            if (!route.shortcut) continue
            expect(
                resolveNavShortcut([NAV_SHORTCUT_PREFIX, route.shortcut])
            ).toBe(route.path)
        }
    })

    it('rejects sequences that are not prefix plus a declared shortcut', () => {
        expect(resolveNavShortcut([NAV_SHORTCUT_PREFIX, 'x'])).toBeUndefined()
        expect(resolveNavShortcut([NAV_SHORTCUT_PREFIX, '!'])).toBeUndefined()
        expect(resolveNavShortcut([NAV_SHORTCUT_PREFIX])).toBeUndefined()
        expect(resolveNavShortcut(['1', '1'])).toBeUndefined()
        expect(resolveNavShortcut([])).toBeUndefined()
    })
})
