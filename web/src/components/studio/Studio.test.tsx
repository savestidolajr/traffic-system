// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { AudioLines, Radio } from 'lucide-react'
import { TooltipProvider } from '@/components/ui/tooltip'
import { StudioCanvas, StudioRail, type StudioStep } from './Studio'

// StudioRail's edge-fade detector observes the rail with a ResizeObserver;
// jsdom does not implement one.
if (typeof window.ResizeObserver !== 'function') {
    class ROStub {
        observe() {}
        unobserve() {}
        disconnect() {}
    }
    window.ResizeObserver = ROStub as unknown as typeof ResizeObserver
}

/**
 * Studio.tsx re-keys the canvas per step (`<StudioCanvas stepKey={step}>`).
 * Every studio keeps its unsaved values in a parent-level react-hook-form and
 * renders only the active step's section, so the remount must never discard
 * what the operator has typed. This test drives the real shared components
 * through the real pattern (form hoisted, sections conditional on the step)
 * and proves a step there-and-back keeps the draft.
 */
function MiniStudio() {
    const [step, setStep] = useState('a')
    const form = useForm<{ draft: string }>({ defaultValues: { draft: '' } })

    const steps: readonly StudioStep[] = [
        { id: 'a', label: 'Step A', icon: Radio },
        { id: 'b', label: 'Step B', icon: AudioLines },
    ]

    return (
        <TooltipProvider>
            <form>
                <StudioRail
                    title="mini"
                    steps={steps}
                    activeId={step}
                    onSelect={setStep}
                />
                <StudioCanvas stepKey={step}>
                    {step === 'a' && (
                        <input aria-label="draft" {...form.register('draft')} />
                    )}
                    {step === 'b' && <div>step b content</div>}
                </StudioCanvas>
            </form>
        </TooltipProvider>
    )
}

afterEach(cleanup)

describe('StudioCanvas stepKey remount', () => {
    it('keeps unsaved typed state when stepping away and back', async () => {
        const user = userEvent.setup()
        render(<MiniStudio />)

        const input = screen.getByRole('textbox', { name: 'draft' })
        await user.type(input, 'South Eastern Freeway')
        expect(input).toHaveValue('South Eastern Freeway')

        // Step away: the input's section unmounts entirely.
        await user.click(screen.getByRole('button', { name: 'Step B' }))
        expect(
            screen.queryByRole('textbox', { name: 'draft' })
        ).not.toBeInTheDocument()
        expect(screen.getByText('step b content')).toBeInTheDocument()

        // Step back: the canvas remounts, but the form value survives.
        await user.click(screen.getByRole('button', { name: 'Step A' }))
        expect(screen.getByRole('textbox', { name: 'draft' })).toHaveValue(
            'South Eastern Freeway'
        )
    })
})
