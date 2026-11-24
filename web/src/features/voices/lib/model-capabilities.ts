export interface ModelCapabilities {
    readonly supportsStyle: boolean
    readonly supportsSpeakerBoost: boolean
    readonly supportsSpeed: boolean
    readonly discreteStability: boolean
}

const MODEL_CAPABILITIES: Record<string, ModelCapabilities> = {
    eleven_v3: {
        supportsStyle: false,
        supportsSpeakerBoost: false,
        // v3 ignores the `speed` setting entirely (verified empirically); pace
        // is handled by the backend's post-process time-stretch, not the model.
        supportsSpeed: false,
        discreteStability: true,
    },
    eleven_turbo_v2_5: {
        supportsStyle: true,
        supportsSpeakerBoost: true,
        supportsSpeed: true,
        discreteStability: false,
    },
    eleven_multilingual_v2: {
        supportsStyle: true,
        supportsSpeakerBoost: true,
        supportsSpeed: true,
        discreteStability: false,
    },
    eleven_flash_v2_5: {
        supportsStyle: false,
        supportsSpeakerBoost: false,
        supportsSpeed: true,
        discreteStability: true,
    },
}

const ALL_CAPABILITIES: ModelCapabilities = {
    supportsStyle: true,
    supportsSpeakerBoost: true,
    supportsSpeed: true,
    discreteStability: false,
}

/**
 * Returns the capabilities for a voice based on its supported model IDs.
 * Uses the first known model's capabilities (primary model).
 * Falls back to all capabilities enabled if no known model is found.
 */
export function getModelCapabilities(modelIds: string[]): ModelCapabilities {
    for (const id of modelIds) {
        const capabilities = MODEL_CAPABILITIES[id]
        if (capabilities) return capabilities
    }
    return ALL_CAPABILITIES
}

/** Human-friendly display name for a model ID. */
export function getModelDisplayName(modelId: string): string {
    const names: Record<string, string> = {
        eleven_v3: 'V3 (Latest)',
        eleven_turbo_v2_5: 'Turbo V2.5',
        eleven_multilingual_v2: 'Multilingual V2',
        eleven_flash_v2_5: 'Flash V2.5',
    }
    return names[modelId] ?? modelId
}

/**
 * Compact model badge label for cards/rows, e.g. "v3", "v2". Families matter
 * most at a glance: v3 vs v2 changes which settings apply and how pronunciation
 * rules behave (v3 skips phoneme rules).
 */
export function getModelShortLabel(modelId: string): string {
    const labels: Record<string, string> = {
        eleven_v3: 'v3',
        eleven_turbo_v2_5: 'Turbo v2.5',
        eleven_multilingual_v2: 'v2',
        eleven_flash_v2_5: 'Flash v2.5',
    }
    return labels[modelId] ?? modelId
}
