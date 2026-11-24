import { useEffect, useMemo } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { AlertTriangle, Loader2 } from 'lucide-react'
import { InfoHint } from '@/components/InfoHint'
import {
    useBookendPreview,
    useBookends,
} from '@/features/bookends/api/bookend-hooks'
import { useIsAdmin } from '@/hooks/use-current-user'
import {
    formatBookendBlocker,
    useBookendTokenBlockers,
} from '../lib/bookend-token-gate'
import type { ScheduleFormData } from '../types'

interface BookendResolvedPreviewProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/** Quiet period after the last edit before resolving — typing a token override
 *  must not fire a request per keystroke. */
const PREVIEW_DEBOUNCE_MS = 400

interface PreviewResult {
    resolvedIntro: string
    resolvedOutro: string
    unresolvedTokens: string[]
    values: Record<string, string>
}

function ResolvedBookendLine({
    title,
    resolvedText,
    isPending,
    emptyMessage,
}: {
    title: string
    resolvedText: string | null
    isPending: boolean
    emptyMessage: string
}) {
    return (
        <div className="rounded-lg border border-border/60 bg-muted/20 p-3">
            <p className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
                {title}
                {isPending && (
                    <Loader2
                        className="ml-auto size-3 animate-spin"
                        aria-label="Resolving"
                    />
                )}
            </p>
            {resolvedText ? (
                <p className="mt-1.5 rounded-md bg-card px-2.5 py-2 text-sm">
                    &ldquo;{resolvedText}&rdquo;
                </p>
            ) : (
                <p className="mt-1 text-xs text-muted-foreground-subtle">
                    {emptyMessage}
                </p>
            )}
        </div>
    )
}

/**
 * Live, debounced resolution of the selected bookend pair against THIS
 * station's market id, station name and token overrides. The backend resolves
 * the templates itself, so what renders here is exactly what the pipeline
 * would substitute — the station's only guard against a derived default
 * airing wrong (market id "goldcoast" derives `{city}` to "Goldcoast", not
 * "Gold Coast").
 *
 * POST /bookends/preview is Admin in RouteRoleMatrix (it carries no market
 * guard) and this step is operator-reachable, so the server-resolved lines
 * are withheld for Operators. The unresolved-token warnings below are computed
 * CLIENT-side from the shared library and the form (see
 * bookend-token-gate.ts) and render for everyone: the backend REFUSES the
 * save when a referenced bookend has an unfillable token, and the operator
 * who owns the schedule PUT is the one who would otherwise discover that from
 * the 400.
 */
export function BookendResolvedPreview({ form }: BookendResolvedPreviewProps) {
    const isAdmin = useIsAdmin()
    const bookendTokenBlockers = useBookendTokenBlockers(form)
    const bookendId = useWatch({
        control: form.control,
        name: 'bookendId',
    })
    const marketId = useWatch({ control: form.control, name: 'marketId' })
    const station = useWatch({ control: form.control, name: 'station' })
    const tokenRows = useWatch({
        control: form.control,
        name: 'bookendTokens',
    })

    const { data: bookendsData } = useBookends()
    const bookend = bookendsData?.bookends.find((b) => b.id === bookendId)

    const preview = useBookendPreview()

    const tokensRecord = useMemo(() => {
        const record: Record<string, string> = {}
        for (const row of tokenRows ?? []) {
            const key = row.key.trim()
            const value = row.value.trim()
            if (key && value) record[key] = value
        }
        return record
    }, [tokenRows])

    const introText = bookend?.introText?.trim() || null
    const outroText = bookend?.outroText?.trim() || null

    // Debounced resolve: fire only after the operator stops typing/selecting,
    // and only when a pair is actually selected. Destructured up front so the
    // effect closes over the stable `mutateAsync` rather than the whole
    // (re-created each render) mutation object — otherwise the timer restarts
    // on every render and the preview never settles.
    const resolve = preview.mutateAsync

    useEffect(() => {
        if (!isAdmin || (!introText && !outroText)) return
        const timer = setTimeout(() => {
            const market = (marketId ?? '').trim()
            resolve({
                introText: introText || undefined,
                outroText: outroText || undefined,
                marketId: market || undefined,
                station: (station ?? '').trim() || undefined,
                tokens:
                    Object.keys(tokensRecord).length > 0
                        ? tokensRecord
                        : undefined,
            }).catch(() => {
                // Failure is surfaced through the mutation's error state.
            })
        }, PREVIEW_DEBOUNCE_MS)
        return () => clearTimeout(timer)
    }, [
        isAdmin,
        introText,
        outroText,
        marketId,
        station,
        tokensRecord,
        resolve,
    ])

    const result: PreviewResult | undefined = preview.data
    const noPairMessage = (title: string) =>
        `No bookend set. The bulletin ${title === 'Intro' ? 'opens on the first read' : 'ends after the last read'}.`

    return (
        <div className="space-y-2">
            {isAdmin && (
                <>
                    <p className="flex items-center gap-1.5 text-sm font-medium leading-none">
                        What the station will hear
                        <InfoHint label="What does this preview show?">
                            The resolved intro and outro for this station,
                            exactly as the pipeline would read them. Fix any
                            wrong derived value with a token override above.
                        </InfoHint>
                    </p>
                    <div className="grid gap-2 sm:grid-cols-2">
                        <ResolvedBookendLine
                            title="Intro"
                            resolvedText={
                                result && introText
                                    ? result.resolvedIntro
                                    : null
                            }
                            isPending={preview.isPending}
                            emptyMessage={
                                bookend
                                    ? 'No intro in this bookend. The bulletin opens on the first read.'
                                    : noPairMessage('Intro')
                            }
                        />
                        <ResolvedBookendLine
                            title="Outro"
                            resolvedText={
                                result && outroText
                                    ? result.resolvedOutro
                                    : null
                            }
                            isPending={preview.isPending}
                            emptyMessage={
                                bookend
                                    ? 'No outro in this bookend. The bulletin ends after the last read.'
                                    : noPairMessage('Outro')
                            }
                        />
                    </div>
                    {/* Token values belong to the PAIR, not to either line — the
                        backend resolves both against one value map. Rendering them
                        once below both lines avoids mis-attributing them to one end. */}
                    {result && Object.keys(result.values).length > 0 && (
                        <div className="flex flex-wrap gap-1">
                            {Object.entries(result.values).map(
                                ([key, value]) => (
                                    <span
                                        key={key}
                                        className="rounded-full border bg-card px-1.5 py-0.5 font-mono text-2xs text-muted-foreground"
                                    >
                                        {key} → {value}
                                    </span>
                                )
                            )}
                        </div>
                    )}
                    {preview.error && (
                        <p className="text-xs text-destructive">
                            Preview failed: {preview.error.message}
                        </p>
                    )}
                </>
            )}
            {/* The unfillable-token warnings come from the CLIENT mirror (bookend-token-gate)
                so they cover EVERY referenced bookend — the primary pair AND rotation members —
                and render for operators too. The backend refuses the save outright (it is not
                dropped on air), so the copy says exactly that. */}
            {bookendTokenBlockers.map((blocker) => (
                <p
                    key={blocker.bookendId}
                    className="flex items-start gap-1.5 text-xs text-destructive"
                >
                    <AlertTriangle
                        className="mt-0.5 size-3 shrink-0"
                        aria-hidden
                    />
                    <span>{formatBookendBlocker(blocker)}</span>
                </p>
            ))}
        </div>
    )
}
