import type { BadgeProps } from '@/components/ui/badge'

// Shared by the fresh-import review, the pending-approvals list, and the
// (now retired) flat items table — one mapping from a report row's Action
// to the Badge tone every one of those surfaces uses.
export function actionBadgeVariant(
    action: string
): NonNullable<BadgeProps['variant']> {
    switch (action) {
        case 'Create':
            return 'success'
        case 'Update':
            return 'default'
        case 'Unchanged':
            return 'secondary'
        case 'Error':
            return 'destructive'
        default:
            return 'outline'
    }
}
