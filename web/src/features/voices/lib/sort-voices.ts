import type { ElevenLabsVoice } from '../types'

const CATEGORY_ORDER: Record<string, number> = {
    premade: 0,
    professional: 1,
    cloned: 2,
}

export function sortVoicesByCategory(
    voices: readonly ElevenLabsVoice[]
): ElevenLabsVoice[] {
    return [...voices].sort((a, b) => {
        const catDiff =
            (CATEGORY_ORDER[a.category ?? ''] ?? 99) -
            (CATEGORY_ORDER[b.category ?? ''] ?? 99)
        return catDiff !== 0 ? catDiff : a.name.localeCompare(b.name)
    })
}
