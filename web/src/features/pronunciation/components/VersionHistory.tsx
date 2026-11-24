import { useState } from 'react'
import { ChevronRight } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { formatDateTime } from '@/lib/formatting'
import { diffEntities } from '@/lib/entity-diff'
import { EntityDiffView } from '@/components/EntityDiffView'
import type { DictionaryVersion } from '../types'

interface VersionHistoryProps {
    readonly versions: readonly DictionaryVersion[]
    readonly currentVersion: number
    readonly onRestore: (version: DictionaryVersion) => void
}

/** Collapsed-by-default audit trail. Restore copies old rules into a new version. */
export function VersionHistory({
    versions,
    currentVersion,
    onRestore,
}: VersionHistoryProps) {
    const [open, setOpen] = useState(false)
    const ordered = [...versions].sort((a, b) => b.version - a.version)
    const byVersion = new Map(versions.map((v) => [v.version, v]))

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
                    Previous versions, authors, dates, rule counts, and change
                    notes.
                </p>
            )}
            {open && (
                <ol className="flex flex-col gap-2">
                    {ordered.map((version) => {
                        const isCurrent = version.version === currentVersion
                        const ruleCount = version.rules.length
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
                                        <span className="text-xs text-muted-foreground">
                                            {ruleCount} rule
                                            {ruleCount === 1 ? '' : 's'}
                                        </span>
                                    </div>
                                    <span className="text-xs text-muted-foreground">
                                        {formatDateTime(version.createdAt)}
                                        {version.createdBy
                                            ? ` · ${version.createdBy}`
                                            : ''}
                                    </span>
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
                                                    { Rules: previous.rules },
                                                    { Rules: version.rules }
                                                )}
                                            />
                                        </div>
                                    )}
                                </div>
                                {!isCurrent && (
                                    <Button
                                        size="sm"
                                        variant="outline"
                                        onClick={() => onRestore(version)}
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
