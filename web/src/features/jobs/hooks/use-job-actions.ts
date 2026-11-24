import { useState } from 'react'
import { ApiError } from '@/lib/api/client'
import { toastAction } from '@/lib/toast-action'
import type { EditedIncidentPayload, RejectDecision } from '../api/job-hooks'
import {
    useApproveJob,
    useRejectJob,
    useRejectToFallbackJob,
    usePublishJob,
    useRegenerateJob,
    useDeleteJob,
} from '../api/job-hooks'

/**
 * A delete that lost its revision precondition means the job changed under the
 * operator (someone else edited/decided it while they were looking) — reload,
 * don't blindly retry (bug-bash #12).
 */
export const DELETE_PRECONDITION_LOST_MESSAGE =
    'This bulletin changed since you opened it. Reload it before trying again.'

/** 428 = no/stale etag sent; 409 = the storage-level conditional delete lost the race. */
export function isDeletePreconditionLost(err: unknown): boolean {
    return err instanceof ApiError && (err.status === 409 || err.status === 428)
}

export function useJobActions(
    jobId: string,
    jobMarket?: string,
    etag?: string | null
) {
    const [isRejectOpen, setIsRejectOpen] = useState(false)

    const approveMutation = useApproveJob()
    const rejectMutation = useRejectJob()
    const rejectToFallbackMutation = useRejectToFallbackJob()
    const publishMutation = usePublishJob()
    const regenerateMutation = useRegenerateJob()
    const deleteMutation = useDeleteJob()

    const format403 = (err: unknown) =>
        err instanceof ApiError && err.status === 403
            ? `You're not assigned to ${jobMarket}`
            : undefined

    const handleReject = async (decision: RejectDecision) => {
        const result = await toastAction(
            () =>
                rejectMutation.mutateAsync({
                    jobId,
                    ...decision,
                    expectedEtag: etag,
                }),
            'Bulletin rejected',
            'Failed to reject bulletin',
            format403
        )
        if (result === true) setIsRejectOpen(false)
    }

    const handleRejectToFallback = async (decision: RejectDecision) => {
        const result = await toastAction(
            () =>
                rejectToFallbackMutation.mutateAsync({
                    jobId,
                    ...decision,
                    expectedEtag: etag,
                }),
            'Fallback queued for delivery',
            'Failed to queue fallback delivery',
            format403
        )
        if (result === true) setIsRejectOpen(false)
    }

    const handleApprove = () =>
        toastAction(
            () => approveMutation.mutateAsync({ jobId, expectedEtag: etag }),
            'Bulletin approved',
            'Failed to approve bulletin',
            format403
        )

    const handlePublish = () =>
        toastAction(
            () =>
                publishMutation.mutateAsync({
                    jobId,
                    expectedEtag: etag,
                }),
            'Bulletin queued for delivery',
            'Failed to queue bulletin delivery',
            format403
        )

    const handleRegenerate = (
        targetDurationSec?: number,
        editedScript?: string,
        editedIncidents?: EditedIncidentPayload[],
        revoiceOnly?: boolean
    ) =>
        toastAction(
            () =>
                regenerateMutation.mutateAsync({
                    jobId,
                    targetDurationSec,
                    editedScript,
                    editedIncidents,
                    revoiceOnly,
                }),
            'Bulletin queued for regeneration',
            'Failed to regenerate bulletin',
            format403
        )

    const handleDelete = () =>
        toastAction(
            // DELETE carries a revision precondition (bug-bash #12): the etag
            // read from the job DTO, exactly like approve/publish. No body or
            // a stale etag is rejected with 428/409 and the delete never
            // proceeds.
            () => deleteMutation.mutateAsync({ jobId, expectedEtag: etag }),
            'Job deleted',
            'Failed to delete job',
            (err) =>
                format403(err) ??
                (isDeletePreconditionLost(err)
                    ? DELETE_PRECONDITION_LOST_MESSAGE
                    : undefined)
        )

    return {
        isRejectOpen,
        setIsRejectOpen,
        handleReject,
        handleRejectToFallback,
        handleApprove,
        handlePublish,
        handleRegenerate,
        handleDelete,
        isApproving: approveMutation.isPending,
        isPublishing: publishMutation.isPending,
        isRegenerating: regenerateMutation.isPending,
        isRejecting: rejectMutation.isPending,
        isRejectingToFallback: rejectToFallbackMutation.isPending,
        isDeleting: deleteMutation.isPending,
    }
}
