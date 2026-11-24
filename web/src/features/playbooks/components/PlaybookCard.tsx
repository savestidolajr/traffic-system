import { BookOpen, Radio } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { formatRelativeTime } from '@/lib/formatting'
import {
    PLAYBOOK_SECTION_LABELS,
    PLAYBOOK_SECTIONS,
    type BulletinPlaybook,
} from '../types'

interface PlaybookCardProps {
    readonly playbook: BulletinPlaybook
    readonly onView: (id: string) => void
    readonly onEdit: (id: string) => void
    readonly onDuplicate: (playbook: BulletinPlaybook) => void
    readonly onDelete: (playbook: BulletinPlaybook) => void
    readonly onArchive: (playbook: BulletinPlaybook) => void
    readonly onUnarchive: (playbook: BulletinPlaybook) => void
    readonly canDelete?: boolean
}

/** A compact library card that makes the diff-oriented nature of a playbook visible. */
export function PlaybookCard({
    playbook,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: PlaybookCardProps) {
    const isArchived = playbook.status === 'archived'
    const blockCount = playbook.currentOverlay.add.length
    const changedCount =
        Object.keys(playbook.currentOverlay.rewrite).length +
        playbook.currentOverlay.disable.length +
        blockCount
    const assignmentLabel =
        playbook.assignedStations.length === 0
            ? 'Unassigned'
            : `${playbook.assignedStations.length} station${playbook.assignedStations.length === 1 ? '' : 's'}`

    return (
        <div
            onClick={() => onView(playbook.id)}
            className="h-full cursor-pointer"
        >
            <SpotlightCard className="flex h-full flex-col shadow-inner-light">
                <div className="flex flex-col gap-4 p-5">
                    <div className="flex items-start justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-primary/10 text-primary-on-tint">
                                <BookOpen className="size-5" aria-hidden />
                            </div>
                            <div className="min-w-0">
                                <h2 className="flex items-center gap-2 truncate text-base font-semibold leading-tight">
                                    <span className="truncate">
                                        {playbook.name}
                                    </span>
                                    {isArchived && (
                                        <Badge variant="muted">Archived</Badge>
                                    )}
                                </h2>
                                <p className="truncate text-xs text-muted-foreground">
                                    {playbook.role} · v{playbook.currentVersion}
                                </p>
                            </div>
                        </div>
                        <EntityActionsMenu
                            onView={() => onView(playbook.id)}
                            onEdit={() => onEdit(playbook.id)}
                            onDuplicate={() => onDuplicate(playbook)}
                            onDelete={
                                canDelete ? () => onDelete(playbook) : undefined
                            }
                            onArchive={
                                isArchived
                                    ? undefined
                                    : () => onArchive(playbook)
                            }
                            onUnarchive={
                                isArchived
                                    ? () => onUnarchive(playbook)
                                    : undefined
                            }
                            ariaLabel="Playbook actions"
                            className="shrink-0"
                        />
                    </div>

                    <div className="rounded-lg border border-border/60 bg-background/40 px-4 py-3">
                        <p className="text-sm leading-snug text-foreground">
                            A station diff over the shipped {playbook.role}{' '}
                            instructions
                        </p>
                        <div className="mt-3 flex flex-wrap gap-1.5">
                            {PLAYBOOK_SECTIONS.map((section) => (
                                <span
                                    key={section}
                                    className="rounded-full bg-muted px-2 py-px text-2xs font-medium text-muted-foreground"
                                >
                                    {PLAYBOOK_SECTION_LABELS[section]}
                                </span>
                            ))}
                        </div>
                    </div>

                    <div className="flex items-center justify-between gap-3 border-t border-border/40 pt-3 text-2xs text-muted-foreground">
                        <span className="inline-flex items-center gap-1">
                            <Radio className="size-3" aria-hidden />
                            {assignmentLabel}
                        </span>
                        <span>
                            {changedCount} change{changedCount === 1 ? '' : 's'}{' '}
                            · {blockCount} added
                        </span>
                    </div>
                    <p className="text-2xs text-muted-foreground">
                        Updated {formatRelativeTime(playbook.updatedAt)}
                    </p>
                </div>
            </SpotlightCard>
        </div>
    )
}
