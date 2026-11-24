import { describe, expect, it } from 'vitest'
import {
    corridorLengthKm,
    deriveBboxCircle,
    deriveCorridorCircle,
    formatCoverageSummary,
    getShapeType,
    greatCircleKm,
    isValidBbox,
    isValidPolygonRing,
    maxExtentDegrees,
    polygonAreaKm2,
    polygonTileGridSize,
    seedBboxAround,
    tileGridSize,
} from './coverage-shapes'

describe('deriveBboxCircle', () => {
    it('derives centre at the box midpoint and radius at the half-diagonal', () => {
        // Same box as the mel-1 mock fixture — values verified against the
        // backend CoverageArea.CreateBoundingBox derivation.
        const circle = deriveBboxCircle(144.7, -38.05, 145.25, -37.6)

        expect(circle.latitude).toBeCloseTo(-37.825, 6)
        expect(circle.longitude).toBeCloseTo(144.975, 6)
        expect(circle.radiusKm).toBeCloseTo(34.8, 1)
    })

    it('never reports a radius below 1 km', () => {
        const circle = deriveBboxCircle(144.99, -37.81, 145.0, -37.8)

        expect(circle.radiusKm).toBe(1)
    })
})

describe('deriveCorridorCircle', () => {
    it('adds the corridor width to the points half-diagonal', () => {
        // Mirrors the backend CoverageArea_Corridor_DerivesEnclosingCircle test.
        const circle = deriveCorridorCircle(
            [
                { lat: -37.8, lng: 144.9 },
                { lat: -37.9, lng: 145.0 },
            ],
            1000
        )

        expect(circle.latitude).toBeCloseTo(-37.85, 6)
        expect(circle.longitude).toBeCloseTo(144.95, 6)
        expect(circle.radiusKm).toBeCloseTo(8.08, 1)
    })
})

describe('isValidBbox', () => {
    it('rejects boxes with missing bounds', () => {
        expect(isValidBbox({ west: 144.7, south: -38.05, east: 145.25 })).toBe(
            false
        )
    })

    it('rejects inverted extents', () => {
        expect(
            isValidBbox({
                west: 145.25,
                south: -38.05,
                east: 144.7,
                north: -37.6,
            })
        ).toBe(false)
    })

    it('rejects boxes over the drawable span cap (5 degrees)', () => {
        expect(
            isValidBbox({
                west: 144.0,
                south: -43.0,
                east: 150.5,
                north: -37.0,
            })
        ).toBe(false)
    })

    it('accepts a box over 1 degree but under the 5-degree span cap', () => {
        expect(
            isValidBbox({
                west: 144.0,
                south: -38.5,
                east: 145.5,
                north: -37.0,
            })
        ).toBe(true)
    })

    it('accepts a well-formed sub-degree box', () => {
        expect(
            isValidBbox({
                west: 144.7,
                south: -38.05,
                east: 145.25,
                north: -37.6,
            })
        ).toBe(true)
    })
})

describe('seedBboxAround', () => {
    it('seeds 0.3 degrees per side centred on the point', () => {
        const box = seedBboxAround(-37.8, 145.0)

        expect(box.west).toBeCloseTo(144.85, 6)
        expect(box.east).toBeCloseTo(145.15, 6)
        expect(box.south).toBeCloseTo(-37.95, 6)
        expect(box.north).toBeCloseTo(-37.65, 6)
        expect(isValidBbox(box)).toBe(true)
    })

    it('clamps at the antimeridian and poles', () => {
        const box = seedBboxAround(89.95, 179.95)

        expect(box.north).toBe(90)
        expect(box.east).toBe(180)
    })
})

describe('getShapeType', () => {
    it('defaults to circle for legacy areas and null', () => {
        expect(getShapeType(null)).toBe('circle')
        expect(getShapeType({ shapeType: undefined })).toBe('circle')
        expect(getShapeType({ shapeType: 'corridor' })).toBe('corridor')
    })
})

describe('isValidPolygonRing', () => {
    it('rejects rings with fewer than 3 points', () => {
        expect(isValidPolygonRing(undefined)).toBe(false)
        expect(
            isValidPolygonRing([
                { lat: -37.8, lng: 144.9 },
                { lat: -37.9, lng: 145.0 },
            ])
        ).toBe(false)
    })

    it('rejects rings whose enclosing box exceeds the 5-degree span cap', () => {
        expect(
            isValidPolygonRing([
                { lat: -37.0, lng: 144.0 },
                { lat: -42.2, lng: 144.5 },
                { lat: -37.5, lng: 145.0 },
            ])
        ).toBe(false)
    })

    it('accepts a ring over 1 degree but under the 5-degree span cap', () => {
        expect(
            isValidPolygonRing([
                { lat: -37.0, lng: 144.0 },
                { lat: -38.2, lng: 144.5 },
                { lat: -37.5, lng: 145.0 },
            ])
        ).toBe(true)
    })

    it('accepts a sub-degree triangle', () => {
        expect(
            isValidPolygonRing([
                { lat: -37.7, lng: 144.9 },
                { lat: -37.9, lng: 144.9 },
                { lat: -37.8, lng: 145.2 },
            ])
        ).toBe(true)
    })
})

describe('formatCoverageSummary', () => {
    it('describes each shape kind factually', () => {
        expect(formatCoverageSummary(null)).toBe('Not configured')
        expect(
            formatCoverageSummary({
                latitude: -37.8,
                longitude: 145,
                radiusKm: 50,
            })
        ).toBe('50 km radius')
        expect(
            formatCoverageSummary({
                latitude: -37.825,
                longitude: 144.975,
                radiusKm: 34.8,
                shapeType: 'bbox',
                west: 144.7,
                south: -38.05,
                east: 145.25,
                north: -37.6,
            })
        ).toMatch(/^Box · ~\d+ × \d+ km$/)
        expect(
            formatCoverageSummary({
                latitude: -37.85,
                longitude: 144.95,
                radiusKm: 8.1,
                shapeType: 'corridor',
                corridorPoints: [
                    { lat: -37.8, lng: 144.9 },
                    { lat: -37.9, lng: 145.0 },
                ],
                corridorRadiusMeters: 500,
            })
        ).toBe('Route · 14 km · ±500 m')
        expect(
            formatCoverageSummary({
                latitude: -37.8,
                longitude: 145.0,
                radiusKm: 12,
                shapeType: 'polygon',
                polygonPoints: [
                    { lat: -37.7, lng: 144.9 },
                    { lat: -37.9, lng: 144.9 },
                    { lat: -37.8, lng: 145.2 },
                ],
            })
        ).toBe('Area · ~292 km² · 3 points')
    })

    it('flags incomplete boxes instead of inventing numbers', () => {
        expect(
            formatCoverageSummary({
                latitude: -37.8,
                longitude: 145,
                radiusKm: 10,
                shapeType: 'bbox',
                west: 144.7,
            })
        ).toBe('Box (incomplete)')
    })
})

describe('corridorLengthKm', () => {
    it('sums consecutive leg distances', () => {
        expect(
            corridorLengthKm([
                { lat: -37.8, lng: 144.9 },
                { lat: -37.9, lng: 145.0 },
            ])
        ).toBeCloseTo(14.17, 1)
        expect(corridorLengthKm([{ lat: -37.8, lng: 144.9 }])).toBe(0)
    })
})

describe('polygonAreaKm2', () => {
    it('computes the shoelace area of a ring', () => {
        expect(
            polygonAreaKm2([
                { lat: -37.7, lng: 144.9 },
                { lat: -37.9, lng: 144.9 },
                { lat: -37.8, lng: 145.2 },
            ])
        ).toBeCloseTo(291.8, 0)
        expect(polygonAreaKm2(undefined)).toBe(0)
    })
})

describe('maxExtentDegrees', () => {
    it('returns the largest side of the enclosing box', () => {
        expect(
            maxExtentDegrees([
                { lat: -37.7, lng: 144.9 },
                { lat: -37.9, lng: 145.2 },
            ])
        ).toBeCloseTo(0.3, 6)
        expect(maxExtentDegrees([])).toBe(0)
    })
})

describe('tileGridSize', () => {
    it("returns a single tile for a box within HERE's 1-degree limit", () => {
        expect(tileGridSize(144.7, -38.05, 145.25, -37.6)).toEqual({
            cols: 1,
            rows: 1,
            tiles: 1,
        })
    })

    it('splits a box exceeding 1 degree per side into a grid, matching the backend', () => {
        // Mirrors HereGeoFilterTests.BuildTiles_BoundingBoxOverOneDegreePerSide_SplitsIntoGrid
        expect(tileGridSize(144.0, -38.0, 147.0, -36.0)).toEqual({
            cols: 3,
            rows: 2,
            tiles: 6,
        })
    })
})

describe('polygonTileGridSize', () => {
    it('returns a single tile for a sub-degree ring', () => {
        expect(
            polygonTileGridSize([
                { lat: -37.7, lng: 144.9 },
                { lat: -37.9, lng: 144.9 },
                { lat: -37.8, lng: 145.2 },
            ])
        ).toEqual({ cols: 1, rows: 1, tiles: 1 })
    })

    it('splits a ring whose enclosing box exceeds 1 degree per side', () => {
        // Enclosing box: lng 151.0-152.5 (1.5deg -> 2 cols), lat -38.0..-36.0 (2deg -> 2 rows)
        expect(
            polygonTileGridSize([
                { lat: -36.0, lng: 151.0 },
                { lat: -36.0, lng: 152.5 },
                { lat: -38.0, lng: 151.75 },
            ])
        ).toEqual({ cols: 2, rows: 2, tiles: 4 })
    })

    it('returns a single tile for empty input', () => {
        expect(polygonTileGridSize(undefined)).toEqual({
            cols: 1,
            rows: 1,
            tiles: 1,
        })
    })
})

describe('greatCircleKm', () => {
    it('matches the backend haversine for a known pair', () => {
        // Melbourne CBD → ~0.2° NE; verified against the C# GreatCircleKm.
        expect(greatCircleKm(-37.8, 145.0, -37.6, 145.2)).toBeCloseTo(28.36, 1)
    })
})
