import { z } from 'zod'
import type { VoiceSettings } from '@/lib/api/schemas/shared'
import {
    VoiceProfileApiSchema,
    VoiceProfileVersionApiSchema,
    VoiceCalibrationApiSchema,
    VoiceProfilesListResponseSchema,
    VoicePreviewResponseSchema,
    VoiceoverResponseSchema,
} from './api/profile-schemas'

export type VoiceCalibration = z.infer<typeof VoiceCalibrationApiSchema>
export type VoiceProfileVersion = z.infer<typeof VoiceProfileVersionApiSchema>
export type VoiceProfile = z.infer<typeof VoiceProfileApiSchema>
export type VoiceProfilesListResponse = z.infer<
    typeof VoiceProfilesListResponseSchema
>
export type VoicePreviewResponse = z.infer<typeof VoicePreviewResponseSchema>

export type VoiceoverResponse = z.infer<typeof VoiceoverResponseSchema>

/** Body for rendering arbitrary copy on a profile's current configuration. */
export interface VoiceoverRequest {
    readonly profileId: string
    readonly script: string
}

/** Body for creating a new variant (mints version 1). */
export interface CreateVoiceProfileRequest {
    readonly name: string
    readonly description: string | null
    readonly baseVoiceId: string
    readonly baseVoiceName: string
    readonly model: string
    readonly settings: VoiceSettings
    readonly changeNote: string | null
    readonly paceBoostWpm?: number
}

/** Body for updating a variant — a settings change mints a new version. */
export interface UpdateVoiceProfileRequest {
    readonly name: string
    readonly description: string | null
    readonly model: string
    readonly settings: VoiceSettings
    readonly changeNote: string | null
    readonly paceBoostWpm?: number
}

/** Body for an ad-hoc preview of unsaved settings. */
export interface PreviewVoiceRequest {
    readonly baseVoiceId: string
    readonly model: string
    readonly settings: VoiceSettings
    readonly paceBoostWpm?: number
}
