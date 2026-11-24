// =============================================================================
// Weather tone — classify a free-text conditions string (HERE weather) into
// a small visual vocabulary the Sources panel can tint and glyph by.
// =============================================================================

export type WeatherTone = 'sun' | 'cloud' | 'rain' | 'storm' | 'fog' | 'snow'

/** Ordered: specific phenomena outrank generic ones (storm before rain). */
const TONE_PATTERNS: ReadonlyArray<readonly [WeatherTone, RegExp]> = [
    ['storm', /thunder|storm|lightning/],
    ['snow', /snow|sleet|blizzard|hail|icy|ice\b/],
    ['rain', /rain|shower|drizzle|downpour|wet/],
    ['fog', /fog|mist|haz[ey]|smoke/],
    ['sun', /sun|clear|fine|bright/],
    ['cloud', /cloud|overcast/],
]

/** Tone for a conditions string; clouds are the calm default. */
export function weatherTone(conditions: string): WeatherTone {
    const lower = conditions.toLowerCase()
    for (const [tone, pattern] of TONE_PATTERNS) {
        if (pattern.test(lower)) return tone
    }
    return 'cloud'
}

/** Corner-bloom gradient per tone, for the weather block's AuroraWash. */
export const WEATHER_TONE_GRADIENT: Record<WeatherTone, string> = {
    sun: 'radial-gradient(ellipse at 100% 0%, #f59e0b, #f97316 45%, transparent 75%)',
    cloud: 'radial-gradient(ellipse at 100% 0%, #94a3b8, #64748b 45%, transparent 75%)',
    rain: 'radial-gradient(ellipse at 100% 0%, #38bdf8, #0ea5e9 45%, transparent 75%)',
    storm: 'radial-gradient(ellipse at 100% 0%, #8b5cf6, #6366f1 45%, transparent 75%)',
    fog: 'radial-gradient(ellipse at 100% 0%, #9ca3af, #6b7280 45%, transparent 75%)',
    snow: 'radial-gradient(ellipse at 100% 0%, #a5f3fc, #67e8f9 45%, transparent 75%)',
}
