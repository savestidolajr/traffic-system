import type { Job } from '../../types'
import { ConfigurationTab } from '../canvas/ConfigurationTab'
import { SystemAuditTab } from '../canvas/SystemAuditTab'

interface ReviewDetailsTabProps {
    readonly job: Job
}

/**
 * Config & audit: the bulletin's configuration as a compact side rail,
 * and one unified timeline (status history with attached log lines) as
 * the main column. ReviewPane owns the outer layout.
 */
export function ReviewDetailsTab({ job }: ReviewDetailsTabProps) {
    return (
        <div className="grid gap-4 xl:gap-5 xl:grid-cols-[minmax(300px,420px)_minmax(0,1fr)] xl:items-start">
            <ConfigurationTab job={job} />
            <SystemAuditTab job={job} />
        </div>
    )
}
