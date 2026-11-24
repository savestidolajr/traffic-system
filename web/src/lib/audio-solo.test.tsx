// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SamplePlayButton } from '@/features/voices/components/SamplePlayButton'
import { claimSolo, playSolo } from './audio-solo'

/**
 * jsdom stubs `HTMLMediaElement.play`/`pause` as unimplemented throwers, so a
 * fake stands in for the media pipeline: it flips `paused` and fires the same
 * `play`/`pause` events a browser does. `refuse` reproduces a blocked or
 * missing file, where `play()` rejects.
 */
let refuse = false

beforeEach(() => {
    refuse = false
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function (
        this: HTMLMediaElement
    ) {
        if (refuse) return Promise.reject(new Error('NotSupportedError'))
        Object.defineProperty(this, 'paused', {
            value: false,
            configurable: true,
        })
        this.dispatchEvent(new Event('play'))
        return Promise.resolve()
    })
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(function (
        this: HTMLMediaElement
    ) {
        Object.defineProperty(this, 'paused', {
            value: true,
            configurable: true,
        })
        this.dispatchEvent(new Event('pause'))
    })
})

afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
})

const audios = () => Array.from(document.querySelectorAll('audio'))

describe('audio playback is exclusive', () => {
    it('starting one sample pauses the one already playing and reverts its button', async () => {
        const user = userEvent.setup()
        render(
            <>
                <SamplePlayButton url="/a.wav" label="Play A" size="sm" />
                <SamplePlayButton url="/b.wav" label="Play B" size="sm" />
            </>
        )

        await user.click(screen.getByRole('button', { name: 'Play A' }))
        expect(audios().map((a) => a.paused)).toEqual([false, true])

        await user.click(screen.getByRole('button', { name: 'Play B' }))

        // Two voices reading over each other was the bug: A must be paused.
        expect(audios().map((a) => a.paused)).toEqual([true, false])
        // And A's button must stop claiming it is playing.
        expect(screen.getByRole('button', { name: 'Play A' })).toBeVisible()
        expect(
            screen.getAllByRole('button', { name: 'Pause sample' })
        ).toHaveLength(1)
    })

    it('leaves the button on Play when the media refuses to start', async () => {
        refuse = true
        const user = userEvent.setup()
        render(<SamplePlayButton url="/missing.wav" label="Play A" size="sm" />)

        await user.click(screen.getByRole('button', { name: 'Play A' }))

        expect(
            screen.queryByRole('button', { name: 'Pause sample' })
        ).not.toBeInTheDocument()
        expect(screen.getByRole('button', { name: 'Play A' })).toBeVisible()
    })

    it('pausing the active sample releases the floor without touching others', async () => {
        const user = userEvent.setup()
        render(
            <>
                <SamplePlayButton url="/a.wav" label="Play A" size="sm" />
                <SamplePlayButton url="/b.wav" label="Play B" size="sm" />
            </>
        )

        await user.click(screen.getByRole('button', { name: 'Play A' }))
        await user.click(screen.getByRole('button', { name: 'Pause sample' }))
        expect(audios().map((a) => a.paused)).toEqual([true, true])

        await user.click(screen.getByRole('button', { name: 'Play B' }))
        expect(audios().map((a) => a.paused)).toEqual([true, false])
    })

    it('lets a native controls play claim the floor before playback', async () => {
        const first = document.createElement('audio')
        const second = document.createElement('audio')
        await playSolo(first)

        Object.defineProperty(second, 'paused', {
            value: false,
            configurable: true,
        })
        second.dispatchEvent(new Event('play'))
        claimSolo(second)

        expect(first.paused).toBe(true)
        expect(second.paused).toBe(false)
    })
})
