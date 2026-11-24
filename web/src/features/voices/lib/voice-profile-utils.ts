import type { VoiceSettings } from '@/lib/api/schemas/shared'
import type { VoiceProfile, VoiceProfileVersion } from '../profile-types'
import { getModelCapabilities } from './model-capabilities'

/** The profile's active version (the `currentVersion` pointer, last as fallback). */
export function currentVersion(profile: VoiceProfile): VoiceProfileVersion {
    return (
        profile.versions.find((v) => v.version === profile.currentVersion) ??
        profile.versions[profile.versions.length - 1]
    )
}

/**
 * Canonical configuration key for a voice + settings combination.
 *
 * Mirrors the backend `VoiceCalibration.ConfigurationKey`
 * (`{voiceId}_{speed}_{stability}_{similarity}_{style}_{speakerBoost}`): the
 * key is settings-derived, so two versions with identical settings share one
 * calibration row (and thus one measured WPM memory). The real format is owned
 * by the backend; this is the UI/mock derivation, kept deterministic so the
 * "shared calibration" indicator and the mock store agree.
 */
export function deriveConfigurationKey(
    voiceId: string,
    settings: VoiceSettings
): string {
    const n = (x: number) => x.toFixed(2)
    return [
        voiceId,
        n(settings.speed),
        n(settings.stability),
        n(settings.similarityBoost),
        n(settings.style),
        settings.useSpeakerBoost ? 'true' : 'false',
    ].join('_')
}

export interface VoiceSettingsField {
    readonly key: keyof VoiceSettings
    readonly label: string
    readonly format: (settings: VoiceSettings) => string
}

const pct = (x: number) => `${Math.round(x * 100)}%`

export const VOICE_SETTINGS_FIELDS: readonly VoiceSettingsField[] = [
    { key: 'speed', label: 'Speed', format: (s) => `${s.speed.toFixed(2)}×` },
    { key: 'stability', label: 'Stability', format: (s) => pct(s.stability) },
    {
        key: 'similarityBoost',
        label: 'Similarity',
        format: (s) => pct(s.similarityBoost),
    },
    { key: 'style', label: 'Style', format: (s) => pct(s.style) },
    {
        key: 'useSpeakerBoost',
        label: 'Speaker boost',
        format: (s) => (s.useSpeakerBoost ? 'on' : 'off'),
    },
] as const

export interface VoiceSettingsChange {
    readonly key: keyof VoiceSettings
    readonly label: string
    readonly from: string
    readonly to: string
}

/** Fields that differ between two settings snapshots, oldest → newest. */
export function diffVoiceSettings(
    prev: VoiceSettings,
    next: VoiceSettings
): VoiceSettingsChange[] {
    const changes: VoiceSettingsChange[] = []
    for (const field of VOICE_SETTINGS_FIELDS) {
        if (prev[field.key] !== next[field.key]) {
            changes.push({
                key: field.key,
                label: field.label,
                from: field.format(prev),
                to: field.format(next),
            })
        }
    }
    return changes
}

/** One-line settings summary for cards/rows, e.g. "1.05× · stab 60% · sim 80%". */
export function summarizeVoiceSettings(settings: VoiceSettings): string {
    return [
        `${settings.speed.toFixed(2)}×`,
        `stab ${pct(settings.stability)}`,
        `sim ${pct(settings.similarityBoost)}`,
    ].join(' · ')
}

/** Words a voice reading at `wpm` fills `seconds` of airtime with. */
export function wordTargetForDuration(wpm: number, seconds: number): number {
    if (wpm <= 0 || seconds <= 0) return 0
    return Math.round((wpm / 60) * seconds)
}

/** The three discrete v3 stability modes, by ascending stability value. */
const STABILITY_MODES: ReadonlyArray<{ value: number; label: string }> = [
    { value: 0, label: 'Creative' },
    { value: 0.5, label: 'Natural' },
    { value: 1, label: 'Robust' },
]

/** Nearest discrete stability mode label (Creative / Natural / Robust). */
export function stabilityModeLabel(value: number): string {
    let best = STABILITY_MODES[0]
    for (const mode of STABILITY_MODES) {
        if (Math.abs(mode.value - value) < Math.abs(best.value - value))
            best = mode
    }
    return best.label
}

export interface VoiceCharacterItem {
    readonly key: string
    readonly label: string
    readonly value: string
}

/**
 * Honest, model-aware character fields for a voice version. Mirrors what the
 * backend actually sends per model (see `ElevenLabsClient.BuildTtsPayload` and
 * `model-capabilities`): v3 honors only a discrete stability mode + similarity,
 * so speed/style/speaker-boost are dropped rather than shown as inert values.
 * v2-family models show the full set. Speaker boost appears only when supported
 * and enabled.
 */
export function voiceCharacterItems(
    settings: VoiceSettings,
    model: string
): VoiceCharacterItem[] {
    const caps = getModelCapabilities([model])
    const items: VoiceCharacterItem[] = []

    if (caps.supportsSpeed) {
        items.push({
            key: 'speed',
            label: 'Speed',
            value: `${settings.speed.toFixed(2)}×`,
        })
    }

    items.push({
        key: 'stability',
        label: 'Stability',
        value: caps.discreteStability
            ? stabilityModeLabel(settings.stability)
            : pct(settings.stability),
    })

    items.push({
        key: 'similarity',
        label: 'Similarity',
        value: pct(settings.similarityBoost),
    })

    if (caps.supportsStyle) {
        items.push({
            key: 'style',
            label: 'Style',
            value: pct(settings.style),
        })
    }

    if (caps.supportsSpeakerBoost && settings.useSpeakerBoost) {
        items.push({ key: 'boost', label: 'Speaker boost', value: 'On' })
    }

    return items
}
