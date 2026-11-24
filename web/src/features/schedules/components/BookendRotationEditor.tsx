import { useWatch, type UseFormReturn } from 'react-hook-form'
import {
    Quote,
    Repeat,
    Clock,
    Plus,
    X,
    ArrowUp,
    ArrowDown,
    ArrowRight,
    RotateCcw,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { useBookends } from '@/features/bookends/api/bookend-hooks'
import { createWindowId } from '../lib/day-track'
import type { BookendSelectionMode, ScheduleFormData } from '../types'

type Window = ScheduleFormData['bookendWindows'][number] & { id?: string }
type WindowWithId = Window & { id: string }

interface BookendRotationEditorProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/**
 * Per-station bookend-pair selection policy. The single/fallback pair is
 * picked by the BookendSelector above; this editor adds rotation on top of
 * it: Rotate cycles an ordered set one bulletin at a time; By-time-of-day
 * maps local-hour windows to pairs (the primary acts as the fallback for any
 * uncovered time). Mirrors VoiceRotationEditor's mode tabs and window rows.
 */
export function BookendRotationEditor({ form }: BookendRotationEditorProps) {
    const { data } = useBookends()
    const bookends = (data?.bookends ?? []).filter(
        (bookend) => bookend.status === 'active'
    )
    const nameOf = (id: string) =>
        bookends.find((bookend) => bookend.id === id)?.name ?? id

    const modeName = 'bookendRotationMode'
    const idsName = 'bookendRotationIds'
    const windowsName = 'bookendWindows'
    const primaryName = 'bookendId'

    const [mode, watchedIds, watchedWindows] = useWatch({
        control: form.control,
        name: [modeName, idsName, windowsName, primaryName],
    })
    const ids = watchedIds ?? []
    const idOf = (window: Window, index: number): string => {
        const withId = window as Window & { id?: string }
        return (
            withId.id ??
            // No id means the window came from the wire (ids are client-only,
            // never persisted). The start|end|bookend composite is NOT unique —
            // a stored rotation can carry two identical windows — so qualify
            // it by list index; the derived id bakes into the form on the
            // first edit and stays stable from then on.
            `${window.start}|${window.end}|${window.bookendId}#${index}`
        )
    }
    const windows: WindowWithId[] = (watchedWindows ?? []).map(
        (window, index) => ({
            ...window,
            id: idOf(window, index),
        })
    )

    const setIds = (next: string[]) => {
        form.setValue(idsName, next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }

    const setWindows = (next: Window[]) =>
        form.setValue(windowsName, next, {
            shouldDirty: true,
            shouldValidate: true,
        })

    const addWindow = () =>
        setWindows([
            ...windows,
            {
                id: createWindowId(),
                start: '06:00',
                end: '10:00',
                bookendId: '',
            },
        ])

    const setMode = (next: BookendSelectionMode) => {
        form.setValue(modeName, next, {
            shouldDirty: true,
            shouldValidate: true,
        })
        if (next === 'roundRobin' && ids.length === 0) {
            const primary = form.getValues(primaryName)
            if (primary) setIds([primary])
        }
        if (next === 'timeOfDay' && windows.length === 0) addWindow()
    }

    const addBookend = (id: string) => {
        if (id && !ids.includes(id)) setIds([...ids, id])
    }
    const removeBookend = (id: string) => setIds(ids.filter((x) => x !== id))
    const move = (index: number, delta: number) => {
        const target = index + delta
        if (target < 0 || target >= ids.length) return
        const next = [...ids]
        ;[next[index], next[target]] = [next[target], next[index]]
        setIds(next)
    }

    const updateWindow = (index: number, patch: Partial<Window>) =>
        setWindows(
            windows.map((window, currentIndex) =>
                currentIndex === index ? { ...window, ...patch } : window
            )
        )
    const removeWindow = (index: number) => {
        setWindows(windows.filter((_, currentIndex) => currentIndex !== index))
    }

    const available = bookends.filter((bookend) => !ids.includes(bookend.id))

    return (
        <Tabs
            value={mode}
            onValueChange={(v) => v && setMode(v as BookendSelectionMode)}
        >
            {/* Equal-width segmented control — a deliberate exception to the
                content-width TabsList, matching the field widths below it. */}
            <TabsList className="grid w-full grid-cols-3">
                <TabsTrigger value="single" className="gap-1.5">
                    <Quote className="size-4" /> Single
                </TabsTrigger>
                <TabsTrigger value="roundRobin" className="gap-1.5">
                    <Repeat className="size-4" /> Rotate
                </TabsTrigger>
                <TabsTrigger value="timeOfDay" className="gap-1.5">
                    <Clock className="size-4" /> By time of day
                </TabsTrigger>
            </TabsList>

            <TabsContent value="single" className="mt-4 space-y-4" />

            <TabsContent value="roundRobin" className="mt-4 space-y-4">
                {ids.length > 0 && (
                    <div className="flex flex-wrap items-center gap-1.5 rounded-lg border bg-muted/20 p-3">
                        {ids.map((id, index) => (
                            <span
                                key={id}
                                className="flex items-center gap-1.5"
                            >
                                <span className="inline-flex items-center gap-1.5 rounded-full border bg-card py-1 pl-1.5 pr-2.5 text-xs">
                                    <span className="flex size-4 items-center justify-center rounded-full bg-muted-foreground/70 text-3xs font-semibold text-white">
                                        {index + 1}
                                    </span>
                                    {nameOf(id)}
                                </span>
                                {index < ids.length - 1 && (
                                    <ArrowRight className="size-3.5 text-muted-foreground-subtle" />
                                )}
                            </span>
                        ))}
                        <RotateCcw
                            className="size-3.5 text-muted-foreground-subtle"
                            aria-label="loops back to the first"
                        />
                    </div>
                )}

                {ids.length > 0 ? (
                    <ol className="space-y-2">
                        {ids.map((id, index) => (
                            <li
                                key={id}
                                className="flex items-center gap-2 rounded-md border bg-card px-3 py-2"
                            >
                                <span className="flex-1 truncate text-sm">
                                    {nameOf(id)}
                                </span>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-7"
                                    disabled={index === 0}
                                    onClick={() => move(index, -1)}
                                    aria-label="Move up"
                                >
                                    <ArrowUp className="size-4" />
                                </Button>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-7"
                                    disabled={index === ids.length - 1}
                                    onClick={() => move(index, 1)}
                                    aria-label="Move down"
                                >
                                    <ArrowDown className="size-4" />
                                </Button>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-7"
                                    onClick={() => removeBookend(id)}
                                    aria-label="Remove bookend"
                                >
                                    <X className="size-4" />
                                </Button>
                            </li>
                        ))}
                    </ol>
                ) : (
                    <p className="text-sm text-muted-foreground">
                        No bookends selected yet.
                    </p>
                )}

                {available.length > 0 && (
                    <Select value="" onValueChange={addBookend}>
                        <SelectTrigger aria-label="Add a bookend to the rotation">
                            <SelectValue placeholder="Add a bookend to the rotation" />
                        </SelectTrigger>
                        <SelectContent>
                            {available.map((bookend) => (
                                <SelectItem key={bookend.id} value={bookend.id}>
                                    {bookend.name}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                )}
            </TabsContent>

            <TabsContent value="timeOfDay" className="mt-4 space-y-4">
                <p className="text-sm text-muted-foreground">
                    Times not covered by a window fall back to the bookend
                    selected above.
                </p>

                <div className="space-y-2">
                    {windows.map((w, i) => (
                        <div
                            key={w.id}
                            className="flex flex-wrap items-center gap-2 rounded-md border bg-card px-3 py-2"
                        >
                            <Input
                                type="time"
                                className="h-9 w-32"
                                value={w.start}
                                onChange={(e) =>
                                    updateWindow(i, { start: e.target.value })
                                }
                                aria-label={`Window ${i + 1} start`}
                            />
                            <span className="text-xs text-muted-foreground">
                                to
                            </span>
                            <Input
                                type="time"
                                className="h-9 w-32"
                                value={w.end}
                                onChange={(e) =>
                                    updateWindow(i, { end: e.target.value })
                                }
                                aria-label={`Window ${i + 1} end`}
                            />
                            <Select
                                value={w.bookendId}
                                onValueChange={(v) =>
                                    // Picking a bookend REPLACES the window's set:
                                    // clearing the carried-through ids here is
                                    // the operator's explicit intent, unlike the
                                    // old silent drop on any unrelated save.
                                    updateWindow(i, {
                                        bookendId: v,
                                        extraBookendIds: undefined,
                                    })
                                }
                            >
                                <SelectTrigger
                                    aria-label="Bookend"
                                    className="h-9 min-w-40 flex-1"
                                >
                                    <SelectValue placeholder="Bookend" />
                                </SelectTrigger>
                                <SelectContent>
                                    {bookends.map((bookend) => (
                                        <SelectItem
                                            key={bookend.id}
                                            value={bookend.id}
                                        >
                                            {bookend.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-9"
                                onClick={(e) => {
                                    e.stopPropagation()
                                    removeWindow(i)
                                }}
                                aria-label="Remove window"
                            >
                                <X className="size-4" />
                            </Button>
                        </div>
                    ))}
                </div>

                <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={addWindow}
                >
                    <Plus className="mr-1 size-4" />
                    Add window
                </Button>
            </TabsContent>
        </Tabs>
    )
}
