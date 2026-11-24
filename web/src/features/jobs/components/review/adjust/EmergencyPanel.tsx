import { TriangleAlert } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { cn } from '@/lib/utils'
import type { EmergencyEvent, SourceFetchDiagnostic } from '../../../types'
import type { EmergencyTierTone } from '../../../lib/emergency'
import {
    emergencyCategoryLabel,
    emergencyFeedState,
    emergencyTierDotClass,
    emergencyTierTone,
    EMERGENCY_TIER_LABEL,
    mustAirCount,
    sortEmergencyEvents,
    suppressionSummary,
} from '../../../lib/emergency'

interface EmergencyPanelProps {
    readonly events: readonly EmergencyEvent[]
    /**
     * This bulletin's `dataquoll` source outcome. Without it an empty list cannot be explained,
     * and the panel would report "no incidents" for a feed that was never asked.
     */
    readonly diagnostic?: SourceFetchDiagnostic
}

function eventPlace(event: EmergencyEvent): string {
    if (event.address?.trim()) return event.address
    if (event.suburb?.trim()) return event.suburb
    return event.title
}

function distanceLabel(distanceKm: number | null | undefined): string {
    return distanceKm == null
        ? 'distance unavailable'
        : `${distanceKm.toFixed(1)} km`
}

function tierToneClass(tone: EmergencyTierTone): string {
    if (tone === 'destructive') return 'text-destructive'
    if (tone === 'warning') return 'text-warning'
    return 'text-muted-foreground'
}

export function EmergencyPanel({ events, diagnostic }: EmergencyPanelProps) {
    const orderedEvents = sortEmergencyEvents(events)
    const forcedCount = mustAirCount(events)
    const suppression = suppressionSummary(events)
    const feedState = emergencyFeedState(events, diagnostic)

    return (
        <SpotlightCard className="flex min-h-0 flex-1 flex-col overflow-hidden p-3">
            <div className="flex h-full min-h-0 flex-col gap-2">
                <header className="flex shrink-0 items-center gap-1.5">
                    <TriangleAlert
                        className="size-3.5 shrink-0 text-muted-foreground"
                        aria-hidden
                    />
                    <h3 className="text-xs font-semibold">
                        Emergency services
                    </h3>
                    <span className="text-2xs text-muted-foreground">
                        ({events.length})
                    </span>
                    {forcedCount > 0 && (
                        <Badge
                            variant="destructive"
                            className="ml-auto px-1.5 py-0.5 text-2xs font-semibold"
                        >
                            {forcedCount} MUST AIR
                        </Badge>
                    )}
                    <span
                        className={cn(
                            'shrink-0 text-2xs text-muted-foreground',
                            forcedCount > 0 ? '' : 'ml-auto'
                        )}
                    >
                        DataQuoll
                    </span>
                </header>

                <p
                    id="emergency-panel-observation-note"
                    className="shrink-0 text-2xs leading-relaxed text-muted-foreground"
                >
                    Observation only. These incidents are not yet wired to the
                    script.
                </p>

                <ul
                    aria-label="Emergency service incidents"
                    aria-describedby="emergency-panel-observation-note"
                    className="min-h-0 flex-1 overflow-y-auto divide-y divide-border/40"
                >
                    {orderedEvents.length === 0 ? (
                        <li
                            className={cn(
                                'py-2 text-xs',
                                feedState.kind === 'broken'
                                    ? 'text-warning'
                                    : 'text-muted-foreground'
                            )}
                        >
                            {feedState.kind === 'events'
                                ? null
                                : feedState.message}
                        </li>
                    ) : (
                        orderedEvents.map((event) => {
                            const forced =
                                event.warningLevel === 'EmergencyWarning'
                            const place = eventPlace(event)
                            const toneClass = tierToneClass(
                                emergencyTierTone(event.warningLevel)
                            )
                            return (
                                <li
                                    key={event.id}
                                    className={cn(
                                        'flex min-w-0 items-start gap-2 py-2 first:pt-1 last:pb-1',
                                        forced &&
                                            'border-l-2 border-l-destructive pl-2'
                                    )}
                                >
                                    <span
                                        className={cn(
                                            'mt-1.5 size-2 shrink-0 rounded-full',
                                            emergencyTierDotClass(
                                                event.warningLevel
                                            )
                                        )}
                                        aria-hidden
                                    />
                                    <div
                                        className={cn(
                                            'min-w-0 flex-1',
                                            // Suppressed records stay listed so the operator can see
                                            // what was seen and rejected, but they must not compete
                                            // for attention with content that could actually air.
                                            // The tier dot keeps full contrast: a high tier still
                                            // has to be findable even when it is not usable.
                                            !event.airEligible && 'opacity-60'
                                        )}
                                    >
                                        <div className="flex min-w-0 items-center gap-1.5">
                                            <span
                                                className={cn(
                                                    'min-w-0 truncate text-xs',
                                                    forced && 'font-semibold'
                                                )}
                                                title={place}
                                            >
                                                {place}
                                            </span>
                                            {forced && (
                                                <span className="shrink-0 text-[9px] font-semibold uppercase tracking-wide text-destructive">
                                                    FORCED
                                                </span>
                                            )}
                                        </div>
                                        <p
                                            className={cn(
                                                'truncate text-2xs',
                                                toneClass
                                            )}
                                        >
                                            {emergencyCategoryLabel(
                                                event.eventCategory ?? ''
                                            )}{' '}
                                            ·{' '}
                                            {
                                                EMERGENCY_TIER_LABEL[
                                                    event.warningLevel
                                                ]
                                            }{' '}
                                            · {distanceLabel(event.distanceKm)}
                                            {!event.airEligible && (
                                                <span className="text-muted-foreground">
                                                    {' '}
                                                    · not airable
                                                </span>
                                            )}
                                        </p>
                                    </div>
                                    <input
                                        type="checkbox"
                                        checked={forced}
                                        disabled
                                        readOnly
                                        title="Read-only in Phase 1; not yet wired to the script"
                                        aria-label={`${place}: ${forced ? 'forced inclusion' : 'not included'}; read-only in Phase 1`}
                                        className={cn(
                                            'mt-0.5 size-3.5 shrink-0 accent-primary',
                                            forced && 'accent-destructive'
                                        )}
                                    />
                                </li>
                            )
                        })
                    )}
                </ul>

                {suppression && (
                    <p className="shrink-0 border-t border-border/40 pt-2 text-2xs leading-relaxed text-muted-foreground">
                        {suppression}
                    </p>
                )}
            </div>
        </SpotlightCard>
    )
}
