import * as React from 'react'
import { cn } from '@/lib/utils'

function Table({ className, ref, ...props }: React.ComponentProps<'table'>) {
    return (
        // `overflow-x-auto`, NOT `overflow-auto`: this wrapper exists only so a table wider
        // than its container can scroll sideways — its height is always auto, so it never
        // scrolls vertically. `index.css` gives `.overflow-auto` a stable scrollbar gutter to
        // stop vertically-scrolling regions reflowing, and that reserved ~15px was dead space
        // on the right of every table in the app: row borders and the header band stopped
        // short of the container's right border, which read as the table being cut off.
        // `overflow-x-auto` does not match that rule, so the gutter is reclaimed and a
        // scrollbar still appears if one is genuinely needed.
        <div className="relative w-full overflow-x-auto">
            <table
                ref={ref}
                className={cn('w-full caption-bottom text-sm', className)}
                {...props}
            />
        </div>
    )
}

function TableHeader({
    className,
    ref,
    ...props
}: React.ComponentProps<'thead'>) {
    return (
        <thead
            ref={ref}
            className={cn(
                // A tinted band separates the controls row from the data, which matters more
                // now that every header cell is a sort button. The row itself is not
                // interactive, so it must not pick up `TableRow`'s hover tint — only the
                // individual sort buttons inside it highlight.
                'bg-muted/30 [&_tr]:border-b [&_tr]:hover:bg-transparent',
                className
            )}
            {...props}
        />
    )
}

function TableBody({
    className,
    ref,
    ...props
}: React.ComponentProps<'tbody'>) {
    return (
        <tbody
            ref={ref}
            className={cn('[&_tr:last-child]:border-0', className)}
            {...props}
        />
    )
}

function TableFooter({
    className,
    ref,
    ...props
}: React.ComponentProps<'tfoot'>) {
    return (
        <tfoot
            ref={ref}
            className={cn(
                'border-t bg-muted/50 font-medium [&>tr]:last:border-b-0',
                className
            )}
            {...props}
        />
    )
}

function TableRow({ className, ref, ...props }: React.ComponentProps<'tr'>) {
    return (
        <tr
            ref={ref}
            className={cn(
                'border-b transition-colors hover:bg-muted/50 data-[state=selected]:bg-muted',
                className
            )}
            {...props}
        />
    )
}

function TableHead({ className, ref, ...props }: React.ComponentProps<'th'>) {
    return (
        <th
            ref={ref}
            className={cn(
                // `text-xs` matches the sort button inside `DataTableColumnHeader`, so a table
                // that mixes sortable and static columns keeps one header type size.
                'h-11 whitespace-nowrap px-4 text-left align-middle text-xs font-medium text-muted-foreground [&:has([role=checkbox])]:pr-0',
                className
            )}
            {...props}
        />
    )
}

function TableCell({ className, ref, ...props }: React.ComponentProps<'td'>) {
    return (
        <td
            ref={ref}
            className={cn(
                // 12px + 20px line-height + 12px = a 44px row: the density this view exists
                // for, still on the coarse-pointer target floor since whole rows are clickable.
                'px-4 py-3 align-middle [&:has([role=checkbox])]:pr-0',
                className
            )}
            {...props}
        />
    )
}

function TableCaption({
    className,
    ref,
    ...props
}: React.ComponentProps<'caption'>) {
    return (
        <caption
            ref={ref}
            className={cn('mt-4 text-sm text-muted-foreground', className)}
            {...props}
        />
    )
}

export {
    Table,
    TableHeader,
    TableBody,
    TableFooter,
    TableHead,
    TableRow,
    TableCell,
    TableCaption,
}
