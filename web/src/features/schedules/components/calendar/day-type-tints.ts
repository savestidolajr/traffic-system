/**
 * Tints for pattern days, assigned by the pattern's position in the calendar so
 * a station's "Public holiday" keeps one colour in the year grid, the legend, the
 * pattern list and the exceptions table.
 *
 * Deliberately few, and drawn only from tokens this theme actually defines
 * (`--primary`, `--info`, `--processing`, `--status-approved`): past four distinct
 * day types a year grid stops being readable, so the fifth pattern onwards reuses
 * the palette rather than inventing an unreadable hue. An earlier draft used
 * Tailwind's `chart-*` names, which this theme does not register — the swatches
 * rendered fully transparent and the legend looked broken.
 *
 * Lives outside the component files on purpose: exporting a non-component from a
 * component module disables React Fast Refresh for that module.
 */
const PATTERN_TINTS = [
    'bg-primary text-primary-foreground hover:bg-primary/80',
    'bg-info text-info-foreground hover:bg-info/80',
    'bg-processing text-processing-foreground hover:bg-processing/80',
    'bg-status-approved text-background hover:bg-status-approved/80',
] as const

export function patternTintClass(patternIndex: number): string {
    return PATTERN_TINTS[patternIndex % PATTERN_TINTS.length]!
}
