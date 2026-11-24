import { describe, it, expect } from 'vitest'
import { weatherTone } from './weather-tone'

describe('weatherTone', () => {
    it('classifies the common HERE condition strings', () => {
        expect(weatherTone('Passing clouds. Warm.')).toBe('cloud')
        expect(weatherTone('Sunny. Mild.')).toBe('sun')
        expect(weatherTone('Clear skies overnight')).toBe('sun')
        expect(weatherTone('Light rain showers')).toBe('rain')
        expect(weatherTone('Morning fog clearing')).toBe('fog')
        expect(weatherTone('Snow flurries')).toBe('snow')
    })

    it('lets specific phenomena outrank generic ones', () => {
        // "thunderstorms with heavy rain" is a storm, not rain
        expect(weatherTone('Thunderstorms with heavy rain')).toBe('storm')
        // "sunny with cloudy periods" — sun wins over cloud (listed first)
        expect(weatherTone('Partly sunny with cloudy periods')).toBe('sun')
    })

    it('is case-insensitive and defaults to cloud', () => {
        expect(weatherTone('THUNDERSTORM WARNING')).toBe('storm')
        expect(weatherTone('Mild.')).toBe('cloud')
        expect(weatherTone('')).toBe('cloud')
    })
})
