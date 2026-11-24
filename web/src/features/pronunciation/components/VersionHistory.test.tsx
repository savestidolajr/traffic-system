// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { VersionHistory } from './VersionHistory'
import type { DictionaryVersion, Rule } from '../types'

/**
 * Regression for bug-bash-2026-08-05 finding 28: pronunciation version history
 * was a bare audit list — an admin restoring a dictionary could not see which
 * rules changed. Each entry must show the real rule diff (identity-matched on
 * the word) against its predecessor.
 */

function rule(stringToReplace: string, replacement: string): Rule {
    return {
        type: 'alias',
        stringToReplace,
        replacement,
        alphabet: null,
        caseSensitive: false,
        wordBoundaries: true,
        appliesOnAllModels: true,
    }
}

function version(
    n: number,
    rules: readonly Rule[],
    overrides: Partial<DictionaryVersion> = {}
): DictionaryVersion {
    return {
        version: n,
        elevenLabsVersionId: `el-${n}`,
        rules: [...rules],
        changeNote: null,
        createdAt: '2026-01-01T00:00:00Z',
        createdBy: 'tester',
        ...overrides,
    } as DictionaryVersion
}

function renderHistory(versions: readonly DictionaryVersion[]) {
    render(
        <VersionHistory
            versions={versions}
            currentVersion={2}
            onRestore={vi.fn()}
        />
    )
}

describe('Pronunciation VersionHistory diff', () => {
    afterEach(cleanup)

    it('shows the rule changes of each version against its predecessor', async () => {
        const user = userEvent.setup()
        const v1 = version(1, [rule('Narrandera', 'Narrandra')])
        const v2 = version(2, [
            rule('Narrandera', 'Narrandra fixed'),
            rule('Wagga', 'Wogga'),
        ])
        renderHistory([v1, v2])

        await user.click(
            screen.getByRole('button', { name: 'Version history' })
        )

        expect(screen.getByText('Changes vs v1')).toBeInTheDocument()
        // The changed replacement and the newly added rule both surface
        expect(screen.getByText('Narrandra fixed')).toBeInTheDocument()
        expect(screen.getByText('Wagga')).toBeInTheDocument()
    })

    it('shows no diff row for the first version', async () => {
        const user = userEvent.setup()
        renderHistory([version(1, [rule('Wagga', 'Wogga')])])

        await user.click(
            screen.getByRole('button', { name: 'Version history' })
        )

        expect(screen.queryByText(/Changes vs v/)).not.toBeInTheDocument()
    })
})
