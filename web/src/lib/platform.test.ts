// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

/**
 * `isMac`/`modKey` are computed once at module load from
 * `navigator.userAgent`, so each case needs a fresh module instance with
 * the user agent stubbed before import.
 */
async function loadPlatformWithUserAgent(userAgent: string) {
    vi.resetModules()
    vi.stubGlobal('navigator', { userAgent })
    return import('./platform')
}

describe('platform modKey', () => {
    afterEach(() => {
        vi.unstubAllGlobals()
        vi.resetModules()
    })

    it('shows the Mac command glyph on Mac user agents', async () => {
        const { isMac, modKey } = await loadPlatformWithUserAgent(
            'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)'
        )
        expect(isMac).toBe(true)
        expect(modKey).toBe('⌘')
    })

    it('shows Ctrl+ on Windows user agents, never the Mac glyph', async () => {
        const { isMac, modKey } = await loadPlatformWithUserAgent(
            'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
        )
        expect(isMac).toBe(false)
        expect(modKey).toBe('Ctrl+')
    })

    it('shows Ctrl+ on Linux user agents, never the Mac glyph', async () => {
        const { isMac, modKey } = await loadPlatformWithUserAgent(
            'Mozilla/5.0 (X11; Linux x86_64)'
        )
        expect(isMac).toBe(false)
        expect(modKey).toBe('Ctrl+')
    })
})
