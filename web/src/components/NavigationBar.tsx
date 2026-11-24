import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { useTheme } from 'next-themes'
import { Radio } from 'lucide-react'
import { ROUTE_JOBS } from '@/lib/routes'
import {
    useCurrentUser,
    useIsAdmin,
    useIsOperator,
} from '@/hooks/use-current-user'
import { useOperatorScope } from '@/features/operators/api/operator-hooks'
import { useWaitingBadge } from '@/features/jobs/hooks/use-waiting-badge'
import { MobileNav } from './navigation/MobileNav'

/**
 * Mobile top bar. Desktop navigation lives in the AppSidebar rail.
 */
export function NavigationBar() {
    const [isOpen, setIsOpen] = useState(false)
    const { theme, setTheme } = useTheme()
    const location = useLocation()
    const { userName } = useCurrentUser()
    const isAdmin = useIsAdmin()
    const isOperator = useIsOperator()
    const waitingBadge = useWaitingBadge()
    const { data: scope } = useOperatorScope()
    const scopeLabel =
        scope?.scoped && scope.markets && scope.markets.length > 0
            ? scope.markets.join(' · ')
            : undefined

    return (
        <nav className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60 md:hidden">
            <div className="flex h-14 items-center justify-between gap-4 px-4">
                {/* Brand goes to the workstation — see AppSidebar's brand link. */}
                <Link to={ROUTE_JOBS} className="flex items-center gap-2">
                    <Radio className="size-5 text-primary" />
                    <span className="font-bold text-foreground">
                        ARN Bulletins
                    </span>
                </Link>

                <MobileNav
                    isOpen={isOpen}
                    onOpenChange={setIsOpen}
                    waitingBadge={waitingBadge}
                    isAdmin={isAdmin}
                    isOperator={isOperator}
                    scopeLabel={scopeLabel}
                    userName={userName}
                    locationPathname={location.pathname}
                    toggleTheme={() =>
                        setTheme(theme === 'dark' ? 'light' : 'dark')
                    }
                />
            </div>
        </nav>
    )
}
