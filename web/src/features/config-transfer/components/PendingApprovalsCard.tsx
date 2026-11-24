import { useState } from 'react'
import { toast } from 'sonner'
import { ApiError } from '@/lib/api/client'
import {
    Card,
    CardContent,
    CardHeader,
    CardTitle,
    CardDescription,
} from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { MovingBorder } from '@/components/ui/aceternity/moving-border'
import {
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import { formatRelativeTime } from '@/lib/formatting'
import { ErrorAlert } from '@/components/ErrorAlert'
import {
    usePendingImports,
    useApproveImport,
    useRejectImport,
} from '../api/config-transfer-hooks'
import type { PendingConfigImport } from '../api/config-transfer-schemas'
import { actionBadgeVariant } from '../lib/action-badge'

const ApproveIcon = ICON.approve
const RejectIcon = ICON.reject
const ViewIcon = ICON.view
const MoreIcon = ICON.more
const ExpandIcon = ICON.expand

interface PendingImportRowProps {
    pending: PendingConfigImport
    onApprove: (id: string) => void
    onReject: (id: string) => void
    approving: boolean
    rejecting: boolean
}

/**
 * One pending import — click-to-expand for the item table, kebab menu for
 * Approve/Reject. Mirrors the gallery-card convention (DeliveryStyleCard,
 * VoiceVariantCard): the whole row opens/closes details, the menu carries
 * every action including a mirrored "Show/Hide details" so keyboard users
 * reach the same toggle without a nested-interactive a11y violation.
 */
function PendingImportRow({
    pending,
    onApprove,
    onReject,
    approving,
    rejecting,
}: PendingImportRowProps) {
    const [expanded, setExpanded] = useState(false)
    const [confirmAction, setConfirmAction] = useState<
        'approve' | 'reject' | null
    >(null)
    const toggleExpanded = () => setExpanded((v) => !v)

    return (
        <>
            <div className="rounded-lg border">
                <div
                    onClick={toggleExpanded}
                    className="flex cursor-pointer items-center gap-3 px-4 py-3"
                >
                    <div className="flex min-w-0 flex-1 flex-col gap-1">
                        <span className="text-sm font-medium">
                            {pending.requestedBy}
                        </span>
                        <span className="font-mono text-xs text-muted-foreground">
                            ID: {pending.id}
                        </span>
                        <span className="text-xs text-muted-foreground">
                            {formatRelativeTime(pending.requestedAtUtc)}
                        </span>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                        {pending.report ? (
                            <>
                                <Badge variant="secondary">
                                    {pending.report.creates} create
                                    {pending.report.creates === 1 ? '' : 's'}
                                </Badge>
                                <Badge variant="default">
                                    {pending.report.updates} update
                                    {pending.report.updates === 1 ? '' : 's'}
                                </Badge>
                                {pending.report.errors > 0 && (
                                    <Badge variant="destructive">
                                        {pending.report.errors} error
                                        {pending.report.errors === 1 ? '' : 's'}
                                    </Badge>
                                )}
                            </>
                        ) : (
                            <Badge variant="warning">Report unavailable</Badge>
                        )}
                    </div>
                    <ExpandIcon
                        className={cn(
                            'size-4 shrink-0 text-muted-foreground transition-transform',
                            expanded && 'rotate-180'
                        )}
                        aria-hidden
                    />
                    <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                            <Button
                                variant="ghost"
                                size="icon"
                                className="size-8 shrink-0"
                                aria-label="Pending import actions"
                                onClick={(e: React.MouseEvent) =>
                                    e.stopPropagation()
                                }
                                onKeyDown={(e: React.KeyboardEvent) =>
                                    e.stopPropagation()
                                }
                            >
                                <MoreIcon />
                            </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent
                            align="end"
                            onClick={(e) => e.stopPropagation()}
                        >
                            <DropdownMenuItem onClick={toggleExpanded}>
                                <ViewIcon />
                                {expanded ? 'Hide details' : 'Show details'}
                            </DropdownMenuItem>
                            <DropdownMenuItem
                                onClick={() => setConfirmAction('approve')}
                                disabled={approving}
                            >
                                <ApproveIcon />
                                Approve
                            </DropdownMenuItem>
                            <DropdownMenuItem
                                onClick={() => setConfirmAction('reject')}
                                disabled={rejecting}
                                className="text-destructive focus:text-destructive"
                            >
                                <RejectIcon />
                                Reject
                            </DropdownMenuItem>
                        </DropdownMenuContent>
                    </DropdownMenu>
                </div>
                {expanded &&
                    (pending.report ? (
                        <div className="overflow-x-auto border-t">
                            <Table>
                                <TableHeader>
                                    <TableRow>
                                        <TableHead>Section</TableHead>
                                        <TableHead>Name</TableHead>
                                        <TableHead>Action</TableHead>
                                        <TableHead>Detail</TableHead>
                                    </TableRow>
                                </TableHeader>
                                <TableBody>
                                    {pending.report.items.map((item, i) => (
                                        <TableRow
                                            key={`${item.section}-${item.key}-${i}`}
                                        >
                                            <TableCell className="font-medium">
                                                {item.section}
                                            </TableCell>
                                            <TableCell>
                                                {item.name ?? item.key}
                                            </TableCell>
                                            <TableCell>
                                                <Badge
                                                    variant={actionBadgeVariant(
                                                        item.action
                                                    )}
                                                >
                                                    {item.action}
                                                </Badge>
                                            </TableCell>
                                            <TableCell
                                                className={
                                                    item.action === 'Error'
                                                        ? 'text-destructive'
                                                        : undefined
                                                }
                                            >
                                                {item.detail ?? '\u2014'}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </div>
                    ) : (
                        <div className="border-t px-4 py-3 text-sm text-muted-foreground">
                            Report unavailable
                        </div>
                    ))}
            </div>

            <AlertDialog
                open={confirmAction !== null}
                onOpenChange={(open) => {
                    if (!open) setConfirmAction(null)
                }}
            >
                <AlertDialogContent>
                    <AlertDialogHeader>
                        <AlertDialogTitle>
                            {confirmAction === 'approve'
                                ? 'Approve import?'
                                : 'Reject import?'}
                        </AlertDialogTitle>
                        <AlertDialogDescription>
                            {confirmAction === 'approve'
                                ? 'This will apply the config changes.'
                                : 'This will discard the pending import.'}
                        </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                        <AlertDialogCancel>Cancel</AlertDialogCancel>
                        <AlertDialogAction
                            onClick={() => {
                                if (confirmAction === 'approve')
                                    onApprove(pending.id)
                                else onReject(pending.id)
                                setConfirmAction(null)
                            }}
                            className={
                                confirmAction === 'reject'
                                    ? 'bg-destructive text-destructive-foreground hover:bg-destructive/90'
                                    : undefined
                            }
                        >
                            {confirmAction === 'approve' ? 'Approve' : 'Reject'}
                        </AlertDialogAction>
                    </AlertDialogFooter>
                </AlertDialogContent>
            </AlertDialog>
        </>
    )
}

export function PendingApprovalsCard() {
    const pendingQuery = usePendingImports()
    const approveMutation = useApproveImport()
    const rejectMutation = useRejectImport()

    const [pendingActionId, setPendingActionId] = useState<string | null>(null)
    const items = pendingQuery.data?.items ?? []
    if (pendingQuery.isError)
        return (
            <ErrorAlert
                error={pendingQuery.error}
                fallbackMessage="Failed to load pending approvals"
                onRetry={() => void pendingQuery.refetch()}
            />
        )

    if (items.length === 0) return null

    const handleApprove = async (id: string) => {
        setPendingActionId(id)
        try {
            const result = await approveMutation.mutateAsync(id)
            toast.success('Import approved and applied')
            // If the result has errors, show them
            if (result && result.errors > 0) {
                toast.error(
                    `Applied with ${result.errors} error${result.errors === 1 ? '' : 's'}`
                )
            }
        } catch (err: unknown) {
            // Status comes off `ApiError` — reading `err.response` never matched
            // (the client's interceptor strips it), so both specific refusals
            // fell through to the generic message. The 409 in particular already
            // NAMES the drift ("Schedules adelaide was Update, now Unchanged…"),
            // which is the only thing that tells an approver what to do next.
            const status = err instanceof ApiError ? err.status : undefined
            if (status === 403) {
                toast.error('Must be approved by a different admin', {
                    description: err instanceof Error ? err.message : undefined,
                })
            } else if (status === 409) {
                toast.error('The plan changed since it was submitted', {
                    description: err instanceof Error ? err.message : undefined,
                })
                // Re-fetch to get updated pending list
                pendingQuery.refetch()
            } else {
                toast.error(
                    err instanceof Error
                        ? err.message
                        : 'Failed to approve import'
                )
            }
        } finally {
            setPendingActionId((current) => (current === id ? null : current))
        }
    }

    const handleReject = async (id: string) => {
        setPendingActionId(id)
        try {
            await rejectMutation.mutateAsync(id)
            toast.success('Import rejected')
        } catch (err: unknown) {
            toast.error('Failed to reject import', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        } finally {
            setPendingActionId((current) => (current === id ? null : current))
        }
    }

    return (
        <MovingBorder tone="warning">
            <Card className="border-0 shadow-none">
                <CardHeader>
                    <CardTitle>Pending approvals</CardTitle>
                    <CardDescription>
                        Imports waiting for a second admin to approve.
                    </CardDescription>
                </CardHeader>
                <CardContent className="flex flex-col gap-3">
                    {items.map((pending) => (
                        <PendingImportRow
                            key={pending.id}
                            pending={pending}
                            onApprove={handleApprove}
                            onReject={handleReject}
                            approving={pendingActionId === pending.id}
                            rejecting={pendingActionId === pending.id}
                        />
                    ))}
                </CardContent>
            </Card>
        </MovingBorder>
    )
}
