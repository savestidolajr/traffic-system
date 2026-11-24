import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

const badgeVariants = cva(
    'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2',
    {
        variants: {
            variant: {
                default:
                    'border-transparent bg-primary text-primary-foreground hover:bg-primary/80',
                secondary:
                    'border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80',
                destructive:
                    'border-transparent bg-destructive text-destructive-foreground hover:bg-destructive/80',
                rejected:
                    'bg-destructive/12 text-destructive-on-tint border-destructive/35',
                outline: 'text-foreground',
                success: 'bg-success/10 text-success border-success/20',
                warning: 'bg-warning/10 text-warning border-warning/20',
                muted: 'border-transparent bg-muted text-muted-foreground',
                approved: 'bg-success/10 text-success border-success/20',
                requested:
                    'bg-status-requested/15 text-status-requested-on-tint border-status-requested/30',
                hereFetched:
                    'bg-status-hereFetched/15 text-status-hereFetched border-status-hereFetched/30',
                incidents:
                    'bg-status-incidents/15 text-status-incidents-on-tint border-status-incidents/30',
                scriptReady:
                    'bg-status-scriptReady/15 text-status-scriptReady-on-tint border-status-scriptReady/30',
                audioReady:
                    'bg-status-audioReady/15 text-status-audioReady-on-tint border-status-audioReady/30',
                synthesizingAudio:
                    'bg-status-audioReady/10 text-status-audioReady-on-tint border-status-audioReady/20',
                fallback:
                    'bg-status-fallback/15 text-status-fallback border-status-fallback/30',
                info: 'bg-info/10 text-info border-info/20',
                broadcasting:
                    'bg-success/10 text-success border-success/20 animate-pulse',
                scheduled:
                    'bg-primary/10 text-primary-on-tint border-primary/20',
            },
        },
        defaultVariants: {
            variant: 'default',
        },
    }
)

export interface BadgeProps
    extends
        React.HTMLAttributes<HTMLDivElement>,
        VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
    return (
        <div className={cn(badgeVariants({ variant }), className)} {...props} />
    )
}

export { Badge }
