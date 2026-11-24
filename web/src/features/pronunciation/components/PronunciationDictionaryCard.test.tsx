// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PronunciationDictionaryCard } from './PronunciationDictionaryCard'
import type { Dictionary } from '../types'

vi.mock('@/lib/formatting', () => ({
    formatRelativeTime: vi.fn(() => 'rel'),
}))

/**
 * A dictionary whose first three rules contain the same word twice. This is a
 * LEGAL dictionary: the duplicate gate keys on `{type}|{word}`, so one alias
 * rule and one phoneme rule for the same word coexist — and the phoneme
 * warning tooltip in the rule table explicitly tells operators to "add an
 * alias respelling for the same word".
 */
function makeDict(): Dictionary {
    return {
        id: 'pd-1',
        dictionaryId: 'pd-1',
        name: 'Test dict',
        description: null,
        status: 'active',
        scope: 'Station',
        currentVersion: 1,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        etag: null,
        createdBy: null,
        assignedStations: [],
        rules: [
            {
                type: 'alias',
                stringToReplace: 'Warringah',
                replacement: 'Warringer',
                alphabet: null,
                caseSensitive: false,
                wordBoundaries: true,
                appliesOnAllModels: true,
            },
            {
                type: 'phoneme',
                stringToReplace: 'Warringah',
                replacement: 'wɒrɪŋɡə',
                alphabet: 'ipa',
                caseSensitive: false,
                wordBoundaries: true,
                appliesOnAllModels: false,
            },
            {
                type: 'alias',
                stringToReplace: 'Epping',
                replacement: 'Ehpping',
                alphabet: null,
                caseSensitive: false,
                wordBoundaries: true,
                appliesOnAllModels: true,
            },
        ],
        versions: [],
    } as Dictionary
}

describe('PronunciationDictionaryCard preview chips', () => {
    afterEach(() => {
        cleanup()
        vi.restoreAllMocks()
    })

    it('renders both preview chips when an alias and a phoneme rule share a word, without duplicate React keys', () => {
        // React logs duplicate-key violations to console.error in dev builds.
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
        const onView = vi.fn()

        render(
            <PronunciationDictionaryCard
                dict={makeDict()}
                assignedStations={[]}
                stationCount={10}
                onView={onView}
                onEdit={onView}
                onDuplicate={vi.fn()}
                onDelete={vi.fn()}
                onArchive={vi.fn()}
                onUnarchive={vi.fn()}
            />
        )

        // Both rules are previewed as chips.
        expect(screen.getAllByText('Warringah')).toHaveLength(2)
        expect(screen.getByText('Epping')).toBeInTheDocument()

        // And React must not have seen two children with the same key.
        const keyWarnings = errorSpy.mock.calls.filter((args) =>
            String(args[0]).includes('same key')
        )
        expect(keyWarnings).toHaveLength(0)
    })
})

/**
 * Mirrors the bookends grant (f28ba062): every pronunciation-dictionary route
 * except DELETE resolves to Operator in RouteRoleMatrix, so an operator gets
 * the full actions menu — View, Edit, Duplicate, Archive — and Delete alone
 * stays Admin because it is unrecoverable and archive is the reversible
 * retire path.
 */
describe('PronunciationDictionaryCard role gating', () => {
    const baseProps = {
        assignedStations: [],
        stationCount: 10,
        onView: vi.fn(),
        onEdit: vi.fn(),
        onDuplicate: vi.fn(),
        onDelete: vi.fn(),
        onArchive: vi.fn(),
        onUnarchive: vi.fn(),
    }

    /**
     * Radix DropdownMenuTrigger listens on pointerdown/pointerup; `.click()` alone
     * fires only mousedown→mouseup→click.
     */
    function openDropdown(trigger: HTMLElement) {
        trigger.dispatchEvent(
            new PointerEvent('pointerdown', { bubbles: true })
        )
        trigger.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
        trigger.click()
    }

    function menuLabels(): readonly string[] {
        return [...document.body.querySelectorAll('[role="menuitem"]')].map(
            (item) => (item.textContent ?? '').trim()
        )
    }

    it('offers an operator the editing actions but not Delete', async () => {
        render(
            <PronunciationDictionaryCard
                dict={makeDict()}
                {...baseProps}
                canDelete={false}
            />
        )

        const trigger = screen.getByRole('button', {
            name: 'Dictionary actions',
        })
        await openDropdown(trigger)

        const labels = menuLabels()
        expect(labels).toContain('Edit')
        expect(labels).toContain('Duplicate')
        expect(labels).toContain('Archive')
        expect(labels).not.toContain('Delete')
    })

    it('offers Delete to an admin', async () => {
        render(<PronunciationDictionaryCard dict={makeDict()} {...baseProps} />)

        const trigger = screen.getByRole('button', {
            name: 'Dictionary actions',
        })
        await openDropdown(trigger)

        expect(menuLabels()).toContain('Delete')
    })

    it('withholds Delete in the table view too', async () => {
        const { PronunciationDictionaryTable } =
            await import('./PronunciationDictionaryTable')
        render(
            <PronunciationDictionaryTable
                dictionaries={[makeDict()]}
                assignmentsByDictionaryId={new Map()}
                stationCount={10}
                {...baseProps}
                canDelete={false}
            />
        )

        const trigger = screen.getByRole('button', {
            name: 'Actions for Test dict',
        })
        await openDropdown(trigger)

        const labels = menuLabels()
        expect(labels).toContain('Edit')
        expect(labels).not.toContain('Delete')
    })
})
