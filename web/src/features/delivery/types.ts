import { z } from 'zod'
import {
    DeliveryStyleVersionApiSchema,
    DeliveryStyleApiSchema,
    DeliveryStylesListResponseSchema,
    DeliveryStylePreviewResponseSchema,
} from './api/delivery-schemas'

export type DeliveryStyleVersion = z.infer<typeof DeliveryStyleVersionApiSchema>
/** Note: DeliveryStyle.assignedStations is always empty; derive via useStyleAssignments(). */
export type DeliveryStyle = z.infer<typeof DeliveryStyleApiSchema>
export type DeliveryStylesListResponse = z.infer<
    typeof DeliveryStylesListResponseSchema
>
export type DeliveryStylePreviewResponse = z.infer<
    typeof DeliveryStylePreviewResponseSchema
>

/** Plain-language tone + curated v3 audio-tag palette the director may use. */
export interface DeliveryStylePolicyInput {
    readonly toneDescription: string
    readonly allowedTags: string[]
    readonly intensity: number
}

/** Body for creating a new delivery style (mints version 1). */
export interface CreateDeliveryStyleRequest {
    readonly name: string
    readonly description: string | null
    readonly policy: DeliveryStylePolicyInput
    readonly changeNote: string | null
}

/** Body for updating a delivery style — a policy change mints a new version. */
export interface UpdateDeliveryStyleRequest {
    readonly name: string
    readonly description: string | null
    readonly policy: DeliveryStylePolicyInput
    readonly changeNote: string | null
}

/** Body for an audition preview of an unsaved policy. */
export interface PreviewDeliveryStyleRequest {
    readonly policy: DeliveryStylePolicyInput
    readonly baseVoiceId?: string
    readonly intent?: string
}
