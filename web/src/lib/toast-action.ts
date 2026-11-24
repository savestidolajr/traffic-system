import { toast } from 'sonner'

/**
 * An immediate reversal offered on the success toast. Use for one-click,
 * low-ceremony decisions where the likeliest mistake is a misclick — the undo
 * runs the compensating action itself, so it must be idempotent-safe.
 */
export interface UndoAction {
    readonly label?: string
    readonly onUndo: () => void | Promise<void>
}

export async function toastAction<T>(
    action: () => Promise<T>,
    successMessage: string,
    errorFallback = 'An unexpected error occurred',
    formatError?: (err: unknown) => string | undefined,
    undo?: UndoAction
): Promise<T | true | undefined> {
    try {
        const result = await action()
        if (successMessage)
            toast.success(
                successMessage,
                undo
                    ? {
                          action: {
                              label: undo.label ?? 'Undo',
                              onClick: () => {
                                  void undo.onUndo()
                              },
                          },
                      }
                    : undefined
            )
        // Void mutations need an explicit success value so callers can
        // distinguish a completed action from toastAction's error path.
        return result === undefined ? true : result
    } catch (err: unknown) {
        const custom = formatError?.(err)
        const message =
            custom ?? (err instanceof Error ? err.message : errorFallback)
        toast.error(message)
        return undefined
    }
}
