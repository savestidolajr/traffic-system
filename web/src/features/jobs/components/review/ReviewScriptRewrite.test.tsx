// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { TooltipProvider } from '@/components/ui/tooltip'
import { ReviewScript } from './ReviewScript'
import * as jobHooks from '../../api/job-hooks'
import { ApiError } from '@/lib/api/client'

// The tone picker is a Radix Select, which measures and pointer-captures.
// jsdom has neither, so stub what it reaches for (mirrors ReviewScript.test.tsx).
class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)
Element.prototype.scrollIntoView = vi.fn()
Element.prototype.hasPointerCapture = vi.fn(() => false)
Element.prototype.setPointerCapture = vi.fn()
Element.prototype.releasePointerCapture = vi.fn()

afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
})

const ORIGINAL =
    'The Monash Freeway is closed citybound at Toorak Road. Partly cloudy, 12 degrees.'
const REWRITTEN =
    'The Monash Freeway is CLOSED citybound at Toorak Road. Take Burke Road instead.'

function renderScript(ui: ReactNode) {
    return render(<TooltipProvider>{ui}</TooltipProvider>)
}

function renderForRewrite(onRevoice = vi.fn()) {
    return {
        onRevoice,
        ...renderScript(
            <ReviewScript
                jobId="job-1"
                text={ORIGINAL}
                canEdit
                onRevoice={onRevoice}
                isRevoicing={false}
                toneCapability={{ model: 'eleven_v3' }}
            />
        ),
    }
}

async function runRewrite(user: UserEvent) {
    await user.click(screen.getByRole('button', { name: /edit/i }))
    await user.click(screen.getByRole('radio', { name: 'Content' }))
    await user.type(
        screen.getByLabelText('Instruction for rewriting the script content'),
        'drop the weather and lead with the closure'
    )
    await user.click(screen.getByRole('button', { name: 'Rewrite' }))
}

function stubRewrite(overrides: Partial<{ preservedLines: number }> = {}) {
    vi.spyOn(jobHooks, 'rewriteScript').mockResolvedValue({
        script: REWRITTEN,
        wordsBefore: 11,
        wordsAfter: 13,
        preservedLines: 0,
        ...overrides,
    })
}

// ---------------------------------------------------------------------------
// Content rewrite from a free-form instruction. The contract that matters: the rewrite is a
// PREVIEW — the draft is untouched until the operator explicitly applies it, and apply feeds the
// existing verbatim editor (Re-voice) rather than a second air path. A grounding refusal must
// surface as something the operator can act on AND keep the instruction so they edit and retry.
// ---------------------------------------------------------------------------
describe('ReviewScript content rewrite', () => {
    it('shows the rewrite as a preview with a diff, never replacing the draft', async () => {
        stubRewrite()
        const { onRevoice } = renderForRewrite()
        const user = userEvent.setup()

        await runRewrite(user)

        // The preview names both sides of the change: the dropped weather line and the new
        // alternative-route line (the shared entity-diff engine renders removed/added pairs).
        expect(
            await screen.findAllByText(/Partly cloudy, 12 degrees/)
        ).not.toHaveLength(0)
        expect(
            screen.getAllByText(/Take Burke Road instead/).length
        ).toBeGreaterThan(0)
        // The editor still holds the ORIGINAL draft — nothing has been applied.
        const editor = screen.getByRole('textbox', {
            name: 'Edit script',
        }) as HTMLTextAreaElement
        expect(editor.value).toContain('Partly cloudy, 12 degrees')
        expect(onRevoice).not.toHaveBeenCalled()
    })

    it('applies the rewrite into the existing editor, still requiring Re-voice', async () => {
        stubRewrite()
        const { onRevoice } = renderForRewrite()
        const user = userEvent.setup()

        await runRewrite(user)
        await screen.findByText(/Preview ·/)
        await user.click(
            screen.getByRole('button', { name: 'Apply to editor' })
        )

        // The rewritten words land in the editor (the verbatim re-voice path)…
        const editor = screen.getByRole('textbox', {
            name: 'Edit script',
        }) as HTMLTextAreaElement
        expect(editor.value).toContain('CLOSED citybound')
        expect(editor.value).not.toContain('Partly cloudy')
        // …but nothing has been sent to the voice: the operator still reads and presses Re-voice.
        expect(onRevoice).not.toHaveBeenCalled()
    })

    it('discards the preview without touching the draft', async () => {
        stubRewrite()
        renderForRewrite()
        const user = userEvent.setup()

        await runRewrite(user)
        await screen.findByText(/Preview ·/)
        await user.click(screen.getByRole('button', { name: 'Discard' }))

        expect(screen.queryByText(/Preview ·/)).not.toBeInTheDocument()
        const editor = screen.getByRole('textbox', {
            name: 'Edit script',
        }) as HTMLTextAreaElement
        expect(editor.value).toContain('Partly cloudy, 12 degrees')
    })

    it('says which station lines were held back, so an unchanged promo is not read as a bug', async () => {
        // Bookend lines and filler statements are ARN's own copy: the backend peels them off, and
        // an operator who asked to change one needs to be told why it came back identical.
        stubRewrite({ preservedLines: 2 })
        renderForRewrite()
        const user = userEvent.setup()

        await runRewrite(user)

        expect(
            await screen.findByText(/2 station lines \(bookend or filler\)/)
        ).toBeInTheDocument()
    })

    it('shows a content-safety refusal and keeps the instruction', async () => {
        vi.spyOn(jobHooks, 'rewriteScript').mockRejectedValue(
            new ApiError('Content safety: flagged wording.', 400)
        )
        renderForRewrite()
        const user = userEvent.setup()

        await runRewrite(user)

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('Blocked by content safety')
        expect(alert).toHaveTextContent('flagged wording')
        expect(
            screen.getByLabelText(
                'Instruction for rewriting the script content'
            )
        ).toHaveValue('drop the weather and lead with the closure')
        const editor = screen.getByRole('textbox', {
            name: 'Edit script',
        }) as HTMLTextAreaElement
        expect(editor.value).toContain('The Monash Freeway is closed citybound')
        expect(editor.value).toContain('Partly cloudy, 12 degrees.')
    })

    it('will not call out with an empty instruction', async () => {
        const spy = vi.spyOn(jobHooks, 'rewriteScript')
        renderForRewrite()
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))
        await user.click(screen.getByRole('radio', { name: 'Content' }))
        expect(screen.getByRole('button', { name: 'Rewrite' })).toBeDisabled()
        expect(spy).not.toHaveBeenCalled()
    })

    it('keeps each mode its own instruction when the operator switches lever', async () => {
        // Two levers behind one box: switching to compare them must not silently bin the sentence
        // already typed, which is the failure a single shared input would have.
        renderForRewrite()
        const user = userEvent.setup()

        await user.click(screen.getByRole('button', { name: /edit/i }))
        await user.type(
            screen.getByLabelText('Describe the read you want'),
            'calmer please'
        )
        await user.click(screen.getByRole('radio', { name: 'Content' }))
        await user.type(
            screen.getByLabelText(
                'Instruction for rewriting the script content'
            ),
            'drop the weather'
        )
        await user.click(screen.getByRole('radio', { name: 'Delivery' }))

        expect(screen.getByLabelText('Describe the read you want')).toHaveValue(
            'calmer please'
        )
    })
})
