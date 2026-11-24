import * as React from 'react'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'

import { cn } from '@/lib/utils'

/** Visual styling shared by every dialog's close button — size, colors, hover/focus states. */
export const DIALOG_CLOSE_BUTTON_BASE_CLASS =
    'flex size-9 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none data-[state=open]:bg-muted'
/** Floating variant for DialogContent's plain header-less layout — pinned to the corner. */
export const DIALOG_CLOSE_BUTTON_CLASS = `absolute right-3 top-3 z-10 ${DIALOG_CLOSE_BUTTON_BASE_CLASS}`

const Dialog = DialogPrimitive.Root

const DialogTrigger = DialogPrimitive.Trigger

const DialogPortal = DialogPrimitive.Portal

const DialogClose = DialogPrimitive.Close

function DialogOverlay({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof DialogPrimitive.Overlay>) {
    return (
        <DialogPrimitive.Overlay
            ref={ref}
            className={cn(
                'fixed inset-0 z-50 bg-overlay/80 backdrop-blur-sm data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
                className
            )}
            {...props}
        />
    )
}

function DialogContent({
    className,
    children,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof DialogPrimitive.Content>) {
    return (
        <DialogPortal>
            <DialogOverlay />
            <DialogPrimitive.Content
                ref={ref}
                className={cn(
                    'fixed left-[50%] top-[50%] z-50 grid w-full max-w-lg translate-x-[-50%] translate-y-[-50%] gap-4 border bg-background p-6 shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] sm:rounded-xl',
                    className
                )}
                {...props}
            >
                {children}
                <DialogPrimitive.Close
                    className={DIALOG_CLOSE_BUTTON_CLASS}
                    aria-label="Close"
                >
                    <X className="size-4" />
                </DialogPrimitive.Close>
            </DialogPrimitive.Content>
        </DialogPortal>
    )
}

const DialogHeader = ({
    className,
    ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
    <div
        className={cn(
            'flex flex-col space-y-1.5 text-center sm:text-left',
            className
        )}
        {...props}
    />
)
DialogHeader.displayName = 'DialogHeader'

const DialogFooter = ({
    className,
    ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
    <div
        className={cn(
            'flex flex-col-reverse gap-2 sm:flex-row sm:flex-wrap sm:justify-end',
            className
        )}
        {...props}
    />
)
DialogFooter.displayName = 'DialogFooter'

function DialogTitle({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof DialogPrimitive.Title>) {
    return (
        <DialogPrimitive.Title
            ref={ref}
            className={cn(
                'text-lg font-semibold leading-none tracking-tight',
                className
            )}
            {...props}
        />
    )
}

function DialogDescription({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof DialogPrimitive.Description>) {
    return (
        <DialogPrimitive.Description
            ref={ref}
            className={cn('text-sm text-muted-foreground', className)}
            {...props}
        />
    )
}

export {
    Dialog,
    DialogPortal,
    DialogOverlay,
    DialogClose,
    DialogTrigger,
    DialogContent,
    DialogHeader,
    DialogFooter,
    DialogTitle,
    DialogDescription,
}
