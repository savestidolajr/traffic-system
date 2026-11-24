import { pascalizeKeys } from '@/lib/api-utils'
import type { OrchestrateRequest } from '../types'

/**
 * Maps frontend OrchestrateRequest to backend PascalCase request.
 */
export function mapToOrchestrateApiRequest(req: OrchestrateRequest) {
    const {
        targetDurationSec,
        market,
        latitude,
        longitude,
        radiusKm,
        voiceSettings,
        shapeType,
        west,
        south,
        east,
        north,
        corridorPoints,
        corridorRadiusMeters,
        polygonPoints,
        ...rest
    } = req
    const coverage: Record<string, unknown> = {
        Latitude: latitude,
        Longitude: longitude,
        RadiusKm: radiusKm,
    }

    if (shapeType) {
        coverage.ShapeType = shapeType
    }
    if (west !== undefined) {
        coverage.West = west
        coverage.South = south
        coverage.East = east
        coverage.North = north
    }
    if (corridorPoints) {
        coverage.CorridorPoints = corridorPoints.map((p) => ({
            Lat: p.lat,
            Lng: p.lng,
        }))
    }
    if (polygonPoints) {
        coverage.PolygonPoints = polygonPoints.map((p) => ({
            Lat: p.lat,
            Lng: p.lng,
        }))
    }
    if (corridorRadiusMeters !== undefined) {
        coverage.CorridorRadiusMeters = corridorRadiusMeters
    }

    const result: Record<string, unknown> = {
        ...pascalizeKeys(rest),
        TargetDurationSeconds: targetDurationSec,
        Location: market,
        Coverage: coverage,
    }

    if (voiceSettings) {
        result.VoiceSettings = pascalizeKeys(voiceSettings)
    }

    return result
}
