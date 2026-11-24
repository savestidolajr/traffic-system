import type { ContentSelection } from '@/lib/api/schemas/shared'

/** Summarise the enabled bulletin content sources for studio step rails.
 *  "Always include" sources are marked so the operator sees them without
 *  opening the step; the rail is narrow, so the marker stays inline. */
export function summariseContentSources(
    content: ContentSelection | null | undefined
): string {
    if (!content) return 'Choose sources'
    const parts: string[] = []
    if (content.traffic) parts.push('Traffic')
    if (content.weather) {
        parts.push(content.weatherRequired ? 'Weather (always)' : 'Weather')
    }
    if (content.fuel) {
        parts.push(content.fuelRequired ? 'Fuel (always)' : 'Fuel')
    }
    return parts.length > 0 ? parts.join(' · ') : 'Choose sources'
}
