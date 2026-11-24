import { prettify } from '@/lib/text'

/** Vendor identifiers, humanized. Unknown values fall back to prettified. */
const PROVIDER_LABELS: Record<string, string> = {
    elevenlabs: 'ElevenLabs',
    azure: 'Azure Speech',
}

/** ElevenLabs model ids share the `eleven_` prefix; fall back to it when the
 *  backend didn't record a provider (some pre-20 Jul 2026 manual jobs). */
export function resolveProviderLabel(provider: string, model: string): string {
    if (PROVIDER_LABELS[provider]) return PROVIDER_LABELS[provider]
    if (provider) return prettify(provider)
    if (model.startsWith('eleven_')) return 'ElevenLabs'
    return 'Unknown'
}
