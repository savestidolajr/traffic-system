import { z } from 'zod'
import { VoiceSettingsApiSchema } from '@/lib/api/schemas/shared'

// API responses are PascalCase (matching the real backend); transforms map
// them to the camelCase the UI works in — same convention as schedule schemas.

export const VoiceCalibrationApiSchema = z
    .object({
        WordsPerMinute: z.number(),
        SyllablesPerSecond: z.number(),
        SyllablesPerWord: z.number(),
        RunCount: z.number(),
        CalibratedAt: z.string().nullable(),
    })
    .transform((c) => ({
        wordsPerMinute: c.WordsPerMinute,
        syllablesPerSecond: c.SyllablesPerSecond,
        syllablesPerWord: c.SyllablesPerWord,
        runCount: c.RunCount,
        calibratedAt: c.CalibratedAt,
    }))

export const VoiceRunSampleApiSchema = z
    .object({
        JobId: z.string(),
        Station: z.string().nullable().default(null),
        AudioUrl: z.string(),
        MeasuredDurationSec: z.number(),
        AiredAt: z.string(),
    })
    .transform((r) => ({
        jobId: r.JobId,
        station: r.Station,
        audioUrl: r.AudioUrl,
        measuredDurationSec: r.MeasuredDurationSec,
        airedAt: r.AiredAt,
    }))

export const VoiceProfileVersionApiSchema = z
    .object({
        Version: z.number(),
        Settings: VoiceSettingsApiSchema,
        Model: z.string(),
        ChangeNote: z.string().nullable().default(null),
        CreatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
        SampleUrl: z.string().nullable().default(null),
        ConfigurationKey: z.string(),
        Calibration: VoiceCalibrationApiSchema.nullable().default(null),
        RunSamples: z.array(VoiceRunSampleApiSchema).optional().default([]),
        PaceBoostWpm: z.number().optional().default(0),
    })
    .transform((v) => ({
        version: v.Version,
        settings: v.Settings,
        model: v.Model,
        changeNote: v.ChangeNote,
        createdAt: v.CreatedAt,
        createdBy: v.CreatedBy,
        sampleUrl: v.SampleUrl,
        configurationKey: v.ConfigurationKey,
        calibration: v.Calibration,
        runSamples: v.RunSamples,
        paceBoostWpm: v.PaceBoostWpm,
    }))

export const VOICE_PROFILE_STATUSES = ['active', 'archived'] as const

export const VoiceProfileApiSchema = z
    .object({
        Id: z.string(),
        Name: z.string(),
        Description: z.string().nullable().default(null),
        BaseVoiceId: z.string(),
        BaseVoiceName: z.string(),
        Provider: z.string().default('elevenlabs'),
        Status: z.enum(VOICE_PROFILE_STATUSES).default('active'),
        CurrentVersion: z.number(),
        CreatedAt: z.string(),
        UpdatedAt: z.string(),
        CreatedBy: z.string().nullable().default(null),
        AssignedStations: z.array(z.string()).optional().default([]),
        Versions: z.array(VoiceProfileVersionApiSchema),
    })
    .transform((p) => ({
        id: p.Id,
        name: p.Name,
        description: p.Description,
        baseVoiceId: p.BaseVoiceId,
        baseVoiceName: p.BaseVoiceName,
        provider: p.Provider,
        status: p.Status,
        currentVersion: p.CurrentVersion,
        createdAt: p.CreatedAt,
        updatedAt: p.UpdatedAt,
        createdBy: p.CreatedBy,
        assignedStations: p.AssignedStations,
        versions: p.Versions,
    }))

export const VoiceProfilesListResponseSchema = z
    .object({
        Items: z.array(VoiceProfileApiSchema),
        Total: z.number(),
    })
    .transform((api) => ({
        profiles: api.Items,
        total: api.Total,
    }))

export const VoicePreviewResponseSchema = z
    .object({
        Url: z.string(),
        EstimatedDurationSec: z.number(),
        WordsPerMinute: z.number(),
        EffectivePaceBoostWpm: z.number().optional().default(0),
    })
    .transform((api) => ({
        url: api.Url,
        estimatedDurationSec: api.EstimatedDurationSec,
        wordsPerMinute: api.WordsPerMinute,
        effectivePaceBoostWpm: api.EffectivePaceBoostWpm,
    }))

export const VoiceoverResponseSchema = z
    .object({
        Url: z.string(),
        FileName: z.string(),
        DurationSec: z.number(),
        WordCount: z.number(),
    })
    .transform((api) => ({
        // Relative and same-origin by design — the bytes come back through our own API so the
        // browser honours the anchor's `download` and saves `fileName`.
        url: api.Url,
        fileName: api.FileName,
        durationSec: api.DurationSec,
        wordCount: api.WordCount,
    }))
