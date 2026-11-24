import { cn } from '@/lib/utils'

function Skeleton({
    className,
    ...props
}: React.HTMLAttributes<HTMLDivElement>) {
    return (
        <div
            className={cn(
                'animate-shimmer rounded-md bg-muted/50 bg-[linear-gradient(110deg,transparent,25%,hsl(var(--foreground)/0.05),50%,transparent,75%)] bg-[length:200%_100%]',
                className
            )}
            {...props}
        />
    )
}

export { Skeleton }
