/** Check if a keyboard event targets a form field or an open dialog. */
export function shouldIgnoreKeyboardEvent(
    e: KeyboardEvent | React.KeyboardEvent
): boolean {
    const target = e.target as HTMLElement
    if (!target) return false
    if (target.closest('input, textarea, [contenteditable="true"]')) return true
    if (document.querySelector('[role=dialog][data-state=open]')) return true
    return false
}
