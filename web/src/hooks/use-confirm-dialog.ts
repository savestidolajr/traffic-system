import { useState, useCallback } from 'react'

export function useConfirmDialog(): {
    isOpen: boolean
    open: () => void
    onOpenChange: (o: boolean) => void
} {
    const [isOpen, setIsOpen] = useState(false)

    const open = useCallback(() => setIsOpen(true), [])

    const onOpenChange = useCallback((o: boolean) => setIsOpen(o), [])

    return { isOpen, open, onOpenChange }
}
