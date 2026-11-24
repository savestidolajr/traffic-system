import type { ReactNode } from 'react'
import { LogOut, User } from 'lucide-react'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Button } from '@/components/ui/button'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'

interface UserMenuProps {
    userName: string | null | undefined
    /** Market scope summary — e.g. "all markets" or "SYDNEY_METRO · BRISBANE". */
    scopeLabel?: string
    /** Optional custom trigger. Defaults to outline icon button. */
    children?: ReactNode
    /** Dropdown placement relative to the trigger. */
    side?: 'top' | 'right' | 'bottom' | 'left'
    align?: 'start' | 'center' | 'end'
    /** Reported so a hover-expand rail can hold itself open. */
    onOpenChange?: (open: boolean) => void
}

export function UserMenu({
    userName,
    scopeLabel,
    children,
    side = 'bottom',
    align = 'end',
    onOpenChange,
}: UserMenuProps) {
    return (
        <DropdownMenu onOpenChange={onOpenChange}>
            <DropdownMenuTrigger asChild>
                {children ?? (
                    <Button variant="outline" size="icon">
                        <User data-icon="inline-start" />
                        <span className="sr-only">User menu</span>
                    </Button>
                )}
            </DropdownMenuTrigger>
            <DropdownMenuContent side={side} align={align}>
                <DropdownMenuLabel>
                    {userName ?? 'My Account'}
                </DropdownMenuLabel>
                {scopeLabel && (
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <p className="truncate px-2 pb-1 text-2xs text-muted-foreground-subtle">
                                Scope: {scopeLabel}
                            </p>
                        </TooltipTrigger>
                        <TooltipContent side="right">
                            <p>{scopeLabel}</p>
                        </TooltipContent>
                    </Tooltip>
                )}
                <DropdownMenuSeparator />
                <DropdownMenuGroup>
                    <DropdownMenuItem asChild>
                        <a
                            href="/.auth/logout?post_logout_redirect_uri=/"
                            className="flex items-center gap-2 cursor-pointer"
                        >
                            <LogOut />
                            Sign out
                        </a>
                    </DropdownMenuItem>
                </DropdownMenuGroup>
            </DropdownMenuContent>
        </DropdownMenu>
    )
}
