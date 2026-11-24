#!/usr/bin/env node
// =============================================================================
// fetch-here-traffic.mjs — pull REAL incidents from the HERE Traffic API and
// regenerate src/lib/api/mock/live-traffic.ts, mirroring the backend pipeline:
//
//   HereTrafficProvider.MapToDomain  → severity / functional-class derivation
//   CorridorAggregator.AggregateByRoad → road keys, grouping, impact scores
//   Routing enrichment               → drive time vs typical (delay minutes)
//   Script generation                → broadcast-style sentences with lineage
//
// The API key is read from HERE_API_KEY or the backend's local.settings.json.
// It is never written into the generated output.
//
// Usage:  node scripts/fetch-here-traffic.mjs [--no-routing]
// =============================================================================

import { readFileSync, writeFileSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUT_PATH = resolve(ROOT, 'src/lib/api/mock/live-traffic.ts')
const NO_ROUTING = process.argv.includes('--no-routing')

// ── Credentials ──────────────────────────────────────────────────────────────

function loadApiKey() {
    if (process.env.HERE_API_KEY) return process.env.HERE_API_KEY
    const settingsPath = resolve(
        ROOT,
        '../src/ArnBulletinGenerator.Functions/local.settings.json'
    )
    try {
        const settings = JSON.parse(readFileSync(settingsPath, 'utf8'))
        const key = settings?.Values?.['Here__ApiKey']
        if (key) return key
    } catch {
        /* fall through */
    }
    console.error(
        'No HERE API key. Set HERE_API_KEY or provide backend local.settings.json.'
    )
    process.exit(1)
}

const API_KEY = loadApiKey()
const TRAFFIC_BASE = 'https://data.traffic.hereapi.com/v7'
const ROUTER_BASE = 'https://router.hereapi.com/v8'

// ── Markets (bboxes match the existing mock fixtures) ────────────────────────

const MARKETS = [
    {
        id: 'sydney',
        bbox: [150.5, -34.1, 151.5, -33.6],
        priorityRoads: ['M1 Pacific Motorway', 'M3', 'Smith Street Motorway'],
        keyPrefix: 'inc-syd',
    },
    {
        id: 'melbourne',
        bbox: [144.5, -38.2, 145.6, -37.4],
        priorityRoads: ['M1', 'M2', 'West Gate Freeway', 'Monash Freeway'],
        keyPrefix: 'inc-mel',
    },
    {
        id: 'brisbane',
        bbox: [152.8, -27.7, 153.3, -27.3],
        priorityRoads: [],
        keyPrefix: 'inc-bne',
    },
    {
        id: 'goldcoast',
        bbox: [153.3, -28.2, 153.6, -27.8],
        priorityRoads: [],
        keyPrefix: 'inc-gc',
    },
    {
        id: 'perth',
        bbox: [115.6, -32.3, 116.1, -31.6],
        priorityRoads: [
            'Mitchell Freeway',
            'Kwinana Freeway',
            'Graham Farmer Freeway',
        ],
        keyPrefix: 'inc-per',
    },
    {
        id: 'adelaide',
        bbox: [138.4, -35.2, 138.8, -34.7],
        priorityRoads: [
            'South Eastern Freeway',
            'Main North Road',
            'Port Wakefield Road',
        ],
        keyPrefix: 'inc-adl',
    },
]

const ALLOWED_TYPES = new Set([
    'accident',
    'congestion',
    'construction',
    'disabledVehicle',
    'laneRestriction',
    'roadClosure',
    'roadHazard',
    'plannedEvent',
])

// ── Backend ports: known roads, severity, road-key extraction ────────────────

/** HereTrafficProvider.KnownRoadFunctionalClass */
const KNOWN_ROAD_FC = new Map(
    Object.entries({
        M1: 1,
        M2: 1,
        M3: 1,
        M4: 1,
        M5: 1,
        M7: 1,
        M8: 1,
        M9: 1,
        M12: 1,
        'Warringah Freeway': 1,
        'Warringah Fwy': 1,
        'Eastern Distributor': 1,
        'Cross City Tunnel': 1,
        'Sydney Harbour Bridge': 1,
        'Sydney Harbour Tunnel': 1,
        'Anzac Bridge': 1,
        WestConnex: 1,
        NorthConnex: 1,
        'Prospect Highway': 2,
        'Hume Highway': 2,
        'Princes Highway': 2,
        'Pacific Highway': 2,
        'Great Western Highway': 2,
        'Parramatta Road': 2,
        'Victoria Road': 2,
        'Monash Freeway': 1,
        'Eastern Freeway': 1,
        'Western Freeway': 1,
        'West Gate Freeway': 1,
        'West Gate Bridge': 1,
        CityLink: 1,
        EastLink: 1,
        'Tullamarine Freeway': 1,
        'Calder Freeway': 1,
        'South Eastern Freeway': 1,
        'Bolte Bridge': 1,
        'Pacific Motorway': 1,
        'Gateway Motorway': 1,
        'Ipswich Motorway': 1,
        'Logan Motorway': 1,
        'Bruce Highway': 1,
    })
)

/** HereTrafficProvider.ApplyKnownRoadOverride */
function applyKnownRoadOverride(linkFc, description) {
    const lower = description.toLowerCase()
    for (const [road, fc] of KNOWN_ROAD_FC)
        if (lower.includes(road.toLowerCase())) return Math.min(linkFc, fc)
    return linkFc
}

/** HereTrafficProvider.DeriveSeverity (1 = critical … 4 = minor) */
function deriveSeverity(functionalClass, type, lengthMeters) {
    let base =
        functionalClass <= 2
            ? 1
            : functionalClass === 3
              ? 2
              : functionalClass === 4
                ? 3
                : 4
    if (/^(accident|congestion)$/i.test(type)) base = Math.max(1, base - 1)
    if (
        (functionalClass >= 5 && lengthMeters > 1000) ||
        (functionalClass === 4 && lengthMeters > 2000)
    )
        base = Math.max(1, base - 1)
    return base
}

/** CorridorAggregator.MajorRoadPatterns (priority order) */
const ROAD_PATTERNS = [
    /\b(M\d+)\b/i,
    /\b(Monash|Eastern|Western|South Eastern)\s+(Freeway|Motorway)\b/i,
    /\b(West Gate|Westgate)\s*(Bridge|Tunnel|Freeway)?\b/i,
    /\b(CityLink|EastLink)\b/i,
    /\b(Tullamarine|Tulla)\s*(Freeway)?\b/i,
    /\b(Calder|Hume|Princes|Great Western)\s+(Highway|Freeway)\b/i,
    /\b(Harbour|Sydney Harbour)\s*(Bridge|Tunnel)\b/i,
    /\b(Anzac|Lane Cove)\s*(Bridge|Tunnel)\b/i,
    /\b(Cross City|Eastern Distributor|Western Distributor)\b/i,
    /\b(WestConnex|NorthConnex)\b/i,
    /\b(Rozelle|Parramatta Road)\b/i,
    /\b(\w+)\s+(Road|Rd|Street|St|Highway|Hwy|Freeway|Fwy|Bridge|Br|Tunnel|Avenue|Ave|Terrace|Tce|Parade|Pde|Drive|Dr|Place|Pl|Crescent|Cres|Court|Ct|Lane|Ln|Boulevard|Blvd|Close|Cl|Grove|Gr|Circuit|Cct|Way)\b/i,
    /\b(Ring Road|Bypass)\b/i,
]

const ABBREVIATIONS = {
    Rd: 'Road',
    St: 'Street',
    Hwy: 'Highway',
    Fwy: 'Freeway',
    Br: 'Bridge',
    Ave: 'Avenue',
    Tce: 'Terrace',
    Pde: 'Parade',
    Dr: 'Drive',
    Pl: 'Place',
    Cres: 'Crescent',
    Ct: 'Court',
    Ln: 'Lane',
    Blvd: 'Boulevard',
    Cl: 'Close',
    Gr: 'Grove',
    Cct: 'Circuit',
    Westgate: 'West Gate',
    Tulla: 'Tullamarine',
}

function normalizeRoadName(name) {
    if (!name?.trim()) return ''
    return name
        .trim()
        .replace(
            /\b(Rd|St|Hwy|Fwy|Br|Ave|Tce|Pde|Dr|Pl|Cres|Ct|Ln|Blvd|Cl|Gr|Cct|Westgate|Tulla)\b/gi,
            (m) => {
                const key = Object.keys(ABBREVIATIONS).find(
                    (k) => k.toLowerCase() === m.toLowerCase()
                )
                return key ? ABBREVIATIONS[key] : m
            }
        )
}

const DIRECTION_KEYWORDS = {
    citybound: 'citybound',
    'city-bound': 'citybound',
    'city bound': 'citybound',
    inbound: 'citybound',
    outbound: 'outbound',
    eastbound: 'eastbound',
    'east-bound': 'eastbound',
    westbound: 'westbound',
    'west-bound': 'westbound',
    northbound: 'northbound',
    'north-bound': 'northbound',
    southbound: 'southbound',
    'south-bound': 'southbound',
}

function extractRoadKey(description) {
    let roadName = ''
    for (const pattern of ROAD_PATTERNS) {
        const match = description.match(pattern)
        if (match) {
            roadName = normalizeRoadName(match[0])
            break
        }
    }
    let direction = null
    const lower = description.toLowerCase()
    for (const [keyword, normalized] of Object.entries(DIRECTION_KEYWORDS))
        if (lower.includes(keyword)) {
            direction = normalized
            break
        }
    return { roadName, direction }
}

/** CorridorAggregator.TypePriority (lower = wins primary type) */
const TYPE_PRIORITY = {
    accident: 1,
    roadClosure: 2,
    disabledVehicle: 3,
    roadHazard: 4,
    laneRestriction: 5,
    construction: 6,
    plannedEvent: 7,
    congestion: 8,
}

const TYPE_PHRASE = {
    accident: 'crash',
    roadClosure: 'road closure',
    disabledVehicle: 'breakdown',
    roadHazard: 'hazard',
    laneRestriction: 'lane restrictions',
    construction: 'roadworks',
    plannedEvent: 'planned event',
    congestion: 'heavy traffic',
}

/** CorridorAggregator.CalculateImpactScore — exact port */
function calculateImpactScore(severity, functionalClass, lengthMeters, count) {
    const roadScore = (6 - functionalClass) * 20.0
    const severityScore = (5 - severity) * 20.0
    const lengthScore =
        lengthMeters > 0 ? Math.min(Math.log10(lengthMeters) * 10.0, 25.0) : 0.0
    const densityScore =
        count > 1 ? Math.min(Math.log2(count) * 15.0, 50.0) : 0.0
    return roadScore + severityScore + lengthScore + densityScore
}

// ── HERE fetch + domain mapping ───────────────────────────────────────────────

async function getJson(url, label) {
    const res = await fetch(url)
    if (!res.ok)
        throw new Error(`${label} HTTP ${res.status}: ${await res.text()}`)
    return res.json()
}

/** HERE caps the filter at 1°×1° — clamp around the bbox centre. */
function clampBbox([w, s, e, n], maxDeg = 0.99) {
    const cx = (w + e) / 2
    const cy = (s + n) / 2
    const halfW = Math.min(e - w, maxDeg) / 2
    const halfH = Math.min(n - s, maxDeg) / 2
    return [cx - halfW, cy - halfH, cx + halfW, cy + halfH].map(
        (v) => +v.toFixed(4)
    )
}

async function fetchIncidents(market) {
    const [w, s, e, n] = clampBbox(market.bbox)
    const url = `${TRAFFIC_BASE}/incidents?in=bbox:${w},${s},${e},${n}&locationReferencing=shape&apiKey=${API_KEY}`
    const data = await getJson(url, `incidents:${market.id}`)
    return data.results ?? []
}

/** Mirror of HereTrafficProvider.MapToDomain */
function mapToDomain(item) {
    const details = item.incidentDetails ?? {}
    const links = item.location?.shape?.links ?? []
    const points = links.flatMap((l) => l.points ?? [])
    const first = points[0]
    const last = points[points.length - 1]
    const totalLength = links.reduce((sum, l) => sum + (l.length ?? 0), 0)
    let minFc = links.length
        ? Math.min(
              ...links.map((l) =>
                  l.functionalClass > 0 ? l.functionalClass : 5
              )
          )
        : 5
    const description = cleanText((details.description?.value ?? '').trim())
    minFc = applyKnownRoadOverride(minFc, description)

    // Backend rule: the summary repeats the event — only carry it when it
    // adds detail the description doesn't already contain.
    const rawSummary = cleanText((details.summary?.value ?? '').trim())
    const summary =
        rawSummary &&
        !description
            .toLowerCase()
            .includes(rawSummary.replace(/\.$/, '').toLowerCase())
            ? rawSummary
            : ''

    // Extract the full road-segment polyline so the map draws polylines
    // instead of circular markers. Valid polylines need ≥2 points.
    const shape =
        points.length >= 2
            ? points.map((p) => ({ lat: p.lat, lng: p.lng }))
            : null

    return {
        id: details.id ?? '',
        type: details.type ?? 'roadHazard',
        description,
        summary,
        lastUpdate: details.lastUpdateTime ?? null,
        severity: deriveSeverity(minFc, details.type ?? '', totalLength),
        functionalClass: minFc,
        lengthMeters: Math.round(totalLength),
        lat: first?.lat ?? null,
        lng: first?.lng ?? null,
        endLat: last?.lat ?? null,
        endLng: last?.lng ?? null,
        shape,
    }
}

/** HERE data quirk: street type and direction arrive concatenated
 *  ("Alfred StreetNorth"); reinsert the space. */
function cleanText(text) {
    return text.replace(
        /\b(Street|Road|Avenue|Highway|Motorway|Freeway|Drive|Parade|Lane)(North|South|East|West)\b/g,
        '$1 $2'
    )
}

function buildHeadline(incident, roadName) {
    const phrase = TYPE_PHRASE[incident.type] ?? incident.type
    if (roadName) return `${roadName} — ${phrase}`
    // No recognisable road: lead with the first clause of the description
    const clause = incident.description.split(/ - |[,.]/)[0].trim()
    return clause.length > 64 ? `${clause.slice(0, 63)}…` : clause || phrase
}

// ── Routing enrichment (drive time vs typical) ───────────────────────────────

async function enrichDriveTime(corridor) {
    if (NO_ROUTING) return
    const m = corridor.members
    const withCoords = m.filter((i) => i.lat != null)
    if (withCoords.length === 0 || corridor.functionalClass > 3) return
    const origin = withCoords[0]
    const dest = withCoords[withCoords.length - 1]
    const oLat = origin.lat
    const oLng = origin.lng
    const dLat = dest.endLat ?? dest.lat
    const dLng = dest.endLng ?? dest.lng
    if (oLat === dLat && oLng === dLng) return
    try {
        const url =
            `${ROUTER_BASE}/routes?transportMode=car&origin=${oLat},${oLng}` +
            `&destination=${dLat},${dLng}&return=summary,typicalDuration&apiKey=${API_KEY}`
        const data = await getJson(url, `route:${corridor.roadName}`)
        const summary = data.routes?.[0]?.sections?.[0]?.summary
        if (!summary?.duration || !summary?.typicalDuration) return
        const drive = Math.round(summary.duration / 60)
        const normal = Math.round(summary.typicalDuration / 60)
        corridor.driveTimeMinutes = drive
        corridor.normalDriveTimeMinutes = normal
        if (drive - normal >= 1) corridor.delayMinutes = drive - normal
    } catch (err) {
        console.warn(
            `  routing skipped for ${corridor.roadName}: ${err.message}`
        )
    }
}

// ── Script generation (broadcast-style, lineage-aligned) ─────────────────────

const NEEDS_ARTICLE =
    /^(M\d+|.*(Motorway|Freeway|Highway|Bridge|Tunnel|Distributor|Bypass|Ring Road).*)$/i

function roadPhrase(roadName) {
    return NEEDS_ARTICLE.test(roadName) ? `the ${roadName}` : roadName
}

/**
 * One or two sentences per corridor. Sentences must survive segmentScript:
 * single terminal period, no internal abbreviations-with-periods, no
 * decimal numbers. Delay sentences echo the corridor's routed delay so the
 * claim checker reads them as sourced (one deliberate exception is planted
 * for the Melbourne demo fixture).
 */
function corridorSentences(corridor, { plantContradiction = false } = {}) {
    const sentences = []
    const road = roadPhrase(corridor.roadName)
    const dir = corridor.direction ? ` ${corridor.direction}` : ''
    const km = Math.round(corridor.totalLengthKm)
    const extent = km >= 2 ? ` over ${km} kilometres` : ''
    const phraseByType = {
        accident: `A crash on ${road}${dir} has traffic moving slowly${extent}.`,
        roadClosure: `${capitalize(road)} is closed${dir}${extent}.`,
        disabledVehicle: `A breakdown on ${road}${dir} is blocking a lane.`,
        roadHazard: `A hazard on ${road}${dir} is slowing traffic.`,
        laneRestriction: `Lane restrictions are in place on ${road}${dir}${extent}.`,
        construction: `Roadworks on ${road}${dir} have lanes closed${extent}.`,
        plannedEvent: `A planned event is affecting ${road}${dir}.`,
        congestion: `Traffic is heavy on ${road}${dir}${extent}.`,
    }
    sentences.push({
        Text: phraseByType[corridor.primaryType] ?? phraseByType.roadHazard,
        CorridorKeys: [corridor.roadName],
    })
    if (corridor.delayMinutes != null && corridor.delayMinutes >= 2) {
        // The claim checker flags at |diff| >= 5 — a planted claim overstates
        // the real delay by 8 minutes.
        const claimed = plantContradiction
            ? corridor.delayMinutes + 8
            : corridor.delayMinutes
        sentences.push({
            Text: `Expect delays of around ${claimed} minutes.`,
            CorridorKeys: [corridor.roadName],
        })
    }
    return sentences
}

function capitalize(text) {
    return text.charAt(0).toUpperCase() + text.slice(1)
}

// ── Per-market pipeline ──────────────────────────────────────────────────────

async function buildMarket(market) {
    console.log(`\n→ ${market.id}: fetching incidents…`)
    const raw = await fetchIncidents(market)

    // Dedup by id AND by description (HERE repeats roadwork notices per
    // ramp), filter to reportable types, map to domain
    const seen = new Set()
    const seenDescriptions = new Set()
    const mapped = []
    for (const item of raw) {
        const inc = mapToDomain(item)
        if (!inc.id || seen.has(inc.id)) continue
        seen.add(inc.id)
        if (!ALLOWED_TYPES.has(inc.type)) continue
        if (!inc.description) continue
        const descKey = inc.description.toLowerCase()
        if (seenDescriptions.has(descKey)) continue
        seenDescriptions.add(descKey)
        mapped.push(inc)
    }
    console.log(`  ${raw.length} raw → ${mapped.length} mapped`)

    // FilterAndSortIncidents ordering: severity, then FC, then length desc
    mapped.sort(
        (a, b) =>
            a.severity - b.severity ||
            a.functionalClass - b.functionalClass ||
            b.lengthMeters - a.lengthMeters
    )

    // The visible incident pool (mock-sized, like the backend's top set)
    const pool = mapped.slice(0, 10)
    const now = Date.now()
    pool.forEach((inc, i) => {
        inc.key = `${market.keyPrefix}-${String(i + 1).padStart(2, '0')}`
        const rk = extractRoadKey(inc.description)
        inc.roadName = rk.roadName
        inc.direction = rk.direction
        inc.isNew = inc.lastUpdate
            ? now - Date.parse(inc.lastUpdate) < 45 * 60 * 1000
            : false
    })

    // CorridorAggregator.AggregateByRoad over the pool
    const groups = new Map()
    for (const inc of pool) {
        const name = inc.roadName || `Unknown-${inc.key}`
        const groupKey = `${name.toLowerCase()}|${inc.direction ?? ''}`
        if (!groups.has(groupKey))
            groups.set(groupKey, {
                name,
                direction: inc.direction,
                members: [],
            })
        groups.get(groupKey).members.push(inc)
    }

    let corridors = [...groups.values()].map((g) => {
        const members = g.members
        const worstSeverity = Math.min(...members.map((i) => i.severity))
        const bestFc = Math.min(...members.map((i) => i.functionalClass))
        const totalLength = members.reduce((s, i) => s + i.lengthMeters, 0)
        const primaryType = [...members].sort(
            (a, b) =>
                (TYPE_PRIORITY[a.type] ?? 99) - (TYPE_PRIORITY[b.type] ?? 99)
        )[0].type
        const primary = [...members].sort(
            (a, b) => a.severity - b.severity || b.lengthMeters - a.lengthMeters
        )[0]
        const typePhrase = TYPE_PHRASE[primaryType] ?? primaryType
        const dirText = g.direction ? ` ${g.direction}` : ''
        const isPlaceholder = g.name.startsWith('Unknown-')
        let impact = calculateImpactScore(
            worstSeverity,
            bestFc,
            totalLength,
            members.length
        )
        const priorityBoosted = market.priorityRoads.some((p) =>
            g.name.toLowerCase().includes(p.toLowerCase())
        )
        if (priorityBoosted) impact *= 1.3
        return {
            roadName: isPlaceholder ? 'an unnamed road' : g.name,
            isPlaceholder,
            severity: worstSeverity,
            functionalClass: bestFc,
            totalLengthKm: +(totalLength / 1000).toFixed(1),
            impactScore: +impact.toFixed(1),
            primaryType,
            description:
                members.length === 1
                    ? `${typePhrase}${dirText}`
                    : `${members.length} incidents including ${typePhrase}${dirText}`,
            direction: g.direction,
            members,
            lat: primary.lat,
            lng: primary.lng,
            priorityBoosted,
            driveTimeMinutes: null,
            normalDriveTimeMinutes: null,
            delayMinutes: null,
        }
    })

    // Placeholder corridors only when there is little else to show
    const known = corridors.filter((c) => !c.isPlaceholder)
    if (known.length >= 2) corridors = known

    corridors.sort((a, b) => b.impactScore - a.impactScore)
    corridors = corridors.slice(0, 7)

    console.log(`  ${corridors.length} corridors; routing top corridors…`)
    for (const corridor of corridors.slice(0, 5))
        await enrichDriveTime(corridor)

    const selectedCount = Math.min(4, Math.max(1, corridors.length))
    const corridorFixtures = corridors.map((c, i) => ({
        RoadName: c.roadName,
        Severity: c.severity,
        ImpactScore: c.impactScore,
        PrimaryType: c.primaryType,
        Description: c.description,
        IncidentCount: c.members.length,
        TotalLengthKm: c.totalLengthKm,
        Rank: i + 1,
        IsSelected: i < selectedCount,
        FunctionalClass: c.functionalClass,
        Direction: c.direction,
        Suburb: null,
        NormalDriveTimeMinutes: c.normalDriveTimeMinutes,
        DelayMinutes: c.delayMinutes,
        Latitude: c.lat,
        Longitude: c.lng,
        ...(c.priorityBoosted ? { PriorityBoosted: true } : {}),
        IncidentKeys: c.members.map((m) => m.key),
    }))

    const incidentFixtures = pool.map((inc) => ({
        Key: inc.key,
        Priority: 5 - inc.severity, // web Priority: higher = more severe
        Headline: buildHeadline(inc, inc.roadName),
        Detail: inc.summary
            ? `${inc.description} ${inc.summary}`.trim()
            : inc.description,
        FunctionalClass: inc.functionalClass,
        LengthMeters: inc.lengthMeters,
        Latitude: inc.lat,
        Longitude: inc.lng,
        IsNew: inc.isNew,
        // Road-segment polyline for the map; null when HERE returned a
        // single-point incident (e.g. a pinpoint hazard).
        // Keys are PascalCase ({ Lat, Lng }) to match the fixture format.
        Shape: inc.shape
            ? inc.shape.map((p) => ({ Lat: p.lat, Lng: p.lng }))
            : null,
    }))

    // Script: one block per selected corridor; melbourne plants exactly one
    // delay contradiction on its first delay-bearing corridor (demo guard).
    const selected = corridors.slice(0, selectedCount)

    // Last resort when live traffic is completely free-flowing: give the top
    // routed corridor a small coherent delay so the demo contradiction (and
    // the delay UI) still have something to bite on. Logged loudly.
    if (
        market.id === 'melbourne' &&
        !selected.some((c) => c.delayMinutes != null && c.delayMinutes >= 2)
    ) {
        const target =
            selected.find((c) => c.normalDriveTimeMinutes != null) ??
            selected[0]
        target.normalDriveTimeMinutes ??= 10
        target.delayMinutes = 6
        target.driveTimeMinutes = target.normalDriveTimeMinutes + 6
        const fixture = corridorFixtures.find(
            (f) => f.RoadName === target.roadName
        )
        if (fixture) {
            fixture.DelayMinutes = target.delayMinutes
            fixture.DriveTimeMinutes = target.driveTimeMinutes
            fixture.NormalDriveTimeMinutes = target.normalDriveTimeMinutes
        }
        console.warn(
            `  NOTE: roads were free-flowing — synthesized a 6 min delay on ${target.roadName} for the contradiction demo`
        )
    }

    let planted = false
    const trafficSentences = selected.flatMap((c) => {
        const plant =
            market.id === 'melbourne' &&
            !planted &&
            c.delayMinutes != null &&
            c.delayMinutes >= 2
        if (plant) planted = true
        return corridorSentences(c, { plantContradiction: plant })
    })

    const selectedIncidents = selected
        .map((c) => incidentFixtures.find((i) => i.Key === c.members[0].key))
        .filter(Boolean)

    return {
        FetchedAtIso: new Date().toISOString(),
        Bbox: market.bbox,
        Incidents: incidentFixtures,
        Corridors: corridorFixtures,
        SelectedIncidents: selectedIncidents,
        TrafficSentences: trafficSentences,
        ContradictionPlanted: planted,
    }
}

// ── Output ───────────────────────────────────────────────────────────────────

const results = {}
for (const market of MARKETS) {
    results[market.id] = await buildMarket(market)
}

const banner = `// =============================================================================
// GENERATED FILE — real HERE Traffic API snapshot. Do not edit by hand.
// Regenerate with:  node scripts/fetch-here-traffic.mjs
// Fetched: ${new Date().toISOString()}
// =============================================================================

import type { IncidentFixture, CorridorFixture } from './fixtures'

export interface LiveTrafficSentence {
    Text: string
    CorridorKeys: string[]
}

export interface LiveTrafficFixture {
    FetchedAtIso: string
    Bbox: number[]
    Incidents: IncidentFixture[]
    Corridors: (CorridorFixture & { IncidentKeys: string[] })[]
    SelectedIncidents: IncidentFixture[]
    /** Broadcast-style traffic sentences with corridor lineage. */
    TrafficSentences: LiveTrafficSentence[]
    /** True when exactly one delay contradiction was planted (demo guard). */
    ContradictionPlanted: boolean
}

export const LIVE_TRAFFIC: Record<
    'sydney' | 'melbourne' | 'brisbane' | 'goldcoast' | 'perth' | 'adelaide',
    LiveTrafficFixture
> = `

writeFileSync(OUT_PATH, banner + JSON.stringify(results, null, 4) + '\n')
console.log(`\n✓ wrote ${OUT_PATH}`)
for (const [id, m] of Object.entries(results))
    console.log(
        `  ${id}: ${m.Incidents.length} incidents, ${m.Corridors.length} corridors, ` +
            `${m.TrafficSentences.length} sentences${m.ContradictionPlanted ? ', 1 planted contradiction' : ''}`
    )
