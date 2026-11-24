import type { AudioTag } from './cues'
import type { Strength } from './strength'

/**
 * Station-safe starting points. A preset describes HOW a station sounds, never
 * what kind of traffic event happened - styles are reusable and assigned per
 * station in the schedule, so they must not be event-reactive. Presets are
 * system-seeded and not user-editable; operators create a style from one.
 */
export type PresetId =
    'standard' | 'peak' | 'advisory' | 'warm' | 'severe' | 'blank'

export interface DeliveryStylePreset {
    readonly id: PresetId
    readonly name: string
    readonly usage: string
    readonly toneSeed: string
    readonly summarySeed: string
    readonly defaultCues: readonly AudioTag[]
    readonly defaultStrength: Strength
}

export const PRESETS: readonly DeliveryStylePreset[] = [
    {
        id: 'standard',
        name: 'Standard traffic',
        usage: 'Clear, neutral, professional. Good default for normal bulletin reads.',
        toneSeed:
            'Read in a clear, neutral, professional traffic style. Keep the pace steady and the delivery easy to follow. Emphasise important delays without sounding dramatic.',
        summarySeed: 'Clear, neutral read for normal bulletins.',
        defaultCues: ['[serious]', '[calm]'],
        defaultStrength: 2,
    },
    {
        id: 'peak',
        name: 'Peak urgency',
        usage: 'Direct, firmer, slightly quicker. Good for breakfast and drive-time.',
        toneSeed:
            'Read with controlled urgency. Keep it direct, alert, and efficient. Emphasise major delays and route impacts, but do not sound panicked.',
        summarySeed: 'Controlled urgent read for peak traffic windows.',
        defaultCues: ['[urgent]', '[serious]', '[calm]'],
        defaultStrength: 2,
    },
    {
        id: 'advisory',
        name: 'Calm advisory',
        usage: 'Steady, slower, reassuring. Good for planned works and off-peak updates.',
        toneSeed:
            'Read in a calm, steady, reassuring style. Give drivers clear information without adding pressure. Keep the tone composed and practical.',
        summarySeed: 'Calm, reassuring read for planned works and off-peak.',
        defaultCues: ['[calm]', '[reassuring]', '[warmly]'],
        defaultStrength: 1,
    },
    {
        id: 'warm',
        name: 'Warm local',
        usage: 'Friendly and conversational without sounding casual. Good for community or regional stations.',
        toneSeed:
            'Read in a warm, local, conversational style while staying clear and professional. Keep it friendly, not casual.',
        summarySeed: 'Warm, conversational read for community stations.',
        defaultCues: ['[warmly]', '[reassuring]', '[calm]'],
        defaultStrength: 2,
    },
    {
        id: 'severe',
        name: 'Severe disruption',
        usage: 'Serious, controlled, low-drama. Good for major incidents or highly disrupted markets.',
        toneSeed:
            'Read in a serious, controlled style. Make the disruption sound important without hype. Keep the delivery measured, clear, and steady.',
        summarySeed: 'Serious, controlled read for major incidents.',
        defaultCues: ['[serious]', '[calm]', '[reassuring]', '[urgent]'],
        defaultStrength: 2,
    },
    {
        id: 'blank',
        name: 'Start blank',
        usage: 'Use when the station has a specific voice direction of its own.',
        toneSeed:
            'Read in a clear, professional traffic style. Keep the delivery natural and easy to follow.',
        summarySeed: '',
        defaultCues: ['[serious]', '[calm]'],
        defaultStrength: 2,
    },
]
