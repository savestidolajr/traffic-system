import { Timer } from 'lucide-react'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { cn } from '@/lib/utils'
import type { MonitoredRoute } from '../../../types'

/**
 * The configured point-to-point runs, measured live against their typical time.
 *
 * Lives on the ADJUST surface, not in the reading column's Sources panel. Sources
 * exists to verify every NON-traffic claim — weather, fuel, good runs — "the same
 * role the incident index plays for corridors" (see `ReviewSources`). A drive time
 * IS traffic, so putting it there meant rewording that panel's whole purpose from
 * "besides traffic" to "besides incident traffic" to make room, which is the tell
 * that it never belonged. Adjust is where the measured road numbers already live.
 *
 * Read-only on purpose, and deliberately compact: a monitored route is configured
 * per station, not curated per bulletin, so unlike every row in the rundown beside
 * it there is nothing here to toggle or reorder — and a run that is two minutes off
 * its usual does not warrant hero numerals.
 */
export function DriveTimesCard({
    routes,
}: {
    readonly routes: readonly MonitoredRoute[]
}) {
    if (routes.length === 0) return null

    return (
        <SpotlightCard className="p-3">
            <div className="mb-2 flex items-center gap-1.5">
                <Timer
                    className="size-3.5 shrink-0 text-muted-foreground"
                    aria-hidden
                />
                <h3 className="text-2xs font-semibold uppercase tracking-wide text-muted-foreground">
                    Drive times
                </h3>
            </div>
            <ul className="flex flex-col divide-y divide-border/40">
                {routes.map((route) => {
                    const late = route.delayMinutes > 0
                    const early = route.delayMinutes < 0
                    const delta = late
                        ? `+${route.delayMinutes} min`
                        : early
                          ? `${route.delayMinutes} min`
                          : 'on time'
                    return (
                        <li
                            key={route.name}
                            className="flex min-w-0 items-baseline justify-between gap-3 py-1.5 first:pt-0 last:pb-0"
                        >
                            <span className="min-w-0 truncate text-sm">
                                {route.name}
                            </span>
                            <span className="flex shrink-0 items-baseline gap-2 text-xs">
                                <span className="font-semibold tabular-nums">
                                    {route.driveTimeMinutes}
                                    <span className="ml-0.5 font-normal text-muted-foreground">
                                        min
                                    </span>
                                </span>
                                <span className="tabular-nums text-muted-foreground">
                                    typical {route.normalDriveTimeMinutes}
                                </span>
                                <span
                                    className={cn(
                                        'tabular-nums',
                                        late
                                            ? 'text-warning'
                                            : early
                                              ? 'text-success'
                                              : 'text-muted-foreground'
                                    )}
                                >
                                    {delta}
                                </span>
                            </span>
                        </li>
                    )
                })}
            </ul>
        </SpotlightCard>
    )
}
