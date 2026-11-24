import {
    CircleCheck,
    Clock,
    FileText,
    CornerDownRight,
    Inbox,
    Radio,
    ListChecks,
    AudioLines,
    Volume2,
    XCircle,
    RadioTower,
    AlertTriangle,
    CalendarX,
    type LucideIcon,
} from 'lucide-react'
import type { BadgeProps } from '@/components/ui/badge'
import type { Status } from '../types'

// =============================================================================
// Unified Status Configuration — Single source of truth
// =============================================================================

interface StatusConfig {
    readonly icon: LucideIcon
    readonly variant: NonNullable<BadgeProps['variant']>
    readonly label: string
    readonly activityLabel: string
}

export const STATUS_CONFIG: Record<Status, StatusConfig> = {
    REQUESTED: {
        icon: Inbox,
        variant: 'requested',
        label: 'Requested',
        activityLabel: 'Queued',
    },
    HERE_FETCHED: {
        icon: Radio,
        variant: 'hereFetched',
        label: 'Traffic Fetched',
        // Stamped at fetch START (see BroadcastOrchestrator.UpdateJobStatus) —
        // the in-flight activity at this milestone is the fetch itself.
        activityLabel: 'Fetching traffic',
    },
    AWAITING_CURATION: {
        icon: Clock,
        variant: 'warning',
        label: 'Awaiting curation',
        activityLabel: 'Awaiting curation',
    },
    INCIDENTS_SELECTED: {
        icon: ListChecks,
        variant: 'incidents',
        label: 'Incidents Selected',
        // The ENTIRE generation loop (draft → review/refine → safety →
        // synthesis → duration fit) runs under this status — the backend's
        // own activity mapping calls it GENERATING_SCRIPT.
        activityLabel: 'Generating script',
    },
    SCRIPT_READY: {
        icon: FileText,
        variant: 'scriptReady',
        label: 'Script Ready',
        // Script AND synthesis are done at this milestone; what remains is
        // audio save + pre-review before WaitingApproval.
        activityLabel: 'Finalizing audio',
    },
    SYNTHESIZING_AUDIO: {
        icon: AudioLines,
        variant: 'synthesizingAudio',
        label: 'Synthesizing Audio',
        activityLabel: 'Synthesizing audio',
    },
    AUDIO_READY: {
        icon: Volume2,
        variant: 'audioReady',
        label: 'Audio Ready',
        activityLabel: 'Audio ready',
    },
    WAITING_APPROVAL: {
        icon: Clock,
        variant: 'warning',
        label: 'Awaiting Review',
        activityLabel: 'Awaiting Review',
    },
    APPROVED: {
        icon: CircleCheck,
        variant: 'approved',
        label: 'Approved',
        activityLabel: 'Decision: Approved',
    },
    REJECTED: {
        icon: XCircle,
        variant: 'rejected',
        label: 'Rejected',
        activityLabel: 'Decision: Rejected',
    },
    DELIVERY_PENDING: {
        icon: RadioTower,
        variant: 'warning',
        label: 'Delivery Pending',
        activityLabel: 'Sending to Playout',
    },
    PUBLISHED: {
        icon: RadioTower,
        variant: 'approved',
        label: 'Published',
        activityLabel: 'Broadcast Complete',
    },
    FALLBACK_USED: {
        icon: CornerDownRight,
        variant: 'fallback',
        label: 'Fallback Used',
        activityLabel: 'Fallback Sequence',
    },
    FAILED: {
        icon: AlertTriangle,
        variant: 'destructive',
        label: 'Failed',
        activityLabel: 'Process Failed',
    },
    MISSED: {
        // Muted on purpose: a missed slot is a settled past fact and must read
        // cold, never hotter than a bulletin still needing a decision.
        icon: CalendarX,
        variant: 'secondary',
        label: 'Missed',
        activityLabel: 'MISSED',
    },
    CANCELLED: {
        icon: XCircle,
        variant: 'secondary',
        label: 'Cancelled',
        activityLabel: 'Delivery cancelled',
    },
}
