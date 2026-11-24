import { z } from 'zod'
import { camelizeKeys } from '../../api-utils'

/**
 * Shared schemas for reuse across multiple features.
 * Focuses on primitive domain objects like VoiceSettings and Coverage.
 *
 * Convention:
 *   - *Schema        = camelCase keys (form validation / frontend-only)
 *   - *ApiSchema     = PascalCase keys + camelizeKeys transform (API response parsing)
 *
 * The backend serializes all DTOs in PascalCase. API-facing schemas MUST
 * accept PascalCase and transform to camelCase for frontend consumption.
 */

// ---------------------------------------------------------------------------
// Form / frontend schemas (camelCase keys)
// ---------------------------------------------------------------------------

export const VoiceSettingsSchema = z.object({
    speed: z.number().min(0.7).max(1.2),
    stability: z.number().min(0).max(1),
    similarityBoost: z.number().min(0).max(1),
    style: z.number().min(0).max(1),
    useSpeakerBoost: z.boolean(),
})

export type VoiceSettings = z.infer<typeof VoiceSettingsSchema>

// ---------------------------------------------------------------------------
// Content selection (mirrors backend ContentSelectionDto — WS1 content sources)
// ---------------------------------------------------------------------------

/** Resolved per-bulletin content sources. Backend nulls = traffic-only legacy.
 *  `weatherRequired`/`fuelRequired` = "Always include": when that source's data
 *  was fetched, the section must air (a failed provider fetch still airs the
 *  bulletin without it). */
export const ContentSelectionSchema = z.object({
    traffic: z.boolean(),
    weather: z.boolean(),
    fuel: z.boolean(),
    weatherRequired: z.boolean(),
    fuelRequired: z.boolean(),
    /** Explicit on-air segment order. Omitted preserves the legacy default. */
    segmentOrder: z.array(z.enum(['Traffic', 'Weather', 'Fuel'])).optional(),
    /** Traffic incident order. Omitted preserves worst-first behaviour. */
    trafficOrder: z.enum(['WorstFirst', 'WorstLast']).optional(),
})

export type ContentSelection = z.infer<typeof ContentSelectionSchema>

/** Backend defaults: traffic on, weather/fuel off (WS1 traffic-first). */
export const DEFAULT_CONTENT_SELECTION: ContentSelection = {
    traffic: true,
    weather: false,
    fuel: false,
    weatherRequired: false,
    fuelRequired: false,
}

/**
 * API-side selection: nullable per-source booleans. Nulls resolve to the
 * backend defaults at the boundary so the UI always holds solid booleans.
 */
export const ContentSelectionApiSchema = z
    .object({
        Traffic: z.boolean().nullish(),
        Weather: z.boolean().nullish(),
        Fuel: z.boolean().nullish(),
        WeatherRequired: z.boolean().nullish(),
        FuelRequired: z.boolean().nullish(),
        SegmentOrder: z.array(z.enum(['Traffic', 'Weather', 'Fuel'])).nullish(),
        TrafficOrder: z.enum(['WorstFirst', 'WorstLast']).nullish(),
    })
    .transform((api): ContentSelection => ({
        traffic: api.Traffic ?? true,
        weather: api.Weather ?? false,
        fuel: api.Fuel ?? false,
        weatherRequired: api.WeatherRequired ?? false,
        fuelRequired: api.FuelRequired ?? false,
        segmentOrder: api.SegmentOrder ?? undefined,
        trafficOrder: api.TrafficOrder ?? undefined,
    }))

/**
 * Fill-priority sources (mirrors backend FillSource). Order in a schedule's
 * fillOrder = preference when a read runs short; the relative order of weather
 * and fuel in that list is also their on-air read order (whichever comes first
 * reads first). Filler always closes the read.
 */
export const FILL_SOURCES = [
    'weather',
    'fuel',
    'minorTraffic',
    'filler',
] as const
export type FillSource = (typeof FILL_SOURCES)[number]

/**
 * Backend default priority: weather, fuel, minor traffic, then filler.
 *
 * Deliberately a separate list so the default order can change without
 * touching the membership list above.
 */
export const DEFAULT_FILL_ORDER: readonly FillSource[] = [
    'weather',
    'fuel',
    'minorTraffic',
    'filler',
]

export const FillOrderApiSchema = z.array(z.enum(FILL_SOURCES))

// ---------------------------------------------------------------------------
// Coverage shapes (mirrors backend CoverageArea / CoverageDto)
// ---------------------------------------------------------------------------

export const COVERAGE_SHAPE_TYPES = [
    'circle',
    'bbox',
    'corridor',
    'polygon',
] as const
export type CoverageShapeType = (typeof COVERAGE_SHAPE_TYPES)[number]

/** HERE bbox filters reject a single query extent over 1 degree per side. The
 *  backend tiles any bbox/polygon shape larger than this into a grid of
 *  sub-1-degree HERE queries — this is the per-tile size, not the limit on
 *  what a station can draw (see MAX_SHAPE_SPAN_DEGREES). */
export const MAX_BBOX_DEGREES = 1
/** Outer cap on a drawn bbox/polygon's extent (mirrors backend
 *  CoverageArea.MaxShapeSpanDegrees). At 5° per side the worst case is a 5x5
 *  tile grid per HERE call — enough for any realistic broadcast licence area. */
export const MAX_SHAPE_SPAN_DEGREES = 5
/** HERE corridor filters: 2..300 points; backend caps at 100 to keep GET URLs short. */
export const MAX_CORRIDOR_POINTS = 100
export const MIN_CORRIDOR_RADIUS_M = 1
export const MAX_CORRIDOR_RADIUS_M = 5000
/** Polygons share the corridor point cap; their enclosing box must fit MAX_SHAPE_SPAN_DEGREES. */
export const MAX_POLYGON_POINTS = 100

export const CoveragePointSchema = z.object({
    lat: z.number().min(-90).max(90),
    lng: z.number().min(-180).max(180),
})

export type CoveragePoint = z.infer<typeof CoveragePointSchema>

/**
 * The geometry fields shared by the traffic area and the source-specific
 * overrides. Weather and fuel overrides are circles in the backend because
 * both HERE providers query one location.
 */
const CoverageGeometrySchema = z.object({
    latitude: z.number().min(-90).max(90),
    longitude: z.number().min(-180).max(180),
    radiusKm: z.number().min(1),
    shapeType: z.enum(COVERAGE_SHAPE_TYPES).optional(),
    west: z.number().min(-180).max(180).optional(),
    south: z.number().min(-90).max(90).optional(),
    east: z.number().min(-180).max(180).optional(),
    north: z.number().min(-90).max(90).optional(),
    corridorPoints: z.array(CoveragePointSchema).optional(),
    corridorRadiusMeters: z
        .number()
        .min(MIN_CORRIDOR_RADIUS_M)
        .max(MAX_CORRIDOR_RADIUS_M)
        .optional(),
    polygonPoints: z.array(CoveragePointSchema).optional(),
})

/**
 * Coverage area. Traffic uses the outer geometry. Optional weather/fuel
 * geometries override only their matching HERE source and inherit the outer
 * area when absent.
 */
export const CoverageAreaSchema = CoverageGeometrySchema.extend({
    weatherCoverage: CoverageGeometrySchema.optional(),
    fuelCoverage: CoverageGeometrySchema.optional(),
}).superRefine((area, ctx) => {
    const validateGeometry = (
        geometry: z.infer<typeof CoverageGeometrySchema>,
        path: (string | number)[]
    ) => {
        if (geometry.shapeType === 'bbox') {
            const { west, south, east, north } = geometry
            if (
                west === undefined ||
                south === undefined ||
                east === undefined ||
                north === undefined
            ) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        'A bbox coverage needs west, south, east and north',
                    path,
                })
                return
            }
            if (east <= west || north <= south) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        'Box must have positive extent (west < east, south < north)',
                    path,
                })
            }
            if (
                east - west > MAX_SHAPE_SPAN_DEGREES ||
                north - south > MAX_SHAPE_SPAN_DEGREES
            ) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `A coverage box must fit inside ${MAX_SHAPE_SPAN_DEGREES} degrees per side`,
                    path,
                })
            }
        }
        if (geometry.shapeType === 'corridor') {
            const points = geometry.corridorPoints ?? []
            if (points.length < 2) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'A corridor needs at least 2 points',
                    path,
                })
            }
            if (points.length > MAX_CORRIDOR_POINTS) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `A corridor supports at most ${MAX_CORRIDOR_POINTS} points`,
                    path,
                })
            }
        }
        if (geometry.shapeType === 'polygon') {
            const points = geometry.polygonPoints ?? []
            if (points.length < 3) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'A polygon needs at least 3 points',
                    path,
                })
            }
            if (points.length > MAX_POLYGON_POINTS) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `A polygon supports at most ${MAX_POLYGON_POINTS} points`,
                    path,
                })
            }
        }
    }

    validateGeometry(area, [])
    for (const [name, geometry] of [
        ['weatherCoverage', area.weatherCoverage],
        ['fuelCoverage', area.fuelCoverage],
    ] as const) {
        if (geometry) {
            if (geometry.shapeType && geometry.shapeType !== 'circle') {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Weather and fuel coverage must use a radius',
                    path: [name, 'shapeType'],
                })
            }
            validateGeometry(geometry, [name])
        }
    }
})
export type CoverageArea = z.infer<typeof CoverageAreaSchema>

/** Circle-only form schema for HERE weather/fuel source overrides. */
export const SourceCoverageSchema = CoverageGeometrySchema.refine(
    (area) => !area.shapeType || area.shapeType === 'circle',
    'Weather and fuel coverage must use a radius'
)

// ---------------------------------------------------------------------------
// Defaults
// ---------------------------------------------------------------------------

export const DEFAULT_VOICE_SETTINGS: VoiceSettings = {
    speed: 0.97,
    stability: 0.7,
    similarityBoost: 0.7,
    style: 0.0,
    useSpeakerBoost: false,
}

export const DEFAULT_COVERAGE: CoverageArea = {
    latitude: -33.8688,
    longitude: 151.2093,
    radiusKm: 50,
}

// ---------------------------------------------------------------------------
// API response schemas (PascalCase keys → camelCase via transform)
// ---------------------------------------------------------------------------

export const VoiceSettingsApiSchema = z
    .object({
        Speed: z.number(),
        Stability: z.number(),
        SimilarityBoost: z.number(),
        Style: z.number(),
        UseSpeakerBoost: z.boolean(),
    })
    .transform(camelizeKeys)

export const CoveragePointApiSchema = z
    .object({
        Lat: z.number(),
        Lng: z.number(),
    })
    .transform((p) => ({ lat: p.Lat, lng: p.Lng }))

const CoverageGeometryApiSchema = z
    .object({
        Latitude: z.number(),
        Longitude: z.number(),
        RadiusKm: z.number(),
        ShapeType: z
            .enum(COVERAGE_SHAPE_TYPES)
            .optional()
            .nullable()
            .catch(null),
        West: z.number().optional().nullable(),
        South: z.number().optional().nullable(),
        East: z.number().optional().nullable(),
        North: z.number().optional().nullable(),
        CorridorPoints: z.array(CoveragePointApiSchema).optional().nullable(),
        CorridorRadiusMeters: z.number().optional().nullable(),
        PolygonPoints: z.array(CoveragePointApiSchema).optional().nullable(),
    })
    .transform((dto) => ({
        latitude: dto.Latitude,
        longitude: dto.Longitude,
        radiusKm: dto.RadiusKm,
        shapeType: dto.ShapeType ?? undefined,
        west: dto.West ?? undefined,
        south: dto.South ?? undefined,
        east: dto.East ?? undefined,
        north: dto.North ?? undefined,
        corridorPoints: dto.CorridorPoints ?? undefined,
        corridorRadiusMeters: dto.CorridorRadiusMeters ?? undefined,
        polygonPoints: dto.PolygonPoints ?? undefined,
    }))

export const CoverageAreaApiSchema = z
    .object({
        Latitude: z.number(),
        Longitude: z.number(),
        RadiusKm: z.number(),
        ShapeType: z
            .enum(COVERAGE_SHAPE_TYPES)
            .optional()
            .nullable()
            .catch(null),
        West: z.number().optional().nullable(),
        South: z.number().optional().nullable(),
        East: z.number().optional().nullable(),
        North: z.number().optional().nullable(),
        CorridorPoints: z.array(CoveragePointApiSchema).optional().nullable(),
        CorridorRadiusMeters: z.number().optional().nullable(),
        PolygonPoints: z.array(CoveragePointApiSchema).optional().nullable(),
        WeatherCoverage: CoverageGeometryApiSchema.optional().nullable(),
        FuelCoverage: CoverageGeometryApiSchema.optional().nullable(),
    })
    .transform((dto): CoverageArea => ({
        latitude: dto.Latitude,
        longitude: dto.Longitude,
        radiusKm: dto.RadiusKm,
        shapeType: dto.ShapeType ?? undefined,
        west: dto.West ?? undefined,
        south: dto.South ?? undefined,
        east: dto.East ?? undefined,
        north: dto.North ?? undefined,
        corridorPoints: dto.CorridorPoints ?? undefined,
        corridorRadiusMeters: dto.CorridorRadiusMeters ?? undefined,
        polygonPoints: dto.PolygonPoints ?? undefined,
        weatherCoverage: dto.WeatherCoverage ?? undefined,
        fuelCoverage: dto.FuelCoverage ?? undefined,
    }))

// ---------------------------------------------------------------------------
// Voice rotation (mirrors backend VoiceRotationPolicy / VoiceRotationDto)
// ---------------------------------------------------------------------------

/** How a station chooses its voice each broadcast. Mirrors backend VoiceSelectionMode. */
export const VOICE_SELECTION_MODES = [
    'single',
    'roundRobin',
    'timeOfDay',
] as const
export type VoiceSelectionMode = (typeof VOICE_SELECTION_MODES)[number]

/** Coerces a persisted/API mode string to a valid mode, defaulting to single. */
export function normalizeVoiceSelectionMode(
    value: string | null | undefined
): VoiceSelectionMode {
    const v = (value ?? '').toLowerCase()
    if (v === 'roundrobin' || v === 'rotate') return 'roundRobin'
    if (v === 'timeofday' || v === 'time-of-day') return 'timeOfDay'
    return 'single'
}

/** A local-time window. The wire carries a SET of voices per window; the editor
 *  only binds the first. `extraVoiceProfileIds` carries the rest untouched so an
 *  unrelated edit + save cannot silently delete them — the save PUT replaces the
 *  whole policy, so anything dropped here is gone server-side. */
export const VoiceWindowSchema = z.object({
    start: z.string(),
    end: z.string(),
    voiceProfileId: z.string(),
    extraVoiceProfileIds: z.array(z.string()).optional(),
})
export type VoiceWindow = z.infer<typeof VoiceWindowSchema>

export interface VoiceRotation {
    mode: VoiceSelectionMode
    voiceProfileIds: string[]
    windows: VoiceWindow[]
}

export const VoiceWindowApiSchema = z
    .object({
        Start: z.string(),
        End: z.string(),
        VoiceProfileIds: z.array(z.string()).nullable().default([]),
    })
    .transform((w): VoiceWindow => ({
        start: w.Start,
        end: w.End,
        voiceProfileId: w.VoiceProfileIds?.[0] ?? '',
        extraVoiceProfileIds: w.VoiceProfileIds?.slice(1),
    }))

export const VoiceRotationApiSchema = z
    .object({
        Mode: z.string(),
        VoiceProfileIds: z.array(z.string()).nullable().default([]),
        Windows: z.array(VoiceWindowApiSchema).nullable().default([]),
    })
    .transform((r): VoiceRotation => ({
        mode: normalizeVoiceSelectionMode(r.Mode),
        voiceProfileIds: r.VoiceProfileIds ?? [],
        windows: r.Windows ?? [],
    }))

// ---------------------------------------------------------------------------
// Bookend rotation (mirrors backend BookendRotationPolicy / BookendRotationDto)
// ---------------------------------------------------------------------------

/** How a station picks its bookend pair each broadcast. Mirrors backend BookendSelectionMode. */
export const BOOKEND_SELECTION_MODES = [
    'single',
    'roundRobin',
    'timeOfDay',
] as const
export type BookendSelectionMode = (typeof BOOKEND_SELECTION_MODES)[number]

/** Coerces a persisted/API mode string to a valid mode, defaulting to single. */
export function normalizeBookendSelectionMode(
    value: string | null | undefined
): BookendSelectionMode {
    const v = (value ?? '').toLowerCase()
    if (v === 'roundrobin' || v === 'rotate') return 'roundRobin'
    if (v === 'timeofday' || v === 'time-of-day') return 'timeOfDay'
    return 'single'
}

/** A local-time window. The wire carries a SET of bookend ids per window; the
 *  editor only binds the first. `extraBookendIds` carries the rest untouched so
 *  an unrelated edit + save cannot silently delete them — the save PUT replaces
 *  the whole policy, so anything dropped here is gone server-side. */
export const BookendWindowSchema = z.object({
    start: z.string(),
    end: z.string(),
    bookendId: z.string(),
    extraBookendIds: z.array(z.string()).optional(),
})
export type BookendWindow = z.infer<typeof BookendWindowSchema>

export interface BookendRotation {
    mode: BookendSelectionMode
    bookendIds: string[]
    windows: BookendWindow[]
}

export const BookendWindowApiSchema = z
    .object({
        Start: z.string(),
        End: z.string(),
        BookendIds: z.array(z.string()).nullable().default([]),
    })
    .transform((w): BookendWindow => ({
        start: w.Start,
        end: w.End,
        bookendId: w.BookendIds?.[0] ?? '',
        extraBookendIds: w.BookendIds?.slice(1),
    }))

export const BookendRotationApiSchema = z
    .object({
        Mode: z.string(),
        BookendIds: z.array(z.string()).nullable().default([]),
        Windows: z.array(BookendWindowApiSchema).nullable().default([]),
    })
    .transform((r): BookendRotation => ({
        mode: normalizeBookendSelectionMode(r.Mode),
        bookendIds: r.BookendIds ?? [],
        windows: r.Windows ?? [],
    }))
