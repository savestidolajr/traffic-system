// @vitest-environment jsdom
import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useCoverageShapeState } from './useCoverageShapeState'
import type { CoverageArea } from '@/lib/api/schemas/shared'

/**
 * Regression for bug-bash-2026-08-05 finding 30: after switching coverage
 * shape type, Save was dead (click fired, no submit event) until any further
 * edit. The root defect: the circle-mode onChange payloads omitted
 * `shapeType`, so the schedule form's shapeType field was CLEARED instead of
 * set to 'circle' — the shape change was never a real form change. Every mode
 * switch must emit the target shapeType.
 */

const POLYGON: CoverageArea = {
    latitude: -33.9,
    longitude: 151.2,
    radiusKm: 8,
    shapeType: 'polygon',
    polygonPoints: [
        { latitude: -33.95, longitude: 151.1 },
        { latitude: -33.85, longitude: 151.2 },
        { latitude: -33.9, longitude: 151.3 },
        { latitude: -33.95, longitude: 151.2 },
    ],
}

const CIRCLE: CoverageArea = {
    latitude: -33.9,
    longitude: 151.2,
    radiusKm: 10,
    shapeType: 'circle',
}

function renderShapeState(onChange = vi.fn()) {
    return renderHook(() =>
        useCoverageShapeState({
            value: POLYGON,
            defaultCenter: [-33.9, 151.2],
            disabled: false,
            maxRadius: 50,
            onChange,
        })
    )
}

describe('useCoverageShapeState mode switch', () => {
    it('emits shapeType circle when switching to circle coverage', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleModeChange('circle')
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'circle' })
        )
    })

    it('emits shapeType bbox when switching to bbox', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleModeChange('bbox')
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'bbox' })
        )
    })

    it('emits shapeType corridor when switching to corridor', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleModeChange('corridor')
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'corridor' })
        )
    })

    it('emits shapeType polygon when switching to polygon', () => {
        const onChange = vi.fn()
        // Start from a circle, not a polygon: switching to the shape you are
        // already in early-returns (newType === currentShapeType) and never
        // fires onChange — that was the original test's blind spot.
        const { result } = renderHook(() =>
            useCoverageShapeState({
                value: CIRCLE,
                defaultCenter: [-33.9, 151.2],
                disabled: false,
                maxRadius: 50,
                onChange,
            })
        )

        act(() => {
            result.current.handleModeChange('polygon')
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'polygon' })
        )
    })

    it('circle edits (radius/map click) also carry shapeType', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleModeChange('circle')
        })
        act(() => {
            result.current.handleRadiusChange(25)
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'circle', radiusKm: 25 })
        )
    })

    // The finding's contract is that EVERY payload carries the target
    // shapeType — not just mode switches. Exercise the non-circle edit
    // handlers so a regression there cannot silently clear the form field
    // again for three of the four shapes.
    it('bbox edits carry shapeType bbox', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleBboxChange({
                west: 150.9,
                south: -34.1,
                east: 151.5,
                north: -33.7,
            })
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'bbox', west: 150.9 })
        )
    })

    it('corridor edits carry shapeType corridor', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handleCorridorChange({
                corridorPoints: [
                    { latitude: -33.9, longitude: 151.1 },
                    { latitude: -33.85, longitude: 151.3 },
                ],
                corridorRadiusMeters: 400,
            })
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({
                shapeType: 'corridor',
                corridorRadiusMeters: 400,
            })
        )
    })

    it('polygon edits carry shapeType polygon', () => {
        const onChange = vi.fn()
        const { result } = renderShapeState(onChange)

        act(() => {
            result.current.handlePolygonChange({
                polygonPoints: [
                    { latitude: -33.95, longitude: 151.1 },
                    { latitude: -33.85, longitude: 151.2 },
                    { latitude: -33.9, longitude: 151.3 },
                ],
            })
        })

        expect(onChange).toHaveBeenLastCalledWith(
            expect.objectContaining({ shapeType: 'polygon' })
        )
    })

    it('the external-value sync settles without re-emitting changes', () => {
        // Simulate the CoverageSection loop: every emitted value is written to
        // the form and watched back as the `value` prop. The deferred sync must
        // converge without pushing further changes back out of the hook.
        let external: CoverageArea = POLYGON
        const onChange = vi.fn((next: CoverageArea) => {
            external = next
        })
        const { result, rerender } = renderHook(
            ({ value }: { value: CoverageArea }) =>
                useCoverageShapeState({
                    value,
                    defaultCenter: [-33.9, 151.2],
                    disabled: false,
                    maxRadius: 50,
                    onChange,
                }),
            { initialProps: { value: POLYGON } }
        )

        act(() => {
            result.current.handleModeChange('circle')
        })
        const callsAfterSwitch = onChange.mock.calls.length

        // Feed the emitted circle back as the new external value (what the
        // schedule form does with setValue -> watch).
        act(() => {
            rerender({ value: external })
        })

        expect(onChange.mock.calls.length).toBe(callsAfterSwitch)
        expect(result.current.currentShapeType).toBe('circle')
    })
})
