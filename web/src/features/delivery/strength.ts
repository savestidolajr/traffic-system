/**
 * Tagging strength, the operator-facing replacement for the raw `intensity`
 * number. The editor never exposes intensity 0 (Neutral) - "no delivery style"
 * is chosen in the station schedule, not authored here. 0 maps to "Neutral"
 * only for displaying a legacy/neutral policy that slipped through.
 */
export type Strength = 1 | 2 | 3

export interface StrengthOption {
    readonly value: Strength
    readonly label: string
    readonly description: string
}

export const STRENGTH_OPTIONS: readonly StrengthOption[] = [
    {
        value: 1,
        label: 'Light touch',
        description: 'Small emphasis only. Mostly natural read.',
    },
    {
        value: 2,
        label: 'Balanced',
        description: 'Noticeable pacing and emphasis where useful.',
    },
    {
        value: 3,
        label: 'High colour',
        description:
            'Frequent cues. Use only when a more performed read is wanted.',
    },
]

const LABEL_BY_VALUE: Record<number, string> = {
    0: 'Neutral',
    1: 'Light touch',
    2: 'Balanced',
    3: 'High colour',
}

export function strengthLabel(value: number): string {
    return LABEL_BY_VALUE[value] ?? `Level ${value}`
}

/** Coerce a stored intensity (which may be 0/legacy) into a valid editor Strength. */
export function toStrength(intensity: number): Strength {
    return intensity >= 1 && intensity <= 3 ? (intensity as Strength) : 2
}
