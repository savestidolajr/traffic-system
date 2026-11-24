import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { CoverageArea } from '@/lib/api/schemas/shared'
import { cn } from '@/lib/utils'
import type { TrafficCorridor, JobIncident } from '../types'
import {
    AVAILABLE_STROKE,
    buildEvidencePoints,
} from '../lib/map/evidence-points'
import type { EvidencePoint } from '../lib/map/evidence-points'

// ---------------------------------------------------------------------------
// Evidence map on HERE's own basemap with the live traffic flow + incident
// overlay from the Traffic Vector Tile API — the same pipeline the bulletin's
// evidence came from. Two layers on one canvas: what the system saw at fetch
// time (our persisted marks) vs what HERE says right now (the live overlay).
// The browser map key is supplied via the `apiKey` prop, fetched at runtime
// from the authenticated backend (GET /api/map-config) — never bundled.
// `AdjustMap` shows `MapUnavailable` instead when no key is configured.
// ---------------------------------------------------------------------------

/** HERE Maps API version served from js.api.here.com. 3.2 carries the
 *  `litenight` dark styles and the restructured `vector.traffic.*` overlay. */
const HERE_JS_BASE = 'https://js.api.here.com/v3/3.2'

let hereLoader: Promise<void> | null = null

function loadScript(src: string): Promise<void> {
    const { promise, resolve, reject } = Promise.withResolvers<void>()
    const el = document.createElement('script')
    el.src = src
    el.async = true
    el.onload = () => resolve()
    el.onerror = () => reject(new Error(`Failed to load ${src}`))
    document.head.appendChild(el)
    return promise
}

/** Load the HERE JS API exactly once: core first, then the sibling modules
 *  (service/mapevents/ui all depend only on core). */
function loadHereMaps(): Promise<void> {
    hereLoader ??= (async () => {
        const css = document.createElement('link')
        css.rel = 'stylesheet'
        css.href = `${HERE_JS_BASE}/mapsjs-ui.css`
        document.head.appendChild(css)
        await loadScript(`${HERE_JS_BASE}/mapsjs-core.js`)
        await Promise.all([
            loadScript(`${HERE_JS_BASE}/mapsjs-service.js`),
            loadScript(`${HERE_JS_BASE}/mapsjs-mapevents.js`),
            loadScript(`${HERE_JS_BASE}/mapsjs-ui.js`),
        ])
    })()
    return hereLoader
}

/** HERE renders marks on a WebGL canvas, where CSS custom properties never
 *  resolve — flatten `hsl(var(--x))` (theme vars hold a bare `H S% L%`
 *  triplet) into a literal `hsl(H, S%, L%)` the HERE colour parser accepts. */
function resolveCssColor(color: string): string {
    return color.replace(
        /hsl\(var\((--[a-z0-9-]+)\)\)/gi,
        (match, name: string) => {
            const triplet = getComputedStyle(document.documentElement)
                .getPropertyValue(name)
                .trim()
            if (!triplet) return match
            return `hsl(${triplet.split(/\s+/).join(', ')})`
        }
    )
}

// Dashed indigo coverage outline, shared by every coverage shape.
const COVERAGE_INDIGO = 'hsl(239, 84%, 67%)'
const COVERAGE_STYLE: H.map.SpatialStyle = {
    strokeColor: COVERAGE_INDIGO,
    fillColor: 'rgba(99, 102, 241, 0.08)',
    lineWidth: 1.5,
    lineDash: [6, 4],
}

/** Mark grammar: solid = in bulletin, hollow = available candidate,
 *  indigo halo = highlighted. */
function pinSvg(color: string, highlighted: boolean, muted: boolean): string {
    const size = highlighted ? 18 : 12
    const c = size / 2
    const halo = highlighted
        ? `<circle cx="${c}" cy="${c}" r="${c - 1}" fill="none" stroke="${COVERAGE_INDIGO}" stroke-width="2" opacity="0.75"/>`
        : ''
    const body = muted
        ? `<circle cx="${c}" cy="${c}" r="${c - 2.5}" fill="none" stroke="${color}" stroke-width="1.75" opacity="0.85"/>`
        : `<circle cx="${c}" cy="${c}" r="${c - 2.5}" fill="${color}" stroke="white" stroke-width="1.5"/>`
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">${halo}${body}</svg>`
}

/** Tap-bubble content. Incident headlines come from HERE free text — escape
 *  them; InfoBubble content is raw HTML. Bubble chrome is light-themed by
 *  the HERE UI CSS, so the text colour is fixed dark, not a theme var. */
function bubbleHtml(label: string): string {
    const escaped = label
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
    return `<div style="max-width:260px;font:500 12px/1.4 system-ui,sans-serif;color:#111827;padding:2px 6px">${escaped}</div>`
}

function lineStringOf(points: readonly { lat: number; lng: number }[]) {
    const line = new H.geo.LineString()
    for (const p of points) line.pushLatLngAlt(p.lat, p.lng)
    return line
}

/** The bulletin's point-in-time coverage shape as HERE map objects. */
function coverageObjects(coverage: CoverageArea | undefined): H.map.Object[] {
    if (!coverage) return []

    switch (coverage.shapeType) {
        case 'bbox':
            if (
                coverage.west != null &&
                coverage.south != null &&
                coverage.east != null &&
                coverage.north != null
            ) {
                return [
                    new H.map.Rect(
                        new H.geo.Rect(
                            coverage.north,
                            coverage.west,
                            coverage.south,
                            coverage.east
                        ),
                        { style: COVERAGE_STYLE }
                    ),
                ]
            }
            break
        case 'polygon':
            if ((coverage.polygonPoints?.length ?? 0) >= 3) {
                // H.map.Polygon rejects a bare LineString — it must be
                // wrapped in an H.geo.Polygon exterior ring.
                return [
                    new H.map.Polygon(
                        new H.geo.Polygon(
                            lineStringOf(coverage.polygonPoints ?? [])
                        ),
                        { style: COVERAGE_STYLE }
                    ),
                ]
            }
            break
        case 'corridor':
            if ((coverage.corridorPoints?.length ?? 0) >= 2) {
                return [
                    new H.map.Polyline(
                        lineStringOf(coverage.corridorPoints ?? []),
                        { style: COVERAGE_STYLE }
                    ),
                ]
            }
            break
        case 'circle':
            return [
                new H.map.Circle(
                    { lat: coverage.latitude, lng: coverage.longitude },
                    coverage.radiusKm * 1000,
                    { style: COVERAGE_STYLE }
                ),
            ]
    }

    // A derived enclosing radius can be much larger than the actual drawn
    // shape. Never turn missing/unknown geometry into a hollow circle.
    return []
}

export interface EvidenceMapProps {
    readonly corridors: readonly TrafficCorridor[]
    readonly incidents: readonly JobIncident[]
    readonly bbox?: readonly number[]
    /** Point-in-time coverage shape snapshot. If absent or incomplete, the
     * evidence points remain visible without an approximate coverage circle. */
    readonly coverage?: CoverageArea
    /** Grow to fill the parent's height instead of a fixed 300px. */
    readonly fill?: boolean
    /** Incident keys (or corridor road names) whose marks should pop. */
    readonly highlightedKeys?: readonly string[]
    /** Keys rendered as hollow "candidate" marks (available, not in bulletin). */
    readonly mutedKeys?: readonly string[]
    /** Clicking a mark reports its incident key (or corridor road name). */
    readonly onMarkerClick?: (key: string) => void
    /** UI chrome variant — 'full' shows the default HERE UI controls,
     *  'minimal' suppresses them for compact/embedded layouts. */
    readonly chrome?: 'full' | 'minimal'
    /** Key to focus and fly the map to on mount (opens its info bubble). */
    readonly focusedKey?: string | null
    /** Whether to show the basemap attribution footnote. */
    readonly footnote?: boolean
    /** HERE Maps JS API key, fetched at runtime from the authenticated backend
     *  (never bundled). The parent shows `MapUnavailable` when unset, so this
     *  is always a real key by the time the component mounts. */
    readonly apiKey: string
}

/**
 * Evidence map on a HERE basemap. Tooltips are omitted (HERE marks are
 * canvas-drawn, not DOM) — click a mark to pin its incident.
 */
export function HereLocationMap({
    corridors,
    incidents,
    bbox,
    coverage,
    fill,
    highlightedKeys,
    mutedKeys,
    onMarkerClick,
    chrome = 'full',
    focusedKey,
    footnote = true,
    apiKey,
}: EvidenceMapProps) {
    const containerRef = useRef<HTMLDivElement>(null)
    const mapRef = useRef<H.Map | null>(null)
    const groupRef = useRef<H.map.Group | null>(null)
    const uiRef = useRef<H.ui.UI | null>(null)
    const bubbleRef = useRef<H.ui.InfoBubble | null>(null)
    const bubbleKeyRef = useRef<string | null>(null)
    // Camera + overlay are synced by VALUE, not identity: the review page
    // polls the job every few seconds, rebuilding the incident/corridor
    // arrays with identical content — without these guards the fit effect
    // yanked the viewport back to the bbox on every poll.
    const fitSigRef = useRef('')
    const overlaySigRef = useRef('')
    // Tap handlers live in the (guard-skipped) overlay effect — route the
    // callback through a ref so a new closure never forces a mark rebuild.
    const onMarkerClickRef = useRef(onMarkerClick)
    // Chrome is fixed for an instance's lifetime (card = minimal, explorer =
    // full) — captured once for the mount-only effect.
    const chromeRef = useRef(chrome)
    // Captured once for the mount-only init effect, like `chrome`.
    const apiKeyRef = useRef(apiKey)
    const focusSigRef = useRef<string | null>(null)
    const flyAnimRef = useRef<number | null>(null)
    const [ready, setReady] = useState(false)
    const [loadError, setLoadError] = useState(false)

    useEffect(() => {
        onMarkerClickRef.current = onMarkerClick
    }, [onMarkerClick])

    /** Pan/zoom to a point FAST. HERE's own `animate` boolean on
     *  `setCenter`/`setLookAtData` has no duration control and runs a
     *  noticeably slow default transition — this is a manual, ease-out
     *  tween (150ms, matches the design system's product-register motion
     *  ceiling) that gives full control and is instant under
     *  prefers-reduced-motion. */
    const flyTo = useCallback((lat: number, lng: number, zoom: number) => {
        const map = mapRef.current
        if (!map) return
        if (flyAnimRef.current != null) {
            cancelAnimationFrame(flyAnimRef.current)
            flyAnimRef.current = null
        }
        const vm = map.getViewModel()
        const reduceMotion = window.matchMedia(
            '(prefers-reduced-motion: reduce)'
        ).matches
        if (reduceMotion) {
            vm.setLookAtData({ position: { lat, lng }, zoom }, false)
            return
        }
        const from = vm.getLookAtData()
        const fromLat = from.position?.lat ?? lat
        const fromLng = from.position?.lng ?? lng
        const fromZoom = from.zoom ?? zoom
        const durationMs = 150
        const start = performance.now()
        const step = (now: number) => {
            const t = Math.min(1, (now - start) / durationMs)
            const eased = 1 - (1 - t) ** 4 // ease-out-quart
            vm.setLookAtData(
                {
                    position: {
                        lat: fromLat + (lat - fromLat) * eased,
                        lng: fromLng + (lng - fromLng) * eased,
                    },
                    zoom: fromZoom + (zoom - fromZoom) * eased,
                },
                false
            )
            flyAnimRef.current = t < 1 ? requestAnimationFrame(step) : null
        }
        flyAnimRef.current = requestAnimationFrame(step)
    }, [])

    /** Open (or replace) the single callout for a mark. Anchored to a geo
     *  position so it survives overlay rebuilds; refs only, hence stable. */
    const openBubble = useCallback((p: EvidencePoint) => {
        const ui = uiRef.current
        if (!ui) return
        if (bubbleRef.current) ui.removeBubble(bubbleRef.current)
        const bubble = new H.ui.InfoBubble(
            { lat: p.lat, lng: p.lng },
            { content: bubbleHtml(p.label) }
        )
        ui.addBubble(bubble)
        bubbleRef.current = bubble
        bubbleKeyRef.current = p.key
    }, [])

    const points = useMemo(
        () => buildEvidencePoints(corridors, incidents),
        [corridors, incidents]
    )

    const center = useMemo((): { lat: number; lng: number } => {
        if (bbox && bbox.length >= 4) {
            return {
                lat: (bbox[1] + bbox[3]) / 2,
                lng: (bbox[0] + bbox[2]) / 2,
            }
        }
        if (points.length > 0) {
            return {
                lat: points.reduce((s, p) => s + p.lat, 0) / points.length,
                lng: points.reduce((s, p) => s + p.lng, 0) / points.length,
            }
        }
        return { lat: -33.87, lng: 151.21 }
    }, [points, bbox])

    // The map mounts once at a neutral camera; the fit effect below takes
    // over immediately after `ready` flips (bbox bounds, else the evidence
    // centroid), so the creation-time center is never user-visible.

    useEffect(() => {
        let disposed = false
        let created = false
        let settleTimer: number | undefined
        let tabindexObserver: MutationObserver | null = null

        const createMap = () => {
            if (created || disposed || !containerRef.current) return
            created = true
            void loadHereMaps()
                .then(() => {
                    if (disposed || !containerRef.current) return
                    const platform = new H.service.Platform({
                        apikey: apiKeyRef.current,
                    })
                    const layers = platform.createDefaultLayers()
                    // Dark base to match the app theme; fall back to the day
                    // map if a future API version drops the litenight style.
                    const base =
                        layers.vector.normal.litenight ??
                        layers.vector.normal.map
                    if (!base) throw new Error('HERE default layers missing')
                    const map = new H.Map(containerRef.current, base, {
                        zoom: 11,
                        center: { lat: -33.87, lng: 151.21 },
                        pixelRatio: window.devicePixelRatio || 1,
                    })
                    new H.mapevents.Behavior(new H.mapevents.MapEvents(map))
                    // 'minimal' (the Adjust-tab card): a bare UI instance —
                    // still required for InfoBubbles — with none of the
                    // default controls; wheel/drag zoom stay live via
                    // Behavior. 'full' (the explorer): zoom, layer switcher,
                    // scale bar.
                    uiRef.current =
                        chromeRef.current === 'minimal'
                            ? new H.ui.UI(map)
                            : H.ui.UI.createDefault(map, layers)
                    // The point of the experiment: HERE's live traffic flow
                    // + incident markers, straight from the Traffic Vector
                    // Tile API.
                    const traffic =
                        layers.vector.traffic?.litenight ??
                        layers.vector.traffic?.map
                    if (traffic) map.addLayer(traffic)
                    const group = new H.map.Group()
                    map.addObject(group)
                    mapRef.current = map
                    groupRef.current = group
                    // HERE's SDK injects its copyright link with
                    // tabindex="1", hijacking the whole page's tab order
                    // (axe: tabindex). The chrome lands asynchronously, so
                    // watch the container and normalize any positive
                    // tabindex the moment it appears.
                    if (containerRef.current) {
                        const normalize = (root: ParentNode) => {
                            for (const el of root.querySelectorAll(
                                '[tabindex]'
                            )) {
                                if (Number(el.getAttribute('tabindex')) > 0)
                                    el.setAttribute('tabindex', '0')
                            }
                        }
                        normalize(containerRef.current)
                        tabindexObserver = new MutationObserver(() => {
                            if (containerRef.current)
                                normalize(containerRef.current)
                        })
                        tabindexObserver.observe(containerRef.current, {
                            childList: true,
                            subtree: true,
                        })
                    }
                    if (import.meta.env.DEV) {
                        // Dev-only E2E hook: lets headless verification read
                        // the camera and compute mark screen positions.
                        // Never set in production builds.
                        const w = window as unknown as Record<string, unknown>
                        w.__arnHereMap = map
                        w.__arnHereUi = uiRef.current
                    }
                    // Construction is now gated on a real measured box (see
                    // the ResizeObserver below), but a host that animates
                    // itself in with a CSS transform (the explorer Dialog's
                    // scale + fade, ~200ms) can still leave the pixel-perfect
                    // canvas read slightly off mid-transition — one deferred
                    // resize after the animation settles corrects it.
                    settleTimer = window.setTimeout(
                        () => mapRef.current?.getViewPort().resize(),
                        400
                    )
                    setReady(true)
                })
                .catch(() => {
                    if (!disposed) setLoadError(true)
                })
        }

        // harp reads the container's box at construction time — a lazily
        // mounted (Suspense) or animated-in (Dialog) host can commit with a
        // 0×0 box for a frame or more, and a map built against 0×0 never
        // requests a single base tile (confirmed: creating H.Map on a 0×0
        // element fetches zero vectortiles/base requests, no matter how
        // long you wait — a later resize() is what recovers it). Rather
        // than guess a mount-order/animation timing story, gate creation on
        // the FIRST measured non-zero size: ResizeObserver's initial
        // callback reports the current box even if it never "changes" from
        // unobserved, so this fires the moment there's something real to
        // build against — immediately for an already-laid-out host, or the
        // instant a lazy/animated one reveals. Once built, the same
        // observer keeps the viewport in sync with every later resize.
        // Primary path: create as soon as the container has a real measured
        // box, WITHOUT depending on a ResizeObserver callback — browsers
        // throttle/suppress RO (and rAF) delivery for background or offscreen
        // tabs, so an RO-only gate can leave the map uncreated (blank) until
        // the tab is focused. A direct getBoundingClientRect read works
        // regardless of visibility. Poll briefly to cover a host that is still
        // laying out (lazy Suspense / Dialog entrance) on the first ticks.
        let pollId: number | undefined
        let polls = 0
        const tryCreateFromMeasuredBox = () => {
            if (created || disposed) return true
            const el = containerRef.current
            if (el) {
                const r = el.getBoundingClientRect()
                if (r.width > 0 && r.height > 0) {
                    createMap()
                    return true
                }
            }
            return false
        }
        const pollForBox = () => {
            if (tryCreateFromMeasuredBox() || polls++ > 40) return
            pollId = window.setTimeout(pollForBox, 50)
        }
        pollForBox()
        // Fallback + ongoing sync: RO fires on reveal/resize (when the tab is
        // live) and keeps the viewport correct after later layout changes.
        const observer = new ResizeObserver((entries) => {
            if (!created) {
                const rect = entries[0]?.contentRect
                if (rect && rect.width > 0 && rect.height > 0) createMap()
                return
            }
            mapRef.current?.getViewPort().resize()
        })
        if (containerRef.current) observer.observe(containerRef.current)

        return () => {
            disposed = true
            window.clearTimeout(settleTimer)
            if (pollId != null) window.clearTimeout(pollId)
            if (flyAnimRef.current != null)
                cancelAnimationFrame(flyAnimRef.current)
            observer.disconnect()
            tabindexObserver?.disconnect()
            mapRef.current?.dispose()
            mapRef.current = null
            groupRef.current = null
            uiRef.current = null
            bubbleRef.current = null
        }
    }, [])

    // Fit the camera when the map appears or the framing VALUES change:
    // bbox bounds when the job has one, otherwise the evidence centroid.
    useEffect(() => {
        if (!ready || !mapRef.current) return
        const sig =
            bbox && bbox.length >= 4
                ? bbox.join(',')
                : `${center.lat.toFixed(5)},${center.lng.toFixed(5)}`
        if (sig === fitSigRef.current) return
        fitSigRef.current = sig
        if (bbox && bbox.length >= 4) {
            mapRef.current.getViewModel().setLookAtData(
                {
                    bounds: new H.geo.Rect(bbox[3], bbox[0], bbox[1], bbox[2]),
                },
                false
            )
        } else {
            mapRef.current
                .getViewModel()
                .setLookAtData({ position: center, zoom: 11 }, false)
        }
    }, [ready, bbox, center])

    // List → map selection: fly to the focused incident and open its
    // callout. Value-guarded so poll-driven re-renders never re-fly, and the
    // guard RESETS when focus clears — a transient rail hover leaves the row,
    // then re-enters the same incident, and must fly again.
    useEffect(() => {
        if (!ready || !mapRef.current) return
        if (!focusedKey) {
            focusSigRef.current = null
            return
        }
        if (focusSigRef.current === focusedKey) return
        focusSigRef.current = focusedKey
        const p = points.find((pt) => pt.key === focusedKey)
        if (!p) return
        const targetZoom = Math.max(mapRef.current.getZoom(), 13)
        flyTo(p.lat, p.lng, targetZoom)
        openBubble(p)
    }, [ready, focusedKey, points, openBubble, flyTo])

    // Rebuild the evidence overlay when the picks CHANGE BY VALUE: lines for
    // road geometry, dots otherwise; solid = in bulletin, hollow = available;
    // indigo halo = highlighted.
    // Tapping a mark opens an info bubble AND pins the incident in the
    // rundown (same gesture as the evidence rail).
    useEffect(() => {
        const group = groupRef.current
        if (!ready || !group) return
        const sig = JSON.stringify([
            points,
            mutedKeys,
            highlightedKeys,
            coverage,
            bbox,
        ])
        if (sig === overlaySigRef.current) return
        overlaySigRef.current = sig
        // The bubble is anchored to a geo position, not to a mark object, so
        // it SURVIVES rebuilds — tapping a mark pins the incident, which
        // re-runs this effect, and closing the bubble here would kill it the
        // instant it opened. Close it only when its incident left the job.
        if (
            bubbleRef.current &&
            bubbleKeyRef.current &&
            !points.some((p) => p.key === bubbleKeyRef.current)
        ) {
            uiRef.current?.removeBubble(bubbleRef.current)
            bubbleRef.current = null
            bubbleKeyRef.current = null
        }
        group.removeAll()

        for (const obj of coverageObjects(coverage)) {
            group.addObject(obj)
        }

        for (const p of points) {
            const lit = highlightedKeys?.includes(p.key) === true
            const muted = mutedKeys?.includes(p.key) === true
            const color = resolveCssColor(muted ? AVAILABLE_STROKE : p.color)
            let obj: H.map.Object
            if (p.shape) {
                const line = new H.geo.LineString()
                for (const [lat, lng] of p.shape) line.pushLatLngAlt(lat, lng)
                // HERE's option validators reject explicitly-undefined
                // properties (`lineDash: undefined` throws) — omit instead.
                obj = new H.map.Polyline(line, {
                    style: {
                        strokeColor: color,
                        lineWidth: muted ? 2.5 : lit ? 7 : 4,
                        ...(muted ? { lineDash: [6, 6] } : {}),
                    },
                    ...(lit ? { zIndex: 1000 } : {}),
                })
            } else {
                const size = lit ? 18 : 12
                obj = new H.map.Marker(
                    { lat: p.lat, lng: p.lng },
                    {
                        icon: new H.map.Icon(pinSvg(color, lit, muted), {
                            size: { w: size, h: size },
                            anchor: { x: size / 2, y: size / 2 },
                        }),
                        ...(lit ? { zIndex: 1000 } : {}),
                    }
                )
            }
            obj.addEventListener('tap', () => {
                openBubble(p)
                onMarkerClickRef.current?.(p.key)
            })
            group.addObject(obj)
        }
    }, [
        ready,
        points,
        coverage,
        bbox,
        center,
        highlightedKeys,
        mutedKeys,
        openBubble,
    ])

    const hasMapContent = points.length > 0 || (bbox?.length ?? 0) >= 4

    return (
        <div
            className={cn(
                'flex flex-col gap-2',
                fill && 'h-full',
                !hasMapContent && 'hidden'
            )}
        >
            <div
                className={cn(
                    'relative overflow-hidden rounded-lg border',
                    fill && 'min-h-60 flex-1'
                )}
                style={fill ? undefined : { height: 300 }}
            >
                <div ref={containerRef} className="size-full" />
                {loadError && (
                    <p className="absolute inset-0 grid place-items-center text-xs text-muted-foreground">
                        HERE map failed to load. Check network access to
                        js.api.here.com.
                    </p>
                )}
            </div>
            {footnote && (
                <p className="text-xs text-muted-foreground">
                    {points.length} incidents shown · basemap + live traffic
                    &copy; HERE (experiment): flow colouring and incident
                    markers are HERE's own live overlay; our marks are the
                    bulletin's evidence at fetch time
                </p>
            )}
        </div>
    )
}
