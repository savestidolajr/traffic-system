/**
 * Minimal ambient typings for the HERE Maps API for JavaScript (v3.2), which
 * `HereLocationMap` loads at runtime from js.api.here.com — HERE distributes
 * npm typings only through its private registry, so the CDN build is untyped.
 * Covers exactly the surface the experiment touches; extend as needed.
 */
declare namespace H {
    namespace geo {
        interface IPoint {
            lat: number
            lng: number
        }
        class LineString {
            pushLatLngAlt(lat: number, lng: number, alt?: number): void
        }
        class Rect {
            constructor(
                top: number,
                left: number,
                bottom: number,
                right: number
            )
        }
        class Polygon {
            constructor(exteriorRing: LineString)
        }
    }

    namespace map {
        namespace layer {
            /** Opaque layer handle from `createDefaultLayers()`. */
            class Layer {}
        }
        interface SpatialStyle {
            strokeColor?: string
            fillColor?: string
            lineWidth?: number
            lineDash?: readonly number[]
        }
        class Object {
            addEventListener(type: 'tap', handler: () => void): void
        }
        class Group extends Object {
            addObject(object: Object): Object
            removeAll(): void
        }
        class Icon {
            constructor(
                svg: string,
                options?: {
                    size?: { w: number; h: number }
                    anchor?: { x: number; y: number }
                }
            )
        }
        class Marker extends Object {
            constructor(
                position: geo.IPoint,
                options?: { icon?: Icon; zIndex?: number }
            )
        }
        class Polyline extends Object {
            constructor(
                line: geo.LineString,
                options?: { style?: SpatialStyle; zIndex?: number }
            )
        }
        class Polygon extends Object {
            constructor(
                geometry: geo.LineString | geo.Polygon,
                options?: { style?: SpatialStyle }
            )
        }
        class Circle extends Object {
            constructor(
                center: geo.IPoint,
                radiusMeters: number,
                options?: { style?: SpatialStyle }
            )
        }
        class Rect extends Object {
            constructor(bounds: geo.Rect, options?: { style?: SpatialStyle })
        }
    }

    class Map {
        constructor(
            element: HTMLElement,
            baseLayer: map.layer.Layer,
            options?: {
                zoom?: number
                center?: geo.IPoint
                pixelRatio?: number
            }
        )
        addLayer(layer: map.layer.Layer): void
        addObject(object: map.Object): map.Object
        setCenter(center: geo.IPoint, animate?: boolean): void
        getZoom(): number
        setZoom(zoom: number, animate?: boolean): void
        getViewPort(): { resize(): void }
        getViewModel(): {
            setLookAtData(
                data: {
                    bounds?: geo.Rect
                    position?: geo.IPoint
                    zoom?: number
                },
                animate?: boolean
            ): void
            getLookAtData(): {
                position?: geo.IPoint
                zoom?: number
            }
        }
        dispose(): void
    }

    namespace mapevents {
        class MapEvents {
            constructor(map: H.Map)
        }
        class Behavior {
            constructor(events: MapEvents)
        }
    }

    namespace service {
        interface DefaultLayers {
            vector: {
                normal: Record<string, map.layer.Layer | undefined>
                traffic?: Record<string, map.layer.Layer | undefined>
            }
        }
        class Platform {
            constructor(options: { apikey: string })
            createDefaultLayers(): DefaultLayers
        }
    }

    namespace ui {
        class InfoBubble {
            constructor(position: geo.IPoint, options?: { content?: string })
        }
        class UI {
            constructor(map: H.Map)
            static createDefault(map: H.Map, layers: service.DefaultLayers): UI
            addBubble(bubble: InfoBubble): void
            removeBubble(bubble: InfoBubble): void
        }
    }
}
