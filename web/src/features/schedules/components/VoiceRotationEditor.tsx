import { useState } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import {
    Mic,
    Repeat,
    Clock,
    Plus,
    X,
    ArrowUp,
    ArrowDown,
    ArrowRight,
    RotateCcw,
    Check,
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
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { SamplePlayButton } from '@/features/voices/components/SamplePlayButton'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { cn } from '@/lib/utils'
import { VoiceProfileSelector } from './VoiceProfileSelector'
import { VoiceTimeline } from './VoiceTimeline'
import { createWindowId } from '../lib/day-track'
import { voiceColor, buildVoiceColorMap } from '../lib/voice-colors'
import type { ScheduleFormData, VoiceSelectionMode } from '../types'

type Window = ScheduleFormData['voiceWindows'][number] & { id?: string }
type WindowWithId = Window & { id: string }

interface VoiceRotationEditorProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/**
 * Per-station voice selection. Single picks one voice; Rotate cycles an ordered
 * set one bulletin at a time; By-time-of-day maps local-hour windows to voices
 * (the primary acts as the fallback for any uncovered time).
 */
export function VoiceRotationEditor({ form }: VoiceRotationEditorProps) {
    const { data } = useVoiceProfiles()
    const profiles = (data?.profiles ?? []).filter((p) => p.status === 'active')
    const nameOf = (id: string) => profiles.find((p) => p.id === id)?.name ?? id
    const sampleUrlOf = (id: string) => {
        const versions = profiles.find((p) => p.id === id)?.versions
        return versions?.[versions.length - 1]?.sampleUrl ?? null
    }

    const [mode, watchedIds, watchedWindows, voiceProfileId] = useWatch({
        control: form.control,
        name: [
            'voiceRotationMode',
            'voiceRotationIds',
            'voiceWindows',
            'voiceProfileId',
        ],
    })
    const ids = watchedIds ?? []
    const idOf = (window: Window, index: number): string => {
        const withId = window as Window & { id?: string }
        return (
            withId.id ??
            // No id means the window came from the wire (ids are client-only,
            // never persisted). The start|end|voice composite is NOT unique —
            // a stored rotation can carry two identical windows — so qualify
            // it by list index; the derived id bakes into the form on the
            // first edit and stays stable from then on.
            `${window.start}|${window.end}|${window.voiceProfileId}#${index}`
        )
    }
    const windows: WindowWithId[] = (watchedWindows ?? []).map(
        (window, index) => ({
            ...window,
            id: idOf(window, index),
        })
    )
    const [selectedWindow, setSelectedWindow] = useState<number | null>(null)

    const setIds = (next: string[]) => {
        form.setValue('voiceRotationIds', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
        form.setValue('voiceProfileId', next[0] ?? '', {
            shouldDirty: true,
            shouldValidate: true,
        })
    }

    const setWindows = (next: Window[]) =>
        form.setValue('voiceWindows', next, {
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
                voiceProfileId: '',
            },
        ])

    const setMode = (next: VoiceSelectionMode) => {
        form.setValue('voiceRotationMode', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
        if (next === 'roundRobin' && ids.length === 0) {
            const primary = form.getValues('voiceProfileId')
            if (primary) setIds([primary])
        }
        if (next === 'timeOfDay' && windows.length === 0) addWindow()
    }

    const addVoice = (id: string) => {
        if (id && !ids.includes(id)) setIds([...ids, id])
    }
    const removeVoice = (id: string) => setIds(ids.filter((x) => x !== id))
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
        setSelectedWindow(null)
    }

    const available = profiles.filter((p) => !ids.includes(p.id))
    const rotateColors = buildVoiceColorMap(ids)
    const windowColors = buildVoiceColorMap(
        windows.map((w) => w.voiceProfileId)
    )

    return (
        <Tabs
            value={mode}
            onValueChange={(v) => v && setMode(v as VoiceSelectionMode)}
        >
            {/* Equal-width segmented control — a deliberate exception to the
                content-width TabsList, matching the field widths below it. */}
            <TabsList className="grid w-full grid-cols-3">
                <TabsTrigger value="single" className="gap-1.5">
                    <Mic className="size-4" /> Single
                </TabsTrigger>
                <TabsTrigger value="roundRobin" className="gap-1.5">
                    <Repeat className="size-4" /> Rotate
                </TabsTrigger>
                <TabsTrigger value="timeOfDay" className="gap-1.5">
                    <Clock className="size-4" /> By time of day
                </TabsTrigger>
            </TabsList>

            <TabsContent value="single" className="mt-4 space-y-4">
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                    {profiles.map((p) => {
                        const selected = voiceProfileId === p.id
                        const sample = sampleUrlOf(p.id)
                        return (
                            <div key={p.id} className="rounded-xl outline-none">
                                <SpotlightCard
                                    className={cn(
                                        'p-3 transition-shadow',
                                        selected &&
                                            'border-primary ring-2 ring-primary'
                                    )}
                                >
                                    <button
                                        type="button"
                                        aria-pressed={selected}
                                        onClick={() =>
                                            form.setValue(
                                                'voiceProfileId',
                                                p.id,
                                                {
                                                    shouldDirty: true,
                                                    shouldValidate: true,
                                                }
                                            )
                                        }
                                        className="w-full text-left outline-none focus-visible:ring-2 focus-visible:ring-primary"
                                    >
                                        <div className="flex items-start justify-between gap-2">
                                            <span className="truncate text-sm font-medium">
                                                {p.name}
                                            </span>
                                            {selected && (
                                                <Check className="size-4 shrink-0 text-primary" />
                                            )}
                                        </div>
                                        <p className="mt-0.5 truncate text-xs text-muted-foreground">
                                            {p.baseVoiceName}
                                        </p>
                                    </button>
                                    {sample && (
                                        <SamplePlayButton
                                            url={sample}
                                            size="sm"
                                            variant="ghost"
                                            label="Preview"
                                            className="mt-2 h-7 px-2 text-xs"
                                        />
                                    )}
                                </SpotlightCard>
                            </div>
                        )
                    })}
                </div>
            </TabsContent>

            <TabsContent value="roundRobin" className="mt-4 space-y-4">
                {ids.length > 0 && (
                    <div className="flex flex-wrap items-center gap-1.5 rounded-lg border bg-muted/20 p-3">
                        {ids.map((id, index) => (
                            <span
                                key={id}
                                className="flex items-center gap-1.5"
                            >
                                <span className="inline-flex items-center gap-1.5 rounded-full border bg-card py-1 pl-1.5 pr-2.5 text-xs">
                                    <span
                                        className={cn(
                                            'flex size-4 items-center justify-center rounded-full text-3xs font-semibold text-white',
                                            voiceColor(
                                                rotateColors.get(id) ?? 0
                                            ).dot
                                        )}
                                    >
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
                                <span
                                    className={cn(
                                        'size-2.5 shrink-0 rounded-full',
                                        voiceColor(rotateColors.get(id) ?? 0)
                                            .dot
                                    )}
                                />
                                <span className="flex-1 truncate text-sm">
                                    {nameOf(id)}
                                </span>
                                {sampleUrlOf(id) && (
                                    <SamplePlayButton
                                        url={sampleUrlOf(id) as string}
                                        size="icon"
                                        variant="ghost"
                                        className="size-7"
                                    />
                                )}
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
                                    onClick={() => removeVoice(id)}
                                    aria-label="Remove voice"
                                >
                                    <X className="size-4" />
                                </Button>
                            </li>
                        ))}
                    </ol>
                ) : (
                    <p className="text-sm text-muted-foreground">
                        No voices selected yet.
                    </p>
                )}

                {available.length > 0 && (
                    <Select value="" onValueChange={addVoice}>
                        <SelectTrigger aria-label="Add a voice to the rotation">
                            <SelectValue placeholder="Add a voice to the rotation" />
                        </SelectTrigger>
                        <SelectContent>
                            {available.map((p) => (
                                <SelectItem key={p.id} value={p.id}>
                                    <span className="flex items-center gap-2">
                                        {p.name}
                                        <span className="text-xs text-muted-foreground">
                                            {p.baseVoiceName}
                                        </span>
                                    </span>
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                )}
            </TabsContent>

            <TabsContent value="timeOfDay" className="mt-4 space-y-4">
                <VoiceProfileSelector
                    form={form}
                    label="Default voice"
                    required
                    description="Used for any time not covered by a window below."
                />

                <div className="space-y-2">
                    {/* A block-level label, not an inline span: inside a
                        `space-y-*` stack an inline element's tight line box
                        collapses the gap to the next row. */}
                    <p className="text-sm font-medium leading-none">
                        Voice timeline
                    </p>
                    <VoiceTimeline
                        windows={windows}
                        defaultVoiceId={voiceProfileId ?? ''}
                        nameOf={nameOf}
                        colorIndexOf={(id) => windowColors.get(id) ?? -1}
                        selectedIndex={selectedWindow}
                        onSelectWindow={setSelectedWindow}
                        onChange={setWindows}
                    />

                    {windows.some((w) => w.voiceProfileId) && (
                        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                            {[...windowColors.entries()].map(([id, idx]) => (
                                <span
                                    key={id}
                                    className="inline-flex items-center gap-1.5 text-xs text-muted-foreground"
                                >
                                    <span
                                        className={cn(
                                            'size-2.5 rounded-full',
                                            voiceColor(idx).dot
                                        )}
                                    />
                                    {nameOf(id)}
                                </span>
                            ))}
                        </div>
                    )}
                    <p className="text-sm text-muted-foreground">
                        Local station time. Click an empty stretch to add a
                        window; drag a segment&rsquo;s edges to resize. Windows
                        may run past midnight, must not overlap.
                    </p>
                </div>

                <div className="space-y-2">
                    {windows.map((w, i) => (
                        // No onClick on the row: it is a container of controls,
                        // and selecting a window stays on the timeline's
                        // segment buttons (keyboard-accessible).
                        <div
                            key={w.id}
                            className={cn(
                                'flex flex-wrap items-center gap-2 rounded-md border bg-card px-3 py-2',
                                selectedWindow === i && 'ring-2 ring-primary'
                            )}
                        >
                            <span
                                className={cn(
                                    'size-2.5 shrink-0 rounded-full',
                                    w.voiceProfileId
                                        ? voiceColor(
                                              windowColors.get(
                                                  w.voiceProfileId
                                              ) ?? 0
                                          ).dot
                                        : 'bg-muted-foreground/40'
                                )}
                            />
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
                                value={w.voiceProfileId}
                                onValueChange={(v) =>
                                    // Picking a voice REPLACES the window's set:
                                    // clearing the carried-through ids here is
                                    // the operator's explicit intent, unlike the
                                    // old silent drop on any unrelated save.
                                    updateWindow(i, {
                                        voiceProfileId: v,
                                        extraVoiceProfileIds: undefined,
                                    })
                                }
                            >
                                <SelectTrigger
                                    aria-label="Voice"
                                    className="h-9 min-w-40 flex-1"
                                >
                                    <SelectValue placeholder="Voice" />
                                </SelectTrigger>
                                <SelectContent>
                                    {profiles.map((p) => (
                                        <SelectItem key={p.id} value={p.id}>
                                            {p.name}
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
