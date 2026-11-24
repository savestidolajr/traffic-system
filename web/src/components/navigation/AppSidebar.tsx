import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { useTheme } from 'next-themes'
import {
    Radio,
    Search,
    Sun,
    Moon,
    User,
    FlaskConical,
    Pin,
    PinOff,
} from 'lucide-react'
import { isRouteActive, ROUTE_JOBS, visibleRoutes } from '@/lib/routes'
import { modKey } from '@/lib/platform'
import { isMockMode } from '@/lib/api/client'
import { OPEN_COMMAND_PALETTE_EVENT } from '@/components/CommandPalette'
import { Badge } from '@/components/ui/badge'
import {
    Tooltip,
    TooltipTrigger,
    TooltipContent,
} from '@/components/ui/tooltip'
import {
    useCurrentUser,
    useIsAdmin,
    useIsOperator,
} from '@/hooks/use-current-user'
import { useOperatorScope } from '@/features/operators/api/operator-hooks'
import { useWaitingBadge } from '@/features/jobs/hooks/use-waiting-badge'
import { AwaitingReviewStatus } from './AwaitingReviewStatus'
import { NotificationsPanel } from '@/features/notifications'
import { UserMenu } from './UserMenu'
import { cn } from '@/lib/utils'

const SIDEBAR_COLLAPSED_W = 'w-[60px]'
const SIDEBAR_EXPANDED_W = 'w-[220px]'
const SIDEBAR_PINNED_KEY = 'arn-sidebar-pinned'
const WIDE_SIDEBAR_QUERY = '(min-width: 1280px)'

/**
 * Aceternity-style hover-expand sidebar: a 60px icon rail that grows to
 * 220px, labels sliding in. Expansion is a controlled state —
 * `hovered || keyboard focus || a menu is open` — so opening the
 * notifications or user dropdown (which portals away pointer and focus)
 * holds the rail open instead of collapsing it under the menu. Pointer
 * clicks deliberately don't pin it (focus counts only when
 * `:focus-visible`). Transient expansion overlays content without reflow;
 * explicit pinning reserves the wider rail until the operator unpins it.
 * Desktop only; mobile keeps the slim top bar.
 */
export function AppSidebar() {
    const location = useLocation()
    const { resolvedTheme, setTheme } = useTheme()
    const { userName } = useCurrentUser()
    const { data: scope } = useOperatorScope()
    const scopeLabel =
        scope?.scoped && scope.markets && scope.markets.length > 0
            ? scope.markets.join(' · ')
            : undefined
    const isAdmin = useIsAdmin()
    const isOperator = useIsOperator()
    const waitingBadge = useWaitingBadge()

    const [hovered, setHovered] = useState(false)
    const [focused, setFocused] = useState(false)
    const [pinned, setPinned] = useState(
        () =>
            typeof window !== 'undefined' &&
            window.localStorage.getItem(SIDEBAR_PINNED_KEY) === 'true'
    )
    const [isWide, setIsWide] = useState(false)
    // Counter, not boolean: switching directly between two menus emits
    // open(B) before close(A)
    const [openMenus, setOpenMenus] = useState(0)
    const interactionOpen = hovered || focused || openMenus > 0
    const open = interactionOpen || (pinned && isWide)

    useEffect(() => {
        if (typeof window === 'undefined' || !window.matchMedia) return
        const media = window.matchMedia(WIDE_SIDEBAR_QUERY)
        const update = () => setIsWide(media.matches)
        update()
        media.addEventListener('change', update)
        return () => media.removeEventListener('change', update)
    }, [])

    useEffect(() => {
        window.localStorage.setItem(SIDEBAR_PINNED_KEY, String(pinned))
    }, [pinned])

    const handleMenuOpenChange = (isOpen: boolean) =>
        setOpenMenus((n) => Math.max(0, n + (isOpen ? 1 : -1)))

    const [hoveredRoute, setHoveredRoute] = useState<string | null>(null)
    // Hover-intent: a deliberate pause expands the rail; a quick pass-through
    // instead surfaces a per-icon tooltip, so a glance names the icon without
    // the whole rail sliding open.
    const expandTimer = useRef<number | null>(null)
    const beginExpand = () => {
        if (expandTimer.current) window.clearTimeout(expandTimer.current)
        expandTimer.current = window.setTimeout(() => setHovered(true), 350)
    }
    const cancelExpand = () => {
        if (expandTimer.current) {
            window.clearTimeout(expandTimer.current)
            expandTimer.current = null
        }
        setHovered(false)
    }
    useEffect(
        () => () => {
            if (expandTimer.current) window.clearTimeout(expandTimer.current)
        },
        []
    )

    const routes = visibleRoutes({ isAdmin, isOperator })

    /** Label visibility follows the controlled state, but labels never
     *  occupy layout when hidden — that's what squashed the icons. */
    const labelClass = cn(
        'min-w-0 flex-1 truncate text-left transition-opacity duration-200 motion-reduce:transition-none',
        open ? 'opacity-100' : 'sr-only opacity-0'
    )

    const rowClass =
        'flex h-9 w-full items-center gap-2.5 rounded-md px-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground'

    return (
        // Layout placeholder: content is laid out against the collapsed
        // 60px rail; the expanding panel floats above it.
        <div
            className={cn(
                'relative hidden shrink-0 md:block',
                pinned && isWide ? SIDEBAR_EXPANDED_W : SIDEBAR_COLLAPSED_W
            )}
        >
            <nav
                aria-label="Primary"
                onMouseEnter={beginExpand}
                onMouseLeave={cancelExpand}
                onFocus={(e) => {
                    // Keyboard focus only: a pointer click must not pin the
                    // rail open after the mouse leaves.
                    if (e.target.matches(':focus-visible')) setFocused(true)
                }}
                onBlur={(e) => {
                    if (!e.currentTarget.contains(e.relatedTarget as Node)) {
                        setFocused(false)
                    }
                }}
                className={cn(
                    'absolute inset-y-0 left-0 z-30 flex h-dvh flex-col gap-1 overflow-hidden border-r px-2.5 py-4',
                    '[background:linear-gradient(hsl(var(--muted)/0.2),hsl(var(--muted)/0.2)),hsl(var(--background))]',
                    'transition-[width,box-shadow] duration-200 ease-out motion-reduce:transition-none',
                    open
                        ? `${SIDEBAR_EXPANDED_W} shadow-xl`
                        : SIDEBAR_COLLAPSED_W
                )}
            >
                {/* Brand — goes to the workstation, not the landing page:
                    a working operator clicking the mark wants their board,
                    `/` stays the front door for fresh arrivals. */}
                <div className="mb-4 flex h-9 items-center gap-1">
                    <Link
                        to={ROUTE_JOBS}
                        className="flex min-w-0 flex-1 items-center gap-2.5 px-1.5"
                        aria-label="ARN Bulletin Generator"
                    >
                        <Radio className="size-5 shrink-0 text-primary" />
                        <span className={cn(labelClass, 'text-sm font-bold')}>
                            ARN Bulletins
                        </span>
                    </Link>
                    {isWide && open && (
                        <button
                            type="button"
                            onClick={() => setPinned((value) => !value)}
                            aria-label={
                                pinned
                                    ? 'Unpin navigation labels'
                                    : 'Pin navigation labels'
                            }
                            aria-pressed={pinned}
                            className="flex size-8 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring pointer-coarse:size-11"
                        >
                            {pinned ? (
                                <PinOff className="size-4" aria-hidden />
                            ) : (
                                <Pin className="size-4" aria-hidden />
                            )}
                        </button>
                    )}
                </div>

                {/* Mock-mode pill: same collapsed-label pattern as nav rows */}
                {isMockMode && (
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <span className="mb-2 flex px-0.5">
                                <Badge
                                    variant="warning"
                                    className="flex h-6 max-w-full items-center gap-1.5 px-2"
                                >
                                    <FlaskConical
                                        className="size-3.5 shrink-0"
                                        aria-hidden
                                    />
                                    <span className={labelClass}>
                                        Demo data
                                    </span>
                                </Badge>
                            </span>
                        </TooltipTrigger>
                        <TooltipContent side="right">
                            Showing simulated data. Not production
                        </TooltipContent>
                    </Tooltip>
                )}

                {/* Command palette: the modKey+K shortcut exists — advertise it */}
                <button
                    type="button"
                    onClick={() =>
                        document.dispatchEvent(
                            new CustomEvent(OPEN_COMMAND_PALETTE_EVENT)
                        )
                    }
                    className={cn(rowClass, 'mb-1')}
                    aria-label="Search and commands"
                    aria-keyshortcuts="Meta+K Control+K"
                >
                    <Search className="size-[18px] shrink-0" aria-hidden />
                    <span className={labelClass}>Search…</span>
                    {open && (
                        <kbd className="rounded border border-border/60 bg-muted/60 px-1 font-mono text-3xs text-muted-foreground">
                            {modKey}K
                        </kbd>
                    )}
                </button>

                <AwaitingReviewStatus
                    state={waitingBadge}
                    showText={open}
                    className="mb-1 w-full"
                    labelClassName={labelClass}
                />

                {/* Nav links */}
                <div className="flex flex-col gap-1">
                    {routes.map((item) => {
                        const Icon = item.icon
                        const active = isRouteActive(
                            item.path,
                            location.pathname
                        )
                        return (
                            <Tooltip
                                key={item.path}
                                open={!open && hoveredRoute === item.path}
                            >
                                <TooltipTrigger asChild>
                                    <Link
                                        to={item.path}
                                        onMouseEnter={() =>
                                            setHoveredRoute(item.path)
                                        }
                                        onMouseLeave={() =>
                                            setHoveredRoute(null)
                                        }
                                        aria-current={
                                            active ? 'page' : undefined
                                        }
                                        className={cn(
                                            rowClass,
                                            active &&
                                                'bg-primary/10 text-primary-on-tint hover:bg-primary/10 hover:text-primary-on-tint'
                                        )}
                                    >
                                        <Icon
                                            className="size-5 shrink-0"
                                            aria-hidden
                                        />
                                        <span className={labelClass}>
                                            {item.label}
                                        </span>
                                    </Link>
                                </TooltipTrigger>
                                <TooltipContent side="right">
                                    {item.label}
                                </TooltipContent>
                            </Tooltip>
                        )
                    })}
                </div>

                <div className="flex-1" />

                {/* Utilities: uniform rows, same vocabulary as the nav links */}
                <div className="flex flex-col gap-1">
                    <NotificationsPanel
                        trigger="row"
                        side="right"
                        align="end"
                        labelClassName={labelClass}
                        onOpenChange={handleMenuOpenChange}
                    />
                    <button
                        type="button"
                        className={rowClass}
                        onClick={() =>
                            setTheme(
                                resolvedTheme === 'dark' ? 'light' : 'dark'
                            )
                        }
                    >
                        <span className="relative shrink-0">
                            <Sun
                                className="size-5 rotate-0 scale-100 transition-all dark:-rotate-90 dark:scale-0"
                                aria-hidden
                            />
                            <Moon
                                className="absolute inset-0 size-5 rotate-90 scale-0 transition-all dark:rotate-0 dark:scale-100"
                                aria-hidden
                            />
                        </span>
                        <span className={labelClass}>
                            {resolvedTheme === 'dark'
                                ? 'Light mode'
                                : 'Dark mode'}
                        </span>
                    </button>
                    <UserMenu
                        userName={userName}
                        side="right"
                        align="end"
                        scopeLabel={scopeLabel}
                        onOpenChange={handleMenuOpenChange}
                    >
                        <button type="button" className={rowClass}>
                            <User className="size-5 shrink-0" aria-hidden />
                            <span className={labelClass}>
                                {userName ?? 'Account'}
                            </span>
                        </button>
                    </UserMenu>
                </div>
            </nav>
        </div>
    )
}
