import { Bell } from 'lucide-react'
import { useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { EmptyState } from '@/components/EmptyState'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Switch } from '@/components/ui/switch'
import { cn } from '@/lib/utils'
import { ROUTE_JOBS } from '@/lib/routes'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useNotifications } from '../hooks/use-notifications'
import { useAlarmPrefs } from '../hooks/use-alarm-prefs'
import { formatRelativeTime } from '@/lib/formatting'

interface NotificationsPanelProps {
    /** 'icon' renders the classic outline bell button; 'row' renders a
     *  full-width sidebar row (icon + label) for rail navigation. */
    readonly trigger?: 'icon' | 'row'
    /** Applied to the row label so the host controls its visibility. */
    readonly labelClassName?: string
    /** Dropdown placement relative to the trigger. */
    readonly side?: 'top' | 'right' | 'bottom' | 'left'
    readonly align?: 'start' | 'center' | 'end'
    /** Reported so a hover-expand rail can hold itself open. */
    readonly onOpenChange?: (open: boolean) => void
}

export function NotificationsPanel({
    trigger = 'icon',
    labelClassName,
    side = 'bottom',
    align = 'end',
    onOpenChange,
}: NotificationsPanelProps = {}) {
    const { notifications, unreadCount, markRead, markAllRead } =
        useNotifications()
    const { soundEnabled, desktopEnabled, toggleSound, toggleDesktop } =
        useAlarmPrefs()
    const navigate = useNavigate()

    const unreadBadge = unreadCount > 0 && (
        <Badge
            variant="destructive"
            className="absolute -right-1.5 -top-1.5 flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-xs leading-none"
        >
            {unreadCount > 9 ? '9+' : unreadCount}
        </Badge>
    )

    const handleToggleDesktop = () => {
        toggleDesktop()
        // When enabling desktop alerts, request permission if not yet decided
        if (
            !desktopEnabled &&
            'Notification' in window &&
            Notification.permission === 'default'
        ) {
            Notification.requestPermission()
        }
    }

    return (
        <DropdownMenu onOpenChange={onOpenChange}>
            <DropdownMenuTrigger asChild>
                {trigger === 'row' ? (
                    <button
                        type="button"
                        className="flex h-9 w-full items-center gap-2.5 rounded-md px-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground data-[state=open]:bg-muted/60 data-[state=open]:text-foreground"
                    >
                        <span className="relative shrink-0">
                            <Bell className="size-5" aria-hidden />
                            {unreadBadge}
                        </span>
                        <span className={labelClassName}>Notifications</span>
                    </button>
                ) : (
                    <Button variant="outline" size="icon" className="relative">
                        <Bell data-icon="inline-start" />
                        {unreadBadge}
                        <span className="sr-only">Notifications</span>
                    </Button>
                )}
            </DropdownMenuTrigger>
            <DropdownMenuContent side={side} align={align} className="w-80">
                <div className="flex items-center justify-between px-2 py-1">
                    <DropdownMenuLabel className="p-0 text-sm font-semibold">
                        Notifications
                    </DropdownMenuLabel>
                    {unreadCount > 0 && (
                        <Button
                            variant="ghost"
                            size="sm"
                            className="h-auto px-2 py-0.5 text-xs"
                            onClick={markAllRead}
                        >
                            Mark all read
                        </Button>
                    )}
                </div>

                {/* Alarm preferences toggles */}
                <div className="flex items-center gap-4 px-3 pb-2">
                    <label className="flex items-center gap-1.5 text-xs text-muted-foreground cursor-pointer">
                        <Switch
                            checked={soundEnabled}
                            onCheckedChange={toggleSound}
                            className="h-4 w-7 [&>span]:h-3 [&>span]:w-3 [&>span]:data-[state=checked]:translate-x-3"
                        />
                        Sound
                    </label>
                    <label className="flex items-center gap-1.5 text-xs text-muted-foreground cursor-pointer">
                        <Switch
                            checked={desktopEnabled}
                            onCheckedChange={handleToggleDesktop}
                            className="h-4 w-7 [&>span]:h-3 [&>span]:w-3 [&>span]:data-[state=checked]:translate-x-3"
                        />
                        Desktop alerts
                    </label>
                    {'Notification' in window &&
                        Notification.permission === 'denied' && (
                            <span className="text-3xs text-muted-foreground-subtle ml-auto">
                                Blocked by browser
                            </span>
                        )}
                </div>

                <DropdownMenuSeparator />
                {notifications.length === 0 ? (
                    <EmptyState variant="row">No notifications yet</EmptyState>
                ) : (
                    <DropdownMenuGroup>
                        <ScrollArea className="max-h-72">
                            {notifications.map((n) => (
                                <DropdownMenuItem
                                    key={n.id}
                                    className={cn(
                                        'items-start gap-3 px-3 py-2.5',
                                        !n.read && 'bg-muted/30'
                                    )}
                                    onSelect={() => {
                                        markRead(n.id)
                                        navigate(`${ROUTE_JOBS}/${n.jobId}`)
                                    }}
                                >
                                    <span className="mt-0.5 shrink-0">
                                        {n.status === 'WAITING_APPROVAL' ? (
                                            <Badge
                                                variant="warning"
                                                className="size-2 p-0"
                                                aria-hidden
                                            />
                                        ) : (
                                            <Badge
                                                variant="destructive"
                                                className="size-2 p-0"
                                                aria-hidden
                                            />
                                        )}
                                    </span>
                                    <span className="flex-1 leading-snug text-foreground">
                                        {n.message}
                                    </span>
                                    <span className="shrink-0 text-xs text-muted-foreground">
                                        {formatRelativeTime(
                                            new Date(n.timestamp)
                                        )}
                                    </span>
                                </DropdownMenuItem>
                            ))}
                        </ScrollArea>
                    </DropdownMenuGroup>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    )
}
