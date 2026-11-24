/** True when running on a Mac-family platform (macOS/iOS). */
export const isMac =
    typeof navigator !== 'undefined' &&
    /Mac|iPod|iPhone|iPad/.test(navigator.userAgent)

/**
 * Platform-appropriate shortcut modifier prefix for displayed hints —
 * '⌘' on Mac, 'Ctrl+' elsewhere. The actual key handlers accept both
 * `metaKey` and `ctrlKey` regardless of platform; this only affects what
 * we show the user.
 */
export const modKey = isMac ? '⌘' : 'Ctrl+'
