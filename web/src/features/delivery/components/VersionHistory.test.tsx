// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { VersionHistory } from './VersionHistory'
import type { DeliveryStyleVersion } from '../types'

/**
 * Regression for bug-bash-2026-08-05 finding 28: version history was a bare
 * audit list — an admin restoring a delivery style could not see what changed.
 * Each entry must show the real policy diff against its predecessor.
 */

function version(
    n: number,
    overrides: Partial<DeliveryStyleVersion> = {}
): DeliveryStyleVersion {
    return {
        version: n,
        policy: {
            toneDescription: 'Calm and steady.',
            allowedTags: ['[calm]'],
            intensity: 2,
        },
        changeNote: null,
        createdAt: '2026-01-01T00:00:00Z',
        createdBy: 'tester',
        ...overrides,
    } as DeliveryStyleVersion
}

function renderHistory(versions: readonly DeliveryStyleVersion[]) {
    render(
        <VersionHistory
            versions={versions}
            currentVersion={2}
            onRestore={vi.fn()}
        />
    )
}

describe('Delivery VersionHistory diff', () => {
    afterEach(cleanup)

    it('shows the policy changes of each version against its predecessor', async () => {
        const user = userEvent.setup()
        const v1 = version(1)
        const v2 = version(2, {
            policy: {
                toneDescription: 'Warm and bright.',
                allowedTags: ['[calm]', '[brightly]'],
                intensity: 1,
            },
            changeNote: 'Warmer tone',
        })
        renderHistory([v1, v2])

        await user.click(
            screen.getByRole('button', { name: 'Version history' })
        )

        expect(screen.getByText('Changes vs v1')).toBeInTheDocument()
        // The changed fields render before -> after. Field labels come from
        // the shared diff engine's humanized camelCase (EntityDiffView), so
        // `toneDescription` renders as "tone Description" and `intensity`
        // keeps its lower-case form.
        expect(screen.getByText('tone Description')).toBeInTheDocument()
        expect(screen.getByText('Calm and steady.')).toBeInTheDocument()
        expect(screen.getByText('Warm and bright.')).toBeInTheDocument()
        expect(screen.getByText('intensity')).toBeInTheDocument()
        // The newly added cue is a whole-item + line
        expect(screen.getByText('[brightly]')).toBeInTheDocument()
    })

    it('shows no diff row for the first version', async () => {
        const user = userEvent.setup()
        renderHistory([version(1)])

        await user.click(
            screen.getByRole('button', { name: 'Version history' })
        )

        expect(screen.queryByText(/Changes vs v/)).not.toBeInTheDocument()
    })

    it('says "No field-level differences" for a metadata-only version', async () => {
        const user = userEvent.setup()
        renderHistory([version(1), version(2, { changeNote: 'Renamed only' })])

        await user.click(
            screen.getByRole('button', { name: 'Version history' })
        )

        expect(
            screen.getByText('No field-level differences.')
        ).toBeInTheDocument()
    })
})
