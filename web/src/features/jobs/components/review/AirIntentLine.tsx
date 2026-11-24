import { formatClockTime } from '@/lib/formatting'
import type { Job } from '../../types'

/**
 * One muted line answering "what is going out, and by when" without opening the
 * fallback panel below.
 *
 * `job.input.timeWindow` is deliberately NOT a slot bracket: `from` is
 * `FallbackDeadlineUtc` (the moment standby coverage takes over) and `to` is
 * `ScheduledForUtc` (the air slot). That inversion is documented at
 * `JobMappingExtensions.BuildBaseProperties` — do not read them as start/end.
 */
export function AirIntentLine({ job }: { readonly job: Job }) {
    const slotAt = job.input?.timeWindow?.to
    const standbyAt = job.input?.timeWindow?.from
    const slot = slotAt ? formatClockTime(slotAt) : ''
    const standby = standbyAt ? formatClockTime(standbyAt) : ''

    const text = ((): string | null => {
        switch (job.status) {
            case 'WAITING_APPROVAL':
            case 'AUDIO_READY':
                if (!slot || !standby) return null
                return `This bulletin goes to playout at ${slot}. If it is not approved by ${standby}, the standby cart is sent instead.`
            case 'APPROVED':
                return slot ? `Approved. Going to playout at ${slot}.` : null
            case 'FALLBACK_USED':
                if (!slot) return null
                return job.fallback?.sourceJobId
                    ? `An earlier bulletin from today is going to playout at ${slot}, not this one.`
                    : `The standby cart is going to playout at ${slot}, not this bulletin.`
            case 'PUBLISHED':
                return job.publish?.destination && job.publish?.publishedAt
                    ? `Sent to ${job.publish.destination} at ${formatClockTime(job.publish.publishedAt)}.`
                    : 'Published.'
            default:
                return null
        }
    })()

    if (!text) return null

    return <p className="text-sm text-muted-foreground">{text}</p>
}
