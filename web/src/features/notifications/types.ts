export interface NotificationItem {
    id: string
    jobId: string
    status: 'WAITING_APPROVAL' | 'FAILED'
    message: string
    timestamp: number
    read: boolean
}
