// =============================================================================
// Mock Fixtures — realistic Australian traffic bulletin data
// All responses are PascalCase to satisfy the real Zod API schemas
// =============================================================================

import type { z } from 'zod'
import type { JobSchema } from '@/features/jobs/api/schemas'
import type { ElevenLabsVoiceSchema } from '@/features/voices/api/schemas'
import type { VoiceProfileApiSchema } from '@/features/voices/api/profile-schemas'
import type { MarketScheduleSchema } from '@/features/schedules/api/schemas'
import DEV_DATA from './dev-data.json'
import ADELAIDE_DEV_JOB from './dev-job-287d14a0.json'
import MELBOURNE_DEV_JOB from './dev-job-f034af33.json'
import { LIVE_TRAFFIC } from './live-traffic'

// The nested fixture shapes (voice settings, coverage, log entries, status
// history, attempt results) are NOT re-declared here: `JobFixture` is
// `z.input<typeof JobSchema>`, so the schema already types every branch. The
// hand-written mirrors that used to sit here were unreferenced and free to
// drift from the schema they duplicated.

export interface IncidentFixture {
    Key: string
    Priority: number
    Headline: string
    Detail: string
    FunctionalClass: number
    LengthMeters: number
    Latitude: number | null
    Longitude: number | null
    IsNew: boolean
    /** Road segment polyline from HERE Traffic API; when set the map draws a line instead of a pin. */
    Shape?: Array<{ Lat: number; Lng: number }> | null
}

export interface CorridorFixture {
    RoadName: string
    Severity: number
    ImpactScore: number
    PrimaryType: string
    Description: string
    IncidentCount: number
    TotalLengthKm: number
    Rank: number
    IsSelected: boolean
    FunctionalClass: number
    Direction: string | null
    Suburb: string | null
    DriveTimeMinutes?: number | null
    NormalDriveTimeMinutes: number | null
    DelayMinutes: number | null
    Latitude: number | null
    Longitude: number | null
    PriorityBoosted?: boolean
    /** Commuter magnitude tier (Severe/Major/Moderate/Minor); optional — falls back to severity. */
    Magnitude?: string | null
}

export type JobFixture = z.input<typeof JobSchema>

export type JobSummaryFixture = Pick<
    JobFixture,
    | 'JobId'
    | 'CreatedAt'
    | 'Station'
    | 'Market'
    | 'TargetDurationSec'
    | 'Status'
    | 'LastUpdatedAt'
    | 'MeasuredDurationSec'
    | 'DeltaSec'
    | 'Etag'
>

const BASE = Date.now()
const t = (offsetSec: number): string => {
    const d = new Date(BASE + offsetSec * 1000)
    return d.toISOString()
}

const ISO_TIMESTAMP =
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/

function shiftFixtureTimestamps(value: unknown, offsetMs: number): unknown {
    if (typeof value === 'string' && ISO_TIMESTAMP.test(value)) {
        return new Date(Date.parse(value) + offsetMs).toISOString()
    }
    if (Array.isArray(value)) {
        return value.map((item) => shiftFixtureTimestamps(item, offsetMs))
    }
    if (value && typeof value === 'object') {
        return Object.fromEntries(
            Object.entries(value).map(([key, item]) => [
                key,
                shiftFixtureTimestamps(item, offsetMs),
            ])
        )
    }
    return value
}

/**
 * Exact API payload captured from dev job 287d14a0b2484f10938b5f62b755d2cb
 * on 20 July 2026. Only timestamps are moved forward for queue behaviour and
 * the protected audio URL is replaced by the matching local recording.
 */
function adelaideDevReviewJob(): JobFixture {
    const original = ADELAIDE_DEV_JOB as JobFixture
    const shifted = shiftFixtureTimestamps(
        original,
        Date.parse(t(-300)) - Date.parse(original.CreatedAt)
    ) as JobFixture

    return {
        ...shifted,
        Etag: 'mock-etag-arn-job-287d14a0-v1',
        Audio: shifted.Audio
            ? {
                  ...shifted.Audio,
                  Data: {
                      ...shifted.Audio.Data,
                      Url: '/mock/samples/kiis-adelaide-287d14a0.mp3',
                  },
              }
            : shifted.Audio,
    }
}

/**
 * Exact API payload captured from dev job f034af33f0ba4f6288f52e22bce8bb27
 * (KIIS 101.1 melbourne) on 21 July 2026, while APPROVED — a read-only Adjust
 * tab showcase (no drag handles; canEdit is false for APPROVED). Only
 * timestamps are moved forward for queue behaviour and the protected audio
 * URL is replaced by the matching local recording.
 */
function melbourneDevReviewJob(): JobFixture {
    const original = MELBOURNE_DEV_JOB as JobFixture
    const shifted = shiftFixtureTimestamps(
        original,
        Date.parse(t(-5400)) - Date.parse(original.CreatedAt)
    ) as JobFixture

    return {
        ...shifted,
        Etag: 'mock-etag-arn-job-f034af33-v1',
        Audio: shifted.Audio
            ? {
                  ...shifted.Audio,
                  Data: {
                      ...shifted.Audio.Data,
                      Url: '/mock/samples/kiis-melbourne-f034af33.mp3',
                  },
              }
            : shifted.Audio,
    }
}

const AUDIO_URI = '/mock/audio-bulletin.wav'

// Real bulletins: scripts generated by the pipeline (scripts/test-bulletin.sh
// --script-only), synthesized with the assigned voice (eleven_v3, Natural) and
// loudness-normalised. Audio files live in web/public/mock/samples/.
const SAMPLE = {
    sydney: {
        script: 'Heads up, Western Motorway citybound is backed up between Burnett and Church, about 12-minute delays and crawling through heavy congestion. Meanwhile Warringah Freeway citybound has roadworks at the ramp to Alfred Street North, only adding a few minutes. President Avenue eastbound has minor construction between South Street and President Avenue, and Princes Highway at Kingsway is under roadworks with short delays. Holmes Drive citybound has roadworks at Bestic Street and congestion building, and Harbour Street at Day Street has a planned event causing a tiny hold up',
        audio: '/mock/samples/sydney.mp3',
        durationSec: 35,
    },
    melbourne: {
        script: 'Heads up, the Tullamarine Freeway citybound is under roadworks with congestion building, so allow a few extra minutes. Toorak Road has works between Chapel Street and Williams Road adding short delays, and Huntingdale Road is down to a crawl through the roadworks near North Road. Out east Stud Road has construction with heavy delays, while Princes Highway westbound is copping lane-blocking works. Further out Donnybrook Road and the Northern Highway are both under roadworks, and Sunshine Avenue has a short hold up',
        audio: '/mock/samples/melbourne.mp3',
        durationSec: 34,
    },
    brisbane: {
        script: 'Heads up on the Gateway Motorway, southbound’s backed up at Wynnum Road, you’re looking at a five-minute hold. And if you’re outbound on the Gateway, there’s construction between Sandgate Road and the Bruce Highway and more works near Dohles Rocks Road, so it’s a 25-minute run today, normally 22. Back closer in, a broken-down car’s blocking the left lane at Mt Gravatt‑Capalaba Road on the Gateway, adding another few minutes. Over on Loganlea Road westbound a vehicle’s broken down on the ramp to the Logan Motorway, barely a minute delay. Bruce Highway citybound has lane-blocking roadworks, and the Warrego’s got about a four-kay stretch of works between River Road and Mt Crosby Road',
        audio: '/mock/samples/brisbane.mp3',
        durationSec: 40,
    },
    perth: {
        script: 'Heads up, Reid Highway’s closed at Altone Road with road construction and five incidents, heavy delays and congestion building, drive time 14 minutes. Meanwhile Altone Road is closed and a bit of a mess with construction. If you’re on Farmer Freeway outbound, resurfacing near Mitchell and Great Eastern is adding a couple of minutes. Over on Riversdale Road there’s roadworks causing heavy delays, Mitchell Freeway’s backed up at Stephenson with congestion building, and Northern Highway is tied up with road construction',
        audio: '/mock/samples/perth.mp3',
        durationSec: 32,
    },
    adelaide: {
        script: 'Heads up on South Road, it’s closed at Ashwin Parade with roadworks at Anzac Highway, drive time 29 minutes and congestion’s building. Meanwhile Wakefield Road has construction at Martins, Ryans and Cavan Roads, about an 11-minute run. Not far from there Cross Road is a bit of a mess with heavy delays around South Road, East Avenue and Marion Road. Further out Bolivar Road and the Port River Expressway are under roadworks, and the Southern Expressway citybound is copping it with citybound roadworks',
        audio: '/mock/samples/adelaide.mp3',
        durationSec: 32,
    },
} as const

/**
 * The tagged read for a script: inserts ElevenLabs v3 audio tags at the named
 * spots. Only tags are added — the spoken words stay byte-identical — and the
 * placement is deliberate, so the mock shows tags sitting where the operator
 * put them among the words. Tag vocabulary matches the operator TonePalette
 * presets (features/jobs/lib/tone-marks.ts).
 */
function taggedRead(
    text: string,
    reads: readonly { readonly match: string; readonly tag: string }[]
): string {
    let out = text
    for (const { match, tag } of reads) {
        if (!out.includes(match)) continue
        out = out.replace(match, `${tag} ${match}`)
    }
    return out
}

// The two WAITING_APPROVAL review jobs carry a steering style, so the inline
// tag read is exercised in mock mode. Placement is MID-SENTENCE on purpose —
// the operator steers the stretch they care about, not the line as a whole.
const SYDNEY_TAGGED = taggedRead(SAMPLE.sydney.script, [
    {
        match: 'about 12-minute delays',
        tag: '[excited]',
    },
    {
        match: 'Warringah Freeway citybound has roadworks',
        tag: '[professional]',
    },
    {
        match: 'Holmes Drive citybound has roadworks',
        tag: '[slow]',
    },
])

const MELBOURNE_TAGGED = taggedRead(SAMPLE.melbourne.script, [
    {
        match: 'so allow a few extra minutes',
        tag: '[slow]',
    },
    {
        match: 'Huntingdale Road is down to a crawl',
        tag: '[sympathetic]',
    },
    {
        match: 'Further out Donnybrook Road',
        tag: '[reassuring]',
    },
])

// ── Live HERE traffic snapshot ────────────────────────────────────────────
// Traffic data below comes from the real HERE Traffic API, regenerated with
// `node scripts/fetch-here-traffic.mjs` (see src/lib/api/mock/live-traffic.ts).
// Scripts are assembled from the snapshot's lineage-aligned sentences.

const scriptFromSentences = (sentences: readonly { Text: string }[]): string =>
    sentences.map((s) => s.Text).join(' ')

const GC_SCRIPT = scriptFromSentences([
    { Text: 'Gold FM 90.1 Traffic update.' },
    ...LIVE_TRAFFIC.goldcoast.TrafficSentences,
    { Text: 'That is the latest from Gold FM 90.1 Traffic.' },
])

// Perth and Adelaide pull from the same real HERE snapshot as the other
// markets — regenerated by scripts/fetch-here-traffic.mjs.
const PERTH_TRAFFIC = LIVE_TRAFFIC.perth
const ADELAIDE_TRAFFIC = LIVE_TRAFFIC.adelaide

/** Each fixture is a complete PascalCase Job object */
export const JOB_FIXTURES: JobFixture[] = [
    // ===========================================================================
    // 1. REQUESTED — 2GB / Sydney (just submitted)
    // ===========================================================================
    {
        JobId: 'arn-job-00001',
        Etag: 'mock-etag-arn-job-00001-v1',
        Station: '2GB 873 Sydney News/Talk',
        Market: 'Sydney',
        TargetDurationSec: 30,
        CreatedAt: t(-300),
        LastUpdatedAt: t(-295),
        Status: 'REQUESTED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T07:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '2GB 873',
                SignOff: 'This traffic report is brought to you by 2GB',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            {
                At: t(-300),
                Level: 0,
                Message: 'Job created. Bulletin type: TRAFFIC_ONLY',
            },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-300),
                By: 'system',
                Note: 'Job submitted for processing',
            },
        ],
    },

    // ===========================================================================
    // 2. HERE_FETCHED — 3AW / Melbourne (traffic data retrieved)
    // ===========================================================================
    {
        JobId: 'arn-job-00002',
        Etag: 'mock-etag-arn-job-00002-v1',
        Station: '3AW 693 Melbourne Current',
        Market: 'Melbourne',
        TargetDurationSec: 45,
        CreatedAt: t(-600),
        LastUpdatedAt: t(-590),
        Status: 'HERE_FETCHED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T07:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '3AW 693',
                SignOff: 'Traffic information supplied by 3AW Melbourne',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 0.95,
                    Stability: 0.6,
                    SimilarityBoost: 0.8,
                    Style: 0.1,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-600), Level: 0, Message: 'Job created' },
            {
                At: t(-595),
                Level: 0,
                Message:
                    'HERE traffic data fetched successfully. 12 incidents found.',
            },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-600),
                By: 'system',
                Note: 'Job submitted',
            },
            {
                Status: 'HERE_FETCHED',
                At: t(-595),
                By: 'system',
                Note: 'Traffic data retrieved from HERE API',
            },
        ],
        Traffic: {
            FetchedAt: t(-595),
            Bbox: LIVE_TRAFFIC.melbourne.Bbox,
            // Live HERE snapshot — corridors not built yet at this stage
            Incidents: LIVE_TRAFFIC.melbourne.Incidents,
            Corridors: [],
        },
    },

    // ===========================================================================
    // 3. INCIDENTS_SELECTED — 4BC / Brisbane
    // ===========================================================================
    {
        JobId: 'arn-job-00003',
        Etag: 'mock-etag-arn-job-00003-v1',
        Station: '4BC 882 Brisbane Talkback',
        Market: 'Brisbane',
        TargetDurationSec: 30,
        CreatedAt: t(-900),
        LastUpdatedAt: t(-880),
        Status: 'INCIDENTS_SELECTED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '4BC 882',
                SignOff: 'Brisbane traffic with 4BC',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.05,
                    Stability: 0.4,
                    SimilarityBoost: 0.7,
                    Style: 0.05,
                    UseSpeakerBoost: true,
                },
            },
        },
        Logs: [
            { At: t(-900), Level: 0, Message: 'Job created' },
            {
                At: t(-890),
                Level: 0,
                Message: 'HERE data fetched. 8 incidents.',
            },
            {
                At: t(-885),
                Level: 0,
                Message: '3 incidents selected for broadcast.',
            },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-900),
                By: 'system',
                Note: 'Job submitted',
            },
            {
                Status: 'HERE_FETCHED',
                At: t(-890),
                By: 'system',
                Note: 'Traffic data retrieved',
            },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-885),
                By: 'system',
                Note: 'Incidents filtered and ranked',
            },
        ],
        Traffic: {
            FetchedAt: t(-890),
            Bbox: LIVE_TRAFFIC.brisbane.Bbox,
            // Live HERE snapshot
            Incidents: LIVE_TRAFFIC.brisbane.Incidents,
            Corridors: LIVE_TRAFFIC.brisbane.Corridors,
        },
        SelectedIncidents: LIVE_TRAFFIC.brisbane.SelectedIncidents,
    },

    // ===========================================================================
    // 4. SCRIPT_READY — Gold FM / Gold Coast
    // ===========================================================================
    {
        JobId: 'arn-job-00004',
        Etag: 'mock-etag-arn-job-00004-v1',
        Station: 'Gold FM 90.1 Gold Coast',
        Market: 'Gold Coast',
        TargetDurationSec: 30,
        CreatedAt: t(-1200),
        LastUpdatedAt: t(-1170),
        Status: 'SCRIPT_READY',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: 'Gold FM 90.1',
                SignOff: 'Gold Coast traffic updates on Gold FM',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_monolingual_v1',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.55,
                    SimilarityBoost: 0.8,
                    Style: 0.05,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-1200), Level: 0, Message: 'Job created' },
            {
                At: t(-1190),
                Level: 0,
                Message: 'HERE data fetched. 5 incidents.',
            },
            { At: t(-1185), Level: 0, Message: '2 incidents selected.' },
            {
                At: t(-1175),
                Level: 0,
                Message:
                    'Script generated successfully. Estimated duration: 28 seconds.',
            },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-1200),
                By: 'system',
                Note: 'Job submitted',
            },
            {
                Status: 'HERE_FETCHED',
                At: t(-1190),
                By: 'system',
                Note: 'Traffic data retrieved',
            },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-1185),
                By: 'system',
                Note: 'Incidents selected',
            },
            {
                Status: 'SCRIPT_READY',
                At: t(-1175),
                By: 'ai',
                Note: 'Bulletin script written',
            },
        ],
        Traffic: {
            FetchedAt: t(-1190),
            Bbox: LIVE_TRAFFIC.goldcoast.Bbox,
            // Live HERE snapshot
            Incidents: LIVE_TRAFFIC.goldcoast.Incidents,
            Corridors: LIVE_TRAFFIC.goldcoast.Corridors,
        },
        SelectedIncidents: LIVE_TRAFFIC.goldcoast.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: GC_SCRIPT,
                EstimatedDurationSec: 28,
            },
        },
    },

    // ===========================================================================
    // 5. SYNTHESIZING_AUDIO — 6PR / Perth
    // ===========================================================================
    {
        JobId: 'arn-job-00005',
        Etag: 'mock-etag-arn-job-00005-v1',
        Station: '6PR 882 Perth Talk Radio',
        Market: 'Perth',
        TargetDurationSec: 30,
        CreatedAt: t(-1500),
        LastUpdatedAt: t(-1470),
        Status: 'SYNTHESIZING_AUDIO',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '6PR 882',
                SignOff: 'Perth traffic reports on 6PR',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-1500), Level: 0, Message: 'Job created' },
            { At: t(-1490), Level: 0, Message: 'HERE data fetched.' },
            { At: t(-1485), Level: 0, Message: 'Incidents selected.' },
            { At: t(-1475), Level: 0, Message: 'Script generated.' },
            {
                At: t(-1470),
                Level: 0,
                Message: 'Audio synthesis started via ElevenLabs.',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-1500), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-1490), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-1485),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-1475), By: 'ai', Note: '' },
            {
                Status: 'SYNTHESIZING_AUDIO',
                At: t(-1470),
                By: 'system',
                Note: 'Audio generation in progress',
            },
        ],
        Traffic: {
            FetchedAt: t(-1490),
            Bbox: PERTH_TRAFFIC.Bbox,
            Incidents: PERTH_TRAFFIC.Incidents,
            Corridors: PERTH_TRAFFIC.Corridors,
        },
        SelectedIncidents: PERTH_TRAFFIC.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: 'On the Mitchell Freeway southbound approaching Vincent Street, a breakdown is blocking the left lane. Traffic banked up past Glendalough. Expect 10 minute delays. On the Kwinana Freeway northbound at Canning Bridge, congestion due to roadworks. Speed down to 40 kilometres per hour. Allow extra travel time into the city.',
                EstimatedDurationSec: 25,
            },
        },
    },

    // ===========================================================================
    // 6. AUDIO_READY — 5AA / Adelaide
    // ===========================================================================
    {
        JobId: 'arn-job-00006',
        Etag: 'mock-etag-arn-job-00006-v1',
        Station: '5AA 1395 Adelaide Reports',
        Market: 'Adelaide',
        TargetDurationSec: 30,
        CreatedAt: t(-1800),
        LastUpdatedAt: t(-1770),
        Status: 'AUDIO_READY',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '5AA 1395',
                SignOff: 'Adelaide traffic with 5AA',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 0.98,
                    Stability: 0.5,
                    SimilarityBoost: 0.78,
                    Style: 0.02,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-1800), Level: 0, Message: 'Job created' },
            { At: t(-1790), Level: 0, Message: 'HERE data fetched.' },
            { At: t(-1785), Level: 0, Message: 'Incidents selected.' },
            { At: t(-1778), Level: 0, Message: 'Script written.' },
            {
                At: t(-1770),
                Level: 0,
                Message: 'Audio synthesis completed. Duration: 25s.',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-1800), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-1790), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-1785),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-1778), By: 'ai', Note: '' },
            {
                Status: 'SYNTHESIZING_AUDIO',
                At: t(-1772),
                By: 'system',
                Note: '',
            },
            {
                Status: 'AUDIO_READY',
                At: t(-1770),
                By: 'system',
                Note: 'Audio ready for review',
            },
        ],
        Traffic: {
            FetchedAt: t(-1790),
            Bbox: ADELAIDE_TRAFFIC.Bbox,
            Incidents: ADELAIDE_TRAFFIC.Incidents,
            Corridors: ADELAIDE_TRAFFIC.Corridors,
        },
        SelectedIncidents: ADELAIDE_TRAFFIC.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: SAMPLE.adelaide.script,
                EstimatedDurationSec: SAMPLE.adelaide.durationSec,
            },
        },
        Audio: {
            Data: {
                Format: 'mp3',
                SampleRateHz: 44100,
                DurationSec: SAMPLE.adelaide.durationSec,
                Url: SAMPLE.adelaide.audio,
            },
            MeasuredDurationSec: SAMPLE.adelaide.durationSec,
            DeltaSec: 2,
        },
        MeasuredDurationSec: SAMPLE.adelaide.durationSec,
        DeltaSec: 2,
    },

    // ===========================================================================
    // 7. WAITING_APPROVAL — 2GB / Sydney (~6 minutes remaining)
    // ===========================================================================
    {
        JobId: 'arn-job-00007',
        Etag: 'mock-etag-arn-job-00007-v1',
        Station: '2GB 873 Sydney News/Talk',
        Market: 'Sydney',
        TargetDurationSec: 30,
        CreatedAt: t(-240),
        LastUpdatedAt: t(-120),
        Status: 'WAITING_APPROVAL',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T07:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '2GB 873',
                SignOff: 'This traffic report is brought to you by 2GB',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-240), Level: 0, Message: 'Job created' },
            {
                At: t(-230),
                Level: 0,
                Message: 'Traffic data fetched: 15 incidents in Sydney region',
            },
            { At: t(-220), Level: 0, Message: '4 incidents selected' },
            {
                At: t(-200),
                Level: 0,
                Message: 'Script written: estimated duration 30s',
            },
            { At: t(-185), Level: 0, Message: 'Audio synthesized in 2.3s' },
            { At: t(-120), Level: 0, Message: 'Job moved to WAITING_APPROVAL' },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-240),
                By: 'system',
                Note: 'Bulletin requested',
            },
            {
                Status: 'HERE_FETCHED',
                At: t(-230),
                By: 'system',
                Note: 'Traffic data retrieved from HERE',
            },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-220),
                By: 'system',
                Note: 'Top incidents auto-selected',
            },
            {
                Status: 'SCRIPT_READY',
                At: t(-200),
                By: 'ai',
                Note: 'Script generated',
            },
            {
                Status: 'SYNTHESIZING_AUDIO',
                At: t(-185),
                By: 'system',
                Note: 'Audio generation',
            },
            {
                Status: 'AUDIO_READY',
                At: t(-180),
                By: 'system',
                Note: 'Audio complete',
            },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-120),
                By: 'system',
                Note: 'Awaiting reviewer decision',
            },
        ],
        Traffic: {
            FetchedAt: t(-230),
            Bbox: LIVE_TRAFFIC.sydney.Bbox,
            // Live HERE snapshot — no persisted Sentences on this job, so the
            // cockpit demonstrates the road-token heuristic fallback.
            Incidents: LIVE_TRAFFIC.sydney.Incidents,
            Corridors: LIVE_TRAFFIC.sydney.Corridors,
            // The recorded live-traffic corpus carries no MonitoredRoutes, so mock mode
            // could not render drive times at all and the card was only ever visible on a
            // deployed environment. Figures mirror a real dev reading of these two runs.
            MonitoredRoutes: [
                {
                    Name: 'the M4 from Parramatta to the city',
                    DriveTimeMinutes: 31,
                    NormalDriveTimeMinutes: 28,
                    DelayMinutes: 3,
                    DistanceKm: 25.4,
                },
                {
                    Name: 'the Warringah Freeway into the city',
                    DriveTimeMinutes: 16,
                    NormalDriveTimeMinutes: 15,
                    DelayMinutes: 1,
                    DistanceKm: 8.2,
                },
            ],
        },
        SelectedIncidents: LIVE_TRAFFIC.sydney.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: SAMPLE.sydney.script,
                EstimatedDurationSec: SAMPLE.sydney.durationSec,
                TaggedText: SYDNEY_TAGGED,
                DeliveryStyleId: 'ds-feb8cbe0566b4d06adb6d083db7625d6',
            },
        },
        Audio: {
            Data: {
                Format: 'mp3',
                SampleRateHz: 44100,
                DurationSec: SAMPLE.sydney.durationSec,
                Url: SAMPLE.sydney.audio,
            },
            MeasuredDurationSec: SAMPLE.sydney.durationSec,
            DeltaSec: 5,
        },
        MeasuredDurationSec: SAMPLE.sydney.durationSec,
        DeltaSec: 5,
        Content: { Traffic: true, Weather: true, Fuel: true },
        Weather: {
            Conditions: 'Passing clouds. Cool.',
            TemperatureC: 15,
            WindDescription: 'Light SE winds',
            PrecipitationProbabilityPercent: 20,
            Alerts: [],
        },
        Fuel: {
            Summaries: [
                {
                    FuelType: 'Unleaded 91',
                    MinPrice: 1.58,
                    AveragePrice: 1.64,
                    StationCount: 49,
                },
                {
                    FuelType: 'Diesel',
                    MinPrice: 1.95,
                    AveragePrice: 2.03,
                    StationCount: 18,
                },
            ],
        },
        GoodRuns: ['M1 Pacific Motorway', 'Smith Street Motorway'],
        // The Sydney station really does carry the Sydney Roads dictionary, and
        // its "Warringah" rule matches this script — so the mock corpus
        // exercises the in-script pronunciation mark (read mode AND the edit
        // mirror) instead of leaving that path uncovered.
        PronunciationDictionaryId: 'qjQzXB65OMLQj8Mm6LFr',
        PronunciationVersionId: 'pwiDUcEeuu5QccKfLiQR',
    },
    // ===========================================================================
    // 8. WAITING_APPROVAL — FULLY POPULATED: the job the reviewer opens for the
    //    detail view, and the one the Adjust surface is exercised against.
    //    Restored 2026-07-29: this fixture was dropped by the d9e61be refactor
    //    while three e2e specs still opened it, so /jobs/arn-job-00008 404'd and
    //    the cockpit rendered a schema error instead of tabs. Kept SEPARATE from
    //    00007 on purpose: the script-editor spec mutates the script, so sharing
    //    one job would couple that spec to the revise spec.
    // ===========================================================================
    {
        JobId: 'arn-job-00008',
        Etag: 'mock-etag-arn-job-00008-v1',
        Station: '3AW 693 Melbourne Current',
        Market: 'Melbourne',
        TargetDurationSec: 30,
        CreatedAt: t(-900),
        LastUpdatedAt: t(-600),
        Status: 'WAITING_APPROVAL',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T07:00:00Z',
                To: '2026-06-02T10:30:00Z',
            },
            Branding: {
                StationTag: '3AW 693',
                SignOff: 'This traffic report is brought to you by 3AW',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-240), Level: 0, Message: 'Job created' },
            {
                At: t(-230),
                Level: 0,
                Message: 'Traffic data fetched: 15 incidents in Sydney region',
            },
            { At: t(-220), Level: 0, Message: '4 incidents selected' },
            {
                At: t(-200),
                Level: 0,
                Message: 'Script written: estimated duration 30s',
            },
            { At: t(-185), Level: 0, Message: 'Audio synthesized in 2.3s' },
            { At: t(-120), Level: 0, Message: 'Job moved to WAITING_APPROVAL' },
        ],
        StatusHistory: [
            {
                Status: 'REQUESTED',
                At: t(-240),
                By: 'system',
                Note: 'Bulletin requested',
            },
            {
                Status: 'HERE_FETCHED',
                At: t(-230),
                By: 'system',
                Note: 'Traffic data retrieved from HERE',
            },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-220),
                By: 'system',
                Note: 'Top incidents auto-selected',
            },
            {
                Status: 'SCRIPT_READY',
                At: t(-200),
                By: 'ai',
                Note: 'Script generated',
            },
            {
                Status: 'SYNTHESIZING_AUDIO',
                At: t(-185),
                By: 'system',
                Note: 'Audio generation',
            },
            {
                Status: 'AUDIO_READY',
                At: t(-180),
                By: 'system',
                Note: 'Audio complete',
            },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-120),
                By: 'system',
                Note: 'Awaiting reviewer decision',
            },
        ],
        Traffic: {
            FetchedAt: t(-880),
            Bbox: LIVE_TRAFFIC.melbourne.Bbox,
            // Live HERE snapshot — no persisted Sentences on this job, so the
            // cockpit demonstrates the road-token heuristic fallback.
            Incidents: LIVE_TRAFFIC.melbourne.Incidents,
            Corridors: LIVE_TRAFFIC.melbourne.Corridors,
            // Flowing roads are scored in the SAME ranked rundown as incidents
            // (damped below them), so the Adjust surface needs them to render a
            // "Good run" row at all. Sourced from Traffic.FlowRoads, NOT the
            // top-level GoodRuns list — that one only names the station's
            // good-run candidates and drives no rows.
            FlowRoads: [
                {
                    RoadName: 'Eastern Freeway',
                    JamFactor: 0.6,
                    LengthKm: 12.4,
                    IsWeighted: true,
                    IsSelected: true,
                    Score: 118,
                    PriorityBoosted: true,
                    FunctionalClass: 2,
                },
                {
                    RoadName: 'CityLink',
                    JamFactor: 1.2,
                    LengthKm: 22.1,
                    IsWeighted: true,
                    IsSelected: true,
                    Score: 96.5,
                    PriorityBoosted: false,
                    FunctionalClass: 1,
                },
                {
                    RoadName: 'Nepean Highway',
                    JamFactor: 2.1,
                    LengthKm: 18.7,
                    IsWeighted: false,
                    IsSelected: false,
                    Score: 54,
                    PriorityBoosted: false,
                    FunctionalClass: 3,
                },
            ],
        },
        SelectedIncidents: LIVE_TRAFFIC.melbourne.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: SAMPLE.melbourne.script,
                EstimatedDurationSec: SAMPLE.melbourne.durationSec,
                TaggedText: MELBOURNE_TAGGED,
                DeliveryStyleId: 'ds-fedf1248af0f4595a9eb7f4dae573a29',
            },
        },
        Audio: {
            Data: {
                Format: 'mp3',
                SampleRateHz: 44100,
                DurationSec: SAMPLE.melbourne.durationSec,
                Url: SAMPLE.melbourne.audio,
            },
            MeasuredDurationSec: SAMPLE.melbourne.durationSec,
            DeltaSec: 5,
        },
        MeasuredDurationSec: SAMPLE.melbourne.durationSec,
        DeltaSec: 5,
        Content: { Traffic: true, Weather: true, Fuel: true },
        Weather: {
            Conditions: 'Passing clouds. Cool.',
            TemperatureC: 15,
            WindDescription: 'Light SE winds',
            PrecipitationProbabilityPercent: 20,
            Alerts: [],
        },
        Fuel: {
            Summaries: [
                {
                    FuelType: 'Unleaded 91',
                    MinPrice: 1.58,
                    AveragePrice: 1.64,
                    StationCount: 49,
                },
                {
                    FuelType: 'Diesel',
                    MinPrice: 1.95,
                    AveragePrice: 2.03,
                    StationCount: 18,
                },
            ],
        },
        GoodRuns: ['Eastern Freeway', 'CityLink'],
    },

    // ===========================================================================
    // 8. WAITING_APPROVAL — real KIIS Adelaide dev bulletin
    //    Captured from dev job 287d14a0b2484f10938b5f62b755d2cb.
    // ===========================================================================
    adelaideDevReviewJob(),

    // ===========================================================================
    // 9. APPROVED — 4BC / Brisbane
    // ===========================================================================
    {
        JobId: 'arn-job-00009',
        Etag: 'mock-etag-arn-job-00009-v1',
        Station: '4BC 882 Brisbane Talkback',
        Market: 'Brisbane',
        TargetDurationSec: 30,
        CreatedAt: t(-2400),
        LastUpdatedAt: t(-2100),
        Status: 'APPROVED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T09:30:00Z',
            },
            Branding: {
                StationTag: '4BC 882',
                SignOff: 'Brisbane traffic with 4BC',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.05,
                    Stability: 0.4,
                    SimilarityBoost: 0.7,
                    Style: 0.05,
                    UseSpeakerBoost: true,
                },
            },
        },
        Logs: [
            { At: t(-2400), Level: 0, Message: 'Job created' },
            { At: t(-2390), Level: 0, Message: 'Traffic data fetched' },
            { At: t(-2380), Level: 0, Message: 'Script generated' },
            { At: t(-2370), Level: 0, Message: 'Audio ready' },
            { At: t(-2360), Level: 0, Message: 'Moved to WAITING_APPROVAL' },
            {
                At: t(-2100),
                Level: 0,
                Message: 'Approved by reviewer Operator.',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-2400), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-2390), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-2385),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-2380), By: 'ai', Note: '' },
            { Status: 'AUDIO_READY', At: t(-2370), By: 'system', Note: '' },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-2360),
                By: 'system',
                Note: 'Awaiting decision',
            },
            {
                Status: 'APPROVED',
                At: t(-2100),
                By: 'operator@arn.com.au',
                Note: 'Approved for playout: good content and pacing',
            },
        ],
        // APPROVED implies the full pipeline ran — the schema enforces the
        // per-status artifact invariants, so the fixture carries traffic.
        Traffic: {
            FetchedAt: t(-2390),
            Bbox: LIVE_TRAFFIC.brisbane.Bbox,
            Incidents: LIVE_TRAFFIC.brisbane.Incidents,
            Corridors: LIVE_TRAFFIC.brisbane.Corridors,
        },
        SelectedIncidents: LIVE_TRAFFIC.brisbane.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: SAMPLE.brisbane.script,
                EstimatedDurationSec: SAMPLE.brisbane.durationSec,
            },
        },
        Audio: {
            Data: {
                Format: 'mp3',
                SampleRateHz: 44100,
                DurationSec: SAMPLE.brisbane.durationSec,
                Url: SAMPLE.brisbane.audio,
            },
            MeasuredDurationSec: SAMPLE.brisbane.durationSec,
            DeltaSec: 10,
        },
        Approval: {
            DecidedAt: t(-2100),
            DecidedBy: 'operator@arn.com.au',
        },
        MeasuredDurationSec: SAMPLE.brisbane.durationSec,
        DeltaSec: 10,
    },

    // ===========================================================================
    // 10. REJECTED — Gold FM / Gold Coast (with rejection history)
    // ===========================================================================
    {
        JobId: 'arn-job-00010',
        Etag: 'mock-etag-arn-job-00010-v1',
        Station: 'Gold FM 90.1 Gold Coast',
        Market: 'Gold Coast',
        TargetDurationSec: 30,
        CreatedAt: t(-3600),
        LastUpdatedAt: t(-3300),
        Status: 'REJECTED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T09:00:00Z',
            },
            Branding: {
                StationTag: 'Gold FM 90.1',
                SignOff: 'Gold Coast traffic updates on Gold FM',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_monolingual_v1',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.55,
                    SimilarityBoost: 0.8,
                    Style: 0.05,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-3600), Level: 0, Message: 'Job created' },
            { At: t(-3580), Level: 0, Message: 'Script and audio generated' },
            { At: t(-3500), Level: 0, Message: 'Moved to WAITING_APPROVAL' },
            {
                At: t(-3320),
                Level: 1,
                Message:
                    'Job REJECTED: reason: Story Bridge incident not relevant for Gold Coast audience',
            },
            {
                At: t(-3300),
                Level: 0,
                Message: 'Fallback not used: editor rejected outright',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-3600), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-3595), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-3590),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-3585), By: 'ai', Note: '' },
            { Status: 'AUDIO_READY', At: t(-3580), By: 'system', Note: '' },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-3500),
                By: 'system',
                Note: '',
            },
            {
                Status: 'REJECTED',
                At: t(-3320),
                By: 'reviewer@arn.com.au',
                Note: 'Rejected: incidents not relevant to Gold Coast audience',
            },
        ],
        Traffic: {
            FetchedAt: t(-3590),
            Bbox: LIVE_TRAFFIC.goldcoast.Bbox,
            Incidents: LIVE_TRAFFIC.goldcoast.Incidents,
            Corridors: LIVE_TRAFFIC.goldcoast.Corridors,
        },
        SelectedIncidents: LIVE_TRAFFIC.goldcoast.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: 'Gold FM 90.1 Traffic for Tuesday morning. On the M1 at Nerang, congestion northbound past the Broadbeach exit...',
                EstimatedDurationSec: 28,
            },
        },
        Audio: {
            Data: {
                Format: 'wav',
                SampleRateHz: 8000,
                DurationSec: 25,
                Url: AUDIO_URI,
            },
            MeasuredDurationSec: 25,
            DeltaSec: -5,
        },
        Rejection: {
            Reason: 'Content not relevant: included Brisbane incidents not affecting Gold Coast listeners',
            DecidedAt: t(-3320),
            DecidedBy: 'reviewer@arn.com.au',
        },
        RejectionHistory: [
            {
                Reason: 'Content relevance',
                Notes: 'The script included a Story Bridge incident not relevant to Gold Coast audience. Editor requested city-specific filtering.',
                RejectedScript:
                    'Gold FM 90.1 Traffic for Tuesday morning. On the M1 at Nerang, congestion northbound... And on the Story Bridge in Brisbane, a breakdown is blocking one lane.',
                At: t(-3320),
                By: 'reviewer@arn.com.au',
            },
        ],
        MeasuredDurationSec: 25,
        DeltaSec: -5,
    },

    // ===========================================================================
    // 10a. MISSED — 5AA / Adelaide (slot passed unapproved; nothing aired)
    // ===========================================================================
    {
        JobId: 'arn-job-00010a',
        Etag: 'mock-etag-arn-job-00010a-v1',
        Station: '5AA 1395 Adelaide Reports',
        Market: 'Adelaide',
        TargetDurationSec: 30,
        CreatedAt: t(-5400),
        LastUpdatedAt: t(-4980),
        Status: 'MISSED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T09:00:00Z',
            },
            Branding: {
                StationTag: '5AA 1395',
                SignOff: 'Adelaide traffic on 5AA',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_monolingual_v1',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.55,
                    SimilarityBoost: 0.8,
                    Style: 0.05,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-5400), Level: 0, Message: 'Job created' },
            { At: t(-5380), Level: 0, Message: 'Script and audio generated' },
            { At: t(-5340), Level: 0, Message: 'Moved to WAITING_APPROVAL' },
            {
                At: t(-4980),
                Level: 1,
                Message:
                    'Missed the playout deadline - the slot has passed and this bulletin was never approved.',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-5400), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-5395), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-5390),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-5385), By: 'ai', Note: '' },
            { Status: 'AUDIO_READY', At: t(-5380), By: 'system', Note: '' },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-5340),
                By: 'system',
                Note: '',
            },
            {
                Status: 'MISSED',
                At: t(-4980),
                By: 'system',
                Note: 'Missed the playout deadline',
            },
        ],
        Traffic: {
            FetchedAt: t(-5390),
            Bbox: LIVE_TRAFFIC.goldcoast.Bbox,
            Incidents: LIVE_TRAFFIC.goldcoast.Incidents,
            Corridors: LIVE_TRAFFIC.goldcoast.Corridors,
        },
        SelectedIncidents: LIVE_TRAFFIC.goldcoast.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: 'Adelaide traffic on 5AA. Delays easing on South Road through Torrensville, and the Southern Expressway is clear both ways.',
                EstimatedDurationSec: 28,
            },
        },
        Audio: {
            Data: {
                Format: 'wav',
                SampleRateHz: 8000,
                DurationSec: 29,
                Url: AUDIO_URI,
            },
            MeasuredDurationSec: 29,
            DeltaSec: -1,
        },
        Missed: {
            DeadlineAt: t(-5580),
            At: t(-4980),
        },
        MeasuredDurationSec: 29,
        DeltaSec: -1,
    },

    // ===========================================================================
    // 11. PUBLISHED — 6PR / Perth
    // ===========================================================================
    {
        JobId: 'arn-job-00011',
        Etag: 'mock-etag-arn-job-00011-v1',
        Station: '6PR 882 Perth Talk Radio',
        Market: 'Perth',
        TargetDurationSec: 30,
        CreatedAt: t(-5400),
        LastUpdatedAt: t(-4900),
        Status: 'PUBLISHED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T05:00:00Z',
                To: '2026-06-02T08:30:00Z',
            },
            Branding: {
                StationTag: '6PR 882',
                SignOff: 'Perth traffic reports on 6PR',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'PA1XYeK584cPbgobwKAc',
                VoiceName: 'ARN Marlowe',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            {
                At: t(-5400),
                Level: 0,
                Message: 'Job created for 8:30 AM playout',
            },
            {
                At: t(-5380),
                Level: 0,
                Message: 'Traffic data fetched for Perth metro',
            },
            { At: t(-5360), Level: 0, Message: 'Script and audio generated' },
            { At: t(-5350), Level: 0, Message: 'Approved by editor' },
            {
                At: t(-4900),
                Level: 0,
                Message: 'Bulletin published to playout system',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-5400), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-5390), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-5385),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-5375), By: 'ai', Note: '' },
            { Status: 'AUDIO_READY', At: t(-5365), By: 'system', Note: '' },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-5360),
                By: 'system',
                Note: '',
            },
            {
                Status: 'APPROVED',
                At: t(-5350),
                By: 'publisher@arn.com.au',
                Note: 'Approved',
            },
            {
                Status: 'PUBLISHED',
                At: t(-4900),
                By: 'system',
                Note: 'Published to playout',
            },
        ],
        Traffic: {
            FetchedAt: t(-5390),
            Bbox: PERTH_TRAFFIC.Bbox,
            Incidents: PERTH_TRAFFIC.Incidents,
            Corridors: PERTH_TRAFFIC.Corridors,
        },
        SelectedIncidents: PERTH_TRAFFIC.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: SAMPLE.perth.script,
                EstimatedDurationSec: SAMPLE.perth.durationSec,
            },
        },
        Audio: {
            Data: {
                Format: 'mp3',
                SampleRateHz: 44100,
                DurationSec: SAMPLE.perth.durationSec,
                Url: SAMPLE.perth.audio,
            },
            MeasuredDurationSec: SAMPLE.perth.durationSec,
            DeltaSec: 2,
        },
        Approval: { DecidedAt: t(-5350), DecidedBy: 'publisher@arn.com.au' },
        Publish: {
            Destination: 'Zetta Playout System',
            PublishedAt: t(-4900),
            PublishedBy: 'publisher@arn.com.au',
        },
        MeasuredDurationSec: SAMPLE.perth.durationSec,
        DeltaSec: 2,
    },

    // ===========================================================================
    // 12. FALLBACK_USED — 5AA / Adelaide
    // ===========================================================================
    {
        JobId: 'arn-job-00012',
        Etag: 'mock-etag-arn-job-00012-v1',
        Station: '5AA 1395 Adelaide Reports',
        Market: 'Adelaide',
        TargetDurationSec: 30,
        CreatedAt: t(-4800),
        LastUpdatedAt: t(-4200),
        Status: 'FALLBACK_USED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T06:00:00Z',
                To: '2026-06-02T09:00:00Z',
            },
            Branding: {
                StationTag: '5AA 1395',
                SignOff: 'Adelaide traffic with 5AA',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 0.98,
                    Stability: 0.5,
                    SimilarityBoost: 0.78,
                    Style: 0.02,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-4800), Level: 0, Message: 'Job created' },
            { At: t(-4780), Level: 0, Message: 'Script and audio ready' },
            { At: t(-4770), Level: 0, Message: 'Pending approval' },
            {
                At: t(-4500),
                Level: 1,
                Message: 'Rejected: script too long for timeslot',
            },
            {
                At: t(-4200),
                Level: 1,
                Message:
                    'Fallback bulletin from job arn-job-00009 used instead',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-4800), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-4790), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-4785),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-4778), By: 'ai', Note: '' },
            { Status: 'AUDIO_READY', At: t(-4770), By: 'system', Note: '' },
            {
                Status: 'WAITING_APPROVAL',
                At: t(-4765),
                By: 'system',
                Note: '',
            },
            {
                Status: 'REJECTED',
                At: t(-4500),
                By: 'producer@arn.com.au',
                Note: 'Script 45s exceeds 30s window',
            },
            {
                Status: 'FALLBACK_USED',
                At: t(-4200),
                By: 'system',
                Note: 'Fallback from job arn-job-00009 applied',
            },
        ],
        Traffic: {
            FetchedAt: t(-4790),
            Bbox: ADELAIDE_TRAFFIC.Bbox,
            Incidents: ADELAIDE_TRAFFIC.Incidents,
            Corridors: ADELAIDE_TRAFFIC.Corridors,
        },
        SelectedIncidents: ADELAIDE_TRAFFIC.SelectedIncidents,
        Script: {
            Data: {
                Version: 1,
                Text: 'Good morning from 5AA Traffic... A lengthy bulletin that was rejected...',
                EstimatedDurationSec: 45,
            },
        },
        Audio: {
            Data: {
                Format: 'wav',
                SampleRateHz: 8000,
                DurationSec: 25,
                Url: AUDIO_URI,
            },
            MeasuredDurationSec: 25,
            DeltaSec: -5,
        },
        Rejection: {
            Reason: 'Script duration 45s exceeds 30s slot',
            DecidedAt: t(-4500),
            DecidedBy: 'producer@arn.com.au',
        },
        RejectionHistory: [
            {
                Reason: 'Duration exceeded',
                Notes: 'The generated script was 45 seconds but the timeslot is only 30 seconds. Editor rejected and requested a shorter version or fallback.',
                RejectedScript:
                    'Good morning from 5AA Traffic... A lengthy bulletin that was rejected...',
                At: t(-4500),
                By: 'producer@arn.com.au',
            },
        ],
        Fallback: {
            SourceJobId: 'arn-job-00009',
            AppliedAt: t(-4200),
            Reason: 'content-limited',
            Streak: 1,
        },
        // The bulletin this slot generated before the standby cover replaced it. The
        // mock MUST carry it: the deployed API always sends the key, and a mock that
        // omits real wire fields is exactly how the log-level off-by-one hid for weeks.
        SupersededTake: {
            CapturedAt: t(-4200),
            Reason: 'content-limited',
            Text: 'Good morning from 5AA Traffic. Heavy going on South Road citybound with a crash near Anzac Highway, and the Southern Expressway is slow from Darlington.',
            TaggedText: null,
            AudioUrl: AUDIO_URI,
            MeasuredDurationSec: 45,
            EstimatedDurationSec: 44.2,
            Sentences: [
                {
                    Text: 'Good morning from 5AA Traffic.',
                    CorridorKeys: [],
                    RouteKeys: [],
                },
                {
                    Text: 'Heavy going on South Road citybound with a crash near Anzac Highway, and the Southern Expressway is slow from Darlington.',
                    CorridorKeys: ['South Road'],
                    RouteKeys: [],
                },
            ],
            WordTimings: null,
            PreReview: null,
            DeliveryStyleId: null,
            DeliveryStyleVersion: null,
        },
        MeasuredDurationSec: 25,
        DeltaSec: -5,
    },

    // ===========================================================================
    // 13. FAILED — 2GB / Sydney (with detailed failure data)
    // ===========================================================================
    {
        JobId: 'arn-job-00013',
        Etag: 'mock-etag-arn-job-00013-v1',
        Station: '2GB 873 Sydney News/Talk',
        Market: 'Sydney',
        TargetDurationSec: 30,
        CreatedAt: t(-7200),
        LastUpdatedAt: t(-7000),
        Status: 'FAILED',
        Input: {
            BulletinType: 'TRAFFIC_ONLY',
            TimeWindow: {
                From: '2026-06-02T04:00:00Z',
                To: '2026-06-02T08:00:00Z',
            },
            Branding: {
                StationTag: '2GB 873',
                SignOff: 'This traffic report is brought to you by 2GB',
            },
            Voice: {
                Provider: 'elevenlabs',
                VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                VoiceName: 'ARN Hudson',
                Model: 'eleven_multilingual_v2',
                Settings: {
                    Speed: 1.0,
                    Stability: 0.5,
                    SimilarityBoost: 0.75,
                    Style: 0.0,
                    UseSpeakerBoost: false,
                },
            },
        },
        Logs: [
            { At: t(-7200), Level: 0, Message: 'Job created' },
            { At: t(-7190), Level: 0, Message: 'Traffic data fetched' },
            { At: t(-7180), Level: 0, Message: 'Script generated' },
            { At: t(-7160), Level: 0, Message: 'Audio synthesis started' },
            {
                At: t(-7150),
                Level: 2,
                Message:
                    'Audio synthesis failed after 3 attempts: ElevenLabs API returned 503',
            },
            {
                At: t(-7000),
                Level: 2,
                Message: 'Job marked as FAILED after exhausting retries',
            },
        ],
        StatusHistory: [
            { Status: 'REQUESTED', At: t(-7200), By: 'system', Note: '' },
            { Status: 'HERE_FETCHED', At: t(-7190), By: 'system', Note: '' },
            {
                Status: 'INCIDENTS_SELECTED',
                At: t(-7185),
                By: 'system',
                Note: '',
            },
            { Status: 'SCRIPT_READY', At: t(-7180), By: 'ai', Note: '' },
            {
                Status: 'SYNTHESIZING_AUDIO',
                At: t(-7160),
                By: 'system',
                Note: 'Attempt 1 of 3',
            },
            {
                Status: 'FAILED',
                At: t(-7000),
                By: 'system',
                Note: 'Failed after 3 synthesis attempts',
            },
        ],
        Script: {
            Data: {
                Version: 1,
                Text: 'Script generated but audio synthesis failed.',
                EstimatedDurationSec: 28,
            },
        },
        Failure: {
            Stage: 'SYNTHESIZING_AUDIO',
            Message:
                'ElevenLabs API returned HTTP 503: service temporarily unavailable after 3 retry attempts',
            OccurredAt: t(-7000),
            AttemptCount: 3,
            LastEstimatedDurationSec: null,
            TargetDurationSec: 30,
            AttemptHistory: [
                {
                    AttemptNumber: 1,
                    EstimatedDurationSec: null,
                    ActualDurationSec: null,
                    Succeeded: false,
                    FailureReason: 'HTTP 503 Service Unavailable',
                    At: t(-7160),
                },
                {
                    AttemptNumber: 2,
                    EstimatedDurationSec: null,
                    ActualDurationSec: null,
                    Succeeded: false,
                    FailureReason: 'HTTP 503 Service Unavailable',
                    At: t(-7120),
                },
                {
                    AttemptNumber: 3,
                    EstimatedDurationSec: null,
                    ActualDurationSec: null,
                    Succeeded: false,
                    FailureReason: 'HTTP 503 Service Unavailable',
                    At: t(-7080),
                },
            ],
        },
    },

    // ===========================================================================
    // 10. APPROVED — real KIIS 101.1 Melbourne dev bulletin, read-only Adjust.
    //     Captured from dev job f034af33f0ba4f6288f52e22bce8bb27.
    // ===========================================================================
    melbourneDevReviewJob(),
]
// Source-diagnostics fixtures exercise the operator-facing degraded-data surfaces.
// The Cairns case mirrors the live incident: QLDTraffic is unavailable while HERE
// remains healthy, so the bulletin still exists but its corroboration is thinner.
const allSourcesOk: NonNullable<JobFixture['SourceDiagnostics']> = [
    {
        SourceId: 'here',
        Outcome: 'Ok',
        Detail: null,
        DataAsOfUtc: null,
        StaleSeconds: null,
    },
    {
        SourceId: 'here-weather',
        Outcome: 'Ok',
        Detail: null,
        DataAsOfUtc: null,
        StaleSeconds: null,
    },
    {
        SourceId: 'here-fuel',
        Outcome: 'Ok',
        Detail: null,
        DataAsOfUtc: null,
        StaleSeconds: null,
    },
    {
        SourceId: 'qldtraffic',
        Outcome: 'Ok',
        Detail: null,
        DataAsOfUtc: null,
        StaleSeconds: null,
    },
]

const sourceDiagnosticsSeed = JOB_FIXTURES.find(
    (job) => job.JobId === 'arn-job-00007'
)
if (sourceDiagnosticsSeed) {
    sourceDiagnosticsSeed.SourceDiagnostics = allSourcesOk
}

const staleSourceJob = JOB_FIXTURES.find((job) => job.JobId === 'arn-job-00008')
if (staleSourceJob) {
    staleSourceJob.SourceDiagnostics = [
        ...allSourcesOk.filter((source) => source.SourceId === 'here'),
        {
            SourceId: 'qldtraffic',
            Outcome: 'ServedStale',
            Detail: 'HTTP 429 (rate limited)',
            DataAsOfUtc: new Date(Date.now() - 7 * 60_000).toISOString(),
            StaleSeconds: 420,
        },
        {
            SourceId: 'victraffic',
            Outcome: 'Skipped',
            Detail: null,
            DataAsOfUtc: null,
            StaleSeconds: null,
        },
    ]
}

const nearCeilingSourceJob = JOB_FIXTURES.find(
    (job) => job.JobId === 'arn-job-00009'
)
if (nearCeilingSourceJob) {
    nearCeilingSourceJob.SourceDiagnostics = [
        {
            SourceId: 'here',
            Outcome: 'Ok',
            Detail: null,
            DataAsOfUtc: null,
            StaleSeconds: null,
        },
        {
            SourceId: 'mainroads-wa',
            Outcome: 'ServedStale',
            Detail: 'HTTP 503 (upstream unavailable)',
            DataAsOfUtc: new Date(Date.now() - 47 * 60_000).toISOString(),
            StaleSeconds: 2820,
        },
    ]
}

const failedSourceJob = JOB_FIXTURES.find(
    (job) => job.JobId === 'arn-job-00010'
)
if (failedSourceJob) {
    failedSourceJob.SourceDiagnostics = [
        {
            SourceId: 'here',
            Outcome: 'Ok',
            Detail: null,
            DataAsOfUtc: null,
            StaleSeconds: null,
        },
        {
            SourceId: 'qldtraffic',
            Outcome: 'Failed',
            Detail: 'HTTP 429 (rate limited)',
            DataAsOfUtc: null,
            StaleSeconds: null,
        },
        {
            SourceId: 'tasalert',
            Outcome: 'Skipped',
            Detail: null,
            DataAsOfUtc: null,
            StaleSeconds: null,
        },
    ]
}

const cairnsSeed = JOB_FIXTURES.find((job) => job.JobId === 'arn-job-00007')
if (cairnsSeed) {
    JOB_FIXTURES.push({
        ...cairnsSeed,
        JobId: 'arn-job-cairns-degraded',
        Etag: 'mock-etag-arn-job-cairns-degraded-v1',
        Station: 'Star 102.7 FM',
        Market: 'Cairns',
        Input: {
            ...cairnsSeed.Input,
            Branding: {
                ...cairnsSeed.Input.Branding,
                StationTag: 'Star 102.7',
                SignOff: 'Cairns traffic with Star 102.7',
            },
        },
        SourceDiagnostics: [
            {
                SourceId: 'here',
                Outcome: 'Ok',
                Detail: null,
                DataAsOfUtc: null,
                StaleSeconds: null,
            },
            {
                SourceId: 'here-weather',
                Outcome: 'Ok',
                Detail: null,
                DataAsOfUtc: null,
                StaleSeconds: null,
            },
            {
                SourceId: 'here-fuel',
                Outcome: 'Ok',
                Detail: null,
                DataAsOfUtc: null,
                StaleSeconds: null,
            },
            {
                SourceId: 'qldtraffic',
                Outcome: 'Failed',
                Detail: 'HTTP 429 (rate limited)',
                DataAsOfUtc: null,
                StaleSeconds: null,
            },
            {
                SourceId: 'victraffic',
                Outcome: 'Skipped',
                Detail: null,
                DataAsOfUtc: null,
                StaleSeconds: null,
            },
        ],
    })
}

// A dedicated pre-draft job keeps the curation workflow reachable in mock mode.
// It is cloned from a real traffic-shaped fixture so receipts and origin badges
// exercise the same fields as the deployed DTO, while its deadline stays live
// for four minutes after the mock app starts.
const curationSeed = JOB_FIXTURES[2]
if (curationSeed) {
    JOB_FIXTURES.push({
        ...curationSeed,
        JobId: 'arn-job-curation-demo',
        Etag: 'mock-etag-arn-job-curation-demo-v1',
        Station: '4BC 882 Curation Desk',
        CreatedAt: new Date(Date.now() - 60_000).toISOString(),
        LastUpdatedAt: new Date().toISOString(),
        CurationDeadlineUtc: new Date(Date.now() + 4 * 60_000).toISOString(),
        Status: 'AWAITING_CURATION',
        Traffic: curationSeed.Traffic
            ? {
                  ...curationSeed.Traffic,
                  Incidents: curationSeed.Traffic.Incidents.map(
                      (incident, index) =>
                          index === 0
                              ? {
                                    ...incident,
                                    LaneDetail:
                                        'Reduced to a single lane in each direction',
                                    Comment: 'Roadworks crew active',
                                }
                              : incident
                  ),
                  Corridors: (curationSeed.Traffic.Corridors ?? []).map(
                      (corridor, index) =>
                          index === 0
                              ? {
                                    ...corridor,
                                    Sources: ['here', 'livetraffic-nsw'],
                                    CorroborationCount: 2,
                                    OfficialEvidences: [
                                        {
                                            SourceId: 'here',
                                            Headline: corridor.Description,
                                            IsIncidentClass: true,
                                            Description: corridor.Description,
                                            MatchBasis: 'official-only',
                                        },
                                        {
                                            SourceId: 'livetraffic-nsw',
                                            Headline:
                                                'Roadworks reported on Bruce Highway',
                                            IsIncidentClass: true,
                                            Description:
                                                'Lane reduction in both directions near the interchange.',
                                            MatchBasis: 'distance-212m',
                                        },
                                    ],
                                }
                              : corridor
                  ),
              }
            : curationSeed.Traffic,
        StatusHistory: [
            ...curationSeed.StatusHistory,
            {
                Status: 'AWAITING_CURATION',
                At: new Date().toISOString(),
                By: 'system',
                Note: 'Traffic fetched; curation window opened',
            },
        ],
    })
}
// ---------------------------------------------------------------------------
// Air-plan demo shaping — opt in with VITE_AIR_PLAN_DEMO=true
// ---------------------------------------------------------------------------
// The "Set to publish" table forecasts what reaches playout for each station's
// NEXT slot. Exercising it locally needs two things the dev-shaped fixtures do
// not give: a slot that is always minutes away (the exported crons fire only
// 06-09 and 15-18 on weekdays, so most of the day every row reads "no slot
// left"), and one bulletin published moments ago so a genuine "will air" row
// exists.
//
// It is behind a flag because the default mock config is contract-tested to be
// the dev snapshot byte for byte (fixtures.test.ts, "keeps every configuration
// domain sourced from the dev snapshot"). That contract is worth more than the
// convenience of always-on demo data, so the experiment opts in:
//
//   VITE_USE_MOCKS=true VITE_AIR_PLAN_DEMO=true npm run dev  (npm run dev:airplan)
// Plain member access, NOT optional chaining: Vite only static-replaces the
// exact `import.meta.env.VITE_*` form, and `?.` left the expression untouched —
// the flag read as false with the variable correctly set.
export const AIR_PLAN_DEMO = import.meta.env.VITE_AIR_PLAN_DEMO === 'true'

/**
 * The demo markets, as one definition: each station's own clock offset, its
 * voice, and which bulletin sits on which of its slots.
 *
 * Two earlier attempts got this wrong in ways worth not repeating. Moving the
 * CRONS while leaving the shipped fixture timestamps alone meant no bulletin
 * matched any slot, so every lane grew a pair of unattached dots parked on the
 * now-beam (that is what surfaced the matcher defect in `slots.ts`). Then giving
 * every station the SAME quarter-hour cron made all six lanes identical, down to
 * the same countdown — real clocks are staggered (ARN's own run at :24, :29,
 * :39, :54), so the offsets here are too.
 *
 * Bulletins are created exactly `ScheduleLeadTimeMinutes` (15) before their own
 * slot, which is what the poller does, so each one lands inside its own window.
 */
const DEMO_QUARTER_MS = 15 * 60 * 1000
const DEMO_LEAD_MS = 15 * 60 * 1000

const AIR_PLAN_DEMO_MARKETS: readonly {
    readonly marketId: string
    readonly name: string
    readonly station: string
    readonly timezone: string
    readonly voiceProfileId: string | null
    /** Minutes past the quarter-hour this station's clock fires. */
    readonly offsetMin: number
    /** `at` is the slot index: 0 is the next slot, -1 the one before it.
     *  `from` clones an existing fixture so a past slot can carry a finished
     *  bulletin without hand-authoring another one. */
    readonly jobs: readonly {
        readonly jobId: string
        readonly from?: string
        readonly at: number
    }[]
}[] = [
    {
        // Waiting review for the next slot, so nothing is set to publish; the
        // slot behind it failed outright.
        marketId: 'sydney',
        name: 'Sydney',
        station: 'KIIS 90s',
        timezone: 'Australia/Sydney',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 0,
        jobs: [
            { jobId: 'arn-job-00007', at: 0 },
            { jobId: 'arn-job-00013', at: -1 },
            // The poller is always a slot ahead: this one is just requested for
            // the slot AFTER next. Left unplaced it kept winning slot 0 on
            // creation time and hid the waiting bulletin behind it.
            { jobId: 'arn-job-00001', at: 1 },
        ],
    },
    {
        // Still generating ahead; the slot behind went unreviewed, which is why
        // the queue rail is shouting about 3AW.
        marketId: 'melbourne',
        name: 'Melbourne',
        station: 'KIIS 101.1',
        timezone: 'Australia/Melbourne',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 4,
        jobs: [
            { jobId: 'arn-job-00002', at: 0 },
            { jobId: 'arn-job-00008', at: -1 },
        ],
    },
    {
        // An APPROVED bulletin on the slot behind that nobody published — a slot
        // that aired silence — so the next slot would repeat it.
        marketId: 'brisbane',
        name: 'Brisbane',
        station: 'KIIS 97.3',
        timezone: 'Australia/Brisbane',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 7,
        jobs: [
            { jobId: 'arn-job-00003', at: 0 },
            { jobId: 'arn-job-00009', at: -1 },
        ],
    },
    {
        // Nothing from today to repeat, so the standby cart covers it. This
        // station used to carry no voice profile in order to force a dead-air
        // row; that state needs BOTH cart layers missing, which no deployed
        // environment has, so demonstrating it would have been a lie.
        marketId: 'goldcoast',
        name: 'Gold Coast',
        station: 'Hot Tomato 102.9',
        timezone: 'Australia/Brisbane',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 11,
        jobs: [
            { jobId: 'arn-job-00004', at: 0 },
            { jobId: 'arn-job-00010', at: -1 },
        ],
    },
    {
        // The healthy lane: published for the next slot, two aired behind it.
        marketId: 'perth',
        name: 'Perth',
        station: 'Gold 96FM',
        timezone: 'Australia/Perth',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 2,
        jobs: [
            { jobId: 'arn-job-00011', at: 0 },
            { jobId: 'arn-job-00091', from: 'arn-job-00011', at: -1 },
            { jobId: 'arn-job-00092', from: 'arn-job-00011', at: -2 },
        ],
    },
    {
        // Audio ready but unreviewed ahead; the standby cart covered the slot
        // behind it, and that cart take is what would repeat.
        marketId: 'adelaide',
        name: 'Adelaide',
        station: 'KIIS 102.3',
        timezone: 'Australia/Adelaide',
        voiceProfileId: 'vp-4bb23218a88e4b17a5032a568313322d',
        offsetMin: 9,
        jobs: [
            { jobId: 'arn-job-00006', at: 0 },
            { jobId: 'arn-job-00012', at: -1 },
        ],
    },
]

/** This station's slot `index` steps from the next one on its own offset clock. */
function demoSlotAt(offsetMin: number, index: number): Date {
    const offsetMs = offsetMin * 60_000
    const next =
        Math.ceil((Date.now() - offsetMs) / DEMO_QUARTER_MS) * DEMO_QUARTER_MS +
        offsetMs
    return new Date(next + index * DEMO_QUARTER_MS)
}

/**
 * The shipped job fixtures carry invented callsigns (`2GB 873 Sydney News/Talk`,
 * `6PR 882 Perth Talk Radio`). ARN runs none of those, so the board read as a
 * different network entirely — the first thing anyone asked about it was "what
 * are these names?". Each one maps onto the real dev station for the SAME city,
 * so the market, coverage and script content all stay coherent.
 */
const DEMO_STATION_RENAMES: Readonly<Record<string, string>> = {
    '2GB 873 Sydney News/Talk': 'KIIS 90s',
    '3AW 693 Melbourne Current': 'KIIS 101.1',
    '4BC 882 Brisbane Talkback': 'KIIS 97.3',
    'Gold FM 90.1 Gold Coast': 'Hot Tomato 102.9',
    '6PR 882 Perth Talk Radio': 'Gold 96FM',
    '5AA 1395 Adelaide Reports': 'KIIS 102.3',
}

if (AIR_PLAN_DEMO) {
    for (const job of JOB_FIXTURES) {
        const renamed = DEMO_STATION_RENAMES[job.Station]
        if (renamed) job.Station = renamed
    }
    for (const market of AIR_PLAN_DEMO_MARKETS) {
        for (const entry of market.jobs) {
            const slotAt = demoSlotAt(market.offsetMin, entry.at).getTime()
            const createdAt = new Date(slotAt - DEMO_LEAD_MS).toISOString()
            // Finished work settled just before its airtime; live work is still
            // moving, so it was touched moments ago.
            const lastUpdatedAt = new Date(
                entry.at < 0 ? slotAt - 60_000 : Math.min(Date.now(), slotAt)
            ).toISOString()

            if (entry.from) {
                const source = JOB_FIXTURES.find((j) => j.JobId === entry.from)
                if (!source) continue
                JOB_FIXTURES.push({
                    ...structuredClone(source),
                    JobId: entry.jobId,
                    CreatedAt: createdAt,
                    LastUpdatedAt: lastUpdatedAt,
                })
                continue
            }

            const target = JOB_FIXTURES.find((j) => j.JobId === entry.jobId)
            if (!target) continue
            target.CreatedAt = createdAt
            target.LastUpdatedAt = lastUpdatedAt
        }
    }
}
export type VoiceFixture = z.input<typeof ElevenLabsVoiceSchema>

export const VOICE_FIXTURES: VoiceFixture[] =
    DEV_DATA.Voices as unknown as VoiceFixture[]

export type VoiceProfileFixture = z.input<typeof VoiceProfileApiSchema>

// Shared voice library: named, versioned variants of the seeded base voices.
// Calibration + run samples populate from real runs, so older/aired versions
// carry measured WPM and on-air samples; fresh versions don't yet.
export const VOICE_PROFILE_FIXTURES: VoiceProfileFixture[] =
    DEV_DATA.VoiceProfiles as unknown as VoiceProfileFixture[]
export type ScheduleFixture = z.input<typeof MarketScheduleSchema>
/** Station schedules exported from the dev API; keep local station cards dev-shaped. */
const DEV_SCHEDULES = (DEV_DATA.Schedules as unknown as ScheduleFixture[]).map(
    (schedule) => ({
        ...schedule,
        PlaybookId: schedule.MarketId === 'sydney' ? 'pb-mock-sydney' : '',
    })
)

/** This station's clock as a cron: four firings an hour on its own offset. */
function demoCron(offsetMin: number): string {
    return `${[0, 15, 30, 45].map((q) => q + offsetMin).join(',')} * * * *`
}

const SCHEDULE_TEMPLATE = DEV_SCHEDULES.find((s) => s.MarketId === 'sydney-2gb')
const DEMO_MARKET_IDS = new Set(AIR_PLAN_DEMO_MARKETS.map((m) => m.marketId))

/**
 * The two invented-callsign schedules shipped enabled (`sydney-2gb`,
 * `melbourne-3aw`). Their bulletins now belong to the real stations, so left in
 * place they showed up as two extra lanes with nothing on them.
 */
const DEMO_RETIRED_MARKET_IDS = new Set(['sydney-2gb', 'melbourne-3aw'])

export const SCHEDULE_FIXTURES: ScheduleFixture[] = !AIR_PLAN_DEMO
    ? DEV_SCHEDULES
    : [
          // Every dev schedule the demo does not replace, untouched.
          ...DEV_SCHEDULES.filter(
              (schedule) =>
                  !DEMO_MARKET_IDS.has(schedule.MarketId) &&
                  !DEMO_RETIRED_MARKET_IDS.has(schedule.MarketId)
          ),
          ...(SCHEDULE_TEMPLATE
              ? AIR_PLAN_DEMO_MARKETS.map((market) => ({
                    ...structuredClone(SCHEDULE_TEMPLATE),
                    MarketId: market.marketId,
                    Name: market.name,
                    Station: market.station,
                    Timezone: market.timezone,
                    CronExpression: demoCron(market.offsetMin),
                    Enabled: true,
                    VoiceProfileId: market.voiceProfileId,
                    VoiceRotation: null,
                    PlaybookId:
                        market.marketId === 'sydney' ? 'pb-mock-sydney' : '',
                }))
              : []),
      ]

// =============================================================================
// Self-service guidance fixtures (PascalCase to satisfy the Zod API schemas).
// =============================================================================

export interface GuidanceFixture {
    Id: string
    Scope: string
    ScopeId: string
    Type: string
    Text: string
    Enabled: boolean
    CreatedBy: string
    CreatedAtUtc: string
    UpdatedAtUtc: string
    Source: string | null
    ReviewAfterUtc: string | null
}

export const GUIDANCE_FIXTURES: GuidanceFixture[] =
    DEV_DATA.Guidance as unknown as GuidanceFixture[]

export interface GuidanceCandidateFixture {
    Market: string
    JobId: string
    Category: string | null
    Feedback: string
    CreatedAtUtc: string
}

/**
 * Operator-note promotion queue. `Market` MUST match a real `MarketSchedule.MarketId`
 * from DEV_DATA (`sydney`, `melbourne`, …) — these previously read `syd-1`, which
 * matches no market, so the notes panel rendered empty in mock mode and was never
 * exercised. Seeded to mirror the real dev corpus shape: one genuine lesson, a
 * repeated console-canned workflow note, and enough rows to push the tail past the
 * 8-entry replay window the generator actually reads.
 */
export const GUIDANCE_CANDIDATE_FIXTURES: GuidanceCandidateFixture[] = [
    // ── sydney: a busy market. Real lessons buried under console-canned noise,
    //    deep enough that several genuine notes sit past the replay window.
    {
        Market: 'sydney',
        JobId: 'arn-job-00001',
        Category: 'ordering',
        Feedback:
            'Led with a minor delay while the M4 was shut. Lead with the closure next time.',
        CreatedAtUtc: t(-30),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00007',
        Category: null,
        Feedback: 'Quoted the run time twice for the same road.',
        CreatedAtUtc: t(-95),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00008',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-140),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00009',
        Category: 'wording',
        Feedback: 'Said "shut" for a road. Roads are closed, shops are shut.',
        CreatedAtUtc: t(-190),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00010',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-240),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00011',
        Category: 'ContentError',
        Feedback: 'QT: edited-script retry test',
        CreatedAtUtc: t(-300),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00012',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-360),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00013',
        Category: 'naming',
        Feedback:
            'Called it the Western Motorway on air. Locally it is the M4.',
        CreatedAtUtc: t(-420),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00014',
        Category: 'revision',
        Feedback: "didn't talk about the weather properly",
        CreatedAtUtc: t(-500),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00015',
        Category: 'Script error',
        Feedback: 'VERIFY: too long, trim secondary roads',
        CreatedAtUtc: t(-560),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00016',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-620),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00017',
        Category: 'ordering',
        Feedback:
            'Harbour Tunnel roadworks read before the Anzac Bridge crash. Worst item leads.',
        CreatedAtUtc: t(-700),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00018',
        Category: 'Rejected by operator',
        Feedback: 'e2e sweep - reject suppression test',
        CreatedAtUtc: t(-780),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00019',
        Category: 'pace',
        Feedback: 'Four clear roads in a row at the end. One clause is plenty.',
        CreatedAtUtc: t(-860),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00020',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-940),
    },
    {
        Market: 'sydney',
        JobId: 'arn-job-00021',
        Category: 'wording',
        Feedback: 'Never say "motorists". Say "drivers".',
        CreatedAtUtc: t(-1020),
    },

    // ── melbourne: the real-world worst case — the canned console note has
    //    crowded out almost everything genuine.
    {
        Market: 'melbourne',
        JobId: 'arn-job-00031',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-45),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00032',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-110),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00033',
        Category: 'revision',
        Feedback: 'Lead with the motorway closure',
        CreatedAtUtc: t(-175),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00034',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-260),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00035',
        Category: 'revision',
        Feedback: 'Revision requested from review',
        CreatedAtUtc: t(-330),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00036',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-410),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00037',
        Category: 'naming',
        Feedback: 'It is "the Monash", not "Monash Freeway", on air.',
        CreatedAtUtc: t(-480),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00038',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-550),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00039',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-640),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00040',
        Category: 'revision',
        Feedback: 'didn\u2019t talk about the weather properly',
        CreatedAtUtc: t(-720),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00041',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-800),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00042',
        Category: 'Rejected to fallback by operator',
        Feedback: 'E2E verification: exercise fallback chain',
        CreatedAtUtc: t(-880),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00043',
        Category: 'revision',
        Feedback:
            'ensure the script is 30 seconds; talk about boom boom island traffic',
        CreatedAtUtc: t(-960),
    },
    {
        Market: 'melbourne',
        JobId: 'arn-job-00044',
        Category: 'Script error',
        Feedback: 'VERIFY fallback path',
        CreatedAtUtc: t(-1040),
    },

    // ── brisbane: a healthy market — mostly real lessons, inside the window.
    {
        Market: 'brisbane',
        JobId: 'arn-job-00051',
        Category: 'ordering',
        Feedback:
            'Gateway Motorway crash should have led, not the Riverside works.',
        CreatedAtUtc: t(-60),
    },
    {
        Market: 'brisbane',
        JobId: 'arn-job-00052',
        Category: 'naming',
        Feedback: 'Say "the Bruce Highway", with the article.',
        CreatedAtUtc: t(-150),
    },
    {
        Market: 'brisbane',
        JobId: 'arn-job-00053',
        Category: 'pace',
        Feedback: 'Read ran long. Trim the third clear-road mention.',
        CreatedAtUtc: t(-240),
    },
    {
        Market: 'brisbane',
        JobId: 'arn-job-00054',
        Category: null,
        Feedback: 'Pronounced Enoggera wrong \u2014 added to the dictionary.',
        CreatedAtUtc: t(-330),
    },

    // ── perth / adelaide / goldcoast: a light tail so switching stations shows
    //    a range of queue depths rather than one shape.
    {
        Market: 'perth',
        JobId: 'arn-job-00061',
        Category: 'wording',
        Feedback: 'Avoid "under the pump" \u2014 too vague for a delay.',
        CreatedAtUtc: t(-80),
    },
    {
        Market: 'perth',
        JobId: 'arn-job-00062',
        Category: 'revision',
        Feedback: 'Script edited and re-voiced',
        CreatedAtUtc: t(-260),
    },
    {
        Market: 'adelaide',
        JobId: 'arn-job-00071',
        Category: 'ordering',
        Feedback: 'South Eastern Freeway closure buried mid-read.',
        CreatedAtUtc: t(-120),
    },
    {
        Market: 'goldcoast',
        JobId: 'arn-job-00081',
        Category: 'naming',
        Feedback:
            'It is the M1, never the Pacific Motorway, for this audience.',
        CreatedAtUtc: t(-200),
    },
]

// =============================================================================
// Content-safety term fixtures (admin-only banned words + place-name exceptions).
// =============================================================================

export interface SafetyTermFixture {
    Id: string
    Kind: string
    Scope: string
    ScopeId: string
    Term: string
    Note: string | null
    CreatedBy: string
    CreatedAtUtc: string
    UpdatedAtUtc: string
}

export const SAFETY_TERM_FIXTURES: SafetyTermFixture[] =
    DEV_DATA.SafetyTerms as unknown as SafetyTermFixture[]

// =============================================================================
// Bulletin playbook fixtures (station diffs over role defaults).
// =============================================================================

export interface PlaybookFixture {
    Id: string
    Name: string
    Description: string | null
    Role: string
    Status: 'active' | 'archived'
    CurrentVersion: number
    CurrentOverlay: {
        Rewrite: Record<string, string>
        Disable: string[]
        Reorder: Record<string, number>
        Add: PlaybookBlockFixture[]
    }
    Versions: Array<{
        Version: number
        Overlay: {
            Rewrite: Record<string, string>
            Disable: string[]
            Reorder: Record<string, number>
            Add: PlaybookBlockFixture[]
        }
        ChangeNote: string | null
        CreatedAt: string
        CreatedBy: string | null
    }>
    CreatedAt: string
    UpdatedAt: string
    CreatedBy: string | null
    AssignedStations: string[]
}

export interface PlaybookBlockFixture {
    Id: string
    Section: 'Role' | 'Material' | 'Selection' | 'Voice' | 'Length' | 'Output'
    Body: string
    Order: number
    Locked: boolean
    Enabled: boolean
}

const COMPOSE2_BLOCKS: PlaybookBlockFixture[] = [
    {
        Id: 'role.identity',
        Section: 'Role',
        Body: `You are the bulletin composer. You write every word that goes to air.`,
        Order: 0,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'role.standard',
        Section: 'Role',
        Body: `Traffic radio is read by someone who knows these roads, to people already sitting on them. At its best it sounds like a mate leaning over and telling you what you are about to hit: quick, specific, and worth the airtime it takes.

That voice comes from letting the material shape the sentence. A closure, a crawl, a crash and a clear run are four different pieces of news and should not arrive in the same wrapper. Give the big one room, move briskly through the rest, and where two stories genuinely relate, carry the thought across instead of starting again.

You are writing this, not filling it in. If the bulletin would survive having its road names swapped between sentences, it is a form and not a read.`,
        Order: 1,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'material.facts-only',
        Section: 'Material',
        Body: `AVAILABLE FACTS are the only material you may voice.`,
        Order: 0,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'material.values-are-exact',
        Section: 'Material',
        Body: `A value is the part that must survive exactly: a figure, a road name, a suburb, a direction. Voice those as supplied. The sentence you build around them is yours entirely — how you open it, how you join it to the last one, how much room you give it. Two reads carrying identical values should still not sound alike.`,
        Order: 1,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'material.selection',
        Section: 'Material',
        Body: `They are what you MAY say, not a list to get through. Choose what a driver needs and leave the rest.`,
        Order: 2,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'material.ownership',
        Section: 'Material',
        Body: `Every value belongs to the subject its brief attaches it to. Re-word a fact freely; do not re-attach it. Carrying a figure or a detail across to another road, event or story changes what the data says, however natural the sentence sounds.`,
        Order: 3,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'material.reasoning',
        Section: 'Material',
        Body: `WHY IT MATTERS is your colleague's reasoning about importance. Use it to decide what leads and what goes, and never voice it: it appears in no data, so on air it is a claim nothing supports.`,
        Order: 4,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'selection.required',
        Section: 'Selection',
        Body: `Voice every story marked REQUIRED, whatever its priority.`,
        Order: 0,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'selection.overshoot',
        Section: 'Selection',
        Body: `The stories are distinct, so nothing here duplicates anything else. Lead with the highest priority. When the slot will not hold everything, drop the lowest-priority optional stories whole rather than squeezing them all in.`,
        Order: 1,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'voice.one-person',
        Section: 'Voice',
        Body: `One person talking, in continuous speech rather than a row of stand-alone lines.`,
        Order: 0,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'voice.variety',
        Section: 'Voice',
        Body: `Vary how sentences open, run and close. Any pattern a listener could hear repeating is a fault, as is a connective that could be deleted without changing the meaning.`,
        Order: 1,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'voice.name-roads',
        Section: 'Voice',
        Body: `Name the road every time. A listener who cannot place a hazard cannot act on it, so "the ramp" or "the motorway" wastes the sentence it appears in.`,
        Order: 2,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'voice.no-advice',
        Section: 'Voice',
        Body: `Say what is happening, never what a driver should do.`,
        Order: 3,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'voice.no-bookend',
        Section: 'Voice',
        Body: `The station's intro and sign-off are added around your read, so write neither.`,
        Order: 4,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'length.fill',
        Section: 'Length',
        Body: `Hit the target length. The slot is a length to fill, not a ceiling to stay under: a short read leaves dead air, and dead air is as much a fault as an overrun.`,
        Order: 0,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'length.ladder',
        Section: 'Length',
        Body: `When you are told to add words, the material is almost never exhausted. Look in this order:

1. A story you dropped. Voice it.
2. Facts you left unvoiced. Most briefs carry more than a read uses: a second location, a direction, a suburb, an extent. They are already supplied, so voicing them costs nothing and risks nothing.
3. The lead. The biggest story can carry a fuller sentence than the ones behind it.`,
        Order: 1,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'length.rewording-is-free',
        Section: 'Length',
        Body: `Rewording is free. The facts are fixed; the sentences around them are yours, and a fuller sentence saying exactly the same thing is a legitimate way to reach the slot.`,
        Order: 2,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'length.no-padding',
        Section: 'Length',
        Body: `What you may not do is pad. No words that carry no fact, no throat-clearing, no restating something already said. Reaching the number with empty words is worse than falling short.`,
        Order: 3,
        Locked: false,
        Enabled: true,
    },
    {
        Id: 'output.format',
        Section: 'Output',
        Body: `Reply with ONLY the bulletin text: no preamble, headings, quotes, code fences, or commentary.`,
        Order: 0,
        Locked: true,
        Enabled: true,
    },
]

function emptyPlaybookOverlay() {
    return { Rewrite: {}, Disable: [], Reorder: {}, Add: [] }
}

const BREAKFAST_ENERGY_BLOCK: PlaybookBlockFixture = {
    Id: 'voice.breakfast-energy',
    Section: 'Voice',
    Body: 'Keep the read bright and alert for the breakfast audience, with a warm lift on the lead story and a quick hand-off into the rest.',
    Order: 5,
    Locked: false,
    Enabled: true,
}

const SYDNEY_OVERLAY = {
    Rewrite: {
        'role.standard':
            'Sound like the trusted breakfast driver who gets straight to the useful detail, keeps the pace lively, and gives the biggest Sydney delay a little more room.',
    },
    Disable: [],
    Reorder: {},
    Add: [BREAKFAST_ENERGY_BLOCK],
}

const VERSION_1_OVERLAY = emptyPlaybookOverlay()
const VERSION_2_OVERLAY = {
    Rewrite: {
        'role.standard':
            'Keep the writer conversational and practical, putting the most useful Sydney traffic detail first.',
    },
    Disable: [],
    Reorder: {},
    Add: [],
}

export const PLAYBOOK_DEFAULTS: Record<string, PlaybookBlockFixture[]> = {
    compose2: COMPOSE2_BLOCKS,
}

export const PLAYBOOK_FIXTURES: PlaybookFixture[] = [
    {
        Id: 'pb-mock-sydney',
        Name: 'Sydney KIIS 90s writer instructions',
        Description:
            'Custom writer instructions for the KIIS 90s breakfast bulletin.',
        Role: 'compose2',
        Status: 'active',
        CurrentVersion: 3,
        CurrentOverlay: SYDNEY_OVERLAY,
        CreatedAt: '2026-08-24T00:00:00Z',
        UpdatedAt: '2026-08-26T00:00:00Z',
        CreatedBy: 'mock.reviewer@arn.com.au',
        AssignedStations: ['sydney'],
        Versions: [
            {
                Version: 1,
                Overlay: VERSION_1_OVERLAY,
                ChangeNote: 'Created from the shipped compose2 writer prompt.',
                CreatedAt: '2026-08-24T00:00:00Z',
                CreatedBy: 'mock.reviewer@arn.com.au',
            },
            {
                Version: 2,
                Overlay: VERSION_2_OVERLAY,
                ChangeNote:
                    'Made the Sydney read more conversational and useful.',
                CreatedAt: '2026-08-25T00:00:00Z',
                CreatedBy: 'mock.reviewer@arn.com.au',
            },
            {
                Version: 3,
                Overlay: SYDNEY_OVERLAY,
                ChangeNote:
                    'Added a bright breakfast-energy voice for KIIS 90s.',
                CreatedAt: '2026-08-26T00:00:00Z',
                CreatedBy: 'mock.reviewer@arn.com.au',
            },
        ],
    },
]
// =============================================================================
// Delivery style fixtures (named, versioned v3 audio-tag policies).
// =============================================================================

export interface DeliveryStyleFixture {
    Id: string
    Name: string
    Description: string | null
    Status: 'active' | 'archived'
    CurrentVersion: number
    CreatedAt: string
    UpdatedAt: string
    CreatedBy: string | null
    AssignedStations: string[]
    Versions: Array<{
        Version: number
        Policy: {
            ToneDescription: string
            AllowedTags: string[]
            Intensity: number
        }
        ChangeNote: string | null
        CreatedAt: string
        CreatedBy: string | null
    }>
}

export const DELIVERY_STYLE_FIXTURES: DeliveryStyleFixture[] =
    DEV_DATA.DeliveryStyles as unknown as DeliveryStyleFixture[]

// =============================================================================
// Bookend fixtures (named, versioned intro+outro pair libraries).
// =============================================================================

export interface BookendFixture {
    Id: string
    Name: string
    Description: string | null
    Status: 'active' | 'archived'
    IntroText: string
    OutroText: string
    CurrentVersion: number
    CreatedAt: string
    UpdatedAt: string
    CreatedBy: string | null
    AssignedStations: string[]
    Tokens: string[]
    Versions: Array<{
        Version: number
        IntroText: string
        OutroText: string
        ChangeNote: string | null
        CreatedAt: string
        CreatedBy: string | null
    }>
}

export const BOOKEND_FIXTURES: BookendFixture[] =
    DEV_DATA.Bookends as unknown as BookendFixture[]

// =============================================================================
// Pronunciation dictionary fixtures (named, versioned ElevenLabs pronunciation rules).
// =============================================================================

export interface PronunciationRuleFixture {
    Type: 'alias' | 'phoneme'
    StringToReplace: string
    Replacement: string
    Alphabet: string | null
    CaseSensitive: boolean
    WordBoundaries: boolean
    AppliesOnAllModels: boolean
}

export interface PronunciationDictionaryFixture {
    Id: string
    DictionaryId: string
    Name: string
    Description: string | null
    Status: 'active' | 'archived'
    Scope: 'Station' | 'Global'
    CurrentVersion: number
    CreatedAt: string
    UpdatedAt: string
    CreatedBy: string | null
    AssignedStations: string[]
    Rules: PronunciationRuleFixture[]
    Versions: Array<{
        Version: number
        ElevenLabsVersionId: string
        Rules: PronunciationRuleFixture[]
        ChangeNote: string | null
        CreatedAt: string
        CreatedBy: string | null
    }>
}

export const PRONUNCIATION_DICTIONARY_FIXTURES: PronunciationDictionaryFixture[] =
    DEV_DATA.PronunciationDictionaries as unknown as PronunciationDictionaryFixture[]
