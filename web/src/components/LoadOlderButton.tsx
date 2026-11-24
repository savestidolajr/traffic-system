import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { ICON } from '@/lib/design/icons'

interface LoadOlderButtonProps {
    readonly onClick: () => void
    readonly loading: boolean
    readonly label?: string
}

/** Shared footer control for "Load older" cursor pagination — Activity and History both page this way. */
export function LoadOlderButton({
    onClick,
    loading,
    label = 'Load older',
}: LoadOlderButtonProps) {
    return (
        <div className="flex justify-center border-t py-3">
            <Button
                variant="outline"
                size="sm"
                onClick={onClick}
                disabled={loading}
            >
                {loading ? (
                    <Spinner data-icon="inline-start" />
                ) : (
                    <ICON.expand
                        data-icon="inline-start"
                        className="size-3.5"
                    />
                )}
                {label}
            </Button>
        </div>
    )
}
