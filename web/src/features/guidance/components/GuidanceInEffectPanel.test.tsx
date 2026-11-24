// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TooltipProvider } from '@/components/ui/tooltip'
import { GuidanceInEffectPanel } from './GuidanceInEffectPanel'

vi.mock('../api/guidance-hooks', () => ({
    useComposedGuidance: () => ({ data: composed, isLoading: false }),
}))

const composed = {
    inEffect: [
        {
            id: 'm1',
            scope: 'Market',
            scopeId: 'portaugusta',
            type: 'Prohibition',
            text: 'NEVER say the word zebra',
            enabled: true,
        },
        {
            id: 'g1',
            scope: 'Global',
            scopeId: '',
            type: 'Style',
            text: 'ALWAYS say the word zebra',
            enabled: true,
        },
        {
            id: 's1',
            scope: 'Station',
            scopeId: 'KIIS 102.3',
            type: 'Editorial',
            text: 'Lead with the coast road',
            enabled: true,
        },
    ],
    droppedCount: 2,
}

/**
 * Regression for bug-bash-2026-08-05 finding 21: the panel rendered directives
 * as an undifferentiated numbered list, so an editorial user could not tell
 * which scope a rule came from or which one wins. Every entry must carry its
 * scope (and precedence must be stated).
 */
function renderPanel() {
    return render(
        <TooltipProvider>
            <GuidanceInEffectPanel
                marketId="portaugusta"
                station="KIIS 102.3"
                label="KIIS 102.3 · portaugusta"
            />
        </TooltipProvider>
    )
}

describe('GuidanceInEffectPanel scope attribution', () => {
    afterEach(cleanup)

    it('labels every in-effect directive with its scope', async () => {
        const user = userEvent.setup()
        renderPanel()

        await user.click(
            screen.getByRole('button', { name: /Reaching the writer/ })
        )

        expect(screen.getByText('Market · portaugusta')).toBeInTheDocument()
        expect(screen.getByText('Global')).toBeInTheDocument()
        expect(screen.getByText('Station · KIIS 102.3')).toBeInTheDocument()
    })

    it('states the precedence order so the winner is legible', async () => {
        const user = userEvent.setup()
        renderPanel()

        await user.click(
            screen.getByRole('button', { name: /Reaching the writer/ })
        )

        await user.hover(
            screen.getByRole('button', { name: 'How is precedence decided?' })
        )
        const tooltip = await screen.findByRole('tooltip')
        expect(tooltip).toHaveTextContent(/Station beats Market beats Global/)
    })
})
