/**
 * Single arbiter for console audio playback.
 *
 * Every player in the app owns its own `<audio>` element — sample buttons on
 * voice cards and the bulletin waveform player. With no arbiter, starting a
 * second one leaves the first running and two voices read over each other.
 */
let current: HTMLAudioElement | null = null

/**
 * Play `audio` as the only thing playing, pausing whatever held the floor.
 *
 * Resolves `false` when the browser refuses to start — a missing or blocked
 * media file, or an autoplay block. Callers MUST NOT flip their own "playing"
 * state optimistically: derive it from the element's `play`/`pause` events so a
 * refused start never leaves a Pause button sitting over silence, and so the
 * player this call displaces reverts its own button without being told.
 */
export function claimSolo(audio: HTMLAudioElement): void {
    if (current && current !== audio && !current.paused) current.pause()
    current = audio
}

export async function playSolo(audio: HTMLAudioElement): Promise<boolean> {
    claimSolo(audio)
    try {
        await audio.play()
        return true
    } catch {
        if (current === audio) current = null
        return false
    }
}
