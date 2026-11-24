// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { OperatorsPage } from './OperatorsPage'
import { useOperatorAssignments } from '@/features/operators/api/operator-hooks'
import type { OperatorDirectory } from '@/features/operators/api/schemas'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
vi.mock('@/features/operators/api/operator-hooks', () => ({
    useOperatorAssignments: vi.fn(),
}))

vi.mock('@/features/schedules/api/schedule-hooks', () => ({
    useSchedules: vi.fn(),
}))

vi.mock('@/features/operators/components/OperatorAssignmentCard', () => ({
    OperatorAssignmentCard: () => null,
}))

vi.mock('@/features/operators/components/OperatorAssignmentTable', () => ({
    OperatorAssignmentTable: () => null,
}))

vi.mock('@/features/operators/components/AssignOperatorDialog', () => ({
    AssignOperatorDialog: () => null,
}))

afterEach(() => {
    cleanup()
})

beforeEach(() => {
    window.localStorage.clear()
    vi.mocked(useSchedules).mockReturnValue({
        data: { schedules: [] },
    } as never)
})

describe('OperatorsPage directory status', () => {
    const bobOperator = {
        objectId: 'operator-1',
        displayName: 'Bob Operator',
        userPrincipalName: 'bob@example.test',
        roles: ['Operator'],
        source: 'Group:APP-IBP-Test-Operator',
        markets: ['sydney'],
        isAdmin: false,
        updatedAtUtc: null,
        updatedBy: null,
    }

    it('shows a banner with the technical detail when the directory is unavailable', () => {
        const directory: OperatorDirectory = {
            operators: [bobOperator],
            directoryStatus: 'Unavailable',
            directoryError: 'Microsoft Graph timed out',
        }
        vi.mocked(useOperatorAssignments).mockReturnValue({
            data: directory,
            isLoading: false,
            isError: false,
            error: null,
            refetch: vi.fn(),
        } as never)

        render(
            <MemoryRouter>
                <OperatorsPage />
            </MemoryRouter>
        )

        expect(
            screen.getByText(
                'Entra directory members and their station access.'
            )
        ).toBeInTheDocument()
        // The directory could not be read, so the list only shows saved
        // assignments. The page must say so and surface the error an admin
        // needs to escalate, even when the list is not empty.
        expect(screen.getByRole('alert')).toBeInTheDocument()
        expect(
            screen.getByText(/Entra directory could not be read/)
        ).toBeInTheDocument()
        expect(
            screen.getByText(/Showing saved assignments only/)
        ).toBeInTheDocument()
        expect(screen.getByText(/Technical detail/)).toBeInTheDocument()
        expect(
            screen.getByText('Microsoft Graph timed out')
        ).toBeInTheDocument()
        // The list surface is shown, not an error or empty state.
        expect(screen.getByLabelText('Search operators')).toBeInTheDocument()
    })

    it('says nobody has signed in yet while still showing the directory banner', () => {
        vi.mocked(useOperatorAssignments).mockReturnValue({
            data: {
                operators: [],
                directoryStatus: 'Unavailable',
                directoryError: 'HTTP 403 Forbidden',
            } satisfies OperatorDirectory,
            isLoading: false,
            isError: false,
            error: null,
            refetch: vi.fn(),
        } as never)

        render(
            <MemoryRouter>
                <OperatorsPage />
            </MemoryRouter>
        )

        expect(screen.getByText('No one has signed in yet')).toBeInTheDocument()
        // The banner shows regardless of how many rows exist.
        expect(screen.getByRole('alert')).toBeInTheDocument()
        expect(screen.getByText(/403/)).toBeInTheDocument()
    })

    it('does not show a directory banner when the directory is available', () => {
        vi.mocked(useOperatorAssignments).mockReturnValue({
            data: {
                operators: [bobOperator],
                directoryStatus: 'Ok',
                directoryError: null,
            } satisfies OperatorDirectory,
            isLoading: false,
            isError: false,
            error: null,
            refetch: vi.fn(),
        } as never)

        render(
            <MemoryRouter>
                <OperatorsPage />
            </MemoryRouter>
        )

        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
        expect(
            screen.queryByText(/Entra directory could not be read/)
        ).not.toBeInTheDocument()
        expect(screen.queryByText(/Technical detail/)).not.toBeInTheDocument()
        // The list surface is still shown.
        expect(screen.getByLabelText('Search operators')).toBeInTheDocument()
    })
})
