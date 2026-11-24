import { z } from 'zod'
import { camelizeKeys } from '../../../lib/api-utils'
import {
    CoverageAreaApiSchema,
    ContentSelectionApiSchema,
    VoiceRotationApiSchema,
    FillOrderApiSchema,
    BookendRotationApiSchema,
} from '../../../lib/api/schemas/shared'

const SchedulePatternApiSchema = z.object({
    Id: z.string(),
    Name: z.string(),
    CronExpression: z.string(),
})

const ScheduleAssignmentApiSchema = z.object({
    StartDate: z.string(),
    EndDate: z.string(),
    PatternId: z.string().nullish(),
    Suppress: z.boolean().default(false),
    Note: z.string().nullish(),
})

const ScheduleCalendarApiSchema = z.object({
    Patterns: z.array(SchedulePatternApiSchema).default([]),
    Assignments: z.array(ScheduleAssignmentApiSchema).default([]),
})

export const MarketScheduleSchema = z
    .object({
        MarketId: z.string(),
        Name: z.string().nullable(),
        Station: z.string().nullable(),
        Timezone: z.string().nullable(),
        CronExpression: z.string().nullable(),
        Enabled: z.boolean(),
        // Defaulted true so a response from a backend predating the field (or a fixture that
        // omits it) reads as armed, matching the server's own absent-column rule rather than
        // rendering every station as held back.
        PlayoutEnabled: z.boolean().default(true),
        TargetDurationSeconds: z.number(),
        NotifyBeforePlayoutMinutes: z.number(),
        EscalationBeforePlayoutMinutes: z.number(),
        VoiceProfileId: z.string().nullable().default(null),
        DeliveryStyleId: z.string().nullable().default(null),
        PlaybookId: z
            .string()
            .nullish()
            .transform((value) => value ?? ''),
        PronunciationDictionaryId: z.string().nullable().default(null),
        BookendId: z.string().nullable().default(null),
        NextRunAt: z.string().nullable(),
        CurrentJobId: z.string().nullable(),
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        Coverage: CoverageAreaApiSchema.nullish(),
        Content: ContentSelectionApiSchema.nullish(),
        VoiceRotation: VoiceRotationApiSchema.nullish(),
        BookendRotation: BookendRotationApiSchema.nullish(),
        BookendTokens: z.record(z.string(), z.string()).nullish(),
        FillOrder: FillOrderApiSchema.nullish(),
        Calendar: ScheduleCalendarApiSchema.nullish(),
    })
    .transform((api) => ({
        ...camelizeKeys(api),
        content: api.Content ?? null,
        voiceRotation: api.VoiceRotation ?? null,
        // Raw (not camelized) so override keys like "City" or "goldcoast" keep
        // their exact casing — the backend matches token names case-insensitively,
        // but a camelize pass would still mangle every key's first letter.
        bookendTokens: api.BookendTokens ?? null,
        bookendRotation: api.BookendRotation ?? null,
        fillOrder: api.FillOrder ?? null,
        // A schedule may legitimately have no coverage while it is being configured.
        // Keep the nullable API value so the detail view can show its unconfigured state.
        coverage: api.Coverage ?? null,
        // Normalised here (not left to camelizeKeys) so every consumer sees the
        // nullable wire fields as the non-optional shape `ScheduleCalendar` declares.
        calendar: api.Calendar
            ? {
                  patterns: api.Calendar.Patterns.map((p) => ({
                      id: p.Id,
                      name: p.Name,
                      cronExpression: p.CronExpression,
                  })),
                  assignments: api.Calendar.Assignments.map((a) => ({
                      startDate: a.StartDate,
                      endDate: a.EndDate,
                      patternId: a.PatternId ?? null,
                      suppress: a.Suppress,
                      note: a.Note ?? null,
                  })),
              }
            : null,
    }))

export const SchedulesListResponseSchema = z
    .object({
        Items: z.array(MarketScheduleSchema),
        Total: z.number(),
    })
    .transform((api) => ({
        schedules: api.Items,
        total: api.Total,
    }))
