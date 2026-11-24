// ---------------------------------------------------------------------------
// Severity palette — single source of truth.
//
// Incident severity and corridor severity share ONE scale: 1 = critical …
// 4 = minor (the DTO field misleadingly named `priority` carries this severity
// — proven by the regen round-trip JobIncident.Priority → TrafficIncident.
// Severity). HERE floors most metro incidents at 1, so the commuter-facing
// MAGNITUDE (Severe/Major/Moderate/Minor, derived per corridor from
// delay/queue/type) is what actually tiers a pin or row — pull colour/label
// from the `corridorDisplay*` helpers + `corridorTierByIncidentKey`, never the
// flat per-incident severity. Minor/low is a neutral grey on purpose — green
// is reserved for good-runs/success.
// ---------------------------------------------------------------------------

const CORRIDOR_SEVERITY_DOT: Record<number, string> = {
    1: 'bg-destructive',
    2: 'bg-orange-400',
    3: 'bg-warning',
    4: 'bg-muted-foreground/50',
}

const CORRIDOR_SEVERITY_LABEL: Record<number, string> = {
    1: 'Critical',
    2: 'Major',
    3: 'Moderate',
    4: 'Minor',
}

/** Tailwind dot class for a corridor severity (1 = critical … 4 = minor). */
export function corridorSeverityDotClass(severity: number): string {
    return CORRIDOR_SEVERITY_DOT[severity] ?? 'bg-muted-foreground/50'
}

export function corridorSeverityLabel(severity: number): string {
    return CORRIDOR_SEVERITY_LABEL[severity] ?? `Severity ${severity}`
}

/** Raw CSS colors for non-Tailwind contexts (leaflet divIcon HTML).
 *  Same palette as the dot classes above. */
const PIN_COLORS = {
    red: 'hsl(var(--destructive))',
    orange: 'hsl(27, 96%, 61%)', // tailwind orange-400, as used by the dots
    amber: 'hsl(var(--warning))',
    muted: 'hsl(var(--muted-foreground))',
} as const

export function corridorSeverityPinColor(severity: number): string {
    switch (severity) {
        case 1:
            return PIN_COLORS.red
        case 2:
            return PIN_COLORS.orange
        case 3:
            return PIN_COLORS.amber
        default:
            return PIN_COLORS.muted
    }
}

// ---------------------------------------------------------------------------
// Corridor MAGNITUDE — the commuter-facing tier (Severe/Major/Moderate/Minor)
// the backend now derives (CorridorMagnitude) and sends on the corridor DTO.
// HERE's raw severity floors nearly every metro corridor at 1 ("critical"), so
// these helpers prefer magnitude when present and fall back to severity for
// cached jobs that predate the field. Magnitude reuses the severity palette via
// the natural tier↔severity mapping, so the two surfaces never drift apart.
// ---------------------------------------------------------------------------

const MAGNITUDE_TO_SEVERITY: Record<string, number> = {
    Severe: 1,
    Major: 2,
    Moderate: 3,
    Minor: 4,
}

export function magnitudeSeverity(
    magnitude: string | null | undefined,
    severity: number
): number {
    return (
        (magnitude != null ? MAGNITUDE_TO_SEVERITY[magnitude] : undefined) ??
        severity
    )
}

/** Corridor dot class, preferring the commuter magnitude tier over flat severity. */
export function corridorDisplayDotClass(
    magnitude: string | null | undefined,
    severity: number
): string {
    return corridorSeverityDotClass(magnitudeSeverity(magnitude, severity))
}

/** Corridor pin colour, preferring the commuter magnitude tier over flat severity. */
export function corridorDisplayPinColor(
    magnitude: string | null | undefined,
    severity: number
): string {
    return corridorSeverityPinColor(magnitudeSeverity(magnitude, severity))
}

/** Corridor label, preferring the magnitude word ("Severe") over the severity label ("Critical"). */
export function corridorDisplayLabel(
    magnitude: string | null | undefined,
    severity: number
): string {
    return magnitude != null && MAGNITUDE_TO_SEVERITY[magnitude] != null
        ? magnitude
        : corridorSeverityLabel(severity)
}

/** A corridor's display tier as carried to a pin: the commuter magnitude word
 *  (when the API provides it) plus the raw severity it falls back to. */
export interface CorridorTier {
    readonly magnitude: string | null
    readonly severity: number
}

/**
 * Index every incident key to the corridor that carries it, so a pin can colour
 * by its corridor's commuter MAGNITUDE rather than the flat per-incident
 * severity HERE floors at "critical". An incident belongs to at most one
 * corridor; later corridors win on the rare shared key.
 */
export function corridorTierByIncidentKey(
    corridors: readonly {
        readonly incidentKeys?: readonly string[] | null
        readonly magnitude?: string | null
        readonly severity: number
    }[]
): Map<string, CorridorTier> {
    const index = new Map<string, CorridorTier>()
    for (const c of corridors) {
        for (const key of c.incidentKeys ?? []) {
            index.set(key, {
                magnitude: c.magnitude ?? null,
                severity: c.severity,
            })
        }
    }
    return index
}

const ROAD_CLASS_LABELS: Record<number, string> = {
    1: 'Motorway',
    2: 'Highway',
    3: 'Major road',
    4: 'Collector',
    5: 'Local road',
}

/** HERE functional class (1 = motorway … 5 = local) to a human road class. */
export function roadClassLabel(functionalClass?: number): string | null {
    if (functionalClass == null) return null
    return ROAD_CLASS_LABELS[functionalClass] ?? null
}

// ---------------------------------------------------------------------------
// Headline / detail parsing. HERE phrases incidents as "{type} on {Road}" with
// a detail like "At Anzac Highway - Road construction". A single roadworks zone
// is reported as many short same-road segments, so the evidence index groups by
// road and shows each segment's distinguishing anchor instead of the repeated
// headline. Kept here so every surface parses incidents the same way.
// ---------------------------------------------------------------------------

const INCIDENT_EVENT_LABELS: Record<string, string> = {
    construction: 'Roadworks',
    roadClosure: 'Road closure',
    accident: 'Crash',
    congestion: 'Congestion',
    disabledVehicle: 'Breakdown',
    roadHazard: 'Hazard',
    laneRestriction: 'Lane restriction',
    plannedEvent: 'Event',
    massTransit: 'Transit delay',
    weather: 'Weather',
}

/** Road an incident headline anchors on ("construction on South Rd" → "South Rd"). */
export function incidentRoad(headline: string): string {
    const idx = headline.toLowerCase().lastIndexOf(' on ')
    return idx >= 0 ? headline.slice(idx + 4).trim() : headline.trim()
}

/** Human event label from a headline's leading type ("roadClosure on …" → "Road closure"). */
export function incidentEvent(headline: string): string {
    const idx = headline.toLowerCase().lastIndexOf(' on ')
    const raw = (idx > 0 ? headline.slice(0, idx) : headline).trim()
    if (INCIDENT_EVENT_LABELS[raw]) return INCIDENT_EVENT_LABELS[raw]
    const spaced = raw.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
    return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

/** Distinguishing location anchor from an incident detail, or null when the
 *  detail adds nothing beyond the event itself ("At Anzac Highway - Road
 *  construction" → "At Anzac Highway"; "Closed" / "Road construction" → null). */
export function incidentAnchor(detail: string): string | null {
    const d = detail?.trim()
    if (!d) return null
    const dash = d.indexOf(' - ')
    const head = (dash >= 0 ? d.slice(0, dash) : d).trim()
    if (
        !head ||
        /^(closed|road ?works?|road construction|construction|delays?|incident)$/i.test(
            head
        )
    )
        return null
    return head
}

// ---------------------------------------------------------------------------
// Adjust-page fact line — everything the record carries that the headline
// doesn't say, composed into the single extra line the rundown can afford per
// incident: the parsed detail, direction of travel, lane state (or the road
// authority's own reason), and the event's active window. Most incidents
// carry only a detail; the line renders only the parts present, so a plain
// incident never gains empty scaffolding.
// ---------------------------------------------------------------------------

const shortDate = (iso: string): string =>
    new Date(iso).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
    })

/** The event's active window, when the record carries one: "20 Aug – 22 Aug"
 *  when both ends are known, "until 22 Aug" for an open-started event, and
 *  "from 20 Aug" only while the start is still in the future (a stale past
 *  start adds nothing a listener can act on). */
export function incidentWindowLabel(incident: {
    readonly startTimeUtc?: string | null
    readonly endTimeUtc?: string | null
}): string | null {
    const { startTimeUtc: start, endTimeUtc: end } = incident
    const futureStart = start && new Date(start).getTime() > Date.now()
    if (futureStart && end) return `${shortDate(start)} – ${shortDate(end)}`
    if (end) return `until ${shortDate(end)}`
    if (futureStart) return `from ${shortDate(start)}`
    return null
}

/** Compact fact line for an incident row: detail, direction, lane state
 *  (HERE's own summary) or, failing that, the road authority's free-text
 *  reason — never both, to keep the line short — then the active window. */
export function incidentFactLine(incident: {
    readonly detail?: string | null
    readonly direction?: string | null
    readonly laneDetail?: string | null
    readonly comment?: string | null
    readonly startTimeUtc?: string | null
    readonly endTimeUtc?: string | null
}): string | null {
    const parts = [
        incident.detail,
        incident.direction,
        incident.laneDetail ?? incident.comment,
        incidentWindowLabel(incident),
    ].filter((p): p is string => Boolean(p && p.trim()))
    return parts.length > 0 ? parts.join(' · ') : null
}

/** Per-row provenance tag: the contributing feeds' display names when any
 *  road authority contributed — silent for plain HERE-only rows, so the tag
 *  marks the exception, not the default. */
export function incidentSourceTag(
    sources: readonly string[] | null | undefined,
    displayName: (id: string) => string
): string | null {
    if (!sources?.length) return null
    const nonHere = sources.filter((s) => s.toLowerCase() !== 'here')
    if (nonHere.length === 0) return null
    return sources.map(displayName).join(' + ')
}

// ---------------------------------------------------------------------------
// Corridor type labels and flow-clearness helpers. Shared between the adjust
// and draft rundown tables so neither duplicates the mapping.
// ---------------------------------------------------------------------------

const TYPE_LABELS: Record<string, string> = {
    accident: 'Accident',
    roadClosure: 'Road closure',
    closure: 'Closure',
    construction: 'Roadworks',
    roadworks: 'Roadworks',
    congestion: 'Congestion',
    disabledVehicle: 'Broken-down vehicle',
    hazard: 'Hazard',
    event: 'Event',
    laneRestriction: 'Lane restriction',
    plannedEvent: 'Planned event',
}

/** Human-readable corridor type from the HERE incident primary type code. */
export function corridorTypeLabel(primaryType: string): string {
    if (TYPE_LABELS[primaryType]) return TYPE_LABELS[primaryType]
    if (!primaryType) return 'Incident'
    return primaryType.charAt(0).toUpperCase() + primaryType.slice(1)
}

/** Human-readable jam-factor description for a flow road. */
export function flowClearness(jamFactor: number): string {
    if (jamFactor < 2) return 'Clear'
    if (jamFactor < 3) return 'Running well'
    return 'Light'
}
