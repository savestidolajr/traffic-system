// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { TooltipProvider } from '@/components/ui/tooltip'
import { AudioWaveformPlayer } from './AudioWaveformPlayer'

class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)

afterEach(cleanup)

describe('AudioWaveformPlayer', () => {
    it('does not request audio until the operator presses Play by default', () => {
        const { container } = render(
            <TooltipProvider>
                <AudioWaveformPlayer url="jobs/job-123/audio" duration={30} />
            </TooltipProvider>
        )

        const audio = container.querySelector('audio')
        expect(audio).not.toBeNull()
        expect(audio).toHaveAttribute('preload', 'none')
        expect(audio).toHaveAttribute('src', '/api/jobs/job-123/audio')
    })

    it('allows the primary review player to opt into preloading', () => {
        const { container } = render(
            <TooltipProvider>
                <AudioWaveformPlayer
                    url="jobs/job-123/audio"
                    duration={30}
                    preload="auto"
                />
            </TooltipProvider>
        )

        expect(container.querySelector('audio')).toHaveAttribute(
            'preload',
            'auto'
        )
    })
    it('uses the shared transfer helper for audio downloads', async () => {
        const user = userEvent.setup()
        const click = vi
            .spyOn(HTMLAnchorElement.prototype, 'click')
            .mockImplementation(() => undefined)

        render(
            <TooltipProvider>
                <AudioWaveformPlayer url="jobs/job-123/audio" duration={30} />
            </TooltipProvider>
        )

        await user.click(screen.getByRole('button', { name: 'Download audio' }))
        expect(click.mock.instances[0]?.download).toMatch(
            /^arn-bulletin-audio-\d{4}-\d{2}-\d{2}\.wav$/
        )
        click.mockRestore()
    })
})
