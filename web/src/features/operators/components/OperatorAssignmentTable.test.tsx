// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OperatorAssignmentTable } from './OperatorAssignmentTable'
import type { OperatorDirectoryEntry } from '../api/schemas'

afterEach(() => {
    cleanup()
})

beforeEach(() => {
    window.localStorage.clear()
})

const entry = (
    objectId: string,
    displayName: string,
    roles: readonly string[],
    markets: readonly string[],
    updatedAtUtc: string | null
): OperatorDirectoryEntry => ({
    objectId,
    displayName,
    userPrincipalName: `${objectId}@example.test`,
    roles: [...roles],
    source:
        roles.length === 0
            ? 'Direct'
            : roles.includes('Admin')
              ? 'Group:APP-IBP-Test-Admin'
              : 'Group:APP-IBP-Test-Operator',
    markets: [...markets],
    isAdmin: roles.includes('Admin'),
    updatedAtUtc,
    updatedBy: updatedAtUtc ? 'admin@example.test' : null,
})
const storedAssignmentEntry: OperatorDirectoryEntry = {
    ...entry(
        'id-stored-assignment',
        'Saved Assignment',
        [],
        ['sydney'],
        '2026-06-01T00:00:00Z'
    ),
    source: 'Assignment',
}

type DirectoryStatus = 'Ok' | 'Unavailable'

// Deliberately NOT in sorted order, with mixed case and all access states.
const entries: readonly OperatorDirectoryEntry[] = [
    entry(
        'id-zara',
        'Zara Ops',
        ['Operator'],
        ['sydney'],
        '2026-03-01T00:00:00Z'
    ),
    entry(
        'id-adam',
        'adam ops',
        ['Operator'],
        ['perth', 'darwin', 'cairns'],
        '2026-01-01T00:00:00Z'
    ),
    entry('id-mia', 'Mia Ops', ['Operator'], [], '2026-05-01T00:00:00Z'),
    entry('id-admin', 'Admin Avery', ['Admin'], [], null),
    entry('id-broken', 'Broken Entra Role', [], [], null),
]

function renderTable(
    tableEntries: readonly OperatorDirectoryEntry[] = entries,
    onEdit = vi.fn(),
    directoryStatus: DirectoryStatus = 'Ok'
) {
    return render(
        <OperatorAssignmentTable
            entries={tableEntries}
            directoryStatus={directoryStatus}
            onEdit={onEdit}
        />
    )
}

const operatorNames = () =>
    screen
        .getAllByRole('row')
        .slice(1)
        .map((row) =>
            within(row)
                .getAllByRole('cell')[0]
                .querySelector('.font-medium')
                ?.textContent?.trim()
        )

const header = (name: string) =>
    screen.getByRole('columnheader', { name: new RegExp(name) })

describe('OperatorAssignmentTable sorting', () => {
    it('renders directory entries in source order until a header is clicked', () => {
        renderTable()
        expect(operatorNames()).toEqual([
            'Zara Ops',
            'adam ops',
            'Mia Ops',
            'Admin Avery',
            'Broken Entra Role',
        ])
        expect(header('Operator')).toHaveAttribute('aria-sort', 'none')
    })

    it('sorts operators A-Z on the first click, ignoring case', async () => {
        const user = userEvent.setup()
        renderTable()

        await user.click(within(header('Operator')).getByRole('button'))

        expect(operatorNames()).toEqual([
            'adam ops',
            'Admin Avery',
            'Broken Entra Role',
            'Mia Ops',
            'Zara Ops',
        ])
        expect(header('Operator')).toHaveAttribute('aria-sort', 'ascending')
    })

    it('flips on the second click and restores source order on the third', async () => {
        const user = userEvent.setup()
        renderTable()
        const button = within(header('Operator')).getByRole('button')

        await user.click(button)
        await user.click(button)
        expect(operatorNames()).toEqual([
            'Zara Ops',
            'Mia Ops',
            'Broken Entra Role',
            'Admin Avery',
            'adam ops',
        ])
        expect(header('Operator')).toHaveAttribute('aria-sort', 'descending')

        await user.click(button)
        expect(operatorNames()).toEqual([
            'Zara Ops',
            'adam ops',
            'Mia Ops',
            'Admin Avery',
            'Broken Entra Role',
        ])
        expect(header('Operator')).toHaveAttribute('aria-sort', 'none')
    })

    it('sorts station counts highest-first on the first click', async () => {
        const user = userEvent.setup()
        renderTable()

        await user.click(within(header('Stations')).getByRole('button'))

        expect(operatorNames()).toEqual([
            'adam ops',
            'Zara Ops',
            'Mia Ops',
            'Admin Avery',
            'Broken Entra Role',
        ])
    })

    it('sorts the newest update first on the first click', async () => {
        const user = userEvent.setup()
        renderTable()

        await user.click(within(header('Updated')).getByRole('button'))

        expect(operatorNames()).toEqual([
            'Mia Ops',
            'Zara Ops',
            'adam ops',
            'Admin Avery',
            'Broken Entra Role',
        ])
    })

    it('renders admin, scoped, unscoped, and misconfigured states distinctly', () => {
        renderTable()

        expect(screen.getByText('All stations (admin)')).toBeInTheDocument()
        const unscoped = screen.getAllByText('All stations (unscoped)')
        expect(unscoped).toHaveLength(2)
        unscoped.forEach((state) => {
            expect(state).not.toHaveAttribute('role', 'alert')
        })
        expect(screen.getByText('Sydney')).toBeInTheDocument()
        expect(
            screen.getByText(/No application role is assigned in Entra/)
        ).toBeInTheDocument()
        expect(screen.getByText('Misconfigured')).toBeInTheDocument()
    })
    it('shows a neutral unknown role when Entra is unavailable for a saved assignment', () => {
        renderTable([storedAssignmentEntry], vi.fn(), 'Unavailable')

        expect(screen.getByText('Role unknown')).toHaveClass('bg-secondary')
        expect(
            screen.getByText(/Entra is unreadable, so the role is unknown/i)
        ).toBeInTheDocument()
        expect(screen.getByText(/from a previous sign-in/i)).toBeInTheDocument()
        expect(screen.getByRole('status')).toHaveClass('text-muted-foreground')
        expect(screen.queryByText('Misconfigured')).not.toBeInTheDocument()
        expect(
            screen.queryByText(/No application role is assigned in Entra/)
        ).not.toBeInTheDocument()
    })

    it('shows a sign-in role quietly, with provenance on the badge and no standing notice', () => {
        // The directory being unreadable is the steady state, so a per-row sentence about it would
        // appear on every row forever. The badge carries the provenance instead.
        renderTable(
            [
                {
                    ...storedAssignmentEntry,
                    roles: ['Operator'],
                },
            ],
            vi.fn(),
            'Unavailable'
        )

        const row = screen.getByRole('row', { name: /Saved Assignment/ })
        expect(within(row).getByText('Operator')).toHaveAttribute(
            'title',
            expect.stringContaining('last sign-in') as unknown as string
        )
        expect(within(row).queryByText('Role unknown')).not.toBeInTheDocument()
        expect(within(row).getByText(/From Assignment/)).toBeInTheDocument()
        expect(
            screen.queryByText(/Entra could not be read/i)
        ).not.toBeInTheDocument()
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('warns when a previous-sign-in row is absent from a successful directory read', () => {
        renderTable([storedAssignmentEntry], vi.fn(), 'Ok')

        expect(screen.getByText('Role unknown')).toHaveClass('bg-warning/10')
        expect(
            screen.getByText(/not returned by the latest Entra read/i)
        ).toBeInTheDocument()
        expect(
            screen.getByText(/kept from a previous sign-in/i)
        ).toBeInTheDocument()
        expect(
            screen.getByText(
                /verify an Entra application role is still assigned/i
            )
        ).toBeInTheDocument()
        expect(
            screen.queryByText(/gives them no access/i)
        ).not.toBeInTheDocument()
        expect(screen.getByRole('alert')).toHaveClass('text-warning')
        expect(screen.queryByText('Misconfigured')).not.toBeInTheDocument()
        expect(
            screen.queryByText(/No application role is assigned in Entra/)
        ).not.toBeInTheDocument()
    })

    it('does not open the station editor for an admin row', async () => {
        const user = userEvent.setup()
        const onEdit = vi.fn()
        renderTable(entries, onEdit)

        await user.click(screen.getByText('Admin Avery'))

        expect(onEdit).not.toHaveBeenCalled()
    })

    it('moves the active sort to the clicked column rather than flipping it', async () => {
        const user = userEvent.setup()
        renderTable()

        await user.click(within(header('Operator')).getByRole('button'))
        await user.click(within(header('Updated')).getByRole('button'))

        expect(header('Operator')).toHaveAttribute('aria-sort', 'none')
        expect(header('Updated')).toHaveAttribute('aria-sort', 'descending')
    })

    it('restores the persisted sort on a remount', async () => {
        const user = userEvent.setup()
        const first = renderTable()
        await user.click(within(header('Operator')).getByRole('button'))
        first.unmount()

        renderTable()
        expect(header('Operator')).toHaveAttribute('aria-sort', 'ascending')
        expect(operatorNames()).toEqual([
            'adam ops',
            'Admin Avery',
            'Broken Entra Role',
            'Mia Ops',
            'Zara Ops',
        ])
    })
})

describe('OperatorAssignmentTable station chips', () => {
    // Regression for B1: the chips used to be keyed on the FORMATTED display
    // label. Dev has two markets whose station name is "Star 102.7 FM"
    // (cairns-4ca / cairns-star1027); any two markets that resolve to the same
    // label (same station + schedule name) then render duplicate React keys,
    // which React flags and which corrupts chip reconciliation ("children
    // duplicated and/or omitted"). Chips must key on the market id, the
    // record identity, not the user-editable label.
    it('renders both chips without duplicate-key errors when two markets share a display label', () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
        try {
            const labels = new Map<string, string>([
                ['cairns-4ca', 'Star 102.7 FM · Cairns Traffic'],
                ['cairns-star1027', 'Star 102.7 FM · Cairns Traffic'],
            ])
            render(
                <OperatorAssignmentTable
                    entries={[
                        entry(
                            'id-x',
                            'X Ops',
                            ['Operator'],
                            ['cairns-4ca', 'cairns-star1027'],
                            '2026-06-01T00:00:00Z'
                        ),
                    ]}
                    directoryStatus="Ok"
                    marketLabels={labels}
                    onEdit={vi.fn()}
                />
            )

            // Both chips are present…
            expect(
                screen.getAllByText('Star 102.7 FM · Cairns Traffic')
            ).toHaveLength(2)
            // …and React never saw duplicate keys.
            const duplicateKeyErrors = errorSpy.mock.calls.filter((call) =>
                String(call[0]).includes('same key')
            )
            expect(
                duplicateKeyErrors,
                'station chips must be keyed by market id, not the shared display label'
            ).toHaveLength(0)
        } finally {
            errorSpy.mockRestore()
        }
    })
})
