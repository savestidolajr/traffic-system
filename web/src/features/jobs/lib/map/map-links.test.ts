// @vitest-environment jsdom

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { googleMapsTrafficUrl, openGoogleMapsTrafficPopup } from './map-links'

describe('googleMapsTrafficUrl', () => {
    it('encodes lat/lng into a Google Maps traffic-layer URL', () => {
        const url = googleMapsTrafficUrl(-33.8688, 151.2093)
        expect(url).toBe(
            'https://www.google.com/maps/@?api=1&map_action=map&center=-33.8688,151.2093&zoom=15&layer=traffic'
        )
    })

    it('uses the default zoom of 15', () => {
        const url = googleMapsTrafficUrl(-33.8688, 151.2093)
        expect(url).toContain('zoom=15')
    })

    it('accepts a custom zoom level', () => {
        const url = googleMapsTrafficUrl(-33.8688, 151.2093, 12)
        expect(url).toContain('zoom=12')
    })

    it('handles zero coordinates', () => {
        expect(googleMapsTrafficUrl(0, 0)).toBe(
            'https://www.google.com/maps/@?api=1&map_action=map&center=0,0&zoom=15&layer=traffic'
        )
    })
})

describe('openGoogleMapsTrafficPopup', () => {
    beforeEach(() => {
        vi.restoreAllMocks()
    })

    function fakeEvent(overrides: Record<string, unknown> = {}) {
        return {
            ctrlKey: false,
            metaKey: false,
            shiftKey: false,
            button: 0,
            preventDefault: vi.fn(),
            ...overrides,
        } as unknown as React.MouseEvent
    }

    it('calls preventDefault and opens a popup on plain left-click', () => {
        const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)
        const event = fakeEvent()

        openGoogleMapsTrafficPopup(-33.8151, 151.0014, event)

        expect(event.preventDefault).toHaveBeenCalledOnce()
        expect(openSpy).toHaveBeenCalledOnce()
        expect(openSpy).toHaveBeenCalledWith(
            'https://www.google.com/maps/@?api=1&map_action=map&center=-33.8151,151.0014&zoom=15&layer=traffic',
            '_blank',
            'width=480,height=640,noopener,noreferrer'
        )
    })

    it('does not intercept a ctrl-click (lets native behaviour proceed)', () => {
        const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)
        const event = fakeEvent({ ctrlKey: true })

        openGoogleMapsTrafficPopup(-33.8151, 151.0014, event)

        expect(event.preventDefault).not.toHaveBeenCalled()
        expect(openSpy).not.toHaveBeenCalled()
    })

    it('does not intercept a meta-click (Cmd on macOS)', () => {
        const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)
        const event = fakeEvent({ metaKey: true })

        openGoogleMapsTrafficPopup(-33.8151, 151.0014, event)

        expect(event.preventDefault).not.toHaveBeenCalled()
        expect(openSpy).not.toHaveBeenCalled()
    })

    it('does not intercept a shift-click', () => {
        const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)
        const event = fakeEvent({ shiftKey: true })

        openGoogleMapsTrafficPopup(-33.8151, 151.0014, event)

        expect(event.preventDefault).not.toHaveBeenCalled()
        expect(openSpy).not.toHaveBeenCalled()
    })

    it('does not intercept a middle-click (button !== 0)', () => {
        const openSpy = vi.spyOn(window, 'open').mockReturnValue(null)
        const event = fakeEvent({ button: 1 })

        openGoogleMapsTrafficPopup(-33.8151, 151.0014, event)

        expect(event.preventDefault).not.toHaveBeenCalled()
        expect(openSpy).not.toHaveBeenCalled()
    })
})
