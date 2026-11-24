import { useWatch, type UseFormReturn } from 'react-hook-form'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { InfoHint } from '@/components/InfoHint'
import { cn } from '@/lib/utils'
import type { ScheduleFormData } from '../types'

interface BookendTokensEditorProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/** The tokens resolved automatically from schedule data (mirrors the backend's
 *  `BookendTokenResolver.BuiltInTokens`). */
const BUILT_IN_TOKENS = ['city', 'market', 'station'] as const

/** Best-effort mirror of the backend's DeriveCity: drop any call-sign suffix
 *  ("cairns-4ca" → "cairns") and title-case the remainder. Deliberately simple
 *  — a market id is a slug, not a place name, so anything multi-word
 *  ("goldcoast") needs an override. The live preview below is the guard; these
 *  defaults just show what the backend derives for free. */
function deriveCity(marketId: string): string {
    const head = marketId.trim().split('-')[0] ?? ''
    if (!head) return ''
    return head.charAt(0).toUpperCase() + head.slice(1).toLowerCase()
}

/**
 * Per-station token overrides for intro/outro templates. The three built-ins
 * (city, market, station) are derived from the schedule and shown greyed so the
 * operator sees what they get for free — they only type rows for what differs.
 * An override row with a built-in key replaces the derived value (e.g.
 * `city` → "Gold Coast"), which is exactly the correction the live preview
 * exists to surface.
 */
export function BookendTokensEditor({ form }: BookendTokensEditorProps) {
    const [marketId, station, rows] = useWatch({
        control: form.control,
        name: ['marketId', 'station', 'bookendTokens'],
    })
    const builtIn: Record<string, string> = {
        city: deriveCity(marketId ?? ''),
        market: (marketId ?? '').trim(),
        station: (station ?? '').trim(),
    }
    const tokens = rows ?? []
    // RHF types array-field errors as a single keyed object even though it
    // stores them positionally at runtime, so index access needs this cast.
    const rowErrors =
        (form.formState.errors.bookendTokens as unknown as
            | Array<{
                  key?: { message?: string }
                  value?: { message?: string }
              }>
            | undefined) ?? []

    const setRows = (next: Array<{ key: string; value: string }>) =>
        form.setValue('bookendTokens', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    const addRow = () => setRows([...tokens, { key: '', value: '' }])
    const updateRow = (
        index: number,
        patch: Partial<{ key: string; value: string }>
    ) =>
        setRows(
            tokens.map((row, i) => (i === index ? { ...row, ...patch } : row))
        )
    const removeRow = (index: number) =>
        setRows(tokens.filter((_, i) => i !== index))

    return (
        <div className="space-y-3 rounded-lg border bg-muted/20 p-4">
            <div>
                <p className="flex items-center gap-1.5 text-sm font-medium leading-none">
                    Token overrides
                    <InfoHint label="How do token overrides work?">
                        Words substituted into{' '}
                        <code className="text-foreground/80">{'{token}'}</code>{' '}
                        placeholders in the bookend&rsquo;s intro and outro
                        lines. The built-ins below are derived from this
                        station; add a row to override one (for example,{' '}
                        <code className="text-foreground/80">city</code> →
                        &ldquo;Gold Coast&rdquo;).
                    </InfoHint>
                </p>
            </div>

            <div className="space-y-1.5">
                {BUILT_IN_TOKENS.map((key) => {
                    // An override row for this token wins at resolve time, so the derived value
                    // below is dead. Saying so is the difference between "my override took" and
                    // "why does it still say Goldcoast?" — the row is otherwise identical.
                    const overriddenBy = tokens.find(
                        (row) =>
                            row.key.trim().toLowerCase() ===
                                key.toLowerCase() && row.value.trim().length > 0
                    )
                    return (
                        <div
                            key={key}
                            className="flex items-center gap-2 rounded-md border border-dashed bg-background/60 px-3 py-1.5 opacity-70"
                        >
                            <ICON.revise
                                className="size-3.5 shrink-0 text-muted-foreground-subtle"
                                aria-hidden
                            />
                            <code className="w-32 shrink-0 text-xs text-muted-foreground">
                                {'{'}
                                {key}
                                {'}'}
                            </code>
                            <span
                                className={cn(
                                    'min-w-0 flex-1 truncate text-xs text-muted-foreground',
                                    overriddenBy && 'line-through'
                                )}
                            >
                                {builtIn[key] || '\u2014'}
                            </span>
                            <span className="shrink-0 text-2xs font-medium uppercase tracking-wide text-muted-foreground-subtle">
                                {overriddenBy ? 'Overridden' : 'Derived'}
                            </span>
                        </div>
                    )
                })}
            </div>

            {tokens.length > 0 && (
                <div className="space-y-1.5">
                    {tokens.map((row, i) => {
                        const rowError = rowErrors[i]
                        const message =
                            rowError?.key?.message ?? rowError?.value?.message
                        return (
                            <div key={i}>
                                <div className="flex items-center gap-2 rounded-md border bg-card px-3 py-1.5">
                                    <code className="shrink-0 text-xs text-muted-foreground">
                                        {'{'}
                                    </code>
                                    <Input
                                        value={row.key}
                                        onChange={(e) =>
                                            updateRow(i, {
                                                key: e.target.value,
                                            })
                                        }
                                        placeholder="token"
                                        aria-label={`Override token key ${i + 1}`}
                                        className="h-8 w-32 font-mono text-xs"
                                    />
                                    <code className="shrink-0 text-xs text-muted-foreground">
                                        {'}'}
                                    </code>
                                    <span className="text-xs text-muted-foreground">
                                        →
                                    </span>
                                    <Input
                                        value={row.value}
                                        onChange={(e) =>
                                            updateRow(i, {
                                                value: e.target.value,
                                            })
                                        }
                                        placeholder="replacement"
                                        aria-label={`Override token value ${i + 1}`}
                                        className="h-8 min-w-0 flex-1 text-xs"
                                    />
                                    <Button
                                        type="button"
                                        variant="ghost"
                                        size="icon"
                                        className="size-7"
                                        onClick={() => removeRow(i)}
                                        aria-label="Remove token override"
                                    >
                                        <ICON.close className="size-4" />
                                    </Button>
                                </div>
                                {message && (
                                    <p className="mt-1 px-1 text-xs text-destructive">
                                        {message}
                                    </p>
                                )}
                            </div>
                        )
                    })}
                </div>
            )}

            <Button type="button" variant="outline" size="sm" onClick={addRow}>
                <ICON.add className="mr-1 size-4" />
                Add override
            </Button>
        </div>
    )
}
