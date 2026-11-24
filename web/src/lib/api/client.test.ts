import { describe, it, expect } from 'vitest'
import { resolveAudioSrc, API_BASE_URL } from './client'

describe('resolveAudioSrc', () => {
    it('passes absolute http(s) URLs through untouched', () => {
        const sas =
            'https://ibpauedevstaapp01.blob.core.windows.net/broadcasts/bulletin.wav?sv=sas'
        expect(resolveAudioSrc(sas)).toBe(sas)
        expect(resolveAudioSrc('http://cdn.example.com/filler.wav')).toBe(
            'http://cdn.example.com/filler.wav'
        )
    })

    it('passes blob:, data: and local: URLs through untouched', () => {
        expect(resolveAudioSrc('blob:https://app/abc')).toBe(
            'blob:https://app/abc'
        )
        expect(resolveAudioSrc('data:audio/wav;base64,AAAA')).toBe(
            'data:audio/wav;base64,AAAA'
        )
        expect(resolveAudioSrc('local://bulletin.wav')).toBe(
            'local://bulletin.wav'
        )
    })

    it('passes mock static asset paths through untouched', () => {
        expect(resolveAudioSrc('/mock/audio-bulletin.wav')).toBe(
            '/mock/audio-bulletin.wav'
        )
        expect(resolveAudioSrc('/mock/samples/sydney.mp3')).toBe(
            '/mock/samples/sydney.mp3'
        )
    })

    it('joins relative proxy paths onto the API base', () => {
        expect(resolveAudioSrc('jobs/abc123/audio')).toBe(
            `${API_BASE_URL}/jobs/abc123/audio`
        )
    })

    it('strips leading slashes before joining', () => {
        expect(resolveAudioSrc('/jobs/abc123/audio')).toBe(
            `${API_BASE_URL}/jobs/abc123/audio`
        )
    })

    it('returns empty string unchanged', () => {
        expect(resolveAudioSrc('')).toBe('')
    })
})
