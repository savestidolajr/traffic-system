import { useState } from 'react'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'

/** Keep delete confirmation state consistent across station/operator cards and tables. */
export function useDeleteConfirm<T>(onConfirm: (item: T) => void) {
    const dialog = useConfirmDialog()
    const [pendingItem, setPendingItem] = useState<T | null>(null)

    const requestDelete = (item: T) => {
        setPendingItem(item)
        dialog.open()
    }

    const confirmDelete = () => {
        if (pendingItem) onConfirm(pendingItem)
        setPendingItem(null)
    }

    return {
        isOpen: dialog.isOpen,
        requestDelete,
        onOpenChange: dialog.onOpenChange,
        confirmDelete,
        pendingItem,
    }
}
