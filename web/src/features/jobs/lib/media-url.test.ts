import { describe, it, expect } from 'vitest'
import { mediaPath, pinMediaSrc } from './media-url'

const BLOB =
    'https://acct.blob.core.windows.net/audio/broadcast_20260619_080000_abc.wav'
const sas = (blob: string, sig: string) =>
    `${blob}?sv=2024-11-04&st=2026-06-19T07%3A55%3A00Z&se=2026-06-19T08%3A55%3A00Z&sr=b&sp=r&sig=${sig}`

describe('mediaPath', () => {
    it('strips the SAS query so re-signed URLs for one blob compare equal', () => {
        expect(mediaPath(sas(BLOB, 'AAA'))).toBe(BLOB)
        expect(mediaPath(sas(BLOB, 'AAA'))).toBe(mediaPath(sas(BLOB, 'BBB')))
    })

    it('returns the URL unchanged when there is no query', () => {
        expect(mediaPath(BLOB)).toBe(BLOB)
    })
})

describe('pinMediaSrc', () => {
    it('keeps the current URL when only the SAS token rotated', () => {
        const current = sas(BLOB, 'AAA')
        const resigned = sas(BLOB, 'BBB')
        // Identity preserved: <audio src> must not change → no reload/stall.
        expect(pinMediaSrc(current, resigned)).toBe(current)
    })

    it('adopts the next URL when the blob path changed (regenerated audio)', () => {
        const current = sas(BLOB, 'AAA')
        const regenerated = sas(
            'https://acct.blob.core.windows.net/audio/broadcast_20260619_081500_abc.wav',
            'CCC'
        )
        expect(pinMediaSrc(current, regenerated)).toBe(regenerated)
    })

    // Regression: the authenticated proxy path replaced SAS URLs for job audio, and a
    // bare `jobs/{id}/audio` is CONSTANT across re-voices — the player kept the first
    // take until a page reload, so four successive pronunciation fixes looked like
    // no-ops (dev job 3f8e788f, 2026-07-30). The backend appends the blob filename.
    it('adopts the next URL when the proxy render token changed (re-voiced audio)', () => {
        const current =
            '/api/jobs/abc123/audio/broadcast_20260730_053554_abc123.wav'
        const revoiced =
            '/api/jobs/abc123/audio/broadcast_20260730_064529_abc123.wav'
        expect(pinMediaSrc(current, revoiced)).toBe(revoiced)
    })

    it('keeps the current URL when the proxy render token is unchanged', () => {
        const url =
            '/api/jobs/abc123/audio/broadcast_20260730_064529_abc123.wav'
        expect(pinMediaSrc(url, url)).toBe(url)
    })
})
