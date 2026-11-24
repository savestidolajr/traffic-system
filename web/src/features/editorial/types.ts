import { z } from 'zod'
import {
    EditorialSettingsApiSchema,
    EffectiveEditorialSettingsApiSchema,
} from './api/editorial-schemas'

export type EditorialSettings = z.infer<typeof EditorialSettingsApiSchema>
export type EffectiveEditorialSettings = z.infer<
    typeof EffectiveEditorialSettingsApiSchema
>

export type EditorialScope = 'Global' | 'Market' | 'Station'

/** Body for storing the numeric settings at one scope; null means "inherit". */
export interface UpsertEditorialSettingsRequest {
    readonly scope: EditorialScope
    readonly scopeId: string | null
    readonly driveTimeHeadlineThresholdMinutes: number | null
    readonly maxCorridors: number | null
    readonly majorTierCap: number | null
    readonly moderateTierCap: number | null
    readonly minorTierCap: number | null
    readonly continuityNewCap: number | null
    readonly continuityOngoingCap: number | null
    readonly continuityClearedCap: number | null
    readonly continuityUpdatedCap: number | null
    readonly guidanceMaxDirectives: number | null
    readonly maxSlowDownFactor: number | null
    readonly maxSpeedUpFactor: number | null
    readonly maxSilencePadSeconds: number | null
    readonly maxSafeOverrunTrimSeconds: number | null
    /** Denylist of authority feeds this scope must not consult; null = inherit. */
    readonly disabledTrafficSources: readonly string[] | null
}
