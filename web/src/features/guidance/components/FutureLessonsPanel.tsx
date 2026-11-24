import { Link } from 'react-router'
import { toast } from 'sonner'
import { EmptyState } from '@/components/EmptyState'
import { InfoHint } from '@/components/InfoHint'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { ICON } from '@/lib/design/icons'
import { formatDateTime, truncateId } from '@/lib/formatting'
import { ROUTE_JOBS } from '@/lib/routes'
import {
    useDismissGuidanceCandidate,
    useGuidanceCandidates,
} from '../api/guidance-hooks'
import type { GuidanceCandidate } from '../api/schemas'

const LessonIcon = ICON.feedback
const PromoteIcon = ICON.send
const MoreIcon = ICON.more
const GRID =
    'grid grid-cols-[7rem_8rem_minmax(0,1fr)_4rem_8rem_5rem_2rem] items-center gap-x-3'

interface FutureLessonsPanelProps {
    readonly marketId: string | null
    readonly label: string | null
    readonly canPromote: boolean
    /** Loads an explicit future lesson into the standing-guidance composer. */
    readonly onPromote: (jobId: string, text: string) => void
}

/** Governance queue for station lessons — operator-taught or learned from published edits. */
export function FutureLessonsPanel({
    marketId,
    label,
    canPromote,
    onPromote,
}: FutureLessonsPanelProps) {
    const candidates = useGuidanceCandidates(marketId)
    const dismiss = useDismissGuidanceCandidate()

    if (!marketId) {
        return (
            <EmptyState>Choose a station above to see its lessons.</EmptyState>
        )
    }

    const lessons = candidates.data ?? []
    const handleDismiss = async (lesson: GuidanceCandidate) => {
        try {
            await dismiss.mutateAsync({
                station: lesson.station,
                id: lesson.id,
            })
            toast.success('Future lesson dismissed')
        } catch (err: unknown) {
            toast.error('Failed to dismiss future lesson', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'The lesson remains active. Try again.',
            })
        }
    }

    return (
        <SpotlightCard className="p-4">
            <div className="flex flex-col gap-3">
                <div className="flex flex-col gap-1">
                    <span className="flex items-center gap-1.5 text-sm font-semibold">
                        <LessonIcon className="size-4 text-muted-foreground" />
                        Future lessons: {label}
                        <InfoHint label="What are future lessons?">
                            Station wording preferences, learned automatically
                            from script edits that operators re-voice and
                            publish. Candidates activate after three
                            independently published edits agree; active lessons
                            reach the writer until they expire, are dismissed,
                            or are promoted to standing guidance.
                        </InfoHint>
                    </span>
                    <p className="text-xs text-muted-foreground">
                        Review what the writer is currently learning for each
                        station in this market.
                    </p>
                </div>

                {candidates.isLoading ? (
                    <Skeleton className="h-24 w-full" />
                ) : lessons.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                        No stored lessons for this market yet.
                    </p>
                ) : (
                    <ol className="flex flex-col divide-y divide-border rounded-lg border border-border">
                        <li
                            className={`${GRID} px-3 py-2 text-2xs uppercase tracking-wide text-muted-foreground`}
                        >
                            <span>Station</span>
                            <span>Category</span>
                            <span>Instruction</span>
                            <span className="text-right">Seen</span>
                            <span className="text-right">Expires</span>
                            <span className="text-right">Job</span>
                            <span aria-hidden />
                        </li>
                        {lessons.map((lesson) => (
                            <LessonRow
                                key={lesson.id}
                                lesson={lesson}
                                canPromote={canPromote}
                                onPromote={onPromote}
                                onDismiss={handleDismiss}
                                dismissing={
                                    dismiss.isPending &&
                                    dismiss.variables?.id === lesson.id
                                }
                            />
                        ))}
                    </ol>
                )}
            </div>
        </SpotlightCard>
    )
}

interface LessonRowProps {
    readonly lesson: GuidanceCandidate
    readonly canPromote: boolean
    readonly onPromote: (jobId: string, text: string) => void
    readonly onDismiss: (lesson: GuidanceCandidate) => Promise<void>
    readonly dismissing: boolean
}

function LessonRow({
    lesson,
    canPromote,
    onPromote,
    onDismiss,
    dismissing,
}: LessonRowProps) {
    return (
        <li
            className={`${GRID} px-3 py-2.5 transition-colors hover:bg-accent/40`}
        >
            <span
                className="truncate text-xs font-medium"
                title={lesson.station}
            >
                {lesson.station}
            </span>
            <span className="min-w-0">
                <Badge
                    variant="secondary"
                    className="inline-block max-w-full truncate align-middle text-2xs font-normal"
                >
                    {lesson.category}
                </Badge>
                <Badge
                    variant={
                        lesson.status === 'Candidate' ? 'outline' : 'secondary'
                    }
                    className={`ml-1 inline-block align-middle text-2xs font-normal ${
                        lesson.status === 'Candidate'
                            ? 'text-muted-foreground'
                            : 'text-accent-foreground'
                    }`}
                >
                    {lesson.status === 'Candidate' ? 'Candidate' : 'Active'}
                </Badge>
                {lesson.promoted && (
                    <Badge
                        variant="outline"
                        className="ml-1 inline-block align-middle text-2xs font-normal"
                    >
                        Promoted
                    </Badge>
                )}
            </span>
            <span className="flex min-w-0 flex-col gap-0.5">
                <span className="text-sm">{lesson.text}</span>
                {lesson.origin === 'Inferred' && (
                    <span className="text-2xs text-muted-foreground">
                        Learned from a published operator edit
                    </span>
                )}
                {lesson.conflictWithLessonId && (
                    <Badge
                        variant="outline"
                        className="w-fit text-2xs font-normal text-warning"
                    >
                        Conflicts with an active lesson
                    </Badge>
                )}
            </span>
            <span className="text-right text-2xs tabular-nums text-muted-foreground">
                ×{lesson.occurrenceCount}
            </span>
            <span className="text-right text-2xs tabular-nums text-muted-foreground">
                {formatDateTime(lesson.expiresAtUtc)}
            </span>
            <span className="truncate text-right text-2xs">
                <Link
                    to={`${ROUTE_JOBS}/${lesson.jobId}`}
                    className="text-muted-foreground underline underline-offset-2 hover:text-foreground"
                >
                    {truncateId(lesson.jobId, 4, 5)}
                </Link>
            </span>
            {canPromote ? (
                <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                        <Button
                            variant="ghost"
                            size="icon"
                            className="size-6"
                            aria-label={`Actions for ${lesson.station} future lesson`}
                            disabled={dismissing}
                        >
                            <MoreIcon />
                        </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                        <DropdownMenuItem
                            onClick={() => onPromote(lesson.jobId, lesson.text)}
                            disabled={lesson.promoted}
                        >
                            <PromoteIcon />
                            {lesson.promoted
                                ? 'Already promoted'
                                : 'Promote to standing guidance'}
                        </DropdownMenuItem>
                        <DropdownMenuItem
                            className="text-destructive focus:text-destructive"
                            onClick={() => void onDismiss(lesson)}
                        >
                            Dismiss lesson
                        </DropdownMenuItem>
                    </DropdownMenuContent>
                </DropdownMenu>
            ) : (
                <span aria-hidden />
            )}
        </li>
    )
}
