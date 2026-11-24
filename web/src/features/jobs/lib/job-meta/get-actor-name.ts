import type { StatusHistoryItem } from '../../types'

/**
 * Returns the actor responsible for a status-history transition.
 *
 * Falls back to `'System'` when the backend does not supply an actor identity.
 * Once the `/jobs/{id}/history` endpoint returns richer claims (role, email),
 * this is the single place to upgrade.
 */
export function getActorName(item: Pick<StatusHistoryItem, 'by'>): string {
    const by = item.by || 'System'
    // Normalize the backend's lowercase machine actors for display
    if (by === 'ai') return 'AI'
    if (by === 'system') return 'System'
    return by
}
