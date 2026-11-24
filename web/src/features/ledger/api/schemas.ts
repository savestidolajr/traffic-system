import { z } from 'zod'
import { camelizeKeys } from '../../../lib/api-utils'
import {
    JobIncidentSchema,
    OfficialEvidenceSchema,
} from '../../jobs/api/schemas'

// =============================================================================
// Event Ledger schemas — PascalCase wire → camelCase UI via camelizeKeys.
// =============================================================================

export const LEDGER_LANES = [
    'New',
    'Changed',
    'Active',
    'Cleared',
    'Stale',
] as const
export type LedgerLane = (typeof LEDGER_LANES)[number]

/** Omission is the only verdict: an event either airs or is kept out. */
export const VERDICTS = ['Omitted'] as const
export type Verdict = (typeof VERDICTS)[number]

const LedgerObservationSchema = z
    .object({
        AtUtc: z.string(),
        JobId: z.string(),
        Summary: z.string(),
    })
    .transform(camelizeKeys)

const LedgerAiringSchema = z
    .object({
        JobId: z.string(),
        AtUtc: z.string(),
        Sentence: z.string(),
    })
    .transform(camelizeKeys)

const LedgerEventSchema = z
    .object({
        Key: z.string(),
        RoadName: z.string(),
        Headline: z.string(),
        Detail: z.string().nullish(),
        Sources: z.array(z.string()),
        FirstSeenUtc: z.string(),
        LastSeenUtc: z.string(),
        StartTimeUtc: z.string().nullish(),
        EndTimeUtc: z.string().nullish(),
        RoadClosed: z.boolean(),
        Lane: z.enum(LEDGER_LANES),
        SightingCount: z.number(),
        Observations: z.array(LedgerObservationSchema),
        Airings: z.array(LedgerAiringSchema),
        Verdict: z.enum(VERDICTS).nullish(),
        VerdictNotes: z.string().nullish(),
        VerdictBy: z.string().nullish(),
        VerdictAtUtc: z.string().nullish(),
        Record: JobIncidentSchema,
        OfficialEvidences: z.array(OfficialEvidenceSchema).nullish(),
    })
    .transform(camelizeKeys)

const FeedHealthSchema = z
    .object({
        SourceId: z.string(),
        LastContributedUtc: z.string().nullish(),
        JobsWithSource: z.number(),
        JobsInWindow: z.number(),
    })
    .transform(camelizeKeys)

export const LedgerSchema = z
    .object({
        Market: z.string(),
        Events: z.array(LedgerEventSchema),
        FeedHealth: z.array(FeedHealthSchema),
    })
    .transform(camelizeKeys)

const EventDeltaSchema = z
    .object({
        Key: z.string(),
        RoadName: z.string(),
        Headline: z.string(),
        Status: z.enum(['New', 'Changed', 'Cleared', 'Unchanged']),
        ChangeSummary: z.string().nullish(),
    })
    .transform(camelizeKeys)

export const JobDeltaSchema = z
    .object({
        JobId: z.string(),
        PreviousJobId: z.string().nullish(),
        PreviousJobAtUtc: z.string().nullish(),
        Deltas: z.array(EventDeltaSchema),
    })
    .transform(camelizeKeys)

export type LedgerEvent = z.output<typeof LedgerEventSchema>
export type LedgerObservation = z.output<typeof LedgerObservationSchema>
export type LedgerAiring = z.output<typeof LedgerAiringSchema>
export type Ledger = z.output<typeof LedgerSchema>
export type FeedHealthRow = z.output<typeof FeedHealthSchema>
export type EventDelta = z.output<typeof EventDeltaSchema>
export type JobDelta = z.output<typeof JobDeltaSchema>
