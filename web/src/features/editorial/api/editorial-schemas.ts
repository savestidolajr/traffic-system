import { z } from 'zod'

export const EditorialSettingsApiSchema = z.object({
    Scope: z.string(),
    ScopeId: z.string(),
    DriveTimeHeadlineThresholdMinutes: z.number().nullable(),
    MaxCorridors: z.number().nullable(),
    MajorTierCap: z.number().nullable(),
    ModerateTierCap: z.number().nullable(),
    MinorTierCap: z.number().nullable(),
    ContinuityNewCap: z.number().nullable(),
    ContinuityOngoingCap: z.number().nullable(),
    ContinuityClearedCap: z.number().nullable(),
    ContinuityUpdatedCap: z.number().nullable(),
    GuidanceMaxDirectives: z.number().nullable(),
    MaxSlowDownFactor: z.number().nullable(),
    MaxSpeedUpFactor: z.number().nullable(),
    MaxSilencePadSeconds: z.number().nullable(),
    MaxSafeOverrunTrimSeconds: z.number().nullable(),
    // null = inherit the wider scope; [] = a deliberate "consult every source" that stops a
    // market's denylist reaching this station.
    DisabledTrafficSources: z.array(z.string()).nullable(),
    UpdatedAtUtc: z.string(),
    UpdatedBy: z.string().nullable(),
})

/**
 * What a bulletin here would actually use after Global → Market → Station overlay, with the
 * shipped default filled in wherever ARN has set nothing. `ConfiguredByArn` names the fields ARN
 * owns, so the console never has to present an inherited value as if it were a choice.
 */
export const EffectiveEditorialSettingsApiSchema = z.object({
    Market: z.string(),
    Station: z.string().nullable(),
    DriveTimeHeadlineThresholdMinutes: z.number(),
    MaxCorridors: z.number(),
    MajorTierCap: z.number(),
    ModerateTierCap: z.number(),
    MinorTierCap: z.number(),
    ContinuityNewCap: z.number(),
    ContinuityOngoingCap: z.number(),
    ContinuityClearedCap: z.number(),
    ContinuityUpdatedCap: z.number(),
    GuidanceMaxDirectives: z.number(),
    MaxSlowDownFactor: z.number(),
    MaxSpeedUpFactor: z.number(),
    MaxSilencePadSeconds: z.number(),
    MaxSafeOverrunTrimSeconds: z.number(),
    ConfiguredByArn: z.array(z.string()),
    /** The POSITIVE resolved set: which authority feeds a bulletin here actually consult. */
    EnabledTrafficSources: z.array(z.string()),
})
