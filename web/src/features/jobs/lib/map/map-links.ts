import type { MouseEvent } from 'react'

/**
 * External map verification links (WALK3-001, 1 Jul walkthrough — an ARN
 * operator couldn't place/verify a Gold Coast incident against Google Maps).
 *
 * Google's traffic layer is an independent data pipeline from HERE
 * (crowdsourced Android probes vs HERE's fleet/probe/government feeds), so
 * this link gives operators a genuine second opinion on live congestion at
 * the incident's coordinates — it does NOT confirm HERE's specific incident
 * record (report ID, description, etc). Label the affordance "Open traffic
 * view in Google Maps," never "Verify incident."
 */

/** Maps URL API "Display a map" action with the traffic layer enabled — the
 *  only Maps URL action that supports `layer=traffic` (the Search action and
 *  the Embed API do not expose it at all). No API key required. See
 *  https://developers.google.com/maps/documentation/urls/get-started */
export function googleMapsTrafficUrl(
    lat: number,
    lng: number,
    zoom = 15
): string {
    return `https://www.google.com/maps/@?api=1&map_action=map&center=${lat},${lng}&zoom=${zoom}&layer=traffic`
}

/**
 * Opens the Google Maps traffic view in a small, fixed-size popup window
 * rather than a full new tab — a quick "peek," not a context switch. Respects
 * modifier clicks (ctrl/cmd/shift) and middle-click so operators can still
 * force a full new tab via the browser's native affordance; only the plain
 * left click is intercepted.
 */
export function openGoogleMapsTrafficPopup(
    lat: number,
    lng: number,
    event: MouseEvent
): void {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0)
        return
    event.preventDefault()
    window.open(
        googleMapsTrafficUrl(lat, lng),
        '_blank',
        'width=480,height=640,noopener,noreferrer'
    )
}
