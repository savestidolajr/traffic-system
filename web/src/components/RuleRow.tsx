import { useState, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { Switch } from '@/components/ui/switch'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'

const ExpandIcon = ICON.expand

interface RuleRowProps {
    /** Whether the item is enabled (controls the Switch on the left). */
    readonly enabled: boolean
    /** Called when the user toggles the Switch. */
    readonly onToggle: () => void
    /** Primary identifier label (road name, term text, statement text, directive). */
    readonly label: string
    /** Secondary label shown dimmed below the primary (e.g. ambiguous road reason). Ignored when `expandable`. */
    readonly secondary?: string
    /** Fixed-width badge/pill rendered between the toggle and the label (e.g. guidance type pill). */
    readonly badge?: ReactNode
    /** Long-text rows (e.g. directives): renders the label as a click-to-expand/collapse toggle instead of static text. */
    readonly expandable?: boolean
    /** Italicises the label instead of rendering a badge — for a free-text preamble rather than a categorical rule. Requires `expandable`. */
    readonly quote?: boolean
    /** Optional decorative badges/pills/selects rendered on the right, always visible (not hover-gated). */
    readonly actions?: ReactNode
    /** Called when the user chooses Edit from the row actions menu. Omit to hide the edit action. */
    readonly onEdit?: () => void
    /** Accessible label for the edit action. */
    readonly editLabel?: string
    /** Called when the user confirms Delete from the row actions menu. Omit to hide the delete action. */
    readonly onDelete?: () => void
    /** Accessible label for the delete action. */
    readonly deleteLabel?: string
    /** Disables all interactive controls (while saving). */
    readonly disabled?: boolean
    /** Hides the edit/delete controls and disables the toggle — the caller lacks permission on this row. */
    readonly readOnly?: boolean
    /** Noun used to build default accessible labels ("Enable {itemLabel}", "Delete {itemLabel}") when the specific label props are omitted. */
    readonly itemLabel?: string
}

/**
 * Shared list-row component for the Bulletin-rules hub and editorial guidance
 * inventory. Every row shares the same rhythm: Switch toggle on the left →
 * optional badge → primary/secondary label → type-specific actions (always
 * visible) → kebab actions menu. Rendered inside a {@link ScopeGroupedList}.
 */
export function RuleRow({
    enabled,
    onToggle,
    label,
    secondary,
    badge,
    expandable,
    quote,
    actions,
    onEdit,
    editLabel,
    onDelete,
    deleteLabel,
    disabled,
    readOnly,
    itemLabel,
}: RuleRowProps) {
    const [expanded, setExpanded] = useState(false)
    const deleteConfirm = useConfirmDialog()

    const toggleAria = itemLabel
        ? `${enabled ? 'Disable' : 'Enable'} ${itemLabel}`
        : enabled
          ? 'Disable'
          : 'Enable'
    const resolvedEditLabel =
        editLabel ?? (itemLabel ? `Edit ${itemLabel}` : 'Edit')
    const resolvedDeleteLabel =
        deleteLabel ?? (itemLabel ? `Delete ${itemLabel}` : 'Delete')
    const actionsLabel = itemLabel ? `${itemLabel} actions` : 'Row actions'

    return (
        <>
            <Switch
                checked={enabled}
                onCheckedChange={onToggle}
                disabled={disabled || readOnly}
                aria-label={toggleAria}
            />
            {/* Badge and label share one flex child so they can STACK below
                `sm`. The badge used to be a fixed `w-20` column on its own
                row-level line: with the Switch, chevron, edit and delete that
                left ~90px for the text at 390px, so every row read
                "no breath'…". Above `sm` it goes back to a fixed `w-28`
                column so badges stay aligned down the list. */}
            <div className="flex min-w-0 flex-1 flex-col items-start gap-0.5 sm:flex-row sm:items-center sm:gap-3">
                {badge && <div className="shrink-0 sm:w-28">{badge}</div>}
                {expandable ? (
                    <button
                        type="button"
                        onClick={() => setExpanded((e) => !e)}
                        aria-expanded={expanded}
                        className="flex w-full min-w-0 flex-1 items-start gap-1.5 py-0.5 text-left"
                    >
                        <span
                            className={cn(
                                'min-w-0 flex-1 text-sm',
                                quote && 'italic text-foreground/90',
                                !expanded && 'line-clamp-2 sm:line-clamp-1'
                            )}
                        >
                            {label}
                        </span>
                        <ExpandIcon
                            className={cn(
                                'mt-0.5 size-3.5 shrink-0 text-muted-foreground-subtle transition-transform',
                                expanded && 'rotate-180'
                            )}
                            aria-hidden
                        />
                    </button>
                ) : (
                    <span className="flex w-full min-w-0 flex-1 flex-col">
                        <span className="truncate text-sm">{label}</span>
                        {secondary && (
                            <span className="truncate text-xs text-muted-foreground">
                                {secondary}
                            </span>
                        )}
                    </span>
                )}
            </div>
            {actions && (
                <span className="flex shrink-0 items-center gap-1.5">
                    {actions}
                </span>
            )}
            {!readOnly && (onEdit || onDelete) && (
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button
                            variant="ghost"
                            size="icon"
                            className="size-8 shrink-0"
                            disabled={disabled}
                            aria-label={actionsLabel}
                            onClick={(event) => event.stopPropagation()}
                            onKeyDown={(event) => event.stopPropagation()}
                        >
                            <ICON.more aria-hidden />
                        </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent
                        align="end"
                        onClick={(event) => event.stopPropagation()}
                    >
                        {onEdit && (
                            <DropdownMenuItem
                                onClick={onEdit}
                                aria-label={resolvedEditLabel}
                            >
                                <ICON.edit aria-hidden />
                                Edit
                            </DropdownMenuItem>
                        )}
                        {onDelete && (
                            <DropdownMenuItem
                                onClick={deleteConfirm.open}
                                disabled={disabled}
                                className="text-destructive focus:text-destructive"
                                aria-label={resolvedDeleteLabel}
                            >
                                <ICON.delete aria-hidden />
                                Delete
                            </DropdownMenuItem>
                        )}
                    </DropdownMenuContent>
                </DropdownMenu>
            )}
            {onDelete && (
                <ConfirmDialog
                    open={deleteConfirm.isOpen}
                    onOpenChange={deleteConfirm.onOpenChange}
                    onConfirm={onDelete}
                    title={`${resolvedDeleteLabel}?`}
                    description={`This will permanently delete ${label}.`}
                    confirmLabel="Delete"
                />
            )}
        </>
    )
}
