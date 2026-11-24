import { AlertCircle } from 'lucide-react'
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { ICON } from '@/lib/design/icons'

interface ErrorAlertProps {
    readonly error: unknown
    readonly title?: string
    readonly fallbackMessage?: string
    readonly onRetry?: () => void
    readonly retryLabel?: string
    readonly actions?: React.ReactNode
    readonly className?: string
}

function ErrorAlert({
    error,
    title = 'Error',
    fallbackMessage = 'Something went wrong',
    onRetry,
    retryLabel = 'Retry',
    actions,
    className,
}: ErrorAlertProps) {
    return (
        <Alert variant="destructive" className={cn(className)}>
            <AlertCircle />
            <AlertTitle>{title}</AlertTitle>
            <AlertDescription>
                {error instanceof Error ? error.message : fallbackMessage}
            </AlertDescription>
            {(onRetry || actions) && (
                <div className="flex gap-2 mt-2">
                    {onRetry && (
                        <Button variant="outline" size="sm" onClick={onRetry}>
                            <ICON.reset data-icon="inline-start" />
                            {retryLabel}
                        </Button>
                    )}
                    {actions}
                </div>
            )}
        </Alert>
    )
}

export { ErrorAlert, type ErrorAlertProps }
