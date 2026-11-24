import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { shouldIgnoreKeyboardEvent } from '@/lib/dom'
import { NAV_SHORTCUT_PREFIX, resolveNavShortcut } from '@/lib/routes'

/** How long an armed `g` prefix stays pending before it expires. */
const SEQUENCE_TIMEOUT_MS = 1500

/**
 * Two-key "go to" navigation: `g` then a route shortcut, e.g. `g` `1` for
 * On air (the GitHub/Linear prefix convention). The second key only
 * navigates when it names a real route, and an armed prefix expires after a
 * pause so a stray `g` never swallows a later unrelated keystroke.
 *
 * The dialog/form-field guards match useGlobalKeydown, both routing through
 * shouldIgnoreKeyboardEvent, so a sequence never fires while the command
 * palette is open or a form field is focused.
 */
export function useNavShortcuts(): void {
    const navigate = useNavigate()
    const pendingRef = useRef(false)
    const timerRef = useRef<number | undefined>(undefined)

    useEffect(() => {
        const onKeyDown = (e: KeyboardEvent) => {
            if (shouldIgnoreKeyboardEvent(e) || e.repeat) return
            if (e.key === NAV_SHORTCUT_PREFIX) {
                pendingRef.current = true
                window.clearTimeout(timerRef.current)
                timerRef.current = window.setTimeout(() => {
                    pendingRef.current = false
                }, SEQUENCE_TIMEOUT_MS)
                return
            }
            if (!pendingRef.current) return
            pendingRef.current = false
            window.clearTimeout(timerRef.current)
            const path = resolveNavShortcut([NAV_SHORTCUT_PREFIX, e.key])
            if (path) {
                e.preventDefault()
                navigate(path)
            }
        }
        window.addEventListener('keydown', onKeyDown)
        return () => {
            window.removeEventListener('keydown', onKeyDown)
            window.clearTimeout(timerRef.current)
        }
    }, [navigate])
}
