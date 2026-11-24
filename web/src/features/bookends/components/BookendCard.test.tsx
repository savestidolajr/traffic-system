// @vitest-environment jsdom
// React 19 moved `act` to the `react` package (not exported from
// react-dom/client); `createRoot` stays in react-dom/client.
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import React from 'react'
import { BookendCard } from './BookendCard'
import { BookendTable } from './BookendTable'
import { APP_ROUTES, canAccessRequiredRole } from '@/lib/routes'
import type { Bookend } from '../types'

vi.mock('@/lib/formatting', () => ({
    formatRelativeTime: vi.fn((iso: string) => `rel:${iso}`),
    formatDateTime: vi.fn((iso: string) => `abs:${iso}`),
}))

// Silence Radix UI / React 19 noise in jsdom
beforeEach(() => {
    vi.restoreAllMocks()
    Element.prototype.getAnimations ??= () => []
})

let root: Root | null = null

async function render(ui: React.ReactElement) {
    const container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    await act(async () => {
        root!.render(ui)
    })
    return container
}

function cleanup() {
    if (root) {
        act(() => {
            root!.unmount()
        })
    }
    root = null
    document.body.innerHTML = ''
}

afterEach(cleanup)

function makeBookend(): Bookend {
    return {
        id: 'bk-1',
        name: 'Morning drive pair',
        description: null,
        status: 'active',
        introText: 'Looking over {city} today, here is your traffic.',
        outroText: 'That is it for {city} traffic on {station}.',
        currentVersion: 2,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-05T00:00:00Z',
        createdBy: null,
        assignedStations: [],
        tokens: ['city', 'station'],
        versions: [],
    } as Bookend
}

const handlers = () => ({
    onView: vi.fn(),
    onEdit: vi.fn(),
    onDuplicate: vi.fn(),
    onDelete: vi.fn(),
    onArchive: vi.fn(),
    onUnarchive: vi.fn(),
})

/**
 * Radix DropdownMenuTrigger listens on pointerdown/pointerup; `.click()` alone
 * fires only mousedown→mouseup→click.
 */
function openDropdown(trigger: HTMLElement) {
    trigger.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    trigger.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
    trigger.click()
}

function menuLabels(): readonly string[] {
    return [...document.querySelectorAll('[role="menuitem"]')].map((item) =>
        (item.textContent ?? '').trim()
    )
}

/**
 * Operators author bookends: every bookend route except DELETE resolves to
 * Operator in `RouteRoleMatrix` (decision 2026-08-13), so the gallery offers an
 * operator the full actions menu — View, Edit, Duplicate, Archive — and
 * withholds Delete alone, which stays Admin because it is unrecoverable and
 * archive is the reversible retire path.
 */
describe('Bookends library role gating', () => {
    it('keeps the bookends nav entry reachable for an operator', () => {
        const entry = APP_ROUTES.find((route) => route.path === '/bookends')
        expect(entry).toBeDefined()
        expect(
            canAccessRequiredRole(entry!.requiredRole, {
                isAdmin: false,
                isOperator: true,
            })
        ).toBe(true)
    })

    it('offers an operator the editing actions but not Delete', async () => {
        const h = handlers()
        const container = await render(
            <BookendCard
                bookend={makeBookend()}
                assignedStations={['KIIS 1065']}
                canDelete={false}
                {...h}
            />
        )

        expect(container.textContent).toContain(
            'Looking over {city} today, here is your traffic.'
        )
        const trigger = container.querySelector<HTMLElement>(
            '[aria-label="Bookends actions"]'
        )
        expect(trigger).not.toBeNull()
        await act(async () => openDropdown(trigger!))

        const labels = menuLabels()
        expect(labels).toContain('Edit')
        expect(labels).toContain('Duplicate')
        expect(labels).toContain('Archive')
        expect(labels).not.toContain('Delete')
    })

    it('offers Delete to an admin', async () => {
        const container = await render(
            <BookendCard
                bookend={makeBookend()}
                assignedStations={[]}
                canDelete
                {...handlers()}
            />
        )

        const trigger = container.querySelector<HTMLElement>(
            '[aria-label="Bookends actions"]'
        )
        await act(async () => openDropdown(trigger!))

        expect(menuLabels()).toContain('Delete')
    })

    it('withholds Delete in the table view too', async () => {
        const container = await render(
            <BookendTable
                bookends={[makeBookend()]}
                assignmentsByBookendId={new Map()}
                canDelete={false}
                {...handlers()}
            />
        )

        const trigger = container.querySelector<HTMLElement>(
            '[aria-label="Actions for Morning drive pair"]'
        )
        expect(trigger).not.toBeNull()
        await act(async () => openDropdown(trigger!))

        const labels = menuLabels()
        expect(labels).toContain('Edit')
        expect(labels).not.toContain('Delete')
    })
})
