import { StationProfileCard } from './StationProfileCard'
import { CARD_GRID } from '@/lib/layout'
import { useScopedSchedules } from '../lib/use-scoped-schedules'
import type { MarketSchedule } from '@/features/schedules/types'

interface StationProfileCardsProps {
    readonly schedules: readonly MarketSchedule[]
    /** Admin-only context: operator display names keyed by lowercased market id. */
    readonly operatorsByMarket?: ReadonlyMap<string, readonly string[]>
    readonly onTrigger: (schedule: MarketSchedule) => void
    readonly onView: (schedule: MarketSchedule) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onDuplicate: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
    readonly onTogglePlayout: (schedule: MarketSchedule) => void
    readonly onDelete: (schedule: MarketSchedule) => void
}

export function StationProfileCards({
    schedules,
    operatorsByMarket,
    onTrigger,
    onView,
    onEdit,
    onDuplicate,
    onToggle,
    onTogglePlayout,
    onDelete,
}: StationProfileCardsProps) {
    const { inScope, outOfScope } = useScopedSchedules(schedules)

    return (
        <div className="flex flex-col gap-6">
            <div className={CARD_GRID}>
                {inScope.map((schedule) => (
                    <StationProfileCard
                        key={schedule.marketId}
                        schedule={schedule}
                        operatorNames={operatorsByMarket?.get(
                            schedule.marketId.toLowerCase()
                        )}
                        onTrigger={onTrigger}
                        onView={onView}
                        onEdit={onEdit}
                        onDuplicate={onDuplicate}
                        onToggle={onToggle}
                        onTogglePlayout={onTogglePlayout}
                        onDelete={onDelete}
                    />
                ))}
            </div>

            {outOfScope.length > 0 && (
                <div>
                    <p className="mb-3 text-xs font-medium text-muted-foreground-subtle uppercase tracking-wide">
                        Outside your markets
                    </p>
                    <div className={CARD_GRID}>
                        {outOfScope.map((schedule) => (
                            <StationProfileCard
                                key={schedule.marketId}
                                schedule={schedule}
                                operatorNames={operatorsByMarket?.get(
                                    schedule.marketId.toLowerCase()
                                )}
                                onTrigger={onTrigger}
                                onView={onView}
                                onEdit={onEdit}
                                onDuplicate={onDuplicate}
                                onToggle={onToggle}
                                onTogglePlayout={onTogglePlayout}
                                onDelete={onDelete}
                                outOfScope
                            />
                        ))}
                    </div>
                </div>
            )}
        </div>
    )
}
