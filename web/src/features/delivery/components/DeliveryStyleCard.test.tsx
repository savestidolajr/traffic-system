// @vitest-environment jsdom
// React 19 moved `act` to the `react` package (not exported from
// react-dom/client); `createRoot` stays in react-dom/client.
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import React from 'react'
import { DeliveryStyleCard } from './DeliveryStyleCard'
import type { DeliveryStyle } from '../types'

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

function openDropdown(trigger: HTMLElement) {
    trigger.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    trigger.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
    trigger.click()
}

/**
 * Regression for bug-bash-2026-08-05 finding 16: archive/unarchive existed
 * only as raw API endpoints — no console surface could retire a delivery
 * style or restore an archived one. The card's actions menu must offer
 * Archive for active items and Unarchive for archived items.
 */
function makeStyle(status: 'active' | 'archived' = 'active'): DeliveryStyle {
    return {
        id: 'style-1',
        name: 'Test Style',
        description: null,
        status,
        currentVersion: 2,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-05T00:00:00Z',
        createdBy: null,
        assignedStations: [],
        versions: [
            {
                version: 1,
                policy: {
                    toneDescription: 'Calm',
                    allowedTags: ['[calm]'],
                    intensity: 2,
                },
                changeNote: null,
                createdAt: '2026-01-01T00:00:00Z',
                createdBy: null,
            },
            {
                version: 2,
                policy: {
                    toneDescription: 'Calm and steady.',
                    allowedTags: ['[calm]', '[serious]'],
                    intensity: 2,
                },
                changeNote: null,
                createdAt: '2026-01-05T00:00:00Z',
                createdBy: null,
            },
        ],
    } as DeliveryStyle
}

describe('DeliveryStyleCard archive actions', () => {
    const baseProps = {
        assignedStations: [],
        onView: vi.fn(),
        onEdit: vi.fn(),
        onDuplicate: vi.fn(),
        onDelete: vi.fn(),
        onArchive: vi.fn(),
        onUnarchive: vi.fn(),
    }

    it('offers Archive (not Unarchive) for an active style', async () => {
        const onArchive = vi.fn()
        const container = await render(
            <DeliveryStyleCard
                style={makeStyle('active')}
                {...baseProps}
                onArchive={onArchive}
            />
        )
        const trigger = container.querySelector(
            'button[aria-label="Style actions"]'
        ) as HTMLButtonElement
        await act(async () => {
            openDropdown(trigger)
        })

        const items = Array.from(
            document.body.querySelectorAll('[role="menuitem"]')
        ).map((el) => el.textContent?.trim())
        expect(items).toContain('Archive')
        expect(items).not.toContain('Unarchive')

        const archiveItem = Array.from(
            document.body.querySelectorAll('[role="menuitem"]')
        ).find((el) => el.textContent?.trim() === 'Archive')
        await act(async () => {
            ;(archiveItem as HTMLElement).click()
        })
        expect(onArchive).toHaveBeenCalledWith(
            expect.objectContaining({ id: 'style-1' })
        )
    })

    it('offers Unarchive (not Archive) for an archived style', async () => {
        const onUnarchive = vi.fn()
        const container = await render(
            <DeliveryStyleCard
                style={makeStyle('archived')}
                {...baseProps}
                onUnarchive={onUnarchive}
            />
        )
        const trigger = container.querySelector(
            'button[aria-label="Style actions"]'
        ) as HTMLButtonElement
        await act(async () => {
            openDropdown(trigger)
        })

        const items = Array.from(
            document.body.querySelectorAll('[role="menuitem"]')
        ).map((el) => el.textContent?.trim())
        expect(items).toContain('Unarchive')
        expect(items).not.toContain('Archive')

        const unarchiveItem = Array.from(
            document.body.querySelectorAll('[role="menuitem"]')
        ).find((el) => el.textContent?.trim() === 'Unarchive')
        await act(async () => {
            ;(unarchiveItem as HTMLElement).click()
        })
        expect(onUnarchive).toHaveBeenCalledWith(
            expect.objectContaining({ id: 'style-1' })
        )
    })
})

/**
 * Mirrors the bookends grant (f28ba062): every delivery-style route except
 * DELETE resolves to Operator in RouteRoleMatrix, so an operator gets the full
 * actions menu — View, Edit, Duplicate, Archive — and Delete alone stays Admin
 * because it is unrecoverable and archive is the reversible retire path.
 */
describe('DeliveryStyleCard role gating', () => {
    const baseProps = {
        assignedStations: [],
        onView: vi.fn(),
        onEdit: vi.fn(),
        onDuplicate: vi.fn(),
        onDelete: vi.fn(),
        onArchive: vi.fn(),
        onUnarchive: vi.fn(),
    }

    function menuLabels(): readonly string[] {
        return [...document.body.querySelectorAll('[role="menuitem"]')].map(
            (item) => (item.textContent ?? '').trim()
        )
    }

    it('offers an operator the editing actions but not Delete', async () => {
        const container = await render(
            <DeliveryStyleCard
                style={makeStyle('active')}
                {...baseProps}
                canDelete={false}
            />
        )

        const trigger = container.querySelector<HTMLElement>(
            'button[aria-label="Style actions"]'
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
            <DeliveryStyleCard style={makeStyle('active')} {...baseProps} />
        )

        const trigger = container.querySelector<HTMLElement>(
            'button[aria-label="Style actions"]'
        )
        expect(trigger).not.toBeNull()
        await act(async () => openDropdown(trigger!))

        expect(menuLabels()).toContain('Delete')
    })

    it('withholds Delete in the table view too', async () => {
        const { DeliveryStyleTable } = await import('./DeliveryStyleTable')
        const container = await render(
            <DeliveryStyleTable
                styles={[makeStyle('active')]}
                assignmentsByStyleId={new Map()}
                {...baseProps}
                canDelete={false}
            />
        )

        const trigger = container.querySelector<HTMLElement>(
            'button[aria-label="Actions for Test Style"]'
        )
        expect(trigger).not.toBeNull()
        await act(async () => openDropdown(trigger!))

        const labels = menuLabels()
        expect(labels).toContain('Edit')
        expect(labels).not.toContain('Delete')
    })
})
