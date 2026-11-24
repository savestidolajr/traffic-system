import { Loader2 } from 'lucide-react'
import {
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
} from '@/components/ui/alert-dialog'

interface LiveEditConfirmationDialogProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** The entity's name, shown in the body ("X is assigned to …"). */
    readonly entityName: string
    /** The lowercase noun for the title: "Change live assigned <noun>?" */
    readonly noun: string
    readonly stations: readonly string[]
    readonly nextVersion: number
    readonly changeNote: string
    readonly isSaving: boolean
    readonly onConfirm: () => void
    /** Domain-specific line on what this edit can and cannot affect. */
    readonly guidance: string
}

/**
 * Shown before saving a NEW version of a library entity that is assigned to
 * stations. Saving takes effect on the next bulletin for those stations, so
 * the operator confirms the on-air impact explicitly. Warning styling, not
 * destructive. Shared by the voices, delivery, bookends, and pronunciation
 * studios.
 */
export function LiveEditConfirmationDialog({
    open,
    onOpenChange,
    entityName,
    noun,
    stations,
    nextVersion,
    changeNote,
    isSaving,
    onConfirm,
    guidance,
}: LiveEditConfirmationDialogProps) {
    const stationList = stations.join(', ')
    return (
        <AlertDialog open={open} onOpenChange={onOpenChange}>
            <AlertDialogContent>
                <AlertDialogHeader>
                    <AlertDialogTitle>
                        Change live assigned {noun}?
                    </AlertDialogTitle>
                    <AlertDialogDescription asChild>
                        <div className="flex flex-col gap-2">
                            <span>
                                {entityName} is assigned to {stationList}.
                                Saving creates v{nextVersion}, live from the
                                next bulletin.
                            </span>
                            <span>{guidance}</span>
                            {changeNote && (
                                <span className="rounded-md border border-border/60 bg-muted/30 px-3 py-2 text-foreground">
                                    {changeNote}
                                </span>
                            )}
                        </div>
                    </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                    <AlertDialogCancel disabled={isSaving}>
                        Go back
                    </AlertDialogCancel>
                    <AlertDialogAction
                        onClick={(event) => {
                            event.preventDefault()
                            onConfirm()
                        }}
                        disabled={isSaving}
                        className="bg-warning text-warning-foreground hover:bg-warning/90"
                    >
                        {isSaving && (
                            <Loader2 className="size-4 animate-spin" />
                        )}
                        Save live version
                    </AlertDialogAction>
                </AlertDialogFooter>
            </AlertDialogContent>
        </AlertDialog>
    )
}
