// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { useState } from 'react'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { TooltipProvider } from '@/components/ui/tooltip'
import { PronunciationRuleTable } from './PronunciationRuleTable'
import type { RuleInput } from '../types'

const { previewMutate } = vi.hoisted(() => ({ previewMutate: vi.fn() }))

vi.mock('@/features/pronunciation/api/pronunciation-hooks', () => ({
    usePreviewPronunciationRule: () => ({
        mutateAsync: previewMutate,
        isPending: false,
    }),
}))

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({
        data: {
            profiles: [
                {
                    id: 'vp-1',
                    name: 'Test voice',
                    baseVoiceName: 'base',
                    baseVoiceId: 'bv-1',
                    status: 'active',
                    currentVersion: 1,
                    versions: [{ version: 1, model: 'eleven_flash_v2' }],
                },
            ],
        },
        isLoading: false,
    }),
}))

vi.mock('@/features/schedules/api/schedule-hooks', () => ({
    useSchedules: () => ({ data: { schedules: [] }, isLoading: false }),
}))

// The bulk-edit pointer targets the admin-only Data transfer route, so the
// toolbar only offers it to an admin.
vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: () => true,
}))

class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)

// jsdom does not implement media playback; the play button awaits audio.play().
Object.defineProperty(HTMLMediaElement.prototype, 'play', {
    configurable: true,
    value: vi.fn().mockResolvedValue(undefined),
})

function aliasRule(stringToReplace: string, replacement: string): RuleInput {
    return {
        type: 'alias',
        stringToReplace,
        replacement,
        alphabet: null,
        caseSensitive: false,
        wordBoundaries: true,
        // Every editor-created rule carries a client-side row uid (see
        // newRule / dictionaryForm / versionForm).
        _uid: crypto.randomUUID(),
    }
}

function StatefulTable({
    initialRules,
}: {
    readonly initialRules: RuleInput[]
}) {
    const [rules, setRules] = useState(initialRules)
    // MemoryRouter: the toolbar links to the Data transfer page, which is where
    // bulk rule editing lives now that the alias-only CSV pair is gone.
    return (
        <MemoryRouter>
            <QueryClientProvider client={new QueryClient()}>
                <TooltipProvider>
                    <PronunciationRuleTable rules={rules} onChange={setRules} />
                </TooltipProvider>
            </QueryClientProvider>
        </MemoryRouter>
    )
}

function renderTable(rules: RuleInput[]) {
    render(<StatefulTable initialRules={rules} />)
}

describe('PronunciationRuleTable row identity', () => {
    beforeEach(() => {
        previewMutate.mockReset()
    })

    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('keeps a playing preview attached to its row when a row above is deleted', async () => {
        const user = userEvent.setup()
        previewMutate.mockResolvedValue({
            url: 'https://cdn.example/clip.mp3',
            durationSeconds: 1.2,
            spokenText: 'bravo',
            phonemeApplied: false,
        })

        renderTable([
            aliasRule('alpha', 'A'),
            aliasRule('bravo', 'B'),
            aliasRule('charlie', 'C'),
        ])

        // Start bravo's preview — its row must show the stop control.
        const bravoInput = screen.getByDisplayValue('bravo')
        const bravoRow = bravoInput.closest('tr') as HTMLElement
        await user.click(
            within(bravoRow).getByRole('button', { name: 'Preview rule' })
        )
        expect(
            within(bravoRow).getByRole('button', { name: 'Stop preview' })
        ).toBeInTheDocument()

        // Delete the row ABOVE bravo (alpha).
        const alphaInput = screen.getByDisplayValue('alpha')
        const alphaRow = alphaInput.closest('tr') as HTMLElement
        await user.click(
            within(alphaRow).getByRole('button', { name: 'Remove rule' })
        )

        // Re-query: the rows moved up, so work from the fresh DOM.
        const bravoRowAfter = screen
            .getByDisplayValue('bravo')
            .closest('tr') as HTMLElement
        const charlieRowAfter = screen
            .getByDisplayValue('charlie')
            .closest('tr') as HTMLElement

        // bravo's preview is still playing — the stop control stays on bravo's
        // row, and must NOT have migrated onto charlie's row.
        expect(
            within(bravoRowAfter).getByRole('button', {
                name: 'Stop preview',
            })
        ).toBeInTheDocument()
        expect(
            within(charlieRowAfter).queryByRole('button', {
                name: 'Stop preview',
            })
        ).not.toBeInTheDocument()
        expect(
            within(charlieRowAfter).getByRole('button', {
                name: 'Preview rule',
            })
        ).toBeInTheDocument()
    })
})

/**
 * Config enters the system through ONE door: the Data transfer page, which
 * dry-run validates a bundle, shows a field-level diff and (in production) needs
 * a second admin. The rule toolbar's own CSV pair was a weaker second door —
 * alias rules only (phoneme rules were silently left out of the export), no
 * validation, no diff, no approval — so it was retired in favour of a pointer at
 * the real one, deep-linked to this section.
 */
describe('PronunciationRuleTable bulk-edit path', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('offers no import control of its own', () => {
        renderTable([aliasRule('alpha', 'A')])
        expect(
            screen.queryByRole('button', { name: /Import/i })
        ).not.toBeInTheDocument()
        expect(
            screen.queryByRole('button', { name: /Export/i })
        ).not.toBeInTheDocument()
        expect(document.querySelector('input[type=file]')).toBeNull()
    })

    it('links to the Data transfer page, scoped to the pronunciation section', () => {
        renderTable([aliasRule('alpha', 'A')])
        const link = screen.getByRole('link', {
            name: /Bulk edit via Data transfer/,
        })
        expect(link).toHaveAttribute(
            'href',
            '/config-transfer?sections=PronunciationDictionaries'
        )
    })
})
