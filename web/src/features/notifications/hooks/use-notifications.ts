import { useEffect, useSyncExternalStore } from 'react'
import { useLiveJobs } from '@/features/jobs/api/job-hooks'
import { formatMarketName } from '@/lib/formatting'
import type { NotificationItem } from '../types'
import { useJobStatusTransitions } from './use-job-status-transitions'

let notificationSnapshot: readonly NotificationItem[] = []
const listeners = new Set<() => void>()

function subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
}

const getSnapshot = () => notificationSnapshot

function emitNotifications(next: readonly NotificationItem[]) {
    notificationSnapshot = next
    for (const listener of listeners) listener()
}

function addNotification(notification: NotificationItem) {
    if (notificationSnapshot.some((item) => item.id === notification.id)) {
        return
    }
    emitNotifications([notification, ...notificationSnapshot])
}

function markRead(id: string) {
    let changed = false
    const next = notificationSnapshot.map((notification) => {
        if (notification.id !== id || notification.read) return notification
        changed = true
        return { ...notification, read: true }
    })
    if (changed) emitNotifications(next)
}

function markAllRead() {
    let changed = false
    const next = notificationSnapshot.map((notification) => {
        if (notification.read) return notification
        changed = true
        return { ...notification, read: true }
    })
    if (changed) emitNotifications(next)
}

export function useNotifications() {
    const { data: jobsData } = useLiveJobs()
    const transitions = useJobStatusTransitions(jobsData?.jobs)
    const notifications = useSyncExternalStore(
        subscribe,
        getSnapshot,
        getSnapshot
    )

    useEffect(() => {
        for (const job of transitions) {
            const id = `${job.jobId}:${job.status}`
            const message =
                job.status === 'WAITING_APPROVAL'
                    ? `${job.station} ${formatMarketName(job.market)}: bulletin awaiting approval`
                    : `${job.station} ${formatMarketName(job.market)}: bulletin failed`

            addNotification({
                id,
                jobId: job.jobId,
                status: job.status as NotificationItem['status'],
                message,
                timestamp: Date.now(),
                read: false,
            })
        }
    }, [transitions])

    const unreadCount = notifications.filter(
        (notification) => !notification.read
    ).length

    return { notifications, unreadCount, markRead, markAllRead }
}
