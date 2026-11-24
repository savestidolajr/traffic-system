import type { KeyboardEvent, MouseEvent } from 'react'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'

interface EntityActionsMenuProps {
    readonly onView: () => void
    readonly onEdit: () => void
    readonly onDuplicate: () => void
    /** Omitted when the caller may edit but not delete (bookends: DELETE stays Admin). */
    readonly onDelete?: () => void
    /** Exactly one of these is passed per item, matching its status: Archive
     *  for active items, Unarchive for archived ones (the item's status lives
     *  at the caller, so the menu renders whichever action is supplied). */
    readonly onArchive?: () => void
    readonly onUnarchive?: () => void
    readonly viewLabel?: string
    readonly ariaLabel?: string
    readonly className?: string
}

/** Shared kebab menu for gallery cards and table rows. */
export function EntityActionsMenu({
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    viewLabel = 'View details',
    ariaLabel = 'Entity actions',
    className,
}: EntityActionsMenuProps) {
    const stopPropagation = (event: MouseEvent | KeyboardEvent) =>
        event.stopPropagation()

    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button
                    variant="ghost"
                    size="icon"
                    className={cn('size-8', className)}
                    aria-label={ariaLabel}
                    onClick={stopPropagation}
                    onKeyDown={stopPropagation}
                >
                    <ICON.more aria-hidden />
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onClick={stopPropagation}>
                <DropdownMenuItem onClick={onView}>
                    <ICON.view aria-hidden />
                    {viewLabel}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onEdit}>
                    <ICON.edit aria-hidden />
                    Edit
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onDuplicate}>
                    <ICON.duplicate aria-hidden />
                    Duplicate
                </DropdownMenuItem>
                {onArchive && (
                    <DropdownMenuItem onClick={onArchive}>
                        <ICON.archive aria-hidden />
                        Archive
                    </DropdownMenuItem>
                )}
                {onUnarchive && (
                    <DropdownMenuItem onClick={onUnarchive}>
                        <ICON.unarchive aria-hidden />
                        Unarchive
                    </DropdownMenuItem>
                )}
                {onDelete && (
                    <DropdownMenuItem
                        onClick={onDelete}
                        className="text-destructive focus:text-destructive"
                    >
                        <ICON.delete aria-hidden />
                        Delete
                    </DropdownMenuItem>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    )
}
