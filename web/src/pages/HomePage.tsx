import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import { Radio } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { useJobs } from '@/features/jobs/api/job-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useSecondTick } from '@/hooks/use-second-tick'
import { ErrorAlert } from '@/components/ErrorAlert'
import { AuroraHero } from '@/components/ui/aceternity/aurora-hero'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { OutcomesCard } from '@/features/home/components/OutcomesCard'
import { UpNextCard } from '@/features/home/components/UpNextCard'
import { WaitingCard } from '@/features/home/components/WaitingCard'
import { useHomeOps } from '@/features/home/lib/use-home-ops'
import { useIsAdmin } from '@/hooks/use-current-user'
import { ROUTE_JOBS, ROUTE_STATIONS } from '@/lib/routes'
import { cn } from '@/lib/utils'

/** Staggered entrance for hero children; static under reduced motion.
 *  Delay goes through inline style — Tailwind's JIT can't see runtime
 *  class names like `[animation-delay:300ms]`. */
const ENTER_CLASS =
    'motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-bottom-3 motion-safe:fill-mode-backwards motion-safe:duration-700'

function enterStyle(delayMs: number): CSSProperties | undefined {
    return delayMs > 0 ? { animationDelay: `${delayMs}ms` } : undefined
}

/**
 * The product's front door: a SaaS-style hero on the Aceternity aurora —
 * but the lower half is a live ops band, not marketing. What's waiting on
 * a human, what airs next, and what just happened, each one click from
 * acting on it. The studio itself lives at /jobs.
 */
export function HomePage() {
    useDocumentTitle('ARN Bulletins')
    useSecondTick() // live countdowns in the waiting card
    // The schedules/jobs feeds are server-filtered to the caller's scope, so
    // every count below is scoped too. Admins read the whole network; an
    // operator must never read a quiet 2-market morning as a network outage.
    const isAdmin = useIsAdmin()

    const {
        data: jobsResult,
        isLoading: jobsLoading,
        isError: jobsError,
        error: jobsErrorDetail,
        refetch: jobsRefetch,
    } = useJobs({})
    const {
        data: schedulesResult,
        isLoading: schedulesLoading,
        isError: schedulesError,
        error: schedulesErrorDetail,
        refetch: schedulesRefetch,
    } = useSchedules()

    const jobs = jobsResult?.jobs
    const schedules = schedulesResult?.schedules ?? []
    const { waiting, settled, stationsOnAir, generating, publishedToday } =
        useHomeOps(jobs, schedules)
    // A query with no data and no fetch in flight is failed-or-stalled, not
    // merely empty (bug-bash #25) — the band must never silently vanish.
    const jobsUnavailable = jobsError || (!jobsLoading && !jobsResult)
    const schedulesUnavailable =
        schedulesError || (!schedulesLoading && !schedulesResult)
    const opsReady = !jobsUnavailable && !schedulesUnavailable

    return (
        <AuroraHero
            className="h-full rounded-none border-0 bg-transparent"
            contentClassName="flex h-full flex-col items-center justify-center gap-8 overflow-y-auto px-6 py-10"
        >
            <div className="flex max-w-3xl flex-col items-center gap-5 text-center">
                {/* Brand mark */}
                <div
                    className={cn(
                        'flex size-12 items-center justify-center rounded-2xl border border-primary/30 bg-primary/10 text-primary-on-tint shadow-glow',
                        ENTER_CLASS
                    )}
                >
                    <Radio className="size-6" aria-hidden />
                </div>

                <div className="rounded-xl bg-background/30 p-5 backdrop-blur-sm">
                    <p
                        className={cn(
                            'text-xs font-medium uppercase tracking-[0.2em] text-foreground/80',
                            ENTER_CLASS
                        )}
                        style={enterStyle(100)}
                    >
                        ARN Bulletins · AI bulletin desk
                    </p>

                    <p
                        className={cn(
                            'mt-3 max-w-xl text-balance text-base text-foreground/80',
                            ENTER_CLASS
                        )}
                        style={enterStyle(300)}
                    >
                        ARN Bulletins reads the live feeds, writes and voices
                        the script, and holds it for your approval in about a
                        minute.
                    </p>
                </div>

                <h1
                    className={cn(
                        'text-balance text-4xl font-display font-bold leading-tight tracking-tight sm:text-5xl',
                        ENTER_CLASS
                    )}
                    style={enterStyle(200)}
                >
                    Live data in.{' '}
                    <span className="text-primary">On-air bulletins out.</span>
                </h1>

                {/* CTAs */}
                <div
                    className={cn(
                        'flex flex-wrap items-center justify-center gap-3',
                        ENTER_CLASS
                    )}
                    style={enterStyle(400)}
                >
                    <GlowBorder className="rounded-md" inset={1}>
                        <Link
                            to={ROUTE_JOBS}
                            className="inline-flex items-center gap-1.5 rounded-[calc(0.375rem-1px)] bg-primary px-5 py-2.5 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
                        >
                            Open the studio
                            <ICON.forward className="size-4" aria-hidden />
                        </Link>
                    </GlowBorder>
                    <Link
                        to={ROUTE_STATIONS}
                        className="inline-flex items-center gap-1.5 rounded-md border border-primary/20 bg-background/85 px-5 py-2.5 text-sm font-medium text-foreground backdrop-blur-sm transition-colors hover:bg-muted/80"
                    >
                        <ICON.settings className="size-4" aria-hidden />
                        Manage stations
                    </Link>
                </div>
            </div>

            {opsReady ? (
                <>
                    {/* The live ops band: real state, one click from acting on it */}
                    <div
                        className={cn(
                            'grid w-full max-w-5xl gap-4 sm:grid-cols-2 lg:grid-cols-3',
                            ENTER_CLASS
                        )}
                        style={enterStyle(500)}
                    >
                        <WaitingCard waiting={waiting} />
                        <UpNextCard schedules={schedules} />
                        <OutcomesCard settled={settled} />
                    </div>

                    {/* The pulse, in one quiet line */}
                    <p
                        className={cn(
                            'text-xs text-foreground/60',
                            ENTER_CLASS
                        )}
                        style={enterStyle(600)}
                    >
                        <span className="font-mono font-semibold tabular-nums text-foreground">
                            {stationsOnAir}
                        </span>{' '}
                        {isAdmin
                            ? 'stations on air'
                            : 'of your stations on air'}{' '}
                        ·{' '}
                        <span className="font-mono font-semibold tabular-nums text-foreground">
                            {generating}
                        </span>{' '}
                        {isAdmin
                            ? 'generating now'
                            : 'of your bulletins generating now'}{' '}
                        ·{' '}
                        <span className="font-mono font-semibold tabular-nums text-foreground">
                            {publishedToday}
                        </span>{' '}
                        {isAdmin
                            ? 'published today'
                            : 'of your bulletins published today'}
                    </p>
                </>
            ) : jobsUnavailable || schedulesUnavailable ? (
                /* The band is the whole point of this page — when the feed
                   dies it must say so and offer a retry, never vanish. */
                <ErrorAlert
                    error={jobsErrorDetail ?? schedulesErrorDetail}
                    fallbackMessage="Failed to load live ops data"
                    onRetry={() => {
                        void jobsRefetch?.()
                        void schedulesRefetch?.()
                    }}
                    className="max-w-md"
                />
            ) : null}
        </AuroraHero>
    )
}
