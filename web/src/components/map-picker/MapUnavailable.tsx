import { MapPinOff } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * Shown in place of a map when the environment has no HERE browser map key
 * (`Here--MapApiKey` in Key Vault) or the map config could not be loaded.
 * Says so plainly rather than silently rendering a blank or third-party map.
 * `compact` drops the explanation for thumbnail-sized maps.
 */
export function MapUnavailable({
    compact = false,
    className,
}: {
    readonly compact?: boolean
    readonly className?: string
}) {
    return (
        <div
            role="status"
            className={cn(
                'flex h-full w-full flex-col items-center justify-center gap-1 text-center text-xs text-muted-foreground',
                compact ? 'p-1' : 'p-4',
                className
            )}
        >
            <span className="flex items-center gap-1.5 font-medium text-foreground">
                <MapPinOff className="size-4 shrink-0" aria-hidden />
                Map unavailable
            </span>
            {!compact && (
                <span>
                    No HERE map key is configured for this environment. Coverage
                    settings still save; ask platform support to set the map
                    key.
                </span>
            )}
        </div>
    )
}
