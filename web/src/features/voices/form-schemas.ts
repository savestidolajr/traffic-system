import { z } from 'zod'
import { VoiceSettingsSchema } from '@/lib/api/schemas/shared'

export const VOICE_PROFILE_NAME_MAX = 60
export const VOICE_PROFILE_NOTE_MAX = 280
/** Mirrors backend VoiceProfile.MaxPaceBoostWpm. */
export const MAX_PACE_BOOST_WPM = 60
/**
 * Mirrors backend SpeechDurationOptions.AbsolutePaceBoostSpeedUpCeiling. The boost is realised by
 * compressing the render, so a voice can never read faster than its calibrated rate x this — past
 * that a larger boost is a no-op, which the studio surfaces rather than silently promising.
 */
export const ABSOLUTE_PACE_CEILING = 1.35

/** Validation schema for the voice workshop (create + tune). */
export const VoiceProfileFormSchema = z.object({
    name: z
        .string()
        .min(1, 'Name is required')
        .max(VOICE_PROFILE_NAME_MAX, 'Keep the name short'),
    description: z.string().max(VOICE_PROFILE_NOTE_MAX).nullable(),
    voiceId: z.string().min(1, 'Pick a base voice'),
    voiceName: z.string().nullable(),
    voiceSettings: VoiceSettingsSchema,
    changeNote: z.string().max(VOICE_PROFILE_NOTE_MAX).nullable(),
    paceBoostWpm: z.number().min(0).max(MAX_PACE_BOOST_WPM),
})

export type VoiceProfileFormData = z.infer<typeof VoiceProfileFormSchema>
