import { Job } from '../types'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { ICON } from '@/lib/design/icons'
// No ICON entry carries this exact glyph (ICON.success is CircleCheck), so the
// publish button keeps its original plain check.
import { Check } from 'lucide-react'

interface JobActionsProps {
    job: Job
    onApprove: () => Promise<void>
    onPublish: () => Promise<void>
    onRegenerate: () => Promise<void>
    isApproving: boolean
    isPublishing: boolean
    isRegenerating: boolean
    /**
     * The air slot has passed. Measured on deployed dev + test 2026-08-24: a healthy
     * covered slot holds FALLBACK_USED for ~0.2s before `system (deadline)` approves and
     * publishes it, so a job an operator actually FINDS in FALLBACK_USED is one whose
     * delivery failed — and all 39 such rows were 12.7 to 28.7 days old. Both actions
     * below act on the slot, so once it is gone neither is a real option: retrying
     * generates a bulletin for dead air, and approving signs off audio nothing will
     * collect. Status alone cannot tell a rescuable cover from a dead one; the slot can.
     */
    slotMissed?: boolean
}

/**
 * Status-appropriate actions for non-waiting bulletins (rejected, fallback,
 * approved). WAITING_APPROVAL renders the fused DecisionBar instead, and
 * delete lives in the workspace's overflow menu.
 */
export function JobActions({
    job,
    onApprove,
    onPublish,
    onRegenerate,
    isApproving,
    isPublishing,
    isRegenerating,
    slotMissed = false,
}: JobActionsProps) {
    const isActionDisabled = isApproving || isPublishing || isRegenerating
    const publishConfirm = useConfirmDialog()
    const retryConfirm = useConfirmDialog()

    if (job.status === 'REJECTED') {
        return (
            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={onRegenerate}
                        disabled={isActionDisabled || isRegenerating}
                    >
                        {isRegenerating && <Spinner data-icon="inline-start" />}
                        <ICON.regenerate data-icon="inline-start" />
                        {isRegenerating ? 'Regenerating…' : 'Regenerate'}
                    </Button>
                </TooltipTrigger>
                <TooltipContent>
                    <p>Regenerate this bulletin</p>
                </TooltipContent>
            </Tooltip>
        )
    }

    // A cover whose slot has gone is a record, not a decision: both actions here act on
    // the slot. See slotMissed on JobActionsProps for the measurement behind this.
    if (job.status === 'FALLBACK_USED' && slotMissed) return null

    if (job.status === 'FALLBACK_USED') {
        return (
            <>
                <div className="flex gap-3">
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <Button
                                variant="default"
                                size="sm"
                                onClick={retryConfirm.open}
                                disabled={isActionDisabled || isRegenerating}
                            >
                                {isRegenerating && (
                                    <Spinner data-icon="inline-start" />
                                )}
                                <ICON.regenerate data-icon="inline-start" />
                                {isRegenerating
                                    ? 'Retrying generation…'
                                    : 'Retry generation'}
                            </Button>
                        </TooltipTrigger>
                        <TooltipContent>
                            <p>Try a fresh bulletin for this slot</p>
                        </TooltipContent>
                    </Tooltip>
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={onApprove}
                                disabled={isActionDisabled || isApproving}
                            >
                                {isApproving && (
                                    <Spinner data-icon="inline-start" />
                                )}
                                <ICON.approve data-icon="inline-start" />
                                {isApproving
                                    ? 'Approving fallback…'
                                    : 'Approve fallback'}
                            </Button>
                        </TooltipTrigger>
                        <TooltipContent>
                            <p>Approve this fallback bulletin</p>
                        </TooltipContent>
                    </Tooltip>
                </div>
                <ConfirmDialog
                    open={retryConfirm.isOpen}
                    onOpenChange={retryConfirm.onOpenChange}
                    onConfirm={onRegenerate}
                    title="Retry generation?"
                    description="Replaces the standby bulletin with a fresh generation attempt."
                    confirmLabel="Retry generation"
                    cancelLabel="Cancel"
                    destructive={false}
                />
            </>
        )
    }

    if (job.status === 'APPROVED') {
        return (
            <>
                <Tooltip>
                    <TooltipTrigger asChild>
                        <Button
                            variant="default"
                            size="sm"
                            onClick={publishConfirm.open}
                            disabled={isActionDisabled || isPublishing}
                        >
                            {isPublishing && (
                                <Spinner data-icon="inline-start" />
                            )}
                            <Check data-icon="inline-start" />
                            {isPublishing ? 'Publishing…' : 'Publish'}
                        </Button>
                    </TooltipTrigger>
                    <TooltipContent>
                        <p>Publish this bulletin</p>
                    </TooltipContent>
                </Tooltip>
                <ConfirmDialog
                    open={publishConfirm.isOpen}
                    onOpenChange={publishConfirm.onOpenChange}
                    onConfirm={onPublish}
                    title="Publish bulletin?"
                    description="This will deliver the approved bulletin to the playout system."
                    confirmLabel="Publish"
                    cancelLabel="Cancel"
                />
            </>
        )
    }

    if (job.status === 'FAILED') {
        return (
            <Tooltip>
                <TooltipTrigger asChild>
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={onRegenerate}
                        disabled={isActionDisabled || isRegenerating}
                    >
                        {isRegenerating && <Spinner data-icon="inline-start" />}
                        <ICON.regenerate data-icon="inline-start" />
                        {isRegenerating ? 'Retrying…' : 'Retry'}
                    </Button>
                </TooltipTrigger>
                <TooltipContent>
                    <p>Retry this bulletin</p>
                </TooltipContent>
            </Tooltip>
        )
    }

    return null
}
