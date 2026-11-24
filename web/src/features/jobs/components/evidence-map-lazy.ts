import { lazy } from 'react'

/**
 * The evidence map on HERE's own basemap with the live traffic flow +
 * incident overlay — the same source the bulletin's evidence came from.
 * Two layers on one canvas: what the system saw at fetch time (our
 * persisted marks) vs what HERE says right now (the live overlay).
 * Shared by the Adjust-tab card and the expanded explorer so the two
 * can never disagree on which variant is live.
 */
export const EvidenceMap = lazy(() =>
    import('./HereLocationMap').then((m) => ({
        default: m.HereLocationMap,
    }))
)
