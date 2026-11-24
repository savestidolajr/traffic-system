import { useMemo } from 'react'
import { Link } from 'react-router'
import { ArrowRight, CalendarClock } from 'lucide-react'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { formatNextRun } from '@/features/schedules/lib/schedule-utils'
import type { MarketSchedule } from '@/features/schedules/types'
import { useIsAdmin } from '@/hooks/use-current-user'
import { ROUTE_STATIONS } from '@/lib/routes'
import { cn } from '@/lib/utils'
import { OpsCardHeader } from './OpsCardHeader'

/** The next scheduled slots across stations — what goes on air, when. */
export function UpNextCard({
    schedules,
}: {
    readonly schedules: readonly MarketSchedule[]
}) {
    // The schedule feed is server-filtered to the caller's scope: an empty
    // list means the CALLER's stations are paused, not the whole network.
    const isAdmin = useIsAdmin()
    const upcoming = useMemo(
        () =>
            schedules
                .filter((s) => s.enabled && s.nextRunAt)
                .slice()
                .sort(
                    (a, b) =>
                        Date.parse(a.nextRunAt ?? '') -
                        Date.parse(b.nextRunAt ?? '')
                )
                .slice(0, 3),
        [schedules]
    )
    return (
        <SpotlightCard className="h-full bg-card/70 p-4 text-left backdrop-blur">
            <div className="flex h-full flex-col gap-2.5">
                <OpsCardHeader icon={CalendarClock} title="On air next" />
                {upcoming.length === 0 ? (
                    <span className="py-1 text-xs text-muted-foreground">
                        {isAdmin
                            ? 'No upcoming runs; every station is paused.'
                            : 'No upcoming runs; your stations are all paused.'}
                    </span>
                ) : (
                    upcoming.map((s) => {
                        const next = formatNextRun(s.nextRunAt, s.enabled)
                        return (
                            <div
                                key={s.marketId}
                                className="flex items-center gap-2 py-0.5"
                            >
                                <span className="flex min-w-0 flex-1 flex-col leading-tight">
                                    <span className="truncate text-sm font-medium">
                                        {s.station ?? s.marketId}
                                    </span>
                                    <span className="truncate text-xs text-muted-foreground">
                                        {next.absolute}
                                    </span>
                                </span>
                                <span
                                    className={cn(
                                        'shrink-0 text-xs',
                                        next.urgent
                                            ? 'font-medium text-warning'
                                            : 'text-muted-foreground'
                                    )}
                                >
                                    {next.relative}
                                </span>
                            </div>
                        )
                    })
                )}
                <Link
                    to={ROUTE_STATIONS}
                    className="mt-auto inline-flex items-center gap-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                >
                    Manage stations
                    <ArrowRight className="size-3" aria-hidden />
                </Link>
            </div>
        </SpotlightCard>
    )
}
