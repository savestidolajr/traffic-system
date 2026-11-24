// =============================================================================
// API endpoint path constants — single source of truth
// =============================================================================

export const AUTH_ME = '/.auth/me'

// Client runtime config (authenticated; served per-request, never bundled)
export const MAP_CONFIG = '/map-config'

// Jobs
export const JOBS = '/jobs'
export const jobDetail = (id: string) => `/jobs/${id}` as const
export const jobProgress = (id: string) => `/jobs/${id}/progress` as const
export const jobApprove = (id: string) => `/jobs/${id}/approve` as const
export const jobReject = (id: string) => `/jobs/${id}/reject` as const
export const jobRejectToFallback = (id: string) =>
    `/jobs/${id}/reject-to-fallback` as const
export const jobPublish = (id: string) => `/jobs/${id}/publish` as const
/** Moves an AwaitingCuration job into generation using its current ETag. */
export const jobProceedCuration = (id: string) => `/jobs/${id}/proceed` as const
export const jobRegenerate = (id: string) => `/jobs/${id}/regenerate` as const
/** Synthesise a draft read without touching the job — the fast experiment loop. */
export const jobAudition = (id: string) => `/jobs/${id}/audition` as const
/** Rewrite the draft into a described register — returns text, does NOT re-voice. */
export const jobRetone = (id: string) => `/jobs/${id}/retone` as const
/** Rewrite the draft's CONTENT from a free-form instruction — returns a grounded preview, does NOT re-voice. */
export const jobRewrite = (id: string) => `/jobs/${id}/rewrite` as const
export const jobPronunciationRules = (id: string) =>
    `/jobs/${id}/pronunciation-rules` as const
/** Choose what airs in this slot: an earlier bulletin from today, or the standby cart. */
export const jobAirSource = (id: string) => `/jobs/${id}/air-source` as const
/**
 * Whether this market's WAV is still sitting in the playout drop folder.
 *
 * Zetta COLLECTS the file, so presence is the only signal that distinguishes
 * "our audio is still swappable" from "it has been picked up and a second
 * write would queue a duplicate". Without it an operator changing a slot after
 * publish is guessing.
 */
export const marketPlayoutTray = (market: string) =>
    `/markets/${market}/playout-tray` as const
export const BROADCAST = '/broadcast'

// Schedules
export const SCHEDULES = '/schedules'
export const scheduleDetail = (id: string) => `/schedules/${id}` as const
export const marketPriorityRoads = (market: string) =>
    `/markets/${market}/priority-roads` as const
export const PRIORITY_ROADS_INVENTORY = '/priority-roads/inventory'
export const marketIncidentPriorities = (market: string) =>
    `/markets/${market}/incident-priorities` as const
export const INCIDENT_PRIORITIES_INVENTORY = '/incident-priorities/inventory'
export const marketIncidentLandscape = (market: string) =>
    `/markets/${market}/incident-landscape` as const
export const INCIDENT_LANDSCAPE_INVENTORY =
    '/incident-landscape/inventory' as const

export const marketFillerStatements = (market: string) =>
    `/markets/${market}/filler-statements` as const
export const FILLER_STATEMENTS_INVENTORY = '/filler-statements/inventory'

export const marketVerifiedPronunciations = (market: string) =>
    `/markets/${market}/verified-pronunciations` as const
export const VERIFIED_PRONUNCIATIONS_INVENTORY =
    '/verified-pronunciations/inventory'

// Voices
export const VOICES = '/voices'
export const voiceCalibrate = (id: string) => `/voices/${id}/calibrate` as const

// Voice profiles (the shared voice library — named, versioned variants)
export const VOICE_PROFILES = '/voices/profiles'
export const VOICE_PROFILE_PREVIEW = '/voices/profiles/preview'
export const voiceProfileDetail = (id: string) =>
    `/voices/profiles/${id}` as const
export const voiceProfileVersionRestore = (id: string, version: number) =>
    `/voices/profiles/${id}/versions/${version}/restore` as const
export const voiceProfileArchive = (id: string) =>
    `/voices/profiles/${id}/archive` as const
export const voiceProfileUnarchive = (id: string) =>
    `/voices/profiles/${id}/unarchive` as const
export const voiceProfileDelete = (id: string) =>
    `/voices/profiles/${id}` as const

/** Renders arbitrary copy (an ad, a promo) on this profile's current configuration. */
export const voiceProfileVoiceover = (id: string) =>
    `/voices/profiles/${id}/voiceover` as const

// Delivery styles (the shared delivery library — named, versioned audio-tag policies)
export const DELIVERY_STYLES = '/delivery-styles'
export const DELIVERY_STYLE_PREVIEW = '/delivery-styles/preview'
export const deliveryStyleDetail = (id: string) =>
    `/delivery-styles/${id}` as const
export const deliveryStyleArchive = (id: string) =>
    `/delivery-styles/${id}/archive` as const
export const deliveryStyleUnarchive = (id: string) =>
    `/delivery-styles/${id}/unarchive` as const
export const deliveryStyleDelete = (id: string) =>
    `/delivery-styles/${id}` as const
// Bulletin playbooks (the station-editable diff over the shipped writer prompt).
export const PLAYBOOKS = '/playbooks'
export const playbookDefault = (role: string) =>
    `/playbooks/default/${role}` as const
export const playbookDetail = (id: string) => `/playbooks/${id}` as const
export const playbookRestoreVersion = (id: string, version: number) =>
    `/playbooks/${id}/versions/${version}/restore` as const
export const playbookArchive = (id: string) =>
    `/playbooks/${id}/archive` as const
export const playbookUnarchive = (id: string) =>
    `/playbooks/${id}/unarchive` as const
export const playbookDelete = (id: string) => `/playbooks/${id}` as const
export const PLAYBOOK_PREVIEW = '/playbooks/preview'

// Bookends (the shared intro+outro pair library — a station assigns one pair).
export const BOOKENDS = '/bookends'
export const BOOKEND_PREVIEW = '/bookends/preview'
export const bookendDetail = (id: string) => `/bookends/${id}` as const
export const bookendVersionRestore = (
    id: string,
    // `number` for real calls; the mock adapter passes the ':version' placeholder when building a
    // match pattern, so this accepts a string too rather than forcing a cast at every call site.
    version: number | string
) => `/bookends/${id}/versions/${version}/restore` as const
export const bookendArchive = (id: string) => `/bookends/${id}/archive` as const
export const bookendUnarchive = (id: string) =>
    `/bookends/${id}/unarchive` as const

// Editorial settings (ARN-editable numeric caps, thresholds, and traffic-source denylist).
export const EDITORIAL_SETTINGS = '/editorial/settings'
export const EDITORIAL_SETTINGS_EFFECTIVE = '/editorial/settings/effective'

// Operators (market scoping)
export const OPERATORS = '/operators'
export const OPERATORS_ME = '/operators/me'
export const operatorAssignment = (objectId: string) =>
    `/operators/${objectId}` as const

// Guidance (self-service standing guidance + candidate promotion)
export const GUIDANCE = '/guidance'
export const guidanceDetail = (id: string) => `/guidance/${id}` as const
export const GUIDANCE_PROMOTE = '/guidance/promote'
export const marketGuidance = (market: string) =>
    `/markets/${market}/guidance` as const
export const marketGuidanceDetail = (market: string, id: string) =>
    `/markets/${market}/guidance/${id}` as const
export const marketGuidanceComposed = (market: string) =>
    `/markets/${market}/guidance/composed` as const
export const marketGuidanceCandidates = (market: string) =>
    `/markets/${market}/guidance/candidates` as const
export const stationGuidanceCandidate = (station: string, id: string) =>
    `/stations/${station}/guidance/candidates/${id}` as const
export const GUIDANCE_INVENTORY = '/guidance/inventory'

// Safety terms (admin-only banned words + place-name exceptions)
export const SAFETY_TERMS = '/safety/terms'
export const safetyTermDetail = (id: string) => `/safety/terms/${id}` as const

// Pronunciation dictionaries (the shared pronunciation library — named, versioned TTS pronunciation rules)
export const PRONUNCIATION_DICTIONARIES = '/pronunciation-dictionaries'
// Distinct literal route (no id): previews ONE candidate rule, saved or not, in a chosen voice.
export const PRONUNCIATION_RULE_PREVIEW =
    '/pronunciation-dictionaries/preview-rule' as const
export const pronunciationDictionaryDetail = (id: string) =>
    `/pronunciation-dictionaries/${id}` as const
export const pronunciationDictionaryArchive = (id: string) =>
    `/pronunciation-dictionaries/${id}/archive` as const
export const pronunciationDictionaryUnarchive = (id: string) =>
    `/pronunciation-dictionaries/${id}/unarchive` as const
export const pronunciationDictionaryDelete = (id: string) =>
    `/pronunciation-dictionaries/${id}` as const
// Config transfer (admin-only export/import/approval)
export const CONFIG_EXPORT = '/config/export'
export const CONFIG_SCHEMA = '/config/schema'
export const CONFIG_IMPORT = '/config/import'
export const CONFIG_IMPORTS_PENDING = '/config/imports/pending'
export const configImportApprove = (id: string) =>
    `/config/imports/pending/${id}/approve` as const
export const configImportReject = (id: string) =>
    `/config/imports/pending/${id}/reject` as const

// Activity / change audit (operator-readable)
export const ACTIVITY = '/activity'
export const ledgerMarket = (market: string) => `/ledger/${market}` as const
export const SPOT_CHECKS = '/spot-checks'
export const spotCheck = (jobId: string) => `/spot-checks/${jobId}` as const
