import { describe, expect, it } from 'vitest'
import {
    EXTENT_WARNING_RATIO,
    MAX_BBOX_DEGREES,
    MAX_OPERATING_LATITUDE,
    MAX_OPERATING_LONGITUDE,
    MAX_SHAPE_SPAN_DEGREES,
    MAX_CIRCLE_RADIUS_KM,
    MIN_CORRIDOR_POINTS,
    MIN_OPERATING_LATITUDE,
    MIN_OPERATING_LONGITUDE,
    MIN_POLYGON_POINTS,
    validateCoverageShape,
} from './coverage-validation'

function codes(area: Parameters<typeof validateCoverageShape>[0]): string[] {
    return validateCoverageShape(area).map((i) => i.code)
}

describe('constraint constants', () => {
    it('pins the HERE-derived limits', () => {
        expect(MAX_CIRCLE_RADIUS_KM).toBe(50)
        expect(MAX_BBOX_DEGREES).toBe(1)
        expect(MAX_SHAPE_SPAN_DEGREES).toBe(5)
        expect(MIN_CORRIDOR_POINTS).toBe(2)
        expect(MIN_POLYGON_POINTS).toBe(3)
        expect(EXTENT_WARNING_RATIO).toBe(0.8)
        // Mirrors MarketSchedule.cs's operating-region constants — the backend
        // 400s outside this band, so the client pins must match exactly.
        expect(MIN_OPERATING_LATITUDE).toBe(-50)
        expect(MAX_OPERATING_LATITUDE).toBe(0)
        expect(MIN_OPERATING_LONGITUDE).toBe(100)
        expect(MAX_OPERATING_LONGITUDE).toBe(180)
    })
})

describe('validateCoverageShape – operating region', () => {
    const INSIDE = { latitude: -37.8, longitude: 144.9 }

    it('accepts coverage inside the region', () => {
        expect(codes({ shapeType: 'circle', ...INSIDE })).toEqual([])
    })

    it('rejects a circle centered outside, attached to latitude', () => {
        const issues = validateCoverageShape({
            shapeType: 'circle',
            // Sign typo: positive latitude is the wrong hemisphere.
            latitude: 45,
            longitude: 144.9,
            radiusKm: 10,
        })
        expect(issues).toEqual([
            expect.objectContaining({
                code: 'outside-operating-region',
                path: 'latitude',
            }),
        ])
        expect(issues[0].message).toContain('operating region')
        expect(issues[0].message).toContain('lat -50..0')
        expect(issues[0].message).toContain('lng 100..180')
    })

    it('rejects a circle east of the region (longitude past 180)', () => {
        expect(
            codes({ shapeType: 'circle', latitude: -37.8, longitude: 185 })
        ).toEqual(['outside-operating-region'])
    })

    it('rejects a bbox whose extreme corner is outside', () => {
        expect(
            codes({
                shapeType: 'bbox',
                west: 144.7,
                south: -38.05,
                east: 145.25,
                north: -37.6,
            })
        ).toEqual([])
        expect(
            codes({
                shapeType: 'bbox',
                // South of the region floor (-50) but within the 5° span cap.
                west: 144.0,
                south: -51.0,
                east: 144.5,
                north: -49.5,
            })
        ).toEqual(['outside-operating-region'])
    })

    it('rejects a corridor with a waypoint outside', () => {
        expect(
            codes({
                shapeType: 'corridor',
                corridorPoints: [
                    { lat: -37.8, lng: 144.9 },
                    { lat: -37.9, lng: 95.0 },
                ],
            })
        ).toEqual(['outside-operating-region'])
    })

    it('rejects a polygon with a point outside', () => {
        expect(
            codes({
                shapeType: 'polygon',
                polygonPoints: [
                    { lat: -51.0, lng: 144.9 },
                    { lat: -50.5, lng: 144.95 },
                    { lat: -50.2, lng: 145.0 },
                ],
            })
        ).toEqual(['outside-operating-region'])
    })

    it('leaves an incomplete bbox to its own issue, not the region check', () => {
        expect(
            codes({ shapeType: 'bbox', west: 144.7, south: -38.05 })
        ).toEqual(['bbox-incomplete'])
    })
})

describe('validateCoverageShape – circle', () => {
    it('accepts circles up to the HERE cap', () => {
        expect(codes({ shapeType: 'circle', radiusKm: 50 })).toEqual([])
        expect(codes({ shapeType: 'circle', radiusKm: 1 })).toEqual([])
    })

    it('rejects circles over the cap, attached to radiusKm', () => {
        const issues = validateCoverageShape({
            shapeType: 'circle',
            radiusKm: 51,
        })
        expect(issues).toHaveLength(1)
        expect(issues[0].code).toBe('circle-too-large')
        expect(issues[0].path).toBe('radiusKm')
    })

    it('treats a missing shapeType as a circle (legacy areas)', () => {
        expect(codes({ radiusKm: 51 })).toEqual(['circle-too-large'])
        expect(codes({ radiusKm: 50 })).toEqual([])
    })
})

describe('validateCoverageShape – bbox', () => {
    const box = { west: 144.7, south: -38.05, east: 145.25, north: -37.6 }

    it('accepts a well-formed sub-degree box', () => {
        expect(codes({ shapeType: 'bbox', ...box })).toEqual([])
    })

    it('flags missing bounds as incomplete (and stops there)', () => {
        expect(
            codes({ shapeType: 'bbox', west: 144.7, south: -38.05 })
        ).toEqual(['bbox-incomplete'])
    })

    it('flags inverted extents', () => {
        expect(
            codes({ shapeType: 'bbox', ...box, west: 146, east: 144 })
        ).toContain('bbox-inverted')
    })

    it('flags boxes over the drawable span cap (5 degrees)', () => {
        expect(
            codes({
                shapeType: 'bbox',
                west: 144.0,
                south: -43.0,
                east: 150.5,
                north: -37.0,
            })
        ).toEqual(['bbox-too-large'])
    })

    it('accepts a box over 1 degree but under the 5-degree span cap', () => {
        expect(
            codes({
                shapeType: 'bbox',
                west: 144.0,
                south: -38.5,
                east: 145.5,
                north: -37.0,
            })
        ).toEqual([])
    })

    it('never applies the circle cap to a derived bbox circle', () => {
        // Derived enclosing circles may legitimately exceed 50 km
        expect(codes({ shapeType: 'bbox', ...box, radiusKm: 70 })).toEqual([])
    })
})

describe('validateCoverageShape – corridor', () => {
    it('requires at least 2 waypoints', () => {
        expect(codes({ shapeType: 'corridor', corridorPoints: [] })).toEqual([
            'corridor-too-few-points',
        ])
        expect(
            codes({
                shapeType: 'corridor',
                corridorPoints: [{ lat: -37.8, lng: 144.9 }],
            })
        ).toEqual(['corridor-too-few-points'])
        expect(codes({ shapeType: 'corridor' })).toEqual([
            'corridor-too-few-points',
        ])
    })

    it('accepts 2+ waypoints', () => {
        expect(
            codes({
                shapeType: 'corridor',
                corridorPoints: [
                    { lat: -37.8, lng: 144.9 },
                    { lat: -37.9, lng: 145.0 },
                ],
            })
        ).toEqual([])
    })
})

describe('validateCoverageShape – polygon', () => {
    const triangle = [
        { lat: -37.7, lng: 144.9 },
        { lat: -37.9, lng: 144.9 },
        { lat: -37.8, lng: 145.2 },
    ]

    it('accepts a sub-degree ring of 3+ points', () => {
        expect(
            codes({ shapeType: 'polygon', polygonPoints: triangle })
        ).toEqual([])
    })

    it('flags rings with fewer than 3 points', () => {
        expect(
            codes({ shapeType: 'polygon', polygonPoints: triangle.slice(0, 2) })
        ).toEqual(['polygon-too-few-points'])
        expect(codes({ shapeType: 'polygon' })).toEqual([
            'polygon-too-few-points',
        ])
    })

    it('flags rings whose enclosing box exceeds the 5-degree span cap', () => {
        expect(
            codes({
                shapeType: 'polygon',
                polygonPoints: [
                    { lat: -37.0, lng: 144.0 },
                    { lat: -42.2, lng: 144.5 },
                    { lat: -37.5, lng: 145.0 },
                ],
            })
        ).toEqual(['polygon-too-large'])
    })

    it('accepts a ring over 1 degree but under the 5-degree span cap', () => {
        expect(
            codes({
                shapeType: 'polygon',
                polygonPoints: [
                    { lat: -37.0, lng: 144.0 },
                    { lat: -38.2, lng: 144.5 },
                    { lat: -37.5, lng: 145.0 },
                ],
            })
        ).toEqual([])
    })
})

describe('issue shape', () => {
    it('every issue carries a default user-facing message', () => {
        const broken = [
            { shapeType: 'circle' as const, radiusKm: 999 },
            { shapeType: 'bbox' as const },
            { shapeType: 'corridor' as const },
            { shapeType: 'polygon' as const },
        ]
        for (const area of broken) {
            for (const issue of validateCoverageShape(area)) {
                expect(issue.message.length).toBeGreaterThan(0)
                expect(['radiusKm', 'shapeType', 'latitude']).toContain(
                    issue.path
                )
            }
        }
    })
})
