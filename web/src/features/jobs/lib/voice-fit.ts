/**
 * Whether an auditioned read at `durationSec` fits the bulletin's slot.
 *
 * The backend's editing guidance (`BulletinEditingGuidanceDto`) exposes
 * word-count bounds and a target duration but no min/max SECONDS band, so the
 * fit band is the slot target ±10% — the tolerance the duration-aware
 * pipeline is built around. The comparison is deliberately unrounded: the band
 * endpoints are the exact cutoffs, so boundary tests are precise.
 */
export interface FitVerdict {
    readonly durationSec: number
    readonly targetSec: number
    readonly lowSec: number
    readonly highSec: number
    readonly fits: boolean
}

/** Fraction of the slot target either side of the band counts as "fits". */
const FIT_TOLERANCE = 0.1

export function fitVerdictFor(
    durationSec: number,
    targetSec: number
): FitVerdict {
    const lowSec = targetSec * (1 - FIT_TOLERANCE)
    const highSec = targetSec * (1 + FIT_TOLERANCE)
    return {
        durationSec,
        targetSec,
        lowSec,
        highSec,
        fits: durationSec >= lowSec && durationSec <= highSec,
    }
}
