import { useEffect, useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Check } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Form, FormControl, FormField, FormItem } from '@/components/ui/form'
import {
    StudioDialog,
    StudioBody,
    StudioCanvas,
    StudioFooter,
} from '@/components/studio/Studio'
import { StudioSubmitButton } from '@/components/studio/StudioSubmitButton'
import { InfoHint } from '@/components/InfoHint'
import { formatMarketName } from '@/lib/formatting'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useUpsertOperatorAssignment } from '../api/operator-hooks'
import { toastAction } from '@/lib/toast-action'
import type { OperatorDirectoryEntry } from '../api/schemas'

const assignmentSchema = z.object({
    markets: z.array(z.string()),
    /** Transient free-text input — committed into `markets` on Enter. */
    customMarket: z.string(),
})

type AssignmentFormValues = z.infer<typeof assignmentSchema>

function buildValues(
    entry: OperatorDirectoryEntry | null
): AssignmentFormValues {
    return {
        markets: [...(entry?.markets ?? [])],
        customMarket: '',
    }
}

interface AssignOperatorDialogProps {
    open: boolean
    onOpenChange: (open: boolean) => void
    /** Existing directory entry to edit. */
    entry: OperatorDirectoryEntry | null
}
export function AssignOperatorDialog({
    open,
    onOpenChange,
    entry,
}: AssignOperatorDialogProps) {
    const { data: schedulesData, isLoading: schedulesLoading } = useSchedules()
    const upsert = useUpsertOperatorAssignment()
    const isAdmin = entry?.isAdmin ?? false
    const displayName =
        entry?.displayName || entry?.userPrincipalName || 'Operator'

    const scheduleMarkets = useMemo(
        () =>
            [
                ...new Set(
                    schedulesData?.schedules.map((s) => s.marketId) ?? []
                ),
            ].sort(),
        [schedulesData]
    )

    // Friendly chip labels: assignments store marketIds; show the station
    // ("3AW 693 Melbourne Current · 3AW Melbourne Traffic") like the card and
    // table views, falling back to a title-cased id for unknown entries.
    const marketLabels = useMemo(() => {
        const map = new Map<string, string>()
        for (const schedule of schedulesData?.schedules ?? []) {
            const label = [schedule.station, schedule.name]
                .filter(Boolean)
                .join(' · ')
            if (label) map.set(schedule.marketId.toLowerCase(), label)
        }
        return map
    }, [schedulesData])

    const form = useForm<AssignmentFormValues>({
        resolver: zodResolver(assignmentSchema),
        mode: 'onChange',
        defaultValues: buildValues(entry),
    })

    // useForm captures defaults on first mount and the dialog stays mounted,
    // so reset explicitly each time it opens (or the subject changes).
    useEffect(() => {
        if (open) form.reset(buildValues(entry))
    }, [open, entry, form])

    const markets = useWatch({ control: form.control, name: 'markets' })
    const customMarket = useWatch({
        control: form.control,
        name: 'customMarket',
    })
    const typedMarket = customMarket?.trim() ?? ''
    // Only judge the typed id once the catalogue is actually loaded — an empty
    // `scheduleMarkets` during loading would otherwise flag every id as unknown.
    const catalogueReady = !schedulesLoading && scheduleMarkets.length > 0
    const typedMarketKnown =
        catalogueReady && scheduleMarkets.includes(typedMarket)
    // Live inline warning (bug-bash-2026-08-05 finding 23): a typo'd market id
    // used to save with a success toast and silently lock the operator out of
    // every station. The warning is derived from the typed value so it shows
    // the moment an unknown id appears — Enter refuses to commit for the same
    // reason, and a silent refusal with no explanation would recreate the
    // silent dead-end. Both must hold together.
    const marketWarning =
        catalogueReady && typedMarket !== '' && !typedMarketKnown
            ? `\u201C${typedMarket}\u201D is not a known station. Check the id against the station list.`
            : null

    // All known stations plus any custom markets already assigned to the user.
    const allMarketSuggestions = useMemo(() => {
        const custom = markets.filter((m) => !scheduleMarkets.includes(m))
        return [...new Set([...scheduleMarkets, ...custom])].sort()
    }, [scheduleMarkets, markets])

    const setMarkets = (next: string[]) => {
        form.setValue('markets', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }

    const toggleMarket = (market: string) => {
        setMarkets(
            markets.includes(market)
                ? markets.filter((m) => m !== market)
                : [...markets, market]
        )
    }

    const addCustomMarket = () => {
        const trimmed = customMarket.trim()
        // The live warning above explains unknown ids; Enter only ever commits
        // catalogue markets (this guard also covers the form's submit path).
        if (!trimmed || schedulesLoading || !scheduleMarkets.includes(trimmed))
            return
        if (!markets.includes(trimmed)) setMarkets([...markets, trimmed])
        form.setValue('customMarket', '')
    }

    const handleSubmit = form.handleSubmit(async (values) => {
        if (!entry) return
        const clearScoping = values.markets.length === 0
        const result = await toastAction(
            () =>
                upsert.mutateAsync({
                    objectId: entry.objectId,
                    markets: values.markets,
                }),
            clearScoping ? 'Station scoping cleared' : 'Station access updated',
            'Failed to save operator assignment'
        )

        if (result !== undefined) onOpenChange(false)
    })

    return (
        <StudioDialog
            open={open}
            onOpenChange={onOpenChange}
            title="Edit station access"
            subject={displayName}
            description="Choose stations to optionally narrow this person's access. No selection leaves them unscoped with access to every station."
        >
            <Form {...form}>
                <form
                    onSubmit={handleSubmit}
                    className="flex min-h-0 flex-1 flex-col"
                >
                    <StudioBody>
                        <StudioCanvas stepKey="stations">
                            <div className="flex flex-col gap-5">
                                <div>
                                    <div className="flex items-center gap-2">
                                        <ICON.org className="size-4 text-primary" />
                                        <h2 className="text-sm font-semibold">
                                            Stations for {displayName}
                                        </h2>
                                        <InfoHint label="What does station selection do?">
                                            Select stations to narrow access.
                                            Leave every station unselected to
                                            keep this person unscoped with
                                            access to all stations.
                                        </InfoHint>
                                    </div>
                                </div>

                                <div className="flex flex-col gap-2">
                                    <Label htmlFor="custom-market">
                                        Add a station
                                    </Label>
                                    {marketWarning && (
                                        <p
                                            className="text-xs text-warning"
                                            role="alert"
                                        >
                                            {marketWarning}
                                        </p>
                                    )}
                                    <FormField
                                        control={form.control}
                                        name="customMarket"
                                        render={({ field }) => (
                                            <FormItem>
                                                <FormControl>
                                                    <Input
                                                        {...field}
                                                        id="custom-market"
                                                        aria-label="Add a station"
                                                        onKeyDown={(e) => {
                                                            if (
                                                                e.key ===
                                                                'Enter'
                                                            ) {
                                                                e.preventDefault()
                                                                addCustomMarket()
                                                            }
                                                        }}
                                                        placeholder="Type a station and press Enter"
                                                        className="h-8"
                                                        disabled={isAdmin}
                                                    />
                                                </FormControl>
                                            </FormItem>
                                        )}
                                    />
                                </div>

                                {allMarketSuggestions.length === 0 ? (
                                    <p className="text-xs text-muted-foreground italic">
                                        No stations are available.
                                    </p>
                                ) : (
                                    <div className="flex max-h-56 flex-wrap gap-1.5 overflow-y-auto rounded-md border bg-background/40 p-2">
                                        {allMarketSuggestions.map((market) => {
                                            const selected =
                                                markets.includes(market)
                                            const label =
                                                marketLabels.get(
                                                    market.toLowerCase()
                                                ) ?? formatMarketName(market)
                                            return (
                                                <button
                                                    key={market}
                                                    type="button"
                                                    aria-pressed={selected}
                                                    aria-label={`${selected ? 'Remove' : 'Assign'} station ${market}`}
                                                    title={market}
                                                    onClick={() =>
                                                        toggleMarket(market)
                                                    }
                                                    disabled={isAdmin}
                                                    className={[
                                                        'inline-flex items-center gap-1 rounded-full border px-2.5 py-1',
                                                        'text-xs font-medium transition-colors',
                                                        selected
                                                            ? 'border-primary/40 bg-primary/10 text-primary-on-tint'
                                                            : 'border-border bg-card text-muted-foreground hover:border-muted-foreground/30 hover:text-foreground',
                                                    ].join(' ')}
                                                >
                                                    {selected ? (
                                                        <Check className="size-3" />
                                                    ) : (
                                                        <ICON.add className="size-3 opacity-50" />
                                                    )}
                                                    <span className="max-w-48 truncate">
                                                        {label}
                                                    </span>
                                                </button>
                                            )
                                        })}
                                    </div>
                                )}
                            </div>
                        </StudioCanvas>
                    </StudioBody>

                    <StudioFooter>
                        <div className="flex flex-1 items-center gap-2 text-xs text-muted-foreground">
                            <ICON.safety className="size-3.5" />
                            {isAdmin
                                ? 'Admins can access all stations'
                                : markets.length > 0
                                  ? `${markets.length} station${markets.length === 1 ? '' : 's'} selected`
                                  : 'No stations selected; all stations available (unscoped)'}
                        </div>
                        <div className="ml-auto flex items-center gap-2">
                            <Button
                                type="button"
                                variant="ghost"
                                onClick={() => onOpenChange(false)}
                                disabled={upsert.isPending}
                            >
                                Cancel
                            </Button>
                            <StudioSubmitButton
                                label={
                                    markets.length === 0
                                        ? 'Clear station scoping'
                                        : 'Save station access'
                                }
                                isSubmitting={upsert.isPending}
                                isValid={
                                    form.formState.isValid &&
                                    Boolean(entry) &&
                                    !isAdmin
                                }
                            />
                        </div>
                    </StudioFooter>
                </form>
            </Form>
        </StudioDialog>
    )
}
