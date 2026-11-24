import { useState } from 'react'
import { ChevronRight } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { toastAction } from '@/lib/toast-action'
import { diffEntities } from '@/lib/entity-diff'
import { EntityDiffView } from '@/components/EntityDiffView'
import { useRestoreBookendVersion } from '../api/bookend-hooks'
import type { Bookend } from '../types'

interface BookendVersionHistoryProps {
    readonly bookend: Bookend
}

/**
 * Collapsed-by-default audit trail. Restore is one click (the Voices pattern):
 * it mints a new current version server-side — no editor round trip — so the
 * studio never leaves the history step while restoring.
 */
export function BookendVersionHistory({ bookend }: BookendVersionHistoryProps) {
    const [open, setOpen] = useState(false)
    const restore = useRestoreBookendVersion()
    const ordered = [...bookend.versions].sort((a, b) => b.version - a.version)
    const byVersion = new Map(bookend.versions.map((v) => [v.version, v]))

    const handleRestore = async (version: number) => {
        await toastAction(
            () =>
                restore.mutateAsync({
                    id: bookend.id,
                    version,
                }),
            `Restored v${version} as the current text`,
            'Could not restore that version'
        )
        // Query invalidation refreshes the gallery and this history list.
    }

    return (
        <section className="flex flex-col gap-2">
            <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                aria-expanded={open}
                className="flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
                {open ? (
                    <ICON.expand className="size-4" />
                ) : (
                    <ChevronRight className="size-4" />
                )}
                Version history
            </button>
            {!open && (
                <p className="pl-5 text-xs text-muted-foreground">
                    Previous versions, authors, dates, and change notes.
                </p>
            )}
            {open && (
                <ol className="flex flex-col gap-2">
                    {ordered.map((version) => {
                        const isCurrent =
                            version.version === bookend.currentVersion
                        const previous = byVersion.get(version.version - 1)
                        return (
                            <li
                                key={version.version}
                                className="flex items-start justify-between gap-3 rounded-lg border border-border/60 p-3"
                            >
                                <div className="flex min-w-0 flex-col gap-0.5">
                                    <div className="flex items-center gap-2">
                                        <span className="text-sm font-medium">
                                            v{version.version}
                                        </span>
                                        {isCurrent && (
                                            <Badge variant="success">
                                                Current
                                            </Badge>
                                        )}
                                    </div>
                                    <span className="text-xs text-muted-foreground">
                                        {new Date(
                                            version.createdAt
                                        ).toLocaleString()}
                                        {version.createdBy
                                            ? ` · ${version.createdBy}`
                                            : ''}
                                    </span>
                                    {version.introText && (
                                        <span className="text-xs text-muted-foreground">
                                            <span className="font-medium text-muted-foreground">
                                                Intro:
                                            </span>{' '}
                                            {version.introText}
                                        </span>
                                    )}
                                    {version.outroText && (
                                        <span className="text-xs text-muted-foreground">
                                            <span className="font-medium text-muted-foreground">
                                                Outro:
                                            </span>{' '}
                                            {version.outroText}
                                        </span>
                                    )}
                                    {version.changeNote && (
                                        <span className="text-xs text-muted-foreground">
                                            {version.changeNote}
                                        </span>
                                    )}
                                    {previous && (
                                        <div className="mt-1.5 rounded-md border border-border/40 bg-muted/20 px-2 py-1.5">
                                            <p className="mb-1 text-2xs uppercase tracking-wide text-muted-foreground">
                                                Changes vs v{previous.version}
                                            </p>
                                            <EntityDiffView
                                                entries={diffEntities(
                                                    {
                                                        introText:
                                                            previous.introText,
                                                        outroText:
                                                            previous.outroText,
                                                    },
                                                    {
                                                        introText:
                                                            version.introText,
                                                        outroText:
                                                            version.outroText,
                                                    }
                                                )}
                                            />
                                        </div>
                                    )}
                                </div>
                                {!isCurrent && (
                                    <Button
                                        type="button"
                                        size="sm"
                                        variant="outline"
                                        onClick={() =>
                                            void handleRestore(version.version)
                                        }
                                        disabled={restore.isPending}
                                    >
                                        <ICON.reset className="size-3.5" />
                                        Restore as new version
                                    </Button>
                                )}
                            </li>
                        )
                    })}
                </ol>
            )}
        </section>
    )
}
