import { sanitizeTags } from '../cues'
import { toStrength, type Strength } from '../strength'
import type { DeliveryStylePreset } from '../presets'
import type { DeliveryStyle, DeliveryStyleVersion } from '../types'

/** Editor working value. `intensity` is the operator-facing Strength (1-3). */
export interface DeliveryStyleFormValue {
    name: string
    description: string
    toneDescription: string
    allowedTags: string[]
    intensity: Strength
    changeNote: string
}

export type AuditionStatus = 'idle' | 'loading' | 'ready' | 'error'

export interface AuditionState {
    status: AuditionStatus
    url: string | null
    estimatedDurationSec: number | null
    tagCount: number | null
    error: string | null
    /** Hash of the policy that produced `url`; compared to the live policy to detect staleness. */
    auditionedHash: string | null
    /** Neutral comparison clip, lazily generated when the operator first switches to it. */
    neutralUrl: string | null
}

export const IDLE_AUDITION: AuditionState = {
    status: 'idle',
    url: null,
    estimatedDurationSec: null,
    tagCount: null,
    error: null,
    auditionedHash: null,
    neutralUrl: null,
}

/** A style must always carry at least one cue, otherwise the director no-ops to neutral. */
const DEFAULT_CUES: readonly string[] = ['[serious]', '[calm]']

/**
 * Deterministic serialization for preview inputs. Object keys are sorted so
 * equivalent inputs produce the same key regardless of construction order.
 */
export function previewFingerprint(value: unknown): string {
    if (Array.isArray(value)) {
        return `[${value.map(previewFingerprint).join(',')}]`
    }
    if (value !== null && typeof value === 'object') {
        const entries = Object.entries(value as Record<string, unknown>).sort(
            ([a], [b]) => a.localeCompare(b)
        )
        return `{${entries
            .map(
                ([key, entry]) =>
                    `${previewFingerprint(key)}:${previewFingerprint(entry)}`
            )
            .join(',')}}`
    }
    return JSON.stringify(value) ?? String(value)
}

/** Compare the current preview input with the key/input used for the last preview. */
export function isPreviewStale(
    currentInput: unknown,
    lastPreviewedInput: unknown
): boolean {
    return (
        previewFingerprint(currentInput) !==
        previewFingerprint(lastPreviewedInput)
    )
}

/**
 * Stable key of the audition-relevant policy fields. Tags are sorted so
 * reordering alone does not invalidate the clip.
 */
export function policyHash(
    v: Pick<
        DeliveryStyleFormValue,
        'toneDescription' | 'allowedTags' | 'intensity'
    >
): string {
    return previewFingerprint({
        t: v.toneDescription.trim(),
        a: [...v.allowedTags].sort(),
        i: v.intensity,
    })
}

export function presetForm(
    preset: DeliveryStylePreset
): DeliveryStyleFormValue {
    return {
        name: '',
        description: preset.summarySeed,
        toneDescription: preset.toneSeed,
        allowedTags: [...preset.defaultCues],
        intensity: preset.defaultStrength,
        changeNote: '',
    }
}

/** Seed the editor from a style's current version (for Edit). */
export function styleForm(style: DeliveryStyle): DeliveryStyleFormValue {
    const policy = style.versions.at(-1)?.policy
    const cues = policy ? sanitizeTags(policy.allowedTags) : []
    return {
        name: style.name,
        description: style.description ?? '',
        toneDescription: policy?.toneDescription ?? '',
        allowedTags: cues.length > 0 ? cues : [...DEFAULT_CUES],
        intensity: toStrength(policy?.intensity ?? 2),
        changeNote: '',
    }
}

/** Seed the editor from a historical version (for Restore). Name/summary stay current. */
export function versionForm(
    style: DeliveryStyle,
    version: DeliveryStyleVersion
): DeliveryStyleFormValue {
    const cues = sanitizeTags(version.policy.allowedTags)
    return {
        name: style.name,
        description: style.description ?? '',
        toneDescription: version.policy.toneDescription,
        allowedTags: cues.length > 0 ? cues : [...DEFAULT_CUES],
        intensity: toStrength(version.policy.intensity),
        changeNote: '',
    }
}
