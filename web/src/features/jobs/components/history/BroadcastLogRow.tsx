import { useState, useCallback } from 'react'
import { ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { TableCell, TableRow } from '@/components/ui/table'
import { STATUS_CONFIG } from '../../config/status-config'
import {
    formatClockTime,
    formatDuration,
    formatMarketName,
} from '@/lib/formatting'
import type { JobSummary } from '../../types'
import { BroadcastLogRowDetail } from './BroadcastLogRowDetail'

interface BroadcastLogRowProps {
    readonly job: JobSummary
    readonly mobile?: boolean
}

/** One settled bulletin: a fixed-height summary row plus an on-demand detail row beneath it. */
export function BroadcastLogRow({ job, mobile }: BroadcastLogRowProps) {
    const [expanded, setExpanded] = useState(false)
    const toggle = useCallback(() => setExpanded((v) => !v), [])

    const config = STATUS_CONFIG[job.status]
    const StatusIcon = config.icon

    const measSec = job.measuredDurationSec ?? 0
    const deltaSecVal = job.deltaSec ?? 0
    const hasDuration = measSec > 0

    if (mobile) {
        return (
            <>
                <div
                    className="flex cursor-pointer flex-col gap-1 p-3 hover:bg-muted/30 active:bg-muted/40"
                    onClick={toggle}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault()
                            toggle()
                        }
                    }}
                    role="button"
                    tabIndex={0}
                    aria-expanded={expanded}
                >
                    {/* Row 1: chevron, time, station (truncates), outcome badge */}
                    <div className="flex items-center gap-2">
                        <ChevronRight
                            className={cn(
                                'size-4 shrink-0 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none',
                                expanded && 'rotate-90'
                            )}
                            aria-hidden
                        />
                        <span className="shrink-0 tabular-nums text-xs text-muted-foreground">
                            {formatClockTime(job.lastUpdatedAt)}
                        </span>
                        <span className="min-w-0 truncate text-sm font-semibold">
                            {job.station}
                        </span>
                        <span
                            className={cn(
                                'ml-auto inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-semibold',
                                config.variant === 'approved' &&
                                    'border-success/20 bg-success/10 text-success',
                                config.variant === 'destructive' &&
                                    'border-destructive/20 bg-destructive/10 text-destructive-on-tint',
                                config.variant === 'fallback' &&
                                    'border-warning/20 bg-warning/10 text-warning'
                            )}
                        >
                            <StatusIcon className="size-3" aria-hidden="true" />
                            {config.label}
                        </span>
                    </div>
                    {/* Row 2: market, duration / target, delta */}
                    <div className="flex items-center gap-2 pl-6 text-xs text-muted-foreground">
                        <span className="truncate">
                            {formatMarketName(job.market)}
                        </span>
                        <span className="ml-auto shrink-0 tabular-nums">
                            {hasDuration
                                ? `${formatDuration(Math.round(measSec))} / ${formatDuration(job.targetDurationSec)}`
                                : '\u2014'}
                        </span>
                        {hasDuration && Math.abs(deltaSecVal) > 2 ? (
                            <span
                                className={cn(
                                    'shrink-0 tabular-nums',
                                    deltaSecVal > 0
                                        ? 'text-destructive'
                                        : 'text-warning'
                                )}
                            >
                                {deltaSecVal > 0 ? '+' : ''}
                                {deltaSecVal}s
                            </span>
                        ) : (
                            <span className="shrink-0 tabular-nums">
                                {'\u2014'}
                            </span>
                        )}
                    </div>
                </div>
                {expanded && (
                    <div className="border-t bg-muted/20 p-3">
                        <BroadcastLogRowDetail jobId={job.jobId} />
                    </div>
                )}
            </>
        )
    }

    // Desktop: table row
    return (
        <>
            <TableRow
                className="cursor-pointer"
                onClick={toggle}
                onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        toggle()
                    }
                }}
                role="button"
                tabIndex={0}
                aria-expanded={expanded}
            >
                <TableCell>
                    <ChevronRight
                        className={cn(
                            'size-4 shrink-0 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none',
                            expanded && 'rotate-90'
                        )}
                        aria-hidden
                    />
                </TableCell>
                <TableCell className="whitespace-nowrap tabular-nums text-muted-foreground">
                    {formatClockTime(job.lastUpdatedAt)}
                </TableCell>
                <TableCell className="font-semibold">{job.station}</TableCell>
                <TableCell className="text-muted-foreground">
                    {formatMarketName(job.market)}
                </TableCell>
                <TableCell>
                    <span
                        className={cn(
                            'inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-semibold',
                            config.variant === 'approved' &&
                                'border-success/20 bg-success/10 text-success',
                            config.variant === 'destructive' &&
                                'border-destructive/20 bg-destructive/10 text-destructive-on-tint',
                            config.variant === 'fallback' &&
                                'border-warning/20 bg-warning/10 text-warning'
                        )}
                    >
                        <StatusIcon className="size-3.5" aria-hidden="true" />
                        {config.label}
                    </span>
                </TableCell>
                <TableCell className="whitespace-nowrap text-right tabular-nums text-muted-foreground">
                    {hasDuration
                        ? `${formatDuration(Math.round(measSec))} / ${formatDuration(job.targetDurationSec)}`
                        : '\u2014'}
                </TableCell>
                <TableCell className="whitespace-nowrap text-right tabular-nums">
                    {hasDuration && Math.abs(deltaSecVal) > 2 ? (
                        <span
                            className={cn(
                                deltaSecVal > 0
                                    ? 'text-destructive'
                                    : 'text-warning'
                            )}
                        >
                            {deltaSecVal > 0 ? '+' : ''}
                            {deltaSecVal}s
                        </span>
                    ) : (
                        <span className="text-muted-foreground">
                            {'\u2014'}
                        </span>
                    )}
                </TableCell>
            </TableRow>
            {expanded && (
                <TableRow className="hover:bg-transparent">
                    <TableCell colSpan={7} className="bg-muted/20 py-3">
                        <BroadcastLogRowDetail jobId={job.jobId} />
                    </TableCell>
                </TableRow>
            )}
        </>
    )
}
