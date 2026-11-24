import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import {
    Sheet,
    SheetContent,
    SheetHeader,
    SheetTitle,
    SheetTrigger,
} from '@/components/ui/sheet'
import {
    Tooltip,
    TooltipTrigger,
    TooltipContent,
} from '@/components/ui/tooltip'
import { Menu, Sun, Moon, User, FlaskConical } from 'lucide-react'
import { isRouteActive, visibleRoutes } from '@/lib/routes'
import { isMockMode } from '@/lib/api/client'
import { NotificationsPanel } from '@/features/notifications'
import { UserMenu } from './UserMenu'
import type { WaitingBadgeState } from '@/features/jobs/hooks/use-waiting-badge'
import { AwaitingReviewStatus } from './AwaitingReviewStatus'

interface MobileNavProps {
    readonly isOpen: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly waitingBadge: WaitingBadgeState
    readonly isAdmin: boolean
    readonly isOperator: boolean
    readonly userName: string | null | undefined
    readonly scopeLabel?: string
    readonly locationPathname: string
    readonly toggleTheme: () => void
}

export function MobileNav({
    isOpen,
    onOpenChange,
    waitingBadge,
    isAdmin,
    isOperator,
    userName,
    scopeLabel,
    locationPathname,
    toggleTheme,
}: MobileNavProps) {
    const routes = visibleRoutes({ isAdmin, isOperator })
    const { waitingCount, urgencyBand, badgeAriaLabel } = waitingBadge
    const badgeHot = urgencyBand === 'critical' || urgencyBand === 'overdue'
    return (
        <div className="flex flex-1 items-center justify-end md:hidden">
            {isMockMode && (
                <Tooltip>
                    <TooltipTrigger asChild>
                        <span className="mr-1">
                            <Badge
                                variant="warning"
                                className="flex h-6 items-center gap-1 px-2"
                            >
                                <FlaskConical
                                    className="size-3.5 shrink-0"
                                    aria-hidden
                                />
                                Demo data
                            </Badge>
                        </span>
                    </TooltipTrigger>
                    <TooltipContent>
                        Showing simulated data. Not production
                    </TooltipContent>
                </Tooltip>
            )}
            <NotificationsPanel />
            <Sheet open={isOpen} onOpenChange={onOpenChange}>
                <SheetTrigger asChild>
                    <Button
                        variant="ghost"
                        size="icon"
                        className="relative"
                        aria-label={
                            badgeAriaLabel
                                ? `Toggle menu; ${badgeAriaLabel}`
                                : 'Toggle menu'
                        }
                    >
                        <Menu data-icon="inline-start" />
                        {waitingCount > 0 && (
                            <span
                                aria-hidden
                                className={cn(
                                    'absolute right-0 top-0 flex h-4 min-w-4 items-center justify-center rounded-full px-1 font-mono text-3xs font-semibold leading-none',
                                    badgeHot
                                        ? 'bg-destructive text-destructive-foreground'
                                        : 'bg-warning text-warning-foreground'
                                )}
                            >
                                {waitingCount}
                            </span>
                        )}
                    </Button>
                </SheetTrigger>
                <SheetContent side="right" className="w-75 sm:w-100">
                    <SheetHeader className="sr-only">
                        <SheetTitle>Navigation</SheetTitle>
                    </SheetHeader>
                    <div className="flex flex-col gap-4 mt-4">
                        <AwaitingReviewStatus
                            state={waitingBadge}
                            showText
                            onNavigate={() => onOpenChange(false)}
                            className="h-11 w-full px-3"
                        />
                        {/* Navigation Links */}
                        <div className="flex flex-col gap-2">
                            {routes.map((item) => (
                                <Link
                                    key={item.label}
                                    to={item.path}
                                    onClick={() => onOpenChange(false)}
                                    className={cn(
                                        'block w-full py-3 px-4 rounded-md text-sm font-medium transition-colors hover:bg-muted active:bg-muted/70',
                                        isRouteActive(
                                            item.path,
                                            locationPathname
                                        )
                                            ? 'text-primary bg-muted/50'
                                            : 'text-muted-foreground'
                                    )}
                                >
                                    {item.label}
                                </Link>
                            ))}
                        </div>

                        {/* Theme Toggle and User Menu in mobile */}
                        <div className="flex items-center gap-2 pt-4 border-t">
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={toggleTheme}
                                className="flex-1"
                            >
                                <Sun
                                    data-icon="inline-start"
                                    className="rotate-0 scale-100 transition-all dark:-rotate-90 dark:scale-0"
                                />
                                <Moon
                                    data-icon="inline-start"
                                    className="rotate-90 scale-0 transition-all dark:rotate-0 dark:scale-100"
                                />
                                Toggle theme
                            </Button>

                            <UserMenu
                                userName={userName}
                                scopeLabel={scopeLabel}
                            >
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    className="flex-1"
                                >
                                    <User data-icon="inline-start" />
                                    Account
                                </Button>
                            </UserMenu>
                        </div>
                    </div>
                </SheetContent>
            </Sheet>
        </div>
    )
}
