// =============================================================================
// Axios Mock Adapter — routes requests to the in-memory store
// =============================================================================

import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { AxiosHeaders } from 'axios'
import { MockStore, MockJobConflictError, mockEditorialApi } from './store'
import type {
    PriorityRoadsResult,
    FillerStatementsResult,
    IncidentPrioritiesResult,
    VerifiedPronunciationsResult,
    IncidentLandscapeResult,
} from './store'
import { type ScheduleFixture, PLAYBOOK_DEFAULTS } from './fixtures'
import { SECTIONS } from '@/features/config-transfer/lib/section-meta'
import {
    JOBS,
    jobDetail,
    jobProgress,
    jobApprove,
    MAP_CONFIG,
    jobReject,
    jobRejectToFallback,
    jobPublish,
    jobProceedCuration,
    jobAudition,
    jobRetone,
    jobRewrite,
    jobRegenerate,
    jobAirSource,
    marketPlayoutTray,
    jobPronunciationRules,
    BROADCAST,
    VOICES,
    voiceCalibrate,
    VOICE_PROFILES,
    VOICE_PROFILE_PREVIEW,
    voiceProfileVoiceover,
    voiceProfileDetail,
    voiceProfileArchive,
    voiceProfileUnarchive,
    SCHEDULES,
    scheduleDetail,
    marketPriorityRoads,
    PRIORITY_ROADS_INVENTORY,
    marketIncidentPriorities,
    INCIDENT_PRIORITIES_INVENTORY,
    INCIDENT_LANDSCAPE_INVENTORY,
    marketIncidentLandscape,
    marketFillerStatements,
    FILLER_STATEMENTS_INVENTORY,
    marketVerifiedPronunciations,
    VERIFIED_PRONUNCIATIONS_INVENTORY,
    OPERATORS_ME,
    OPERATORS,
    operatorAssignment,
    GUIDANCE,
    guidanceDetail,
    GUIDANCE_PROMOTE,
    marketGuidance,
    marketGuidanceDetail,
    marketGuidanceComposed,
    marketGuidanceCandidates,
    stationGuidanceCandidate,
    GUIDANCE_INVENTORY,
    SAFETY_TERMS,
    safetyTermDetail,
    DELIVERY_STYLES,
    DELIVERY_STYLE_PREVIEW,
    deliveryStyleDetail,
    PLAYBOOKS,
    PLAYBOOK_PREVIEW,
    playbookDefault,
    playbookDetail,
    playbookArchive,
    playbookUnarchive,
    playbookDelete,
    deliveryStyleArchive,
    deliveryStyleUnarchive,
    BOOKENDS,
    BOOKEND_PREVIEW,
    bookendDetail,
    bookendVersionRestore,
    bookendArchive,
    bookendUnarchive,
    PRONUNCIATION_DICTIONARIES,
    PRONUNCIATION_RULE_PREVIEW,
    pronunciationDictionaryDetail,
    pronunciationDictionaryArchive,
    pronunciationDictionaryUnarchive,
    CONFIG_EXPORT,
    CONFIG_SCHEMA,
    CONFIG_IMPORT,
    CONFIG_IMPORTS_PENDING,
    ACTIVITY,
    configImportApprove,
    configImportReject,
} from '@/lib/api/paths'

declare global {
    interface Window {
        __ARN_MOCK_RESET__?: () => void
    }
}

function urlToPath(url: string | undefined): string {
    if (!url) return ''
    let path = url
    try {
        path = new URL(url).pathname
    } catch {
        /* relative URL — use as-is, strip below */
    }
    const qi = path.indexOf('?')
    if (qi >= 0) path = path.slice(0, qi)
    const hi = path.indexOf('#')
    if (hi >= 0) path = path.slice(0, hi)
    if (path.startsWith('/api/')) path = path.slice(4)
    else if (path === '/api') path = '/'
    return path
}

function matchPath(
    pattern: string,
    pathname: string
): { matched: boolean; params: Record<string, string> } {
    const patParts = pattern.split('/')
    const pathParts = pathname.split('/')
    if (patParts.length !== pathParts.length)
        return { matched: false, params: {} }
    const params: Record<string, string> = {}
    for (let i = 0; i < patParts.length; i++) {
        if (patParts[i].startsWith(':')) {
            params[patParts[i].slice(1)] = pathParts[i]
        } else if (patParts[i] !== pathParts[i]) {
            return { matched: false, params: {} }
        }
    }
    return { matched: true, params }
}

function ok<T>(
    config: InternalAxiosRequestConfig,
    data: T,
    headers: AxiosHeaders = new AxiosHeaders({
        'content-type': 'application/json',
    })
): AxiosResponse<T> {
    return {
        data,
        status: 200,
        statusText: 'OK',
        headers,
        config,
    }
}

function noContent(
    config: InternalAxiosRequestConfig
): AxiosResponse<Record<string, never>> {
    return {
        data: {},
        status: 204,
        statusText: 'No Content',
        headers: new AxiosHeaders(),
        config,
    }
}

function notFound(
    config: InternalAxiosRequestConfig
): AxiosResponse<{ error: string }> {
    return {
        data: { error: 'Not found' },
        status: 404,
        statusText: 'Not Found',
        headers: new AxiosHeaders(),
        config,
    }
}
function notModified(
    config: InternalAxiosRequestConfig,
    etag: string
): AxiosResponse<Record<string, never>> {
    return {
        data: {},
        status: 304,
        statusText: 'Not Modified',
        headers: new AxiosHeaders({ etag }),
        config,
    }
}

// Mirrors the real backend's ErrorResponse shape ({ Error, Detail, Status })
// so the apiClient response interceptor's error-message extraction (which
// reads data.Error/data.error) surfaces the same guidance text in mock mode.
function conflict(
    config: InternalAxiosRequestConfig,
    message: string
): AxiosResponse<{ Error: string; Detail: string | null; Status: number }> {
    return {
        data: { Error: message, Detail: null, Status: 409 },
        status: 409,
        statusText: 'Conflict',
        headers: new AxiosHeaders(),
        config,
    }
}

function badRequest(
    config: InternalAxiosRequestConfig,
    message: string,
    refused: string[]
): AxiosResponse<{ Error: string; Refused: string[] }> {
    return {
        data: { Error: message, Refused: refused },
        status: 400,
        statusText: 'Bad Request',
        headers: new AxiosHeaders(),
        config,
    }
}

function throwBadRequest(
    config: InternalAxiosRequestConfig,
    message: string,
    refused: string[]
): never {
    throw {
        message,
        isAxiosError: true,
        response: badRequest(config, message, refused),
    }
}

/**
 * Rejects the adapter's promise the same way axios's real http/xhr adapters
 * do for a non-2xx response (via `settle`): an axios-error-shaped object
 * carrying `.response`, so the client's response interceptor extracts the
 * conflict message exactly as it would from a real 409. Our custom adapter
 * otherwise just resolves — plain `return conflict(...)` would surface as a
 * *successful* response to callers, which is not what a precondition
 * failure should look like.
 */
function throwConflict(
    config: InternalAxiosRequestConfig,
    message: string
): never {
    throw { message, isAxiosError: true, response: conflict(config, message) }
}

const TAG_PATTERN = /\[[^[\]]{0,80}\]/g

const stripTags = (s: string) =>
    s.replace(TAG_PATTERN, ' ').replace(/\s+/g, ' ').trim()

const countWords = (s: string) => {
    const plain = stripTags(s)
    return plain ? plain.split(/\s+/).length : 0
}

/** The backend punctuates every part but the last so the synthesiser hears a phrase boundary. */
const joinRead = (parts: readonly string[]) => {
    const kept = parts.map((p) => p.trim()).filter(Boolean)
    return kept
        .map((p, i) =>
            i === kept.length - 1 || '.!?,:;…—-'.includes(p[p.length - 1])
                ? p
                : `${p}.`
        )
        .join(' ')
}

/**
 * Mirrors the backend's `StationLineSplitter`: peel the market's filler statements off the tail so
 * a rewrite never sees them. Without this the mock could not reproduce the state the real endpoint
 * now returns — station lines held back and reported — and the UI note would be unreachable offline.
 */
async function peelStationLines(
    store: MockStore,
    market: string | undefined,
    text: string
): Promise<{ body: string; preserved: string[] }> {
    const key = (s: string) =>
        s
            .replace(/[^\p{L}\p{N}\s]/gu, '')
            .toLowerCase()
            .replace(/\s+/g, ' ')
            .trim()
    // The job carries a display market ("Sydney"); the statement map is keyed by the canonical
    // id ("sydney"), exactly as the backend's MarketKey.Normalize resolves it.
    const result = market
        ? await store.getFillerStatements(market.trim().toLowerCase())
        : null
    const stationKeys = new Set(
        (result?.Statements ?? []).map((s) => key(s.Text)).filter(Boolean)
    )
    if (stationKeys.size === 0) return { body: text, preserved: [] }

    const sentences = text
        .split(/(?<=[.!?])\s+/)
        .map((s) => s.trim())
        .filter(Boolean)
    let end = sentences.length
    while (end > 0 && stationKeys.has(key(sentences[end - 1]))) end--
    if (end === 0) return { body: text, preserved: [] }

    return {
        body: sentences.slice(0, end).join(' '),
        preserved: sentences.slice(end),
    }
}

/**
 * Reads a query parameter the way the real API would see it. `config.params` is checked FIRST
 * because axios has not serialised it into `config.url` by the time an adapter runs: reading only
 * the URL silently returned null for every caller that passes `params`, so scoped lookups all fell
 * back to their default and, e.g., `GET /editorial/settings?scope=Market&scopeId=portaugusta`
 * served the Global row. That made the console appear to leak one scope's stored values into
 * another in mock mode, which the real backend never does.
 */
function queryParam(
    config: InternalAxiosRequestConfig,
    key: string
): string | null {
    const fromParams = (config.params as Record<string, unknown> | undefined)?.[
        key
    ]
    if (fromParams !== undefined && fromParams !== null && fromParams !== '')
        return String(fromParams)

    try {
        return new URL(config.url ?? '', 'http://mock').searchParams.get(key)
    } catch {
        return null
    }
}

export function createMockAdapter(): (
    config: InternalAxiosRequestConfig
) => Promise<AxiosResponse> {
    let store = new MockStore()
    if (typeof window !== 'undefined') {
        window.__ARN_MOCK_RESET__ = () => {
            store = new MockStore()
        }
    }

    return async (
        config: InternalAxiosRequestConfig
    ): Promise<AxiosResponse> => {
        const method = (config.method ?? 'get').toLowerCase()
        const path = urlToPath(config.url)
        const body = config.data
            ? typeof config.data === 'string'
                ? JSON.parse(config.data)
                : config.data
            : {}

        // ---- Client runtime config ----
        // Mock mode ships no HERE key, so every map shows `MapUnavailable`.
        // PlayoutEnabled mirrors a playout-enabled environment (test/prod) so the station
        // cards exercise the real per-station copy rather than the "environment never
        // delivers" branch development shows.
        if (path === MAP_CONFIG && method === 'get') {
            return ok(config, { HereApiKey: '', PlayoutEnabled: true })
        }

        // ---- Jobs ----
        if (path === JOBS && method === 'get') {
            const result = await store.listJobs()
            const statusParam = queryParam(config, 'status')
            let items = result.Items
            if (statusParam) {
                const wanted = new Set(
                    statusParam
                        .split(',')
                        .map((s) => s.trim())
                        .filter(Boolean)
                )
                items = items.filter((j) => wanted.has(j.Status))
            }

            const limitParam = queryParam(config, 'limit')
            if (!limitParam)
                return ok(config, { Items: items, Total: items.length })

            // Paginated path (History): newest-first by LastUpdatedAt; `to` narrows
            // strictly older than the cursor, mirroring the real backend's exact
            // exclusive-upper-bound semantics so "Load older" behaves the same way here.
            // The History client sends COMPOSITE cursors (`LastUpdatedAt|JobId`,
            // finding 11) and the backend parses both forms — Date.parse chokes on
            // the trailing |JobId (NaN, and NaN || now re-served the newest page
            // forever once >limit jobs existed), so take the timestamp half.
            const toParam = queryParam(config, 'to') ?? ''
            const toMs = Date.parse(toParam.split('|')[0]) || Date.now()
            const limit = Number(limitParam)
            const older = items
                .filter((j) => Date.parse(j.LastUpdatedAt) < toMs)
                .sort(
                    (a, b) =>
                        Date.parse(b.LastUpdatedAt) -
                        Date.parse(a.LastUpdatedAt)
                )
            const page = older.slice(0, limit)
            return ok(config, {
                Items: page,
                Total: page.length,
                HasMore: older.length > limit,
            })
        }
        const progressMatch = matchPath(jobProgress(':id'), path)
        if (progressMatch.matched && method === 'get') {
            const progress = store.getJobProgress(progressMatch.params.id)
            if (!progress) return notFound(config)
            const etag = `"mock-progress-${progress.JobId}-v${progress.Version}"`
            const requestedEtag = new AxiosHeaders(config.headers).get(
                'If-None-Match'
            )
            if (requestedEtag === etag) return notModified(config, etag)
            return ok(
                config,
                progress,
                new AxiosHeaders({
                    'content-type': 'application/json',
                    etag,
                })
            )
        }

        const jobMatch = matchPath(jobDetail(':id'), path)
        if (jobMatch.matched && method === 'get') {
            const job = await store.getJob(jobMatch.params.id)
            if (!job) return notFound(config)
            return ok(config, job)
        }

        if (jobMatch.matched && method === 'delete') {
            await store.deleteJob(jobMatch.params.id)
            return noContent(config)
        }

        const approveMatch = matchPath(jobApprove(':id'), path)
        if (approveMatch.matched && method === 'post') {
            try {
                await store.approveJob(
                    approveMatch.params.id,
                    body.expectedEtag
                )
            } catch (err) {
                if (err instanceof MockJobConflictError)
                    throwConflict(config, err.message)
                throw err
            }
            return ok(config, {})
        }
        const proceedCurationMatch = matchPath(jobProceedCuration(':id'), path)
        if (proceedCurationMatch.matched && method === 'post') {
            try {
                await store.proceedCuration(
                    proceedCurationMatch.params.id,
                    body.ExpectedEtag ?? body.expectedEtag,
                    body.EditedIncidents ?? body.editedIncidents ?? []
                )
            } catch (err) {
                if (err instanceof MockJobConflictError)
                    throwConflict(config, err.message)
                throw err
            }
            return ok(config, {
                Action: 'proceeded',
                JobId: proceedCurationMatch.params.id,
                Message: 'Curation saved; draft generation started.',
            })
        }

        const rejectMatch = matchPath(jobReject(':id'), path)
        if (rejectMatch.matched && method === 'post') {
            await store.rejectJob(
                rejectMatch.params.id,
                body.reasonCode ?? '',
                body.notes ?? '',
                body.futureLesson
            )
            return ok(config, {})
        }

        const rejectFallbackMatch = matchPath(jobRejectToFallback(':id'), path)
        if (rejectFallbackMatch.matched && method === 'post') {
            await store.rejectToFallbackJob(
                rejectFallbackMatch.params.id,
                body.reasonCode ?? '',
                body.notes ?? '',
                body.futureLesson
            )
            return ok(config, {})
        }

        const publishMatch = matchPath(jobPublish(':id'), path)
        if (publishMatch.matched && method === 'post') {
            try {
                await store.publishJob(
                    publishMatch.params.id,
                    body.expectedEtag
                )
            } catch (err) {
                if (err instanceof MockJobConflictError)
                    throwConflict(config, err.message)
                throw err
            }
            return ok(config, {})
        }

        const airSourceMatch = matchPath(jobAirSource(':id'), path)
        if (airSourceMatch.matched && method === 'post') {
            try {
                await store.setAirSource(
                    airSourceMatch.params.id,
                    body.kind === 'cart' ? 'cart' : 'repeat',
                    body.sourceJobId,
                    body.expectedEtag
                )
            } catch (err) {
                if (err instanceof MockJobConflictError)
                    throwConflict(config, err.message)
                throw err
            }
            return ok(config, {
                action: 'air-source-set',
                jobId: airSourceMatch.params.id,
                message: 'Slot changed and written to the playout drop.',
            })
        }

        // Pickup is Zetta's, not ours, so mock mode reports the one state an
        // operator most needs to recognise: our WAV still sitting in the folder,
        // uncollected, where a change still replaces what airs.
        const trayMatch = matchPath(marketPlayoutTray(':market'), path)
        if (trayMatch.matched && method === 'get') {
            const written = new Date(Date.now() - 11 * 60 * 1000)
            return ok(config, {
                fileName: `${trayMatch.params.market.slice(0, 3).toUpperCase()}_TRAIN_TRAFF.wav`,
                enabled: true,
                awaitingPickup: true,
                writtenAtUtc: written.toISOString(),
            })
        }

        // Audition never synthesises in mock mode — it returns the job's existing audio so
        // the control, the spinner and the inline player can all be exercised offline.
        // Offline stand-in: prepend a manner tag so the whole rewrite loop is exercisable.
        const retoneMatch = matchPath(jobRetone(':id'), path)
        if (retoneMatch.matched && method === 'post') {
            const b = body as { text?: string; intent?: string }
            const text = String(b?.text ?? '')
            if (!text.trim() || !String(b?.intent ?? '').trim())
                return notFound(config)
            const job = await store.getJob(retoneMatch.params.id)
            const { body: rest, preserved } = await peelStationLines(
                store,
                job?.Market,
                text
            )
            // PascalCase, exactly like the real API. Answering in camelCase here is what let a
            // casing bug reach a deployed environment: both calls returned 200 and the UI ignored them.
            return ok(config, {
                Script: joinRead([`[quietly] ${rest}`, ...preserved]),
                WordsBefore: countWords(text),
                WordsAfter: countWords(text),
                Tags: ['[quietly]'],
                PreservedLines: preserved.length,
            })
        }

        // Content rewrite: the offline stand-in reorders the same script so the preview diff and
        // apply flow remain exercisable without a model call. The real endpoint supplies compact
        // reference data to the editor but does not run a grounding refusal gate.
        // PascalCase like the real API.
        const rewriteMatch = matchPath(jobRewrite(':id'), path)
        if (rewriteMatch.matched && method === 'post') {
            const b = body as { text?: string; instruction?: string }
            const text = String(b?.text ?? '')
            const instruction = String(b?.instruction ?? '')
            if (!text.trim() || !instruction.trim()) return notFound(config)

            const job = await store.getJob(rewriteMatch.params.id)
            const { body: rest, preserved } = await peelStationLines(
                store,
                job?.Market,
                text
            )
            const sentences = stripTags(rest)
                .split(/(?<=[.!?])\s+/)
                .map((s) => s.trim())
                .filter(Boolean)
            const reordered =
                sentences.length > 1
                    ? `${sentences[1]} ${sentences[0]} ${sentences.slice(2).join(' ')}`.trim()
                    : stripTags(rest)
            const script = joinRead([reordered, ...preserved])
            return ok(config, {
                Script: script,
                WordsBefore: countWords(text),
                WordsAfter: countWords(script),
                PreservedLines: preserved.length,
            })
        }

        const auditionMatch = matchPath(jobAudition(':id'), path)
        if (auditionMatch.matched && method === 'post') {
            const job = await store.getJob(auditionMatch.params.id)
            const text = String((body as { text?: string })?.text ?? '')
            if (!text.trim()) return notFound(config)
            return ok(config, {
                Url: job?.Audio?.Data?.Url ?? '',
                DurationSec: job?.Audio?.Data?.DurationSec ?? 0,
                TagCount: (text.match(/\[[^[\]]{0,80}\]/g) ?? []).length,
            })
        }

        const regenerateMatch = matchPath(jobRegenerate(':id'), path)
        if (regenerateMatch.matched && method === 'post') {
            await store.regenerateJob(regenerateMatch.params.id, body)
            return ok(config, {})
        }

        const pronunciationRuleMatch = matchPath(
            jobPronunciationRules(':id'),
            path
        )
        if (pronunciationRuleMatch.matched && method === 'post') {
            const saved = await store.addJobPronunciationRule(
                pronunciationRuleMatch.params.id,
                body
            )
            if (!saved) return notFound(config)
            return ok(config, saved)
        }

        // ---- Broadcast ----
        if (path === BROADCAST && method === 'post') {
            return ok(config, await store.createJob())
        }

        // ---- Voices ----
        if (path === VOICES && method === 'get') {
            return ok(config, await store.listVoices())
        }

        const calibrateMatch = matchPath(voiceCalibrate(':id'), path)
        if (calibrateMatch.matched && method === 'post') {
            return ok(config, await store.calibrateVoice())
        }

        // ---- Voice profiles (shared voice library) ----
        // Preview is the exact `/voices/profiles/preview`; check it before the
        // `:id` detail match so the literal segment wins.
        if (path === VOICE_PROFILE_PREVIEW && method === 'post') {
            return ok(config, await store.previewVoice(body))
        }

        // Voiceover render: a literal sub-route of a profile id, so it must be matched
        // before the `:id` detail route below.
        const voiceoverMatch = matchPath(voiceProfileVoiceover(':id'), path)
        if (voiceoverMatch.matched && method === 'post') {
            const rendered = await store.renderVoiceover(
                voiceoverMatch.params.id,
                body
            )
            if (!rendered) return notFound(config)
            return ok(config, rendered)
        }

        if (path === VOICE_PROFILES && method === 'get') {
            return ok(config, await store.listVoiceProfiles())
        }

        if (path === VOICE_PROFILES && method === 'post') {
            return ok(config, await store.createVoiceProfile(body))
        }

        const profileRestoreMatch = matchPath(
            '/voices/profiles/:id/versions/:version/restore',
            path
        )
        if (profileRestoreMatch.matched && method === 'post') {
            const restored = await store.restoreVoiceProfileVersion(
                profileRestoreMatch.params.id,
                Number(profileRestoreMatch.params.version)
            )
            if (!restored) return notFound(config)
            return ok(config, restored)
        }

        const profileArchiveMatch = matchPath(voiceProfileArchive(':id'), path)
        if (profileArchiveMatch.matched && method === 'post') {
            const archived = await store.archiveVoiceProfile(
                profileArchiveMatch.params.id
            )
            if (!archived) return notFound(config)
            return ok(config, archived)
        }

        const profileUnarchiveMatch = matchPath(
            voiceProfileUnarchive(':id'),
            path
        )
        if (profileUnarchiveMatch.matched && method === 'post') {
            const unarchived = await store.unarchiveVoiceProfile(
                profileUnarchiveMatch.params.id
            )
            if (!unarchived) return notFound(config)
            return ok(config, unarchived)
        }

        const profileMatch = matchPath(voiceProfileDetail(':id'), path)
        if (profileMatch.matched && method === 'get') {
            const profile = await store.getVoiceProfile(profileMatch.params.id)
            if (!profile) return notFound(config)
            return ok(config, profile)
        }

        if (profileMatch.matched && method === 'put') {
            const updated = await store.updateVoiceProfile(
                profileMatch.params.id,
                body
            )
            if (!updated) return notFound(config)
            return ok(config, updated)
        }

        if (profileMatch.matched && method === 'delete') {
            const deleted = await store.deleteVoiceProfile(
                profileMatch.params.id
            )
            if (!deleted) return notFound(config)
            return noContent(config)
        }

        if (path === SCHEDULES && method === 'get') {
            return ok(config, await store.listSchedules())
        }

        const scheduleMatch = matchPath(scheduleDetail(':id'), path)
        if (scheduleMatch.matched && method === 'get') {
            const schedule = await store.getSchedule(scheduleMatch.params.id)
            if (!schedule) return notFound(config)
            return ok(config, schedule)
        }

        if (scheduleMatch.matched && method === 'put') {
            const updated = await store.upsertSchedule(
                scheduleMatch.params.id,
                body as Partial<ScheduleFixture>
            )
            return ok(config, updated)
        }

        if (scheduleMatch.matched && method === 'delete') {
            const deleted = await store.deleteSchedule(scheduleMatch.params.id)
            if (!deleted) return notFound(config)
            return noContent(config)
        }

        // ---- Priority Roads ----
        if (path === PRIORITY_ROADS_INVENTORY && method === 'get') {
            const inventory: PriorityRoadsResult[] = []
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                inventory.push(await store.getPriorityRoads(m))
            }
            return ok(config, inventory)
        }

        const roadsMatch = matchPath(marketPriorityRoads(':market'), path)
        if (roadsMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getPriorityRoads(roadsMatch.params.market)
            )
        }

        if (roadsMatch.matched && method === 'put') {
            await store.setPriorityRoads(
                roadsMatch.params.market,
                body.roads ?? []
            )
            return ok(
                config,
                await store.getPriorityRoads(roadsMatch.params.market)
            )
        }
        // ---- Incident Priorities ----
        if (path === INCIDENT_PRIORITIES_INVENTORY && method === 'get') {
            const inventory: IncidentPrioritiesResult[] = []
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                inventory.push(await store.getIncidentPriorities(m))
            }
            return ok(config, inventory)
        }

        const incidentPrioritiesMatch = matchPath(
            marketIncidentPriorities(':market'),
            path
        )
        if (incidentPrioritiesMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getIncidentPriorities(
                    incidentPrioritiesMatch.params.market
                )
            )
        }

        if (incidentPrioritiesMatch.matched && method === 'put') {
            await store.setIncidentPriorities(
                incidentPrioritiesMatch.params.market,
                body.rules ?? []
            )
            return ok(
                config,
                await store.getIncidentPriorities(
                    incidentPrioritiesMatch.params.market
                )
            )
        }
        const incidentLandscapeMatch = matchPath(
            marketIncidentLandscape(':market'),
            path
        )
        if (incidentLandscapeMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getIncidentLandscape(
                    incidentLandscapeMatch.params.market
                )
            )
        }
        if (path === INCIDENT_LANDSCAPE_INVENTORY && method === 'get') {
            const inventory: IncidentLandscapeResult[] = []
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                inventory.push(await store.getIncidentLandscape(m))
            }
            return ok(config, inventory)
        }

        if (path === FILLER_STATEMENTS_INVENTORY && method === 'get') {
            const inventory: FillerStatementsResult[] = []
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                inventory.push(await store.getFillerStatements(m))
            }
            return ok(config, inventory)
        }

        // ---- Filler Statements ----
        const fillerMatch = matchPath(marketFillerStatements(':market'), path)
        if (fillerMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getFillerStatements(fillerMatch.params.market)
            )
        }

        if (fillerMatch.matched && method === 'put') {
            await store.setFillerStatements(
                fillerMatch.params.market,
                body.statements ?? []
            )
            return ok(
                config,
                await store.getFillerStatements(fillerMatch.params.market)
            )
        }

        if (path === VERIFIED_PRONUNCIATIONS_INVENTORY && method === 'get') {
            const inventory: VerifiedPronunciationsResult[] = []
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                inventory.push(await store.getVerifiedPronunciations(m))
            }
            return ok(config, inventory)
        }

        // ---- Verified Pronunciations ----
        const verifiedMatch = matchPath(
            marketVerifiedPronunciations(':market'),
            path
        )
        if (verifiedMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getVerifiedPronunciations(
                    verifiedMatch.params.market
                )
            )
        }

        if (verifiedMatch.matched && method === 'put') {
            await store.setVerifiedPronunciations(
                verifiedMatch.params.market,
                body.words ?? []
            )
            return ok(
                config,
                await store.getVerifiedPronunciations(
                    verifiedMatch.params.market
                )
            )
        }

        // ---- Operators ----
        if (path === OPERATORS_ME && method === 'get') {
            return ok(config, await store.getOperatorScope())
        }

        if (path === OPERATORS && method === 'get') {
            return ok(config, {
                Operators: await store.listOperators(),
                DirectoryStatus: 'Ok',
                DirectoryError: null,
            })
        }

        // 'me' is the reserved scope endpoint, never an objectId — refuse it
        // here so these handlers stay correct regardless of declaration order.
        const operatorPathMatch = matchPath(
            operatorAssignment(':objectId'),
            path
        )
        const operatorMatch =
            operatorPathMatch.matched &&
            operatorPathMatch.params.objectId === 'me'
                ? { matched: false, params: {} }
                : operatorPathMatch
        if (operatorMatch.matched && method === 'put') {
            const request = body as {
                markets: string[]
                displayName?: string
            }
            const result = await store.upsertOperator(
                operatorMatch.params.objectId,
                {
                    Markets: request.markets,
                    DisplayName: request.displayName,
                }
            )
            return ok(config, {
                ObjectId: result.ObjectId,
                DisplayName: result.DisplayName,
                Markets: result.Markets,
                UpdatedAt: result.UpdatedAtUtc ?? new Date().toISOString(),
                UpdatedBy: result.UpdatedBy ?? '',
            })
        }

        if (operatorMatch.matched && method === 'delete') {
            await store.deleteOperator(operatorMatch.params.objectId)
            return noContent(config)
        }

        // ---- Guidance ----
        if (path === GUIDANCE_INVENTORY && method === 'get') {
            const merged = [...(await store.listGlobalGuidance())]
            for (const m of (queryParam(config, 'markets') ?? '')
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)) {
                merged.push(...(await store.listMarketGuidance(m)))
            }
            return ok(config, merged)
        }

        const gComposedMatch = matchPath(
            marketGuidanceComposed(':market'),
            path
        )
        if (gComposedMatch.matched && method === 'get') {
            let station: string | undefined
            try {
                station =
                    new URL(config.url ?? '', 'http://mock').searchParams.get(
                        'station'
                    ) ?? undefined
            } catch {
                /* no station */
            }
            return ok(
                config,
                await store.composedGuidance(
                    gComposedMatch.params.market,
                    station
                )
            )
        }

        const gCandidatesMatch = matchPath(
            marketGuidanceCandidates(':market'),
            path
        )
        if (gCandidatesMatch.matched && method === 'get') {
            return ok(
                config,
                await store.listCandidates(gCandidatesMatch.params.market)
            )
        }

        const gCandidateDetailMatch = matchPath(
            stationGuidanceCandidate(':station', ':id'),
            path
        )
        if (gCandidateDetailMatch.matched && method === 'delete') {
            await store.dismissCandidate(gCandidateDetailMatch.params.id)
            return ok(config, {
                dismissed: gCandidateDetailMatch.params.id,
                station: gCandidateDetailMatch.params.station,
            })
        }

        const gMarketDetailMatch = matchPath(
            marketGuidanceDetail(':market', ':id'),
            path
        )
        if (gMarketDetailMatch.matched && method === 'delete') {
            await store.deleteGuidance(gMarketDetailMatch.params.id)
            return ok(config, { deleted: gMarketDetailMatch.params.id })
        }

        const gMarketMatch = matchPath(marketGuidance(':market'), path)
        if (gMarketMatch.matched && method === 'get') {
            return ok(
                config,
                await store.listMarketGuidance(gMarketMatch.params.market)
            )
        }
        if (gMarketMatch.matched && method === 'put') {
            return ok(
                config,
                await store.upsertGuidance(
                    'Market',
                    gMarketMatch.params.market,
                    body
                )
            )
        }

        if (path === GUIDANCE_PROMOTE && method === 'post') {
            return ok(config, await store.promoteGuidance(body))
        }
        if (path === GUIDANCE && method === 'get') {
            return ok(config, await store.listGlobalGuidance())
        }
        if (path === GUIDANCE && method === 'put') {
            return ok(
                config,
                await store.upsertGuidance(
                    body.scope ?? 'Global',
                    body.scopeId ?? '',
                    body
                )
            )
        }
        const gDetailMatch = matchPath(guidanceDetail(':id'), path)
        if (gDetailMatch.matched && method === 'delete') {
            await store.deleteGuidance(gDetailMatch.params.id)
            return ok(config, { deleted: gDetailMatch.params.id })
        }

        // ---- Safety terms (admin) ----
        if (path === SAFETY_TERMS && method === 'get') {
            return ok(config, await store.listSafetyTerms())
        }
        if (path === SAFETY_TERMS && method === 'put') {
            return ok(
                config,
                await store.upsertSafetyTerm({
                    id: body.id,
                    kind: body.kind,
                    scope: body.scope,
                    scopeId: body.scopeId,
                    term: body.term,
                    note: body.note,
                })
            )
        }
        const safetyDetailMatch = matchPath(safetyTermDetail(':id'), path)
        if (safetyDetailMatch.matched && method === 'delete') {
            await store.deleteSafetyTerm(safetyDetailMatch.params.id)
            return ok(config, { deleted: safetyDetailMatch.params.id })
        }
        if (path === PLAYBOOK_PREVIEW && method === 'post') {
            await new Promise((resolve) => setTimeout(resolve, 1500))
            return ok(config, {
                BaselineScript:
                    'Traffic is building on the M4 citybound, with about twelve-minute delays from the Cumberland Highway through to Church Street. The Warringah Freeway is slower northbound near the Falcon Street ramps, while roadworks continue on the Princes Highway at Kingsgrove. A crash is blocking a lane on Parramatta Road westbound at Leichhardt. That is the latest Sydney traffic.',
                Script: 'Morning Sydney, the M4 citybound is the one to watch, with delays around twelve minutes between the Cumberland Highway and Church Street. Northbound traffic is crawling on the Warringah Freeway near Falcon Street, and crews are still working on the Princes Highway at Kingsgrove. There is also a lane blocked by a crash on Parramatta Road at Leichhardt.',
                Refused: [],
                Notes: [],
                PredictedSeconds: 21.8,
            })
        }

        // ---- Bulletin playbooks ----
        const playbookDefaultMatch = matchPath(playbookDefault(':role'), path)
        if (playbookDefaultMatch.matched && method === 'get') {
            const blocks =
                PLAYBOOK_DEFAULTS[playbookDefaultMatch.params.role] ?? []
            return ok(config, {
                Role: playbookDefaultMatch.params.role,
                DefaultAvailable: blocks.length > 0,
                Blocks: blocks,
            })
        }
        if (path === PLAYBOOKS && method === 'get') {
            const result = await store.listPlaybooks()
            return ok(config, result)
        }

        const playbookDetailMatch = matchPath(playbookDetail(':id'), path)
        const refusedSections = Array.isArray(body?.Overlay?.Add)
            ? body.Overlay.Add.filter(
                  (block: { Section?: unknown }) =>
                      typeof block.Section === 'string' &&
                      ['selection', 'output'].includes(
                          block.Section.toLowerCase()
                      )
              ).map(
                  (block: { Section?: unknown }) =>
                      `${String(block.Section)} blocks are protected and cannot be added.`
              )
            : []
        if (
            refusedSections.length > 0 &&
            (method === 'post' || method === 'put')
        ) {
            throwBadRequest(
                config,
                'The playbook overlay contains refused changes.',
                refusedSections
            )
        }
        if (playbookDetailMatch.matched && method === 'get') {
            const playbook = await store.getPlaybook(
                playbookDetailMatch.params.id
            )
            if (!playbook) return notFound(config)
            return ok(config, playbook)
        }
        if (playbookDetailMatch.matched && method === 'put') {
            const playbook = await store.updatePlaybook(
                playbookDetailMatch.params.id,
                body
            )
            if (!playbook) return notFound(config)
            return ok(config, playbook)
        }
        if (path === PLAYBOOKS && method === 'post') {
            return ok(config, await store.createPlaybook(body))
        }
        const playbookRestoreMatch = matchPath(
            '/playbooks/:id/versions/:version/restore',
            path
        )
        if (playbookRestoreMatch.matched && method === 'post') {
            const playbook = await store.restorePlaybookVersion(
                playbookRestoreMatch.params.id,
                Number(playbookRestoreMatch.params.version)
            )
            if (!playbook) return notFound(config)
            return ok(config, playbook)
        }

        const playbookArchiveMatch = matchPath(playbookArchive(':id'), path)
        if (playbookArchiveMatch.matched && method === 'post') {
            const archived = await store.archivePlaybook(
                playbookArchiveMatch.params.id
            )
            if (!archived) return notFound(config)
            return ok(config, archived)
        }
        const playbookUnarchiveMatch = matchPath(playbookUnarchive(':id'), path)
        if (playbookUnarchiveMatch.matched && method === 'post') {
            const unarchived = await store.unarchivePlaybook(
                playbookUnarchiveMatch.params.id
            )
            if (!unarchived) return notFound(config)
            return ok(config, unarchived)
        }
        const playbookDeleteMatch = matchPath(playbookDelete(':id'), path)
        if (playbookDeleteMatch.matched && method === 'delete') {
            const deleted = await store.deletePlaybook(
                playbookDeleteMatch.params.id
            )
            if (!deleted) return notFound(config)
            return noContent(config)
        }

        // ---- Delivery styles ----
        if (path === DELIVERY_STYLE_PREVIEW && method === 'post') {
            return ok(config, await store.previewDeliveryStyle(body))
        }

        if (path === DELIVERY_STYLES && method === 'get') {
            return ok(config, await store.listDeliveryStyles())
        }
        if (path === DELIVERY_STYLES && method === 'post') {
            return ok(config, await store.createDeliveryStyle(body))
        }

        const dsRestoreMatch = matchPath(
            '/delivery-styles/:id/versions/:version/restore',
            path
        )
        if (dsRestoreMatch.matched && method === 'post') {
            const restored = await store.restoreDeliveryStyleVersion(
                dsRestoreMatch.params.id,
                Number(dsRestoreMatch.params.version)
            )
            if (!restored) return notFound(config)
            return ok(config, restored)
        }

        const dsArchiveMatch = matchPath(deliveryStyleArchive(':id'), path)
        if (dsArchiveMatch.matched && method === 'post') {
            const archived = await store.archiveDeliveryStyle(
                dsArchiveMatch.params.id
            )
            if (!archived) return notFound(config)
            return ok(config, archived)
        }

        const dsUnarchiveMatch = matchPath(deliveryStyleUnarchive(':id'), path)
        if (dsUnarchiveMatch.matched && method === 'post') {
            const unarchived = await store.unarchiveDeliveryStyle(
                dsUnarchiveMatch.params.id
            )
            if (!unarchived) return notFound(config)
            return ok(config, unarchived)
        }

        const dsDetailMatch = matchPath(deliveryStyleDetail(':id'), path)
        if (dsDetailMatch.matched && method === 'get') {
            const style = await store.getDeliveryStyle(dsDetailMatch.params.id)
            if (!style) return notFound(config)
            return ok(config, style)
        }
        if (dsDetailMatch.matched && method === 'put') {
            const updated = await store.updateDeliveryStyle(
                dsDetailMatch.params.id,
                body
            )
            if (!updated) return notFound(config)
            return ok(config, updated)
        }

        if (dsDetailMatch.matched && method === 'delete') {
            const deleted = await store.deleteDeliveryStyle(
                dsDetailMatch.params.id
            )
            if (!deleted) return notFound(config)
            return noContent(config)
        }

        // ---- Bookends (shared intro+outro pair library) ----
        if (path === BOOKEND_PREVIEW && method === 'post') {
            return ok(config, await store.previewBookend(body))
        }

        if (path === BOOKENDS && method === 'get') {
            return ok(config, await store.listBookends())
        }
        if (path === BOOKENDS && method === 'post') {
            return ok(config, await store.createBookend(body))
        }

        const bkRestoreMatch = matchPath(
            bookendVersionRestore(':id', ':version'),
            path
        )
        if (bkRestoreMatch.matched && method === 'post') {
            const restored = await store.restoreBookendVersion(
                bkRestoreMatch.params.id,
                Number(bkRestoreMatch.params.version)
            )
            if (!restored) return notFound(config)
            return ok(config, restored)
        }

        const bkArchiveMatch = matchPath(bookendArchive(':id'), path)
        if (bkArchiveMatch.matched && method === 'post') {
            const archived = await store.archiveBookend(
                bkArchiveMatch.params.id
            )
            if (!archived) return notFound(config)
            return ok(config, archived)
        }

        const bkUnarchiveMatch = matchPath(bookendUnarchive(':id'), path)
        if (bkUnarchiveMatch.matched && method === 'post') {
            const unarchived = await store.unarchiveBookend(
                bkUnarchiveMatch.params.id
            )
            if (!unarchived) return notFound(config)
            return ok(config, unarchived)
        }

        const bkDetailMatch = matchPath(bookendDetail(':id'), path)
        if (bkDetailMatch.matched && method === 'get') {
            const bookend = await store.getBookend(bkDetailMatch.params.id)
            if (!bookend) return notFound(config)
            return ok(config, bookend)
        }
        if (bkDetailMatch.matched && method === 'put') {
            const updated = await store.updateBookend(
                bkDetailMatch.params.id,
                body
            )
            if (!updated) return notFound(config)
            return ok(config, updated)
        }
        if (bkDetailMatch.matched && method === 'delete') {
            const deleted = await store.deleteBookend(bkDetailMatch.params.id)
            if (deleted === 'in_use') {
                // Backend refuses to delete an assigned bookend (409 library.in_use).
                return throwConflict(
                    config,
                    'This bookend is still assigned to one or more stations.'
                )
            }
            if (deleted === 'missing') return notFound(config)
            return noContent(config)
        }

        // ---- Pronunciation dictionaries ----
        // The literal preview-rule route must be checked before the {id}-patterned ones so the
        // word "preview-rule" is never parsed as a dictionary id.
        if (path === PRONUNCIATION_RULE_PREVIEW && method === 'post') {
            return ok(config, await store.previewPronunciationRule(body))
        }

        if (path === PRONUNCIATION_DICTIONARIES && method === 'get') {
            return ok(config, await store.listPronunciationDictionaries())
        }
        if (path === PRONUNCIATION_DICTIONARIES && method === 'post') {
            return ok(config, await store.createPronunciationDictionary(body))
        }

        const pdRestoreMatch = matchPath(
            '/pronunciation-dictionaries/:id/versions/:version/restore',
            path
        )
        if (pdRestoreMatch.matched && method === 'post') {
            const restored = await store.restorePronunciationDictionaryVersion(
                pdRestoreMatch.params.id,
                Number(pdRestoreMatch.params.version)
            )
            if (!restored) return notFound(config)
            return ok(config, restored)
        }

        const pdArchiveMatch = matchPath(
            pronunciationDictionaryArchive(':id'),
            path
        )
        if (pdArchiveMatch.matched && method === 'post') {
            const archived = await store.archivePronunciationDictionary(
                pdArchiveMatch.params.id
            )
            if (!archived) return notFound(config)
            return ok(config, archived)
        }

        const pdUnarchiveMatch = matchPath(
            pronunciationDictionaryUnarchive(':id'),
            path
        )
        if (pdUnarchiveMatch.matched && method === 'post') {
            const unarchived = await store.unarchivePronunciationDictionary(
                pdUnarchiveMatch.params.id
            )
            if (!unarchived) return notFound(config)
            return ok(config, unarchived)
        }

        const pdDetailMatch = matchPath(
            pronunciationDictionaryDetail(':id'),
            path
        )
        if (pdDetailMatch.matched && method === 'get') {
            const dict = await store.getPronunciationDictionary(
                pdDetailMatch.params.id
            )
            if (!dict) return notFound(config)
            return ok(config, dict)
        }
        if (pdDetailMatch.matched && method === 'put') {
            const updated = await store.updatePronunciationDictionary(
                pdDetailMatch.params.id,
                body
            )
            if (!updated) return notFound(config)
            return ok(config, updated)
        }
        if (pdDetailMatch.matched && method === 'delete') {
            const deleted = await store.deletePronunciationDictionary(
                pdDetailMatch.params.id
            )
            if (!deleted) return notFound(config)
            return noContent(config)
        }
        if (path === '/editorial/settings/effective' && method === 'get') {
            return ok(
                config,
                mockEditorialApi.effectiveEditorialSettings(
                    queryParam(config, 'market') ?? '',
                    queryParam(config, 'station') ?? null
                )
            )
        }
        if (path === '/editorial/settings' && method === 'get') {
            return ok(
                config,
                mockEditorialApi.getEditorialSettings(
                    queryParam(config, 'scope') ?? 'Global',
                    queryParam(config, 'scopeId') ?? ''
                )
            )
        }
        if (path === '/editorial/settings' && method === 'put') {
            return ok(config, mockEditorialApi.upsertEditorialSettings(body))
        }
        if (path === '/editorial/settings' && method === 'delete') {
            mockEditorialApi.deleteEditorialSettings(
                queryParam(config, 'scope') ?? 'Global',
                queryParam(config, 'scopeId') ?? ''
            )
            return ok(config, { Deleted: 'settings' })
        }
        // ---- Config transfer ----
        if (path === CONFIG_EXPORT && method === 'get') {
            const sectionsParam = queryParam(config, 'sections')
            const sections = sectionsParam
                ? sectionsParam.split(',').map((s) => s.trim())
                : undefined
            return ok(config, await store.exportConfig(sections))
        }

        if (path === CONFIG_SCHEMA && method === 'get') {
            // Sections are derived from the one list, never retyped: the mock
            // used to hardcode its own eight-section subset, so mock mode
            // disagreed with the deployed schema about what a bundle contains —
            // a third place for the same drift the picker suffered.
            return ok(config, {
                $schema: 'https://json-schema.org/draft/2020-12/schema',
                title: 'ARN Config Bundle',
                description:
                    'Exported configuration bundle for the ARN Traffic platform.',
                type: 'object',
                properties: {
                    Schema: { type: 'string' },
                    SchemaVersion: { type: 'number' },
                    ExportedAtUtc: { type: 'string', format: 'date-time' },
                    Source: { type: 'string' },
                    ...Object.fromEntries(
                        SECTIONS.map((section) => [
                            section,
                            { type: 'array' as const },
                        ])
                    ),
                },
            })
        }

        if (path === CONFIG_IMPORT && method === 'post') {
            const isDryRun = queryParam(config, 'dryRun') !== 'false'
            if (isDryRun) {
                const plan = store.planImport(body)
                return ok(config, {
                    DryRun: true,
                    RequiresApproval: false,
                    Applied: false,
                    Creates: plan.creates,
                    Updates: plan.updates,
                    Unchanged: plan.unchanged,
                    Errors: plan.errors,
                    Items: plan.items,
                })
            }
            const result = store.applyImport(body)
            return ok(config, {
                DryRun: false,
                RequiresApproval: false,
                Applied: true,
                Creates: result.creates,
                Updates: result.updates,
                Unchanged: result.unchanged,
                Errors: result.errors,
                Items: result.items,
            })
        }

        if (path === CONFIG_IMPORTS_PENDING && method === 'get') {
            return ok(config, {
                Items: [
                    {
                        Id: 'pending-001',
                        RequestedBy: 'admin@arn.com.au',
                        RequestedAtUtc: new Date(
                            Date.now() - 3600000
                        ).toISOString(),
                        Report: {
                            DryRun: false,
                            RequiresApproval: true,
                            Applied: false,
                            Creates: 1,
                            Updates: 2,
                            Unchanged: 3,
                            Errors: 0,
                            Items: [
                                {
                                    Section: 'PriorityRoads',
                                    Key: 'syd-1',
                                    Name: 'syd-1',
                                    Action: 'Update',
                                    Detail: null,
                                    Outcome: null,
                                },
                                {
                                    Section: 'SafetyTerms',
                                    Key: 'st-global-1',
                                    Name: 'gridlock',
                                    Action: 'Update',
                                    Detail: null,
                                    Outcome: null,
                                },
                                {
                                    Section: 'FillerStatements',
                                    Key: 'mel-1',
                                    Name: 'mel-1',
                                    Action: 'Unchanged',
                                    Detail: null,
                                    Outcome: null,
                                },
                                {
                                    Section: 'VoiceProfiles',
                                    Key: 'vp-arn-hudson',
                                    Name: 'ARN Hudson, Natural',
                                    Action: 'Unchanged',
                                    Detail: null,
                                    Outcome: null,
                                },
                                {
                                    Section: 'Guidance',
                                    Key: 'g-global-1',
                                    Name: 'Keep it warm and conversational',
                                    Action: 'Unchanged',
                                    Detail: null,
                                    Outcome: null,
                                },
                            ],
                        },
                    },
                ],
                Total: 1,
            })
        }

        const configApproveMatch = matchPath(configImportApprove(':id'), path)
        if (configApproveMatch.matched && method === 'post') {
            // Apply the seeded fixture bundle naively
            const result = store.applyImport({
                SafetyTerms: [
                    {
                        Id: 'st-import-1',
                        Kind: 'Banned',
                        Scope: 'Global',
                        ScopeId: '',
                        Term: 'gridlock',
                        Note: null,
                        CreatedBy: 'admin@arn.com.au',
                        CreatedAtUtc: new Date().toISOString(),
                        UpdatedAtUtc: new Date().toISOString(),
                    },
                ],
            })
            return ok(config, {
                DryRun: false,
                RequiresApproval: false,
                Applied: true,
                Creates: result.creates,
                Updates: result.updates,
                Unchanged: result.unchanged,
                Errors: result.errors,
                Items: result.items,
            })
        }

        const configRejectMatch = matchPath(configImportReject(':id'), path)
        if (configRejectMatch.matched && method === 'post') {
            return ok(config, { rejected: configRejectMatch.params.id })
        }

        // ---- Event ledger and spot checks ----
        const ledgerMatch = matchPath('/ledger/:market', path)
        if (ledgerMatch.matched && method === 'get') {
            return ok(
                config,
                await store.getLedger(
                    decodeURIComponent(ledgerMatch.params.market)
                )
            )
        }
        const verdictMatch = matchPath(
            '/ledger/:market/events/:eventKey/verdict',
            path
        )
        if (verdictMatch.matched && method === 'post') {
            await store.setEventVerdict(
                decodeURIComponent(verdictMatch.params.market),
                decodeURIComponent(verdictMatch.params.eventKey),
                body.Verdict ?? body.verdict ?? null,
                body.Notes ?? body.notes ?? null
            )
            return noContent(config)
        }
        const deltaMatch = matchPath('/jobs/:id/delta', path)
        if (deltaMatch.matched && method === 'get') {
            const job = await store.getJob(deltaMatch.params.id)
            if (!job) return notFound(config)
            return ok(config, await store.getJobDelta(deltaMatch.params.id))
        }
        if (path === '/spot-checks' && method === 'get') {
            return ok(config, { Items: await store.listSpotChecks() })
        }
        const spotCheckMatch = matchPath('/spot-checks/:id', path)
        if (spotCheckMatch.matched && method === 'post') {
            const job = await store.getJob(spotCheckMatch.params.id)
            if (!job) return notFound(config)
            await store.markSpotCheck(spotCheckMatch.params.id)
            return noContent(config)
        }

        if (path === ACTIVITY && method === 'get') {
            const resourceFilter = queryParam(config, 'resource')
            const entityIdFilter = queryParam(config, 'entityId')
            const now = Date.now()
            const hour = 3600000
            const allEntries = [
                {
                    AtUtc: new Date(now - hour * 2).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/schedules/ade-1',
                    Resource: 'schedules',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-2fbc21d3f2b9443c88812fd8f091bc47',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 26).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/schedules/ade-1',
                    Resource: 'schedules',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-a50de7bf0caa432aad9dfd394750adba',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 5).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/markets/syd-1/priority-roads',
                    Resource: 'priority-roads',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Roads: [
                            { Name: 'M4 Motorway', GoodRunPriority: 'high' },
                        ],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 30).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'POST',
                    Path: '/api/voices/profiles',
                    Resource: 'voices/profiles',
                    EntityId: 'vp-2fbc21d3f2b9443c88812fd8f091bc47',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({ Name: 'Warm Breakfast' }),
                },
                {
                    AtUtc: new Date(now - hour * 48).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/safety/terms',
                    Resource: 'safety-terms',
                    // Market-scoped, not the term's own id -- ActivityLoggingMiddleware's
                    // response-body fallback resolves ScopeId (market) for this route, since
                    // the market is in the body/response, not the URL. See ActivityPathResolver.
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Kind: 'Banned',
                        Scope: 'Market',
                        ScopeId: 'mel-1',
                        Term: 'blocked-test-word',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 8).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/markets/syd-1/filler-statements',
                    Resource: 'filler-statements',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Statements: [{ Text: 'Stay safe on the roads.' }],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 12).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/markets/syd-1/guidance',
                    Resource: 'guidance',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Type: 'Editorial',
                        Text: 'Prefer clear traffic leads.',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 36).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/operators/aad-operator-001',
                    Resource: 'operators',
                    EntityId: 'aad-operator-001',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        DisplayName: 'Sarah Chen',
                        Markets: ['syd-1', 'mel-1'],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 6).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/voices/profiles/vp-2fbc21d3f2b9443c88812fd8f091bc47',
                    Resource: 'voices/profiles',
                    EntityId: 'vp-2fbc21d3f2b9443c88812fd8f091bc47',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Name: 'Warm Breakfast',
                        Stability: 0.65,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 15).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/delivery-styles/ds-standard-traffic',
                    Resource: 'delivery-styles',
                    EntityId: 'ds-standard-traffic',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Name: 'Standard Traffic',
                        Enabled: true,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 18).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/pronunciation-dictionaries/pd-sydney-roads',
                    Resource: 'pronunciation-dictionaries',
                    EntityId: 'pd-sydney-roads',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Name: 'Sydney Roads',
                        RuleCount: 12,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 22).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/guidance',
                    Resource: 'guidance',
                    // Global-scope directive: the market URL segment is absent, so the
                    // real middleware resolves EntityId from the response body's own
                    // Id (never ScopeId, which is empty for Global) -- see
                    // ActivityPathResolver / the guidance resource-table row.
                    EntityId: 'guid-house-style-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Type: 'HouseStyle',
                        Scope: 'Global',
                        Text: 'Keep drive-time bulletins under 30 seconds unless there is a major closure.',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 40).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/safety-terms/st-global-3',
                    Resource: 'safety-terms',
                    EntityId: 'st-global-3',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Kind: 'Banned',
                        Enabled: true,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 3).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'POST',
                    Path: '/api/jobs/arn-job-00007/approve',
                    Resource: 'jobs',
                    EntityId: 'arn-job-00007',
                    StatusCode: 200,
                    RequestBodyJson: null,
                },
                {
                    AtUtc: new Date(now - hour * 9).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'POST',
                    Path: '/api/broadcast',
                    Resource: 'broadcast',
                    // Ad-hoc trigger -- an action, not a single addressable entity.
                    EntityId: null,
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({ Market: 'syd-1' }),
                },
                {
                    AtUtc: new Date(now - hour * 60).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'POST',
                    Path: '/api/config/import',
                    Resource: 'config',
                    // Whole-bundle action -- not a single entity.
                    EntityId: null,
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Applied: true,
                        Creates: 2,
                        Updates: 1,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 72).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'POST',
                    Path: '/api/config/imports/pending/imp-2026-07-05/approve',
                    Resource: 'config-imports',
                    EntityId: 'imp-2026-07-05',
                    StatusCode: 200,
                    RequestBodyJson: null,
                },
                // ---- Per-market schedule entries (for Edit-station History tab) ----
                {
                    AtUtc: new Date(now - hour * 4).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/schedules/syd-1',
                    Resource: 'schedules',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-50de7bf0caa432aad9dfd394750adba',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 28).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/schedules/syd-1',
                    Resource: 'schedules',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Coverage: {
                            Latitude: -33.8688,
                            Longitude: 151.2093,
                            RadiusKm: 50,
                        },
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 10).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/schedules/mel-1',
                    Resource: 'schedules',
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-a50de7bf0caa432aad9dfd394750adba',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 35).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/schedules/mel-1',
                    Resource: 'schedules',
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Name: 'Melbourne Morning',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 14).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/schedules/bne-1',
                    Resource: 'schedules',
                    EntityId: 'bne-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-2fbc21d3f2b9443c88812fd8f091bc47',
                        TargetDurationSeconds: 30,
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 20).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/schedules/per-1',
                    Resource: 'schedules',
                    EntityId: 'per-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-a50de7bf0caa432aad9dfd394750adba',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 24).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/schedules/gco-1',
                    Resource: 'schedules',
                    EntityId: 'gco-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        VoiceProfileId: 'vp-50de7bf0caa432aad9dfd394750adba',
                    }),
                },
                // ---- Per-market Priority Roads ----
                {
                    AtUtc: new Date(now - hour * 16).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/markets/mel-1/priority-roads',
                    Resource: 'priority-roads',
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Roads: [
                            {
                                Name: 'M1 Princes Freeway',
                                GoodRunPriority: 'critical',
                            },
                        ],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 50).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/markets/ade-1/priority-roads',
                    Resource: 'priority-roads',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Roads: [
                            {
                                Name: 'South Eastern Freeway',
                                GoodRunPriority: 'high',
                            },
                        ],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 44).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/markets/bne-1/priority-roads',
                    Resource: 'priority-roads',
                    EntityId: 'bne-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Roads: [
                            {
                                Name: 'Pacific Motorway M1',
                                GoodRunPriority: 'critical',
                            },
                        ],
                    }),
                },
                // ---- Per-market Filler Statements ----
                {
                    AtUtc: new Date(now - hour * 11).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/markets/mel-1/filler-statements',
                    Resource: 'filler-statements',
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Statements: [
                            { Text: 'Remember to pack a raincoat today.' },
                        ],
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 55).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/markets/ade-1/filler-statements',
                    Resource: 'filler-statements',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Statements: [
                            { Text: 'School zones are active — take care.' },
                        ],
                    }),
                },
                // ---- Per-market Safety Terms ----
                {
                    AtUtc: new Date(now - hour * 7).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/safety/terms',
                    Resource: 'safety-terms',
                    EntityId: 'syd-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Kind: 'Banned',
                        Scope: 'Market',
                        ScopeId: 'syd-1',
                        Term: 'car crash',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 62).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/safety/terms',
                    Resource: 'safety-terms',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Kind: 'AllowedException',
                        Scope: 'Market',
                        ScopeId: 'ade-1',
                        Term: 'South Road',
                    }),
                },
                // ---- Per-market Guidance ----
                {
                    AtUtc: new Date(now - hour * 13).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/markets/mel-1/guidance',
                    Resource: 'guidance',
                    EntityId: 'mel-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Type: 'Editorial',
                        Text: 'Lead with the West Gate if there is a delay.',
                    }),
                },
                {
                    AtUtc: new Date(now - hour * 45).toISOString(),
                    ActorName: 'Jamie Operator',
                    ActorObjectId: 'op-jamie',
                    Method: 'PUT',
                    Path: '/api/markets/ade-1/guidance',
                    Resource: 'guidance',
                    EntityId: 'ade-1',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        Type: 'HouseStyle',
                        Text: 'Use "South Road corridor" for consistency.',
                    }),
                },
                // ---- Additional operator history ----
                {
                    AtUtc: new Date(now - hour * 42).toISOString(),
                    ActorName: 'Priya Admin',
                    ActorObjectId: 'op-priya',
                    Method: 'PUT',
                    Path: '/api/operators/aad-operator-002',
                    Resource: 'operators',
                    EntityId: 'aad-operator-002',
                    StatusCode: 200,
                    RequestBodyJson: JSON.stringify({
                        DisplayName: 'James Wilson',
                        Markets: ['bne-1', 'gco-1'],
                    }),
                },
            ]

            const items = allEntries
                .filter((e) => !resourceFilter || e.Resource === resourceFilter)
                .filter((e) => !entityIdFilter || e.EntityId === entityIdFilter)
                .sort((a, b) => (a.AtUtc < b.AtUtc ? 1 : -1))

            const limitParam = queryParam(config, 'limit')
            if (!limitParam)
                return ok(config, { Items: items, Total: items.length })

            // Paginated path (Activity page's table): `to` narrows strictly older than
            // the cursor, mirroring the real backend's exclusive-upper-bound semantics.
            const toMs =
                Date.parse(queryParam(config, 'to') ?? '') || Date.now()
            const older = items.filter((e) => Date.parse(e.AtUtc) < toMs)
            const limit = Number(limitParam)
            const page = older.slice(0, limit)
            return ok(config, {
                Items: page,
                Total: page.length,
                HasMore: older.length > limit,
            })
        }

        // ---- Fallback 404 ----
        return notFound(config)
    }
}
