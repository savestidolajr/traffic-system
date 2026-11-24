/**
 * Stabilises a blob audio URL against SAS-token rotation.
 *
 * The job-detail endpoint re-signs the audio blob's SAS token on every request
 * (fresh `st`/`se`/`sig` query params), so the polling review screen receives a
 * *new URL string* for the *same audio* every few seconds. Binding that string
 * straight to an `<audio src>` reloads the element each poll — resetting
 * `currentTime` to 0, silencing playback, and stranding the karaoke highlight at
 * the pre-reload position. These helpers let the player ignore signature-only
 * changes and reload only when the underlying blob actually changes.
 */

/** The blob path without its query string (the SAS token lives entirely in the
 *  query, so two re-signed URLs for the same blob share one path). */
export function mediaPath(url: string): string {
    const q = url.indexOf('?')
    return q === -1 ? url : url.slice(0, q)
}

/**
 * Pick the URL the media element should actually load. Keeps the current URL
 * when only the SAS token rotated (same blob path); adopts the next URL when the
 * path changed — a genuinely different / regenerated audio file.
 *
 * This relies on the backend giving every render its own path. Direct SAS URLs
 * carry a fresh timestamped blob filename; the authenticated proxy path carries
 * the same filename as a trailing render segment
 * (`jobs/{id}/audio/broadcast_{ts}_{id}.wav`). A bare, constant `jobs/{id}/audio`
 * would compare equal after a re-voice and strand the player on the old take.
 */
export function pinMediaSrc(current: string, next: string): string {
    return mediaPath(current) === mediaPath(next) ? current : next
}
