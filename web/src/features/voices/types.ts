import { z } from 'zod'
import { ElevenLabsVoiceSchema, VoicesListResponseSchema } from './api/schemas'

export type ElevenLabsVoice = z.infer<typeof ElevenLabsVoiceSchema>

export type VoicesListResponse = z.infer<typeof VoicesListResponseSchema>

export type { VoiceSettings } from '@/lib/api/schemas/shared'
