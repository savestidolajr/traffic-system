import { Link, useLocation } from 'react-router'
import { Radio } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Spotlight } from '@/components/ui/aceternity/spotlight'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { useDocumentTitle } from '@/hooks/use-document-title'
import {
    ROUTE_HISTORY,
    ROUTE_HOME,
    ROUTE_JOBS,
    ROUTE_STATIONS,
} from '@/lib/routes'

export function NotFoundPage() {
    useDocumentTitle('Page Not Found')
    const location = useLocation()
    return (
        <div className="relative flex min-h-full flex-col items-center justify-center gap-4 overflow-hidden px-4 text-center animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
            <Spotlight />
            <div className="relative flex size-14 items-center justify-center rounded-2xl border border-primary/25 bg-primary/10 text-primary-on-tint shadow-glow">
                <ICON.broadcast className="size-7" aria-hidden />
            </div>
            <h1 className="relative text-7xl font-bold tracking-tight text-foreground">
                404
            </h1>
            <div className="relative space-y-1">
                <p className="max-w-md text-muted-foreground">
                    This page doesn't exist or may have moved.
                </p>
                <p className="font-mono text-xs text-muted-foreground-subtle">
                    {location.pathname}
                </p>
            </div>
            <GlowBorder className="relative mt-2 rounded-md">
                <Link
                    to={ROUTE_JOBS}
                    className="inline-flex items-center gap-1.5 rounded-[calc(0.375rem-1px)] bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
                >
                    Back to the queue
                    <ICON.forward className="size-4" aria-hidden />
                </Link>
            </GlowBorder>
            <nav
                className="relative mt-2 flex flex-col items-center gap-2 text-sm"
                aria-label="Alternative navigation"
            >
                <span className="text-xs text-muted-foreground">
                    or try one of these
                </span>
                <div className="flex items-center gap-3">
                    <Link
                        to={ROUTE_HOME}
                        className="inline-flex items-center gap-1.5 rounded-md border border-border/50 bg-background/60 px-3 py-2 text-sm font-medium text-foreground backdrop-blur transition-colors hover:bg-accent hover:text-accent-foreground"
                    >
                        <Radio className="size-4" aria-hidden />
                        On air
                    </Link>
                    <Link
                        to={ROUTE_HISTORY}
                        className="inline-flex items-center gap-1.5 rounded-md border border-border/50 bg-background/60 px-3 py-2 text-sm font-medium text-foreground backdrop-blur transition-colors hover:bg-accent hover:text-accent-foreground"
                    >
                        <ICON.history className="size-4" aria-hidden />
                        History
                    </Link>
                    <Link
                        to={ROUTE_STATIONS}
                        className="inline-flex items-center gap-1.5 rounded-md border border-border/50 bg-background/60 px-3 py-2 text-sm font-medium text-foreground backdrop-blur transition-colors hover:bg-accent hover:text-accent-foreground"
                    >
                        <ICON.broadcast className="size-4" aria-hidden />
                        Stations
                    </Link>
                </div>
            </nav>
        </div>
    )
}
