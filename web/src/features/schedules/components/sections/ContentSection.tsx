import type { UseFormReturn } from 'react-hook-form'
import { FormField, FormItem, FormMessage } from '@/components/ui/form'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import {
    Popover,
    PopoverContent,
    PopoverTrigger,
} from '@/components/ui/popover'
import { cn } from '@/lib/utils'
import {
    Route,
    CloudSun,
    Fuel,
    Construction,
    MessageSquareQuote,
    ChevronUp,
    ChevronDown,
    HelpCircle,
    X,
    Plus,
    ListOrdered,
} from 'lucide-react'
import { FILL_SOURCES, type FillSource } from '@/lib/api/schemas/shared'
import type { ScheduleFormData } from '../../types'

interface ContentSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

const SOURCES = [
    {
        key: 'traffic' as const,
        label: 'Traffic',
        icon: Route,
        description: 'Incidents and delays, the core of every bulletin',
    },
    {
        key: 'weather' as const,
        label: 'Weather',
        icon: CloudSun,
        description: 'Current conditions; severe alerts lead the bulletin',
    },
    {
        key: 'fuel' as const,
        label: 'Fuel',
        icon: Fuel,
        description: 'Cheapest unleaded and diesel nearby',
    },
] as const

const FILL_META: Record<
    FillSource,
    { label: string; icon: typeof CloudSun; note: string }
> = {
    weather: {
        label: 'Weather',
        icon: CloudSun,
        note: 'One line: condition and temperature',
    },
    fuel: {
        label: 'Fuel',
        icon: Fuel,
        note: 'Cheapest unleaded (and diesel)',
    },
    minorTraffic: {
        label: 'Minor traffic',
        icon: Construction,
        note: 'Roadworks and small delays surfaced when major incidents are light',
    },
    filler: {
        label: 'Filler',
        icon: MessageSquareQuote,
        note: "This station's standing filler lines",
    },
}

/**
 * "Always include" toggle for a supplementary source (weather/fuel). Rendered
 * only while the source is selected; deselecting clears the flag elsewhere.
 * Honest semantics: the section airs whenever its data arrives, and a failed
 * provider fetch still airs the bulletin without it.
 */
function AlwaysIncludeControl({
    label,
    checked,
    onCheckedChange,
}: {
    readonly label: string
    readonly checked: boolean
    readonly onCheckedChange: (checked: boolean) => void
}) {
    return (
        <div className="mt-auto flex flex-col gap-1 border-t border-muted/40 pt-2">
            <label className="flex cursor-pointer items-center justify-between gap-2">
                <span className="text-xs font-medium text-muted-foreground">
                    Always include
                </span>
                <Switch checked={checked} onCheckedChange={onCheckedChange} />
            </label>
            <p className="text-2xs leading-relaxed text-muted-foreground-subtle">
                Airs whenever {label.toLowerCase()} data arrives; if the
                provider is down the bulletin still airs without it.
            </p>
        </div>
    )
}

export function ContentSection({ form }: ContentSectionProps) {
    const content = form.watch('content')
    const fillOrder = form.watch('fillOrder')

    const setFillOrder = (next: FillSource[]) => {
        form.setValue('fillOrder', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }
    const moveFill = (index: number, delta: -1 | 1) => {
        const next = [...fillOrder]
        const target = index + delta
        if (target < 0 || target >= next.length) return
        ;[next[index], next[target]] = [next[target], next[index]]
        setFillOrder(next)
    }
    type RunningSegment = 'Traffic' | 'Weather' | 'Fuel'
    const enabledSegments = SOURCES.filter(({ key }) => content?.[key]).map(
        ({ key, label, icon }) => ({
            value: (key === 'traffic'
                ? 'Traffic'
                : key === 'weather'
                  ? 'Weather'
                  : 'Fuel') as RunningSegment,
            label,
            icon,
        })
    )
    const enabledSegmentValues = enabledSegments.map(({ value }) => value)
    const configuredOrder = content?.segmentOrder
    const runningOrder: RunningSegment[] = configuredOrder
        ? [
              ...configuredOrder.filter((segment) =>
                  enabledSegmentValues.includes(segment)
              ),
              ...enabledSegmentValues.filter(
                  (segment) => !configuredOrder.includes(segment)
              ),
          ]
        : enabledSegmentValues

    const setRunningOrder = (next: RunningSegment[]) => {
        form.setValue('content.segmentOrder', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }
    const moveRunningSegment = (index: number, delta: -1 | 1) => {
        const target = index + delta
        if (target < 0 || target >= runningOrder.length) return
        const next = [...runningOrder]
        ;[next[index], next[target]] = [next[target], next[index]]
        setRunningOrder(next)
    }
    const setTrafficOrder = (worstLast: boolean) => {
        form.setValue(
            'content.trafficOrder',
            worstLast ? 'WorstLast' : 'WorstFirst',
            { shouldDirty: true, shouldValidate: true }
        )
    }
    const excludedFill = FILL_SOURCES.filter((s) => !fillOrder.includes(s))

    return (
        <div className="flex flex-col gap-4">
            <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <Route className="size-4" />
                Content sources
                <Popover>
                    <PopoverTrigger asChild>
                        <button
                            type="button"
                            className="inline-flex items-center gap-1 text-2xs font-medium text-muted-foreground-subtle transition-colors hover:text-foreground"
                        >
                            <HelpCircle className="size-3" aria-hidden />
                            If a source is down
                        </button>
                    </PopoverTrigger>
                    <PopoverContent
                        align="start"
                        className="w-72 text-xs leading-relaxed text-muted-foreground"
                    >
                        <p className="font-semibold text-foreground">
                            Optional or always included
                        </p>
                        <p className="mt-1.5">
                            A source without "Always include" is voiced only
                            when the read has room for it.
                        </p>
                        <p className="mt-1.5">
                            An "Always include" source airs whenever its data
                            arrives.
                        </p>
                        <p className="mt-1.5">
                            Either way, if a provider is down the bulletin still
                            airs without that section.
                        </p>
                    </PopoverContent>
                </Popover>
            </div>

            <div className="grid gap-3 sm:grid-cols-3">
                {SOURCES.map(({ key, label, icon: Icon, description }) => {
                    const selected = content?.[key] ?? false
                    const requiredKey:
                        'weatherRequired' | 'fuelRequired' | null =
                        key === 'weather'
                            ? 'weatherRequired'
                            : key === 'fuel'
                              ? 'fuelRequired'
                              : null
                    const requiredChecked = requiredKey
                        ? (content?.[requiredKey] ?? false)
                        : false
                    const setRequired = (checked: boolean) => {
                        if (requiredKey) {
                            form.setValue(`content.${requiredKey}`, checked, {
                                shouldDirty: true,
                                shouldValidate: true,
                            })
                        }
                    }
                    return (
                        <FormField
                            key={key}
                            control={form.control}
                            name={`content.${key}`}
                            render={({ field }) => (
                                <FormItem className="h-full">
                                    <SpotlightCard
                                        radius={280}
                                        className={cn(
                                            'h-full border-2 px-4 py-4 transition-all duration-200',
                                            selected
                                                ? 'border-primary/50 bg-primary/[0.03]'
                                                : 'border-muted/50 hover:border-muted-foreground/30'
                                        )}
                                    >
                                        <div className="flex h-full w-full flex-col gap-2">
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    // Deselecting a source must
                                                    // clear its "Always include"
                                                    // flag: a required flag on an
                                                    // unselected source is a 400.
                                                    if (
                                                        field.value &&
                                                        requiredKey
                                                    ) {
                                                        form.setValue(
                                                            `content.${requiredKey}`,
                                                            false,
                                                            {
                                                                shouldDirty: true,
                                                                shouldValidate: true,
                                                            }
                                                        )
                                                    }
                                                    const nextEnabled =
                                                        !field.value
                                                    field.onChange(nextEnabled)
                                                    if (content?.segmentOrder) {
                                                        const segment =
                                                            key === 'traffic'
                                                                ? 'Traffic'
                                                                : key ===
                                                                    'weather'
                                                                  ? 'Weather'
                                                                  : 'Fuel'
                                                        const nextOrder =
                                                            content.segmentOrder.filter(
                                                                (item) =>
                                                                    item !==
                                                                    segment
                                                            )
                                                        if (nextEnabled)
                                                            nextOrder.push(
                                                                segment
                                                            )
                                                        form.setValue(
                                                            'content.segmentOrder',
                                                            nextOrder,
                                                            {
                                                                shouldDirty: true,
                                                                shouldValidate: true,
                                                            }
                                                        )
                                                    }
                                                    if (
                                                        key === 'traffic' &&
                                                        !nextEnabled &&
                                                        content?.trafficOrder
                                                    ) {
                                                        form.setValue(
                                                            'content.trafficOrder',
                                                            undefined,
                                                            {
                                                                shouldDirty: true,
                                                                shouldValidate: true,
                                                            }
                                                        )
                                                    }
                                                }}
                                                className="flex flex-1 flex-col gap-2 text-left"
                                            >
                                                <Icon
                                                    className={cn(
                                                        'size-6 transition-colors duration-200',
                                                        selected
                                                            ? 'text-primary'
                                                            : 'text-muted-foreground-subtle'
                                                    )}
                                                />
                                                <span
                                                    className={cn(
                                                        'text-sm font-medium transition-colors duration-200',
                                                        selected
                                                            ? 'text-foreground'
                                                            : 'text-muted-foreground'
                                                    )}
                                                >
                                                    {label}
                                                </span>
                                                <span className="text-xs leading-relaxed text-muted-foreground-subtle">
                                                    {description}
                                                </span>
                                            </button>
                                            {requiredKey && selected && (
                                                <AlwaysIncludeControl
                                                    label={label}
                                                    checked={requiredChecked}
                                                    onCheckedChange={
                                                        setRequired
                                                    }
                                                />
                                            )}
                                        </div>
                                    </SpotlightCard>
                                </FormItem>
                            )}
                        />
                    )
                })}
            </div>
            <section
                className="space-y-3 rounded-xl border border-primary/20 bg-primary/[0.02] p-4"
                aria-labelledby="running-order-heading"
            >
                <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                        <h2
                            id="running-order-heading"
                            className="flex items-center gap-2 text-sm font-semibold text-foreground"
                        >
                            <ListOrdered className="size-4 text-primary" />
                            Running order
                        </h2>
                        <p className="mt-1 text-xs text-muted-foreground">
                            Set the order of the enabled bulletin sections.
                        </p>
                    </div>
                    {content?.traffic && (
                        <label className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
                            <span>
                                {content.trafficOrder === 'WorstLast'
                                    ? 'Building to the worst'
                                    : 'Worst hold-up first (recommended)'}
                            </span>
                            <Switch
                                checked={content.trafficOrder === 'WorstLast'}
                                onCheckedChange={setTrafficOrder}
                                aria-label="Traffic incident order"
                            />
                        </label>
                    )}
                </div>
                <div className="flex flex-col gap-2">
                    {runningOrder.map((segment, index) => {
                        const meta = enabledSegments.find(
                            ({ value }) => value === segment
                        )
                        if (!meta) return null
                        const Icon = meta.icon
                        return (
                            <div
                                key={segment}
                                className="flex items-center gap-3 rounded-md border border-border/70 bg-background/40 px-3 py-2"
                            >
                                <span className="w-4 text-center font-mono text-xs text-muted-foreground-subtle">
                                    {index + 1}
                                </span>
                                <Icon
                                    className="size-4 text-muted-foreground"
                                    aria-hidden
                                />
                                <span className="flex-1 text-sm text-foreground">
                                    {meta.label}
                                </span>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-7"
                                    aria-label={`Move ${meta.label} up`}
                                    disabled={index === 0}
                                    onClick={() =>
                                        moveRunningSegment(index, -1)
                                    }
                                >
                                    <ChevronUp aria-hidden />
                                </Button>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-7"
                                    aria-label={`Move ${meta.label} down`}
                                    disabled={index === runningOrder.length - 1}
                                    onClick={() => moveRunningSegment(index, 1)}
                                >
                                    <ChevronDown aria-hidden />
                                </Button>
                            </div>
                        )
                    })}
                </div>
                <p className="text-xs text-muted-foreground">
                    Enforced on every bulletin - the writer cannot vary these.
                </p>
            </section>

            <FormField
                control={form.control}
                name="content"
                render={() => (
                    <FormItem>
                        <FormMessage />
                    </FormItem>
                )}
            />

            <div className="mt-2 flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <ListOrdered className="size-4" />
                Fill priority
                <Popover>
                    <PopoverTrigger asChild>
                        <button
                            type="button"
                            className="inline-flex items-center gap-1 text-2xs font-medium text-muted-foreground-subtle transition-colors hover:text-foreground"
                        >
                            <HelpCircle className="size-3" aria-hidden />
                            How this works
                        </button>
                    </PopoverTrigger>
                    <PopoverContent
                        align="start"
                        className="w-72 text-xs leading-relaxed text-muted-foreground"
                    >
                        <p className="font-semibold text-foreground">
                            Priority and read order
                        </p>
                        <p className="mt-1.5">
                            When traffic alone can't fill the slot, these
                            sources make up the time in the order below.
                        </p>
                        <p className="mt-1.5">
                            The same order sets the on-air placement of weather
                            and fuel: whichever comes first reads first. Filler
                            always closes the read.
                        </p>
                    </PopoverContent>
                </Popover>
            </div>
            <div className="flex flex-col gap-2">
                {fillOrder.map((source, index) => {
                    const { label, icon: Icon, note } = FILL_META[source]
                    const fetchedOff =
                        (source === 'weather' && !content?.weather) ||
                        (source === 'fuel' && !content?.fuel)
                    return (
                        <div
                            key={source}
                            className="flex items-center gap-3 rounded-md border border-muted/50 px-3 py-2"
                        >
                            <span className="w-4 text-center font-mono text-xs text-muted-foreground-subtle">
                                {index + 1}
                            </span>
                            <Icon className="size-4 text-muted-foreground" />
                            <div className="flex min-w-0 flex-1 flex-col">
                                <span className="text-sm text-foreground">
                                    {label}
                                </span>
                                <span className="truncate text-xs text-muted-foreground-subtle">
                                    {fetchedOff
                                        ? 'Not fetched: enable the source above for this to fill'
                                        : note}
                                </span>
                            </div>
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-7"
                                aria-label={`Move ${label} up`}
                                disabled={index === 0}
                                onClick={() => moveFill(index, -1)}
                            >
                                <ChevronUp />
                            </Button>
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-7"
                                aria-label={`Move ${label} down`}
                                disabled={index === fillOrder.length - 1}
                                onClick={() => moveFill(index, 1)}
                            >
                                <ChevronDown />
                            </Button>
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-7"
                                aria-label={`Exclude ${label} from fill`}
                                onClick={() =>
                                    setFillOrder(
                                        fillOrder.filter((s) => s !== source)
                                    )
                                }
                            >
                                <X />
                            </Button>
                        </div>
                    )
                })}
                {fillOrder.length === 0 && (
                    <p className="rounded-md border border-dashed border-warning/40 px-3 py-2 text-xs text-muted-foreground">
                        No fill sources: a short read goes straight to the
                        fallback bulletin when traffic can't fill the slot.
                    </p>
                )}
                {excludedFill.length > 0 && (
                    <div className="flex flex-wrap items-center gap-2">
                        <span className="text-xs text-muted-foreground-subtle">
                            Excluded:
                        </span>
                        {excludedFill.map((source) => (
                            <Button
                                key={source}
                                type="button"
                                variant="outline"
                                size="sm"
                                className="h-7 gap-1 text-xs"
                                onClick={() =>
                                    setFillOrder([...fillOrder, source])
                                }
                            >
                                <Plus className="size-3" />
                                {FILL_META[source].label}
                            </Button>
                        ))}
                    </div>
                )}
            </div>

            <p className="text-xs text-muted-foreground">
                Priority roads are set market-wide in{' '}
                <strong className="text-foreground">
                    Bulletin rules → Priority roads
                </strong>
                .
            </p>
        </div>
    )
}
