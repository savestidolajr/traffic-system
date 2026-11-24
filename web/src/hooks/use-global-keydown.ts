import { useEffect, useRef } from 'react'
import { shouldIgnoreKeyboardEvent } from '@/lib/dom'

/**
 * Register a global keydown listener on window.
 * Automatically ignores events targeting form fields or open dialogs.
 *
 * The handler is read through a ref kept current each render, so callers can
 * pass a fresh closure every render without a manual dependency array and the
 * window listener is only re-bound when `key` changes.
 */
export function useGlobalKeydown(key: string, handler: () => void): void {
    const handlerRef = useRef(handler)
    useEffect(() => {
        handlerRef.current = handler
    })

    useEffect(() => {
        const onKeyDown = (e: KeyboardEvent) => {
            if (shouldIgnoreKeyboardEvent(e)) return
            if (e.key === key) handlerRef.current()
        }
        window.addEventListener('keydown', onKeyDown)
        return () => window.removeEventListener('keydown', onKeyDown)
    }, [key])
}
