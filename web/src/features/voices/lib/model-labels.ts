import { prettify } from '@/lib/text'

/** ElevenLabs model ids → short operator-facing labels (v3 vs multilingual vs turbo). */
export const VOICE_MODEL_LABELS: Record<string, string> = {
    eleven_v3: 'Eleven v3',
    eleven_multilingual_v2: 'Multilingual v2',
    eleven_turbo_v2: 'Turbo v2',
    eleven_turbo_v2_5: 'Turbo v2.5',
    eleven_flash_v2_5: 'Flash v2.5',
}

/** Human label for a model id; unknown ids fall back to a prettified id. */
export function voiceModelLabel(model: string): string {
    return VOICE_MODEL_LABELS[model] ?? prettify(model)
}
