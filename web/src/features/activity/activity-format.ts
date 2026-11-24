import { truncateId } from '@/lib/formatting'
import type { ActivityLogEntry } from './api/activity-schemas'

/**
 * Human-readable verb phrase per HTTP method. Mirrors the mutating methods the
 * backend audit logs (POST/PUT/DELETE only); an unknown method falls back to
 * its raw verb so the column never renders an empty sentence.
 */
const VERB_PHRASES: Readonly<Record<string, string>> = {
    PUT: 'Updated',
    POST: 'Created',
    DELETE: 'Deleted',
}

/**
 * Friendly noun per resolved activity resource. Mirrors the backend's
 * ActivityPathResolver resource table; any resource without an entry (e.g.
 * 'jobs', 'operators', 'config-imports') falls back to its raw segment so a
 * newly added resource stays readable rather than rendering blank.
 */
const RESOURCE_NOUNS: Readonly<Record<string, string>> = {
    schedules: 'schedule',
    'priority-roads': 'priority roads',
    'voices/profiles': 'voice profile',
    'safety-terms': 'banned words',
    'filler-statements': 'filler statements',
    guidance: 'guidance',
    'delivery-styles': 'delivery style',
    broadcast: 'bulletin',
    jobs: 'bulletin',
}

/**
 * Renders a collapsed audit entry as a human sentence ('Updated schedule ·
 * ade-1', 'Requested bulletin') instead of the raw REST call. The entity id is
 * appended when present and truncated via truncateId when long, so a voice-
 * profile GUID never leaks into the collapsed column.
 */
export function formatActivityAction(
    entry: Pick<ActivityLogEntry, 'method' | 'resource' | 'entityId'>
): string {
    const phrase =
        entry.method === 'POST' && entry.resource === 'broadcast'
            ? 'Requested bulletin'
            : `${VERB_PHRASES[entry.method] ?? entry.method} ${
                  RESOURCE_NOUNS[entry.resource] ?? entry.resource
              }`
    return entry.entityId
        ? `${phrase} \u00b7 ${truncateId(entry.entityId)}`
        : phrase
}
