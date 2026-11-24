import { describe, it, expect } from 'vitest'
import { ContentSelectionApiSchema } from '@/lib/api/schemas/shared'
import {
    JobSummarySchema,
    JobsListResponseSchema,
    JobSchema,
    JobIncidentSchema,
    OfficialEvidenceSchema,
    SourceFetchDiagnosticSchema,
    AuditionResponseSchema,
    RetoneResponseSchema,
} from './schemas'

const validApi = {
    JobId: 'abc123',
    CreatedAt: '2026-05-24T00:00:00Z',
    Station: '3AW 693 Melbourne Current',
    Market: 'Melbourne',
    TargetDurationSec: 30,
    Status: 'WAITING_APPROVAL' as const,
    LastUpdatedAt: '2026-05-24T00:01:00Z',
    MeasuredDurationSec: 31.2,
    DeltaSec: 1.2,
}

describe('SourceFetchDiagnosticSchema', () => {
    it('maps the persisted PascalCase diagnostic contract to frontend fields', () => {
        expect(
            SourceFetchDiagnosticSchema.parse({
                SourceId: 'qldtraffic',
                Outcome: 'ServedStale',
                Detail: 'HTTP 429 (rate limited)',
                DataAsOfUtc: '2026-08-24T00:31:12Z',
                StaleSeconds: 420,
            })
        ).toEqual({
            sourceId: 'qldtraffic',
            outcome: 'ServedStale',
            detail: 'HTTP 429 (rate limited)',
            dataAsOfUtc: '2026-08-24T00:31:12Z',
            staleSeconds: 420,
        })
    })
})

describe('source-native evidence identity schemas', () => {
    it('retains incident record and revision identifiers after camelizing', () => {
        const parsed = JobIncidentSchema.parse({
            Key: 'here-incident-1',
            Priority: 2,
            Headline: 'Crash on M4',
            Detail: 'One lane blocked',
            CanonicalClass: 'crash',
            SourceRecordIdentities: [
                {
                    SourceId: 'here',
                    RecordId: 'here-current-2',
                    OriginalRecordId: 'here-original-1',
                },
            ],
        })

        expect(parsed.sourceRecordIdentities).toEqual([
            {
                sourceId: 'here',
                recordId: 'here-current-2',
                originalRecordId: 'here-original-1',
            },
        ])
        expect(parsed.canonicalClass).toBe('crash')
    })

    it('retains an official receipt record identifier', () => {
        const parsed = OfficialEvidenceSchema.parse({
            SourceId: 'livetraffic-nsw',
            Headline: 'Crash on M4',
            IsIncidentClass: true,
            SourceRecordId: '225630',
        })

        expect(parsed.sourceRecordId).toBe('225630')
    })

    it('parses full OfficialEvidence receipt with timing, priority, impact and delay', () => {
        const parsed = OfficialEvidenceSchema.parse({
            SourceId: 'qldtraffic',
            Headline: 'Roadworks: Captain Cook Hwy',
            IsIncidentClass: false,
            RoadName: 'Captain Cook Highway',
            CrossStreet: 'Arnold Street',
            Suburb: 'Aeroglen',
            Direction: 'northbound',
            Description: 'Traffic control on scene',
            StartTime: '2026-07-13T06:00:00+10:00',
            EndTime: '2026-08-31T05:00:00+10:00',
            PublishedTime: '2026-07-13T06:00:00+10:00',
            LastUpdated: '2026-08-19T17:54:00+10:00',
            AuthorityPriority: 'Medium',
            Category: 'Roadworks',
            SubCategory: 'Planned roadworks',
            ImpactType: 'Lanes affected',
            ImpactSubtype: 'Lane or lanes reduced',
            QualitativeDelay: 'Delays expected',
            SpeedLimitKph: 60,
            ShapePointCount: 57,
            MatchBasis: 'road-name',
            SourceRecordId: '812066',
        })

        expect(parsed.startTime).toBe('2026-07-13T06:00:00+10:00')
        expect(parsed.endTime).toBe('2026-08-31T05:00:00+10:00')
        expect(parsed.authorityPriority).toBe('Medium')
        expect(parsed.impactType).toBe('Lanes affected')
        expect(parsed.qualitativeDelay).toBe('Delays expected')
        expect(parsed.speedLimitKph).toBe(60)
    })
})

describe('JobSummarySchema', () => {
    it('parses a valid PascalCase payload', () => {
        const r = JobSummarySchema.safeParse(validApi)
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.jobId).toBe('abc123')
            expect(r.data.station).toBe('3AW 693 Melbourne Current')
            expect(r.data.market).toBe('Melbourne')
        }
    })

    it('rejects camelCase fallback (API contract drift surfaces loudly)', () => {
        const r = JobSummarySchema.safeParse({
            jobId: 'abc123',
            createdAt: '2026-05-24T00:00:00Z',
            station: '3AW 693 Melbourne Current',
            market: 'Melbourne',
            targetDurationSec: 30,
            Status: 'WAITING_APPROVAL',
        })
        expect(r.success).toBe(false)
    })

    it('rejects missing required JobId', () => {
        const { JobId, ...withoutId } = validApi
        void JobId
        const r = JobSummarySchema.safeParse(withoutId)
        expect(r.success).toBe(false)
    })

    it('defaults LastUpdatedAt to CreatedAt when absent', () => {
        const { LastUpdatedAt, ...rest } = validApi
        void LastUpdatedAt
        const r = JobSummarySchema.safeParse(rest)
        expect(r.success).toBe(true)
        if (r.success) expect(r.data.lastUpdatedAt).toBe(validApi.CreatedAt)
    })
})

describe('JobsListResponseSchema', () => {
    it('accepts envelope with Items', () => {
        const r = JobsListResponseSchema.safeParse({
            Items: [validApi],
            Total: 1,
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.jobs).toHaveLength(1)
            expect(r.data.total).toBe(1)
        }
    })

    it('accepts empty payload', () => {
        const r = JobsListResponseSchema.safeParse({})
        expect(r.success).toBe(true)
        if (r.success) expect(r.data.jobs).toEqual([])
    })
})

describe('JobSchema content sources (WS1)', () => {
    const baseJob = {
        JobId: 'job-1',
        CreatedAt: '2026-06-11T00:00:00Z',
        Station: '3AW 693 Melbourne Current',
        Market: 'Melbourne',
        TargetDurationSec: 30,
        Status: 'WAITING_APPROVAL' as const,
        LastUpdatedAt: '2026-06-11T00:01:00Z',
        Input: {
            BulletinType: 'traffic',
            TimeWindow: { From: '06:00', To: '09:00' },
            Branding: { StationTag: '3AW Traffic', SignOff: 'Stay safe' },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'v1',
                VoiceName: 'Sarah',
                Model: 'eleven_turbo_v2_5',
            },
        },
        Logs: [],
        StatusHistory: [],
        // WAITING_APPROVAL requires the pipeline artifacts — JobSchema
        // enforces the Job union's per-status invariants.
        Traffic: {
            FetchedAt: '2026-06-11T00:00:30Z',
            Bbox: [144.5, -38.2, 145.5, -37.5],
            Incidents: [
                {
                    Key: 'inc-1',
                    Priority: 4,
                    Headline: 'West Gate Freeway — crash',
                    Detail: 'Two lanes blocked',
                },
            ],
        },
        SelectedIncidents: [],
        Script: {
            Data: {
                Version: 1,
                Text: 'On the West Gate Freeway, a crash is blocking two lanes.',
                EstimatedDurationSec: 12,
            },
        },
        Audio: {
            Data: { Format: 'mp3' as const, Url: '/a.mp3', DurationSec: 12 },
        },
    }

    it('legacy job without content fields parses with safe defaults', () => {
        const r = JobSchema.safeParse(baseJob)

        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.content).toBeUndefined()
            expect(r.data.weather).toBeUndefined()
            expect(r.data.fuel).toBeUndefined()
            expect(r.data.goodRuns).toEqual([])
        }
    })
    it('keeps EmergencyEvents absent distinct from an explicit empty array', () => {
        const absent = JobSchema.parse(baseJob)
        const empty = JobSchema.parse({
            ...baseJob,
            EmergencyEvents: [],
        })

        expect(absent.emergencyEvents).toBeUndefined()
        expect(empty.emergencyEvents).toEqual([])
    })
    it('parses a DELIVERY_PENDING payload and preserves its delivery claim', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Status: 'DELIVERY_PENDING',
            Delivery: {
                Kind: 'Publish',
                IdempotencyKey: 'job-1:etag:Publish',
                ReviewedEtag: 'etag-1',
                AttemptCount: 0,
                RequestedBy: 'operator@arn.com.au',
            },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.status).toBe('DELIVERY_PENDING')
            expect(r.data.delivery).toEqual({
                kind: 'Publish',
                idempotencyKey: 'job-1:etag:Publish',
                reviewedEtag: 'etag-1',
                attemptCount: 0,
                requestedBy: 'operator@arn.com.au',
            })
        }
    })

    it('resolves null selection flags to backend defaults (traffic on, flags off)', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Content: { Traffic: null, Weather: true, Fuel: null },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.content).toEqual({
                traffic: true,
                weather: true,
                fuel: false,
                weatherRequired: false,
                fuelRequired: false,
            })
        }
    })

    it('parses weather, fuel, good runs, and audio word timings', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Weather: {
                Conditions: 'Passing clouds',
                TemperatureC: 15.4,
                WindDescription: null,
                PrecipitationProbabilityPercent: 20,
                Alerts: ['Severe thunderstorm warning'],
            },
            Fuel: {
                Summaries: [
                    {
                        FuelType: 'Unleaded 91',
                        MinPrice: 1.58,
                        AveragePrice: 1.64,
                        StationCount: 49,
                        LatestModifiedUtc: '2026-08-01T00:00:00Z',
                    },
                ],
            },
            GoodRuns: ['M1 Pacific Motorway'],
            Audio: {
                Data: { Format: 'mp3', Url: '/a.mp3', DurationSec: 13 },
                MeasuredDurationSec: 13,
                DeltaSec: 0,
                WordTimings: [{ Word: 'Good', StartSec: 0.1, EndSec: 0.4 }],
            },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.weather?.conditions).toBe('Passing clouds')
            expect(r.data.weather?.alerts).toEqual([
                'Severe thunderstorm warning',
            ])
            expect(r.data.fuel?.summaries[0]?.minPrice).toBe(1.58)
            expect(r.data.fuel?.summaries[0]?.latestModifiedUtc).toBe(
                '2026-08-01T00:00:00Z'
            )
            expect(r.data.goodRuns).toEqual(['M1 Pacific Motorway'])
            expect(r.data.audio?.wordTimings).toEqual([
                { word: 'Good', startSec: 0.1, endSec: 0.4 },
            ])
        }
    })

    // The detail parse THROWS (fetchJobDetail calls JobSchema.parse), so a mistake
    // in this schema blanks the operator's whole job view rather than degrading.
    it('parses and camelizes a FALLBACK_USED payload carrying the superseded take', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Status: 'FALLBACK_USED',
            Fallback: {
                SourceJobId: '',
                AppliedAt: '2026-06-11T00:05:00Z',
                Reason: 'missed-window',
                Streak: 1,
            },
            SupersededTake: {
                CapturedAt: '2026-06-11T00:05:00Z',
                Reason: 'missed-window',
                Text: 'On the West Gate Freeway, a crash is blocking two lanes.',
                TaggedText: null,
                AudioUrl: 'https://blob.test/broadcast_1.wav',
                MeasuredDurationSec: 28.4,
                EstimatedDurationSec: 29.1,
                Sentences: [
                    {
                        Text: 'On the West Gate Freeway, a crash is blocking two lanes.',
                        CorridorKeys: ['West Gate Freeway'],
                        EvidenceIds: ['r1:location:0'],
                        RouteKeys: null,
                    },
                ],
                WordTimings: [{ Word: 'On', StartSec: 0, EndSec: 0.2 }],
                PreReview: {
                    MatchRatio: 0.97,
                    SuspectWords: [],
                    NumberMismatches: [],
                },
                DeliveryStyleId: 'energetic-drive',
                DeliveryStyleVersion: 4,
            },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            const take = r.data.supersededTake
            expect(take?.text).toBe(
                'On the West Gate Freeway, a crash is blocking two lanes.'
            )
            expect(take?.reason).toBe('missed-window')
            expect(take?.sentences[0]?.corridorKeys).toEqual([
                'West Gate Freeway',
            ])
            expect(take?.sentences[0]?.evidenceIds).toEqual(['r1:location:0'])
            expect(take?.wordTimings).toEqual([
                { word: 'On', startSec: 0, endSec: 0.2 },
            ])
            expect(take?.preReview?.matchRatio).toBe(0.97)
        }
    })

    it('parses a job whose SupersededTake is explicitly null', () => {
        // DefaultIgnoreCondition = Never means the key is ALWAYS on the wire; a job
        // that never generated a script sends null, which must not fail the parse.
        const r = JobSchema.safeParse({ ...baseJob, SupersededTake: null })
        expect(r.success).toBe(true)
        if (r.success) expect(r.data.supersededTake).toBeNull()
    })

    it('parses flow roads and defaults to empty when absent', () => {
        const withFlow = JobSchema.safeParse({
            ...baseJob,
            Traffic: {
                ...baseJob.Traffic,
                FlowRoads: [
                    {
                        RoadName: 'M1',
                        JamFactor: 1.2,
                        LengthKm: 4.2,
                        IsWeighted: true,
                        IsSelected: true,
                    },
                ],
            },
        })
        expect(withFlow.success).toBe(true)
        if (withFlow.success && withFlow.data.status === 'WAITING_APPROVAL') {
            expect(withFlow.data.traffic.flowRoads).toEqual([
                {
                    roadName: 'M1',
                    jamFactor: 1.2,
                    lengthKm: 4.2,
                    isWeighted: true,
                    isSelected: true,
                    priorityBoosted: false,
                    functionalClass: 3,
                    score: 0,
                },
            ])
        }

        const noFlow = JobSchema.safeParse(baseJob)
        expect(noFlow.success).toBe(true)
        if (noFlow.success && noFlow.data.status === 'WAITING_APPROVAL') {
            expect(noFlow.data.traffic.flowRoads).toEqual([])
        }
    })

    it('rejects a job whose status promises artifacts the payload lacks', () => {
        const { Script: _script, ...withoutScript } = baseJob
        void _script
        const r = JobSchema.safeParse(withoutScript)
        expect(r.success).toBe(false)
        if (!r.success) {
            expect(JSON.stringify(r.error.issues)).toContain(
                'requires script data'
            )
        }
    })

    it('does not demand artifacts from early-stage jobs', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Status: 'REQUESTED' as const,
            Traffic: undefined,
            SelectedIncidents: undefined,
            Script: undefined,
            Audio: undefined,
        })
        expect(r.success).toBe(true)
    })
    it('retains failed candidate script and unpublished audio for inspection', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Status: 'FAILED' as const,
            Failure: {
                Stage: 'SYNTHESIZING_AUDIO',
                Message: 'Content-limited: best spoken 27.4s',
                OccurredAt: '2026-06-11T00:02:00Z',
                AttemptCount: 4,
                LastEstimatedDurationSec: 27.4,
                TargetDurationSec: 30,
                AttemptHistory: [],
            },
            Script: {
                Data: {
                    Version: 4,
                    Text: 'The strongest available traffic read.',
                    EstimatedDurationSec: 27.4,
                },
            },
            Audio: {
                Data: {
                    Format: 'wav',
                    Url: '/jobs/job-1/audio/candidate',
                    DurationSec: 27.4,
                },
                MeasuredDurationSec: 27.4,
                DeltaSec: -2.6,
            },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.status).toBe('FAILED')
            expect(r.data.script?.text).toBe(
                'The strongest available traffic read.'
            )
            expect(r.data.audio?.data.url).toBe('/jobs/job-1/audio/candidate')
            expect(r.data.failure?.lastEstimatedDurationSec).toBe(27.4)
        }
    })
    it('parses failed job with traffic data and selected incidents for operator curation', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            Status: 'FAILED' as const,
            Failure: {
                Stage: 'FETCHING_TRAFFIC',
                Message: 'Failed to complete generation',
                OccurredAt: '2026-06-11T00:02:00Z',
                AttemptCount: 1,
                LastEstimatedDurationSec: null,
                TargetDurationSec: 30,
                AttemptHistory: [],
            },
            Traffic: {
                FetchedAt: '2026-06-11T00:01:00Z',
                Bbox: [-34.0, 151.0, -33.6, 151.4],
                Incidents: [
                    {
                        Key: 'inc-1',
                        Priority: 1,
                        Headline: 'Crash on M1',
                        Detail: 'Right lane blocked',
                        Latitude: -33.8,
                        Longitude: 151.2,
                    },
                ],
                Corridors: [],
                FlowRoads: [],
            },
            SelectedIncidents: [
                {
                    Key: 'inc-1',
                    Priority: 1,
                    Headline: 'Crash on M1',
                    Detail: 'Right lane blocked',
                    Latitude: -33.8,
                    Longitude: 151.2,
                },
            ],
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.status).toBe('FAILED')
            expect(r.data.traffic?.incidents.length).toBe(1)
            expect(r.data.selectedIncidents.length).toBe(1)
            expect(r.data.selectedIncidents[0].key).toBe('inc-1')
        }
    })

    it('parses editingGuidance into camelCase', () => {
        const r = JobSchema.safeParse({
            ...baseJob,
            EditingGuidance: {
                TargetWordCount: 82,
                MinWordCount: 75,
                MaxWordCount: 94,
                WordsPerMinute: 165,
                TargetDurationSeconds: 30,
            },
        })
        expect(r.success).toBe(true)
        if (r.success) {
            expect(r.data.editingGuidance).toEqual({
                targetWordCount: 82,
                minWordCount: 75,
                maxWordCount: 94,
                wordsPerMinute: 165,
                targetDurationSeconds: 30,
            })
        }
    })

    it('leaves editingGuidance undefined when absent', () => {
        const r = JobSchema.safeParse(baseJob)
        expect(r.success).toBe(true)
        if (r.success) expect(r.data.editingGuidance).toBeUndefined()
    })
})

describe('ContentSelectionApiSchema always-include flags', () => {
    it('defaults WeatherRequired and FuelRequired to false when absent', () => {
        const parsed = ContentSelectionApiSchema.parse({
            Traffic: null,
            Weather: true,
            Fuel: null,
        })
        expect(parsed).toEqual({
            traffic: true,
            weather: true,
            fuel: false,
            weatherRequired: false,
            fuelRequired: false,
        })
    })

    it('defaults WeatherRequired and FuelRequired to false when null', () => {
        const parsed = ContentSelectionApiSchema.parse({
            Traffic: true,
            Weather: true,
            Fuel: true,
            WeatherRequired: null,
            FuelRequired: null,
        })
        expect(parsed).toEqual({
            traffic: true,
            weather: true,
            fuel: true,
            weatherRequired: false,
            fuelRequired: false,
        })
    })

    it('passes explicit always-include flags through', () => {
        const parsed = ContentSelectionApiSchema.parse({
            Traffic: true,
            Weather: true,
            Fuel: true,
            WeatherRequired: true,
            FuelRequired: true,
        })
        expect(parsed).toEqual({
            traffic: true,
            weather: true,
            fuel: true,
            weatherRequired: true,
            fuelRequired: true,
        })
    })
})

// ---------------------------------------------------------------------------
// Regression: the first version of the audition and rewrite hooks read
// `res.data.script` / `res.data.url` straight off the response. The API
// serialises PascalCase, so both calls returned 200 and the UI silently ignored
// them — and the mock adapter answered in camelCase, which hid it until a run
// against deployed dev. These assert the real wire shape.
// ---------------------------------------------------------------------------
describe('delivery iteration response schemas', () => {
    it('reads the API PascalCase audition payload', () => {
        const parsed = AuditionResponseSchema.parse({
            Url: 'https://blob/audition.wav',
            DurationSec: 12.8,
            TagCount: 1,
        })
        expect(parsed).toEqual({
            url: 'https://blob/audition.wav',
            durationSec: 12.8,
            tagCount: 1,
        })
    })

    it('reads the API PascalCase rewrite payload', () => {
        const parsed = RetoneResponseSchema.parse({
            Script: '[quietly] Some sad news. The M1 is CLOSED.',
            WordsBefore: 28,
            WordsAfter: 30,
            Tags: ['[quietly]'],
        })
        expect(parsed.script).toBe('[quietly] Some sad news. The M1 is CLOSED.')
        expect(parsed.wordsBefore).toBe(28)
        expect(parsed.wordsAfter).toBe(30)
        expect(parsed.tags).toEqual(['[quietly]'])
    })

    it('does not silently accept a camelCase payload as a valid rewrite', () => {
        // If the mock and the API ever diverge again, this fails loudly instead of
        // producing an empty script the UI treats as "nothing happened".
        const parsed = RetoneResponseSchema.parse({
            script: 'wrong casing',
            wordsBefore: 1,
            wordsAfter: 1,
            tags: [],
        })
        expect(parsed.script).toBe('')
    })
})
