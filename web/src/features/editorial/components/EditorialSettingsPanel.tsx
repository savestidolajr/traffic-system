import { useState } from 'react'
import { toast } from 'sonner'
import { InfoHint } from '@/components/InfoHint'
import { ListSkeleton } from '@/components/LoadingSkeleton'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Slider } from '@/components/ui/slider'
import { Switch } from '@/components/ui/switch'
import { ICON } from '@/lib/design/icons'
import {
    useDeleteEditorialSettings,
    useEditorialSettings,
    useEffectiveEditorialSettings,
    useUpsertEditorialSettings,
} from '../api/editorial-hooks'
import { sourceDisplayName } from '@/features/jobs/lib/source-display-names'
import type { EditorialScope, UpsertEditorialSettingsRequest } from '../types'

/**
 * The road-authority feeds a scope may switch off, mirroring the backend's
 * `OfficialTrafficSources.All`. HERE is deliberately absent: it is the primary provider, not a
 * corroborating authority, and whether a bulletin carries traffic at all is already a per-station
 * decision on the station's content selection.
 */
const ALL_TRAFFIC_SOURCES = [
    'livetraffic-nsw',
    'qldtraffic',
    'victraffic',
    'mainroads-wa',
    'trafficsa',
    'tasalert',
] as const

interface EditorialSettingsPanelProps {
    readonly scope: EditorialScope
    readonly scopeId: string
    readonly market: string
    readonly station: string | null
}

type FieldKey = keyof Omit<UpsertEditorialSettingsRequest, 'scope' | 'scopeId'>

type FieldSpec = (
    | { readonly hint: string; readonly hintLabel: string }
    | { readonly hint?: undefined; readonly hintLabel?: undefined }
) & {
    readonly key: FieldKey
    readonly label: string
    /** Rendered beside the value. A bare "5" or "1.15" does not say roads, minutes or multiplier. */
    readonly unit: string
    readonly min: number
    readonly max: number
    readonly step?: number
}

interface SettingSection {
    readonly title: string
    readonly blurb: string
    readonly fields: readonly FieldSpec[]
}

/**
 * Every value here is a BOUNDED range, so it is a slider, not a number box.
 *
 * A text box hides the two facts that matter — what the limits are, and where this station sits
 * between them — and invites typing a figure nobody can judge ("is 9 roads a lot?"). It also made
 * the panel a grid of two-character inputs, which is what three earlier layouts were really
 * fighting: single-field cards, `ConfigRow` rows (a read-only `<dl>` primitive, so the form
 * inherited its rules and label column) and a two-column card grid all kept the box and moved it
 * around. The control was the problem.
 *
 * Width is the tab's own, with no `max-w-*`. `RuleTab` sets none and no sibling manager adds one,
 * so the three widths this panel went through were inventions rather than a standard.
 *
 * DELIBERATELY ABSENT: maxSilencePadSeconds and maxSafeOverrunTrimSeconds. Both persist, validate
 * and resolve onto the job, but NOTHING consumes them: every production read is the deployed
 * SpeechDurationOptions value — `_durationOptions.MaxSilencePadSeconds` in AudioPipelineService and
 * ScriptGenerationEngine, `durationOptions.MaxSafeOverrunTrimSeconds` in AudioDurationFineTuner.
 * Verified on dev 2026-08-24: set to 1.7 and 9.5, both resolved and changed nothing. Hidden rather
 * than shown-and-disabled because an operator who raised the silence pad to stop the 05:55 Sydney
 * slot falling to the standby cart would reasonably believe they had mitigated dead air, and would
 * be wrong. Re-add BOTH in the change that threads the overrides through
 * IAudioPipelineService.SynthesizeWithDurationTargetAsync (and IsSlotFilled) down to
 * AudioDurationFineTuner. decisions.md 2026-08-24, "Editorial-settings audit".
 */
const SECTIONS: readonly SettingSection[] = [
    {
        title: 'How much reaches the read',
        blurb: 'Caps on the rundown the writer is given.',
        fields: [
            {
                key: 'maxCorridors',
                label: 'Most roads in a read',
                unit: 'roads',
                hint: 'Maximum number of road corridors selected for this station.',
                hintLabel: 'When should this be raised?',
                min: 1,
                // Backend EditorialSettings.Validate accepts caps through 25; keep the UI
                // editable for imported values up to that domain limit.
                max: 25,
            },
            {
                key: 'majorTierCap',
                label: 'Major incidents',
                unit: 'items',
                min: 1,
                max: 12,
            },
            {
                key: 'moderateTierCap',
                label: 'Moderate incidents',
                unit: 'items',
                min: 1,
                max: 12,
            },
            {
                key: 'minorTierCap',
                label: 'Minor incidents',
                unit: 'items',
                min: 1,
                max: 12,
            },
        ],
    },
    {
        title: 'Follow-on bulletins',
        blurb: 'How many items of each kind a later bulletin carries forward.',
        fields: [
            {
                key: 'continuityNewCap',
                label: 'New',
                unit: 'items',
                min: 0,
                max: 10,
            },
            {
                key: 'continuityOngoingCap',
                label: 'Still going',
                unit: 'items',
                min: 0,
                max: 10,
            },
            {
                key: 'continuityClearedCap',
                label: 'Cleared',
                unit: 'items',
                min: 0,
                max: 10,
            },
            {
                key: 'continuityUpdatedCap',
                label: 'Updated',
                unit: 'items',
                min: 0,
                max: 10,
            },
        ],
    },
    {
        title: 'Thresholds',
        blurb: 'The bar a fact has to clear to earn airtime.',
        fields: [
            {
                key: 'driveTimeHeadlineThresholdMinutes',
                label: 'Drive time worth airing',
                unit: 'min',
                hint: 'Minimum delay in minutes before a highway drive time is promoted.',
                hintLabel: 'When should this be lowered?',
                // Backend EditorialSettings.Validate accepts thresholds through 60; keep the
                // UI editable for imported values up to that domain limit.
                min: 1,
                max: 60,
            },
            {
                key: 'guidanceMaxDirectives',
                label: 'Standing directives',
                unit: 'rules',
                min: 1,
                max: 20,
            },
        ],
    },
    {
        title: 'Slot fit',
        blurb: 'At 1.0× a read that cannot reach its slot goes to the standby cart instead.',
        fields: [
            {
                key: 'maxSlowDownFactor',
                label: 'Stretch a short read',
                unit: '×',
                hint: 'How much a short read may be stretched to fill the slot. 1.0 means never slow the read.',
                hintLabel: 'What does this change?',
                min: 1,
                max: 1.25,
                step: 0.01,
            },
            {
                key: 'maxSpeedUpFactor',
                label: 'Compress a long read',
                unit: '×',
                hint: 'How much a long read may be compressed.',
                hintLabel: 'What does this change?',
                min: 1,
                max: 1.3,
                step: 0.01,
            },
        ],
    },
]

const FIELDS: readonly FieldSpec[] = SECTIONS.flatMap(
    (section) => section.fields
)

/** `null` = the editor explicitly reverted this field back to the inherited value. */
type DraftValue = number | null

const RevertIcon = ICON.reset

export function EditorialSettingsPanel({
    scope,
    scopeId,
    market,
    station,
}: EditorialSettingsPanelProps) {
    const stored = useEditorialSettings(scope, scopeId)
    const effective = useEffectiveEditorialSettings(market, station)
    const save = useUpsertEditorialSettings()
    const clear = useDeleteEditorialSettings()
    // Holds ONLY fields the editor has touched; everything else reads through to what is stored.
    // Copying the stored row into state would need an effect to resync after each save — the
    // cascading-render pattern react-hooks 7 rejects — and would also silently discard a
    // concurrent change made elsewhere.
    const [draft, setDraft] = useState<Record<string, DraftValue>>({})

    // Stored denylist for THIS scope: null means the scope inherits, which is different from an
    // empty list ("consult everything, ignore the wider denylist"). `undefined` = untouched, so a
    // save leaves the field alone rather than flattening an inherit into an explicit choice.
    const [sourceDraft, setSourceDraft] = useState<string[] | null | undefined>(
        undefined
    )
    const storedDisabled = stored.data?.DisabledTrafficSources ?? null
    const effectiveDisabled = ALL_TRAFFIC_SOURCES.filter(
        (id) => !(effective.data?.EnabledTrafficSources ?? []).includes(id)
    )
    const disabledNow =
        sourceDraft !== undefined
            ? (sourceDraft ?? effectiveDisabled)
            : (storedDisabled ?? effectiveDisabled)
    const sourcesAreOwned =
        sourceDraft !== undefined
            ? sourceDraft !== null
            : storedDisabled !== null

    const toggleSource = (id: string) => {
        const next = disabledNow.includes(id)
            ? disabledNow.filter((s) => s !== id)
            : [...disabledNow, id]
        setSourceDraft(next)
    }

    const pascal = (key: FieldKey) => key.charAt(0).toUpperCase() + key.slice(1)

    const readNumber = (
        source: Record<string, unknown> | undefined,
        key: FieldKey
    ) => {
        const raw = source?.[pascal(key)]
        return typeof raw === 'number' ? raw : undefined
    }

    const storedNumber = (key: FieldKey) =>
        readNumber(stored.data as unknown as Record<string, unknown>, key)
    /** What a bulletin here would use if this scope set nothing. */
    const inheritedNumber = (key: FieldKey) =>
        readNumber(effective.data as unknown as Record<string, unknown>, key)

    /** True when THIS scope owns the value, rather than taking the wider one. */
    const isOwned = (key: FieldKey) => {
        const d = draft[key]
        if (d === null) return false
        if (typeof d === 'number') return true
        return storedNumber(key) !== undefined
    }

    /**
     * A slider always has a position, so there is no "empty box means inherit" trick to explain:
     * the thumb shows the value in force, and whether it is this scope's own is said once per row.
     */
    const valueOf = (key: FieldKey, field: FieldSpec) => {
        const d = draft[key]
        if (typeof d === 'number') return d
        if (d === null) return inheritedNumber(key) ?? field.min
        return storedNumber(key) ?? inheritedNumber(key) ?? field.min
    }

    const forSave = (key: FieldKey): number | null => {
        const d = draft[key]
        if (d === null) return null
        if (typeof d === 'number') return d
        return storedNumber(key) ?? null
    }

    const ownedCount = FIELDS.filter((field) => isOwned(field.key)).length

    const handleSave = () => {
        const body: Record<string, unknown> = {
            scope,
            scopeId: scope === 'Global' ? null : scopeId,
        }
        for (const field of FIELDS) body[field.key] = forSave(field.key)
        // This PUT is a whole-record upsert, so an untouched denylist must be RESENT rather than
        // omitted — sending null while only editing a cap would silently clear the scope's sources.
        body.disabledTrafficSources =
            sourceDraft !== undefined ? sourceDraft : storedDisabled

        save.mutate(body as unknown as UpsertEditorialSettingsRequest, {
            onSuccess: () => toast.success('Settings saved.'),
            onError: (error) =>
                toast.error(
                    error instanceof Error
                        ? error.message
                        : 'Could not save these settings.'
                ),
        })
    }

    // A slider always has a thumb somewhere, so an unknown inherited value cannot be rendered as
    // "empty" the way a text box could — it would silently show `min`, which reads as a confident
    // wrong number (1 road, 1 minute). Wait for the effective values instead.
    if (!effective.data) return <ListSkeleton />

    return (
        <div className="flex flex-col gap-8">
            {SECTIONS.map((section) => (
                <section key={section.title}>
                    <div className="flex items-baseline gap-2">
                        <h4 className="text-sm font-medium">{section.title}</h4>
                        <p className="text-xs text-muted-foreground">
                            {section.blurb}
                        </p>
                    </div>

                    {/* Two per row: a bounded range needs enough track to aim at, not the whole
                        tab. At full width the slider was ~500px of travel for a 0–25 value, so
                        the extra span bought precision nobody needs and pushed the section to
                        twice the height it earns. */}
                    <div className="mt-3 grid gap-x-10 gap-y-1 xl:grid-cols-2">
                        {section.fields.map((field) => {
                            const id = `editorial-${field.key}`
                            const value = valueOf(field.key, field)
                            const owned = isOwned(field.key)
                            const inherited = inheritedNumber(field.key)
                            return (
                                // Name on the left, the range in the middle, the value and its
                                // provenance on the right. One line per setting.
                                <div
                                    key={field.key}
                                    className="grid grid-cols-[minmax(7.5rem,12.5rem)_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-2.5 hover:bg-muted/40"
                                >
                                    <div className="flex min-w-0 items-center gap-1.5">
                                        <Label
                                            htmlFor={id}
                                            className="truncate"
                                        >
                                            {field.label}
                                        </Label>
                                        {field.hint !== undefined && (
                                            <InfoHint label={field.hintLabel}>
                                                {field.hint}
                                            </InfoHint>
                                        )}
                                    </div>

                                    <Slider
                                        id={id}
                                        aria-label={field.label}
                                        min={field.min}
                                        max={field.max}
                                        step={field.step ?? 1}
                                        value={[value]}
                                        onValueChange={([next]) =>
                                            setDraft((prev) => ({
                                                ...prev,
                                                [field.key]: next ?? field.min,
                                            }))
                                        }
                                    />

                                    <div className="flex w-[7.25rem] items-center justify-end gap-1.5">
                                        <span className="tabular-nums text-sm font-medium">
                                            {value}
                                        </span>
                                        <span className="w-8 text-xs text-muted-foreground">
                                            {field.unit}
                                        </span>
                                        {owned ? (
                                            <Button
                                                variant="ghost"
                                                size="icon"
                                                className="size-7"
                                                aria-label={`Revert ${field.label} to inherited`}
                                                title={
                                                    inherited === undefined
                                                        ? 'Revert to inherited'
                                                        : `Revert to ${inherited}`
                                                }
                                                onClick={() =>
                                                    setDraft((prev) => ({
                                                        ...prev,
                                                        [field.key]: null,
                                                    }))
                                                }
                                            >
                                                <RevertIcon aria-hidden />
                                            </Button>
                                        ) : (
                                            // Holds the column so every row's value sits on the
                                            // same axis whether or not it can be reverted.
                                            <span className="size-7" />
                                        )}
                                    </div>
                                </div>
                            )
                        })}
                    </div>
                </section>
            ))}

            <section>
                <div className="flex items-baseline gap-2">
                    <h4 className="text-sm font-medium">
                        Traffic data sources
                    </h4>
                    <p className="text-xs text-muted-foreground">
                        Road authorities this station corroborates against. HERE
                        is always consulted.
                    </p>
                    <InfoHint label="What does switching a source off do?">
                        An off source is simply not requested. A source that is
                        on and failing keeps reporting on the bulletin, so this
                        is not a way to silence a broken feed.
                    </InfoHint>
                    {sourcesAreOwned && (
                        <Badge variant="secondary" className="ml-auto">
                            Yours
                        </Badge>
                    )}
                </div>

                {/* Switch, not a raw checkbox: Switch is this app's on/off control (see the
                    station content toggles), and browser-default checkboxes were the one
                    visually foreign element on the page. Switch sits tight against its OWN
                    label — spread across columns with justify-between, each toggle sat nearer
                    the next feed's name than its own. */}
                <div className="mt-3 grid gap-x-8 gap-y-1 sm:grid-cols-2 xl:grid-cols-3">
                    {ALL_TRAFFIC_SOURCES.map((id) => {
                        const switchId = `editorial-source-${id}`
                        const isOn = !disabledNow.includes(id)
                        return (
                            <div
                                key={id}
                                className="flex items-center gap-3 rounded-md px-2 py-2 hover:bg-muted/40"
                            >
                                <Switch
                                    id={switchId}
                                    checked={isOn}
                                    onCheckedChange={() => toggleSource(id)}
                                    aria-label={sourceDisplayName(id)}
                                />
                                <Label
                                    htmlFor={switchId}
                                    className={
                                        isOn
                                            ? 'cursor-pointer'
                                            : 'cursor-pointer text-muted-foreground'
                                    }
                                >
                                    {sourceDisplayName(id)}
                                </Label>
                            </div>
                        )
                    })}
                </div>
            </section>

            <div className="flex flex-wrap items-center gap-2 border-t pt-4">
                {/* Replaces the old "empty box inherits the greyed value" legend: with a slider
                    there is no empty state to explain, so the only thing worth saying is how
                    much of this scope is its own. */}
                <p className="text-xs text-muted-foreground">
                    {ownedCount === 0
                        ? `Every value is inherited${scope === 'Global' ? '' : ' from the network default'}.`
                        : `${ownedCount} of ${FIELDS.length} values set here; the rest are inherited.`}
                </p>
                <Button
                    variant="outline"
                    className="ml-auto"
                    disabled={clear.isPending}
                    onClick={() =>
                        clear.mutate(
                            { scope, scopeId },
                            {
                                onSuccess: () =>
                                    toast.success(
                                        'Cleared: this scope now inherits.'
                                    ),
                            }
                        )
                    }
                >
                    Clear and inherit
                </Button>
                <Button onClick={handleSave} disabled={save.isPending}>
                    {save.isPending ? 'Saving…' : 'Save settings'}
                </Button>
            </div>
        </div>
    )
}
