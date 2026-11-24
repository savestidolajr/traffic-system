import { useMemo, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Loader2, ArrowDownUp } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ROUTE_CONFIG_TRANSFER } from '@/lib/routes'
import { useIsAdmin } from '@/hooks/use-current-user'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectLabel,
    SelectSeparator,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { EmptyState } from '@/components/EmptyState'
import { FieldLabel } from '@/components/FieldLabel'
import { InfoHint } from '@/components/InfoHint'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { currentVersion } from '@/features/voices/lib/voice-profile-utils'
import type { VoiceProfile } from '@/features/voices/profile-types'
import { resolveAudioSrc } from '@/lib/api/client'
import { usePreviewPronunciationRule } from '../api/pronunciation-hooks'
import { PHONEME_ALPHABETS } from '../api/pronunciation-schemas'
import type { RuleInput } from '../types'
import {
    newRule,
    PRONUNCIATION_WORD_MAX,
    PRONUNCIATION_REPLACEMENT_MAX,
    ruleRowProblems,
    rulePreviewProblem,
} from '../lib/pronunciation-form'
import { cn } from '@/lib/utils'

const PHONEME_ALPHABET_LABELS: Record<
    (typeof PHONEME_ALPHABETS)[number],
    string
> = {
    ipa: 'IPA',
    cmu: 'CMU',
}

interface PronunciationRuleTableProps {
    readonly rules: RuleInput[]
    readonly onChange: (rules: RuleInput[]) => void
    /** The dictionary being edited (null in create mode) — orders the preview voices. */
    readonly dictionaryId?: string | null
    /** Station labels this dictionary is assigned to (the dictionary DTO's AssignedStations). */
    readonly assignedStations?: readonly string[]
}

/**
 * Rule-table editor for pronunciation dictionaries. Supports alias rows
 * (word → respelling) and phoneme rows (word → IPA/CMU). Phoneme rows
 * show a clear warning that they won't apply on eleven_v3.
 *
 * Rules render in a shadcn Table (a dictionary can hold hundreds), bounded
 * in a scroll region, with a search box that filters by word/phrase or
 * replacement so a single rule is easy to locate in a large set.
 *
 * Every row also carries a play button that auditions THAT rule in the voice
 * selected in the toolbar (the selected voice's own model). Because the
 * backend preview endpoint validates a single candidate through the same
 * domain construction a save uses, the audition works on rules the operator
 * has just typed — the whole reason the voice selector lives here rather
 * than in the saved-dictionary Preview step.
 */
export function PronunciationRuleTable({
    rules,
    onChange,
    dictionaryId = null,
    assignedStations = [],
}: PronunciationRuleTableProps) {
    const [query, setQuery] = useState('')
    // The bulk path lives on the admin-only Data transfer page, so only an admin
    // is pointed at it.
    const isAdmin = useIsAdmin()

    // ── Per-row live validation (duplicate word groups, self-respell, unfinished rows) ──
    const rowProblems = useMemo(() => ruleRowProblems(rules), [rules])

    // ── Preview voice ────────────────────────────────────────────────────────────────
    // Voice profiles belonging to stations assigned this dictionary come FIRST (the station
    // label comes from the DTO; a schedule whose pronunciationDictionaryId is this dictionary
    // is the same assignment read live from schedules, which cannot be stale), then every
    // other active profile under a separator.
    const { data: schedulesData } = useSchedules()
    const { data: voicesData, isLoading: voicesLoading } = useVoiceProfiles()

    const assignedVoiceProfileIds = useMemo(() => {
        const ids = new Set<string>()
        const labelToProfileId = new Map<string, string>()
        for (const schedule of schedulesData?.schedules ?? []) {
            const label = schedule.station ?? schedule.name ?? schedule.marketId
            if (label && schedule.voiceProfileId) {
                labelToProfileId.set(label, schedule.voiceProfileId)
                // Live assignment: this schedule uses THIS dictionary.
                if (
                    dictionaryId &&
                    schedule.pronunciationDictionaryId === dictionaryId
                ) {
                    ids.add(schedule.voiceProfileId)
                }
            }
        }
        for (const station of assignedStations) {
            const id = labelToProfileId.get(station)
            if (id) ids.add(id)
        }
        return ids
    }, [schedulesData, assignedStations, dictionaryId])

    const profiles = useMemo(() => {
        const active = (voicesData?.profiles ?? []).filter(
            (p) => p.status === 'active'
        )
        return {
            assigned: active.filter((p) => assignedVoiceProfileIds.has(p.id)),
            rest: active.filter((p) => !assignedVoiceProfileIds.has(p.id)),
        }
    }, [voicesData, assignedVoiceProfileIds])

    // The picker's state holds ONLY an explicit operator choice. The effective voice falls
    // back to the first assigned-station voice (else the first active profile) so a freshly
    // opened editor can play immediately. Deriving it beats storing it and syncing in an
    // effect: the effect version rendered once with no voice, then again with one, and had
    // to re-fire whenever the library reloaded.
    const [voiceOverrideId, setVoiceOverrideId] = useState<string | null>(null)
    const orderedProfiles = useMemo(
        () => [...profiles.assigned, ...profiles.rest],
        [profiles]
    )
    const selectedProfile = useMemo(() => {
        const chosen =
            voiceOverrideId === null
                ? undefined
                : orderedProfiles.find((p) => p.id === voiceOverrideId)
        // An override naming a profile that has since been archived falls back rather
        // than leaving the picker pointing at nothing.
        return chosen ?? orderedProfiles[0] ?? null
    }, [voiceOverrideId, orderedProfiles])

    const addRow = (type: 'alias' | 'phoneme') => {
        // Clearing the search guarantees the freshly-added (empty) row is
        // visible — otherwise an active filter would hide it immediately.
        setQuery('')
        onChange([...rules, newRule(type)])
    }

    const removeRow = (index: number) => {
        const next = rules.filter((_, i) => i !== index)
        onChange(next)
    }

    const updateRow = (index: number, patch: Partial<RuleInput>) => {
        const next = rules.map((r, i) => (i === index ? { ...r, ...patch } : r))
        onChange(next)
    }

    // Keep each rule paired with its index in the source array so edits and
    // removes target the right rule even when the view is filtered.
    const visible = useMemo(() => {
        const indexed = rules.map((rule, index) => ({ rule, index }))
        const q = query.trim().toLowerCase()
        if (!q) return indexed
        return indexed.filter(
            ({ rule }) =>
                rule.stringToReplace.toLowerCase().includes(q) ||
                rule.replacement.toLowerCase().includes(q)
        )
    }, [rules, query])

    // Count what a save would persist, not what is on screen: the studio drops rows with no
    // word (the blank row "+ Alias rule" adds), so counting every row made the header disagree
    // with the saved version's rule count.
    const savedCount = useMemo(
        () => rules.filter((r) => r.stringToReplace.trim().length > 0).length,
        [rules]
    )

    return (
        <div className="flex flex-col gap-3">
            <FieldLabel
                required
                hintLabel="What is a pronunciation rule?"
                hint="Fixes for place names, road names, or any word TTS struggles with. Alias rules apply on every model; phoneme rules only apply on v2-family models (eleven_v3 skips them)."
            >
                Pronunciation rules
                {savedCount > 0 && (
                    <span className="ml-1 font-normal text-muted-foreground">
                        ({savedCount})
                    </span>
                )}
            </FieldLabel>

            {rules.length === 0 ? (
                <EmptyState variant="row">
                    No rules yet. Add an alias or phoneme rule below.
                </EmptyState>
            ) : (
                <>
                    {/* One toolbar above the table: find a rule on the left, pick the
                        voice every row's play button auditions in on the right. Both
                        controls act on the table, so they sit with it rather than in
                        the section header. */}
                    <div className="flex flex-wrap items-center gap-2">
                        <div className="relative min-w-56 flex-1">
                            <ICON.search className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                            <Input
                                placeholder="Search rules by word or respelling…"
                                value={query}
                                onChange={(e) => setQuery(e.target.value)}
                                className="pl-8"
                                aria-label="Search pronunciation rules"
                            />
                        </div>
                        <Label
                            htmlFor="rule-preview-voice"
                            className="text-sm text-muted-foreground"
                        >
                            Preview voice
                        </Label>
                        <Select
                            value={selectedProfile?.id ?? ''}
                            onValueChange={setVoiceOverrideId}
                            disabled={voicesLoading}
                        >
                            <SelectTrigger
                                id="rule-preview-voice"
                                className="w-64"
                                aria-label="Preview voice"
                            >
                                <SelectValue
                                    placeholder={
                                        voicesLoading
                                            ? 'Loading voices…'
                                            : 'Select a voice'
                                    }
                                />
                            </SelectTrigger>
                            <SelectContent>
                                {profiles.assigned.length > 0 && (
                                    <SelectGroup>
                                        <SelectLabel>
                                            Assigned stations
                                        </SelectLabel>
                                        {profiles.assigned.map((profile) => (
                                            <SelectItem
                                                key={profile.id}
                                                value={profile.id}
                                            >
                                                {profile.name}:{' '}
                                                {profile.baseVoiceName}
                                            </SelectItem>
                                        ))}
                                    </SelectGroup>
                                )}
                                {profiles.assigned.length > 0 &&
                                    profiles.rest.length > 0 && (
                                        <SelectSeparator />
                                    )}
                                {profiles.rest.length > 0 && (
                                    <SelectGroup>
                                        <SelectLabel>Other voices</SelectLabel>
                                        {profiles.rest.map((profile) => (
                                            <SelectItem
                                                key={profile.id}
                                                value={profile.id}
                                            >
                                                {profile.name}:{' '}
                                                {profile.baseVoiceName}
                                            </SelectItem>
                                        ))}
                                    </SelectGroup>
                                )}
                                {!voicesLoading &&
                                    profiles.assigned.length === 0 &&
                                    profiles.rest.length === 0 && (
                                        <SelectItem value="__none" disabled>
                                            No active voices
                                        </SelectItem>
                                    )}
                            </SelectContent>
                        </Select>
                        {/* The model, not the base voice name — the select already shows
                            the voice, and the model is what decides whether phoneme rows
                            can be auditioned at all. */}
                        {selectedProfile && (
                            <span className="text-xs text-muted-foreground">
                                {currentVersion(selectedProfile).model}
                            </span>
                        )}
                    </div>

                    {/* Rules table — bounded scroll for large dictionaries */}
                    <div className="max-h-90 overflow-y-auto rounded-lg border border-border/60">
                        <Table>
                            <TableHeader>
                                <TableRow>
                                    <TableHead className="w-29">Type</TableHead>
                                    <TableHead>Word / phrase</TableHead>
                                    <TableHead>Replacement</TableHead>
                                    <TableHead className="w-16 text-center">
                                        Case
                                    </TableHead>
                                    <TableHead className="w-18 text-center">
                                        Bounds
                                    </TableHead>
                                    <TableHead className="w-10">
                                        <span className="sr-only">Preview</span>
                                    </TableHead>
                                    <TableHead className="w-12">
                                        <span className="sr-only">Actions</span>
                                    </TableHead>
                                </TableRow>
                            </TableHeader>
                            <TableBody>
                                {visible.length === 0 ? (
                                    <TableRow>
                                        <TableCell
                                            colSpan={7}
                                            className="h-20 text-center text-xs text-muted-foreground"
                                        >
                                            No rules match “{query}”.
                                        </TableCell>
                                    </TableRow>
                                ) : (
                                    visible.map(({ rule, index }) => (
                                        <RuleRow
                                            // The uid (assigned when the row was created,
                                            // seeded, or imported) stays with the rule
                                            // across deletes, so per-row state — an
                                            // in-flight or playing preview — survives rows
                                            // above it being removed. Index would shift
                                            // every lower row into a recycled fiber.
                                            key={rule._uid ?? index}
                                            rule={rule}
                                            problem={
                                                rowProblems.get(index)
                                                    ?.message ?? null
                                            }
                                            profile={selectedProfile}
                                            onChange={(patch) =>
                                                updateRow(index, patch)
                                            }
                                            onRemove={() => removeRow(index)}
                                        />
                                    ))
                                )}
                            </TableBody>
                        </Table>
                    </div>
                </>
            )}

            {/* Actions sit under the rows they extend, in the same two-ended grammar as
                the toolbar above: add one rule on the left, move the whole set on the
                right. Export/import used to sit in the section header, which read as a
                header action rather than something acting on the table. */}
            <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                    <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => addRow('alias')}
                    >
                        <ICON.add className="size-3.5" />
                        Alias rule
                    </Button>
                    <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => addRow('phoneme')}
                    >
                        <ICON.add className="size-3.5" />
                        Phoneme rule
                    </Button>
                </div>
                {isAdmin && (
                    <div className="flex items-center gap-2">
                        <Button asChild type="button" variant="ghost" size="sm">
                            <Link
                                to={`${ROUTE_CONFIG_TRANSFER}?sections=PronunciationDictionaries`}
                            >
                                <ArrowDownUp className="size-3.5" />
                                Bulk edit via Data transfer
                            </Link>
                        </Button>
                        <InfoHint label="How do I edit rules in bulk?">
                            Export the pronunciation section as JSON, edit it,
                            and import it back. That path carries phoneme rules
                            as well as aliases, shows a field-level diff before
                            anything changes, and, in production, needs a second
                            admin to approve.
                        </InfoHint>
                    </div>
                )}
            </div>
        </div>
    )
}

interface RuleRowProps {
    readonly rule: RuleInput
    /** Live validation problem for THIS row (duplicate-group member, self-respell, unfinished). */
    readonly problem: string | null
    /** The voice every row's play button auditions in (null while none is selected). */
    readonly profile: VoiceProfile | null
    readonly onChange: (patch: Partial<RuleInput>) => void
    readonly onRemove: () => void
}

function RuleRow({ rule, problem, profile, onChange, onRemove }: RuleRowProps) {
    const isPhoneme = rule.type === 'phoneme'
    // The specific reason this row cannot be previewed, or null when it can — mirrors the
    // backend's rejections so the tooltip teaches the fix (e.g. "eleven_v3 reads inline IPA
    // only") instead of a generic "cannot preview".
    const previewProblem = useMemo(
        () =>
            rulePreviewProblem(
                rule,
                profile ? currentVersion(profile).model : null,
                problem
            ),
        [rule, profile, problem]
    )

    return (
        <TableRow className={problem ? 'bg-destructive/5' : undefined}>
            <TableCell>
                <div className="flex items-center gap-1.5">
                    <Badge variant={isPhoneme ? 'warning' : 'info'}>
                        {isPhoneme ? 'Phoneme' : 'Alias'}
                    </Badge>
                    {isPhoneme && (
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span className="inline-flex">
                                    <ICON.warning className="size-3.5 text-warning" />
                                    <span className="sr-only">
                                        Won't apply on eleven_v3
                                    </span>
                                </span>
                            </TooltipTrigger>
                            <TooltipContent side="top" className="max-w-65">
                                Phoneme rules are silently skipped by eleven_v3.
                                Add an alias respelling for the same word to get
                                on-air effect on all models.
                            </TooltipContent>
                        </Tooltip>
                    )}
                </div>
            </TableCell>
            <TableCell>
                <div className="flex flex-col gap-1">
                    <Input
                        value={rule.stringToReplace}
                        onChange={(e) =>
                            onChange({ stringToReplace: e.target.value })
                        }
                        placeholder="for example, Warringah"
                        className={cn(
                            'h-8 text-sm',
                            problem && 'border-destructive'
                        )}
                        aria-label="Word or phrase"
                        maxLength={PRONUNCIATION_WORD_MAX}
                    />
                    {problem && (
                        <p className="text-xs leading-snug text-destructive">
                            {problem}
                        </p>
                    )}
                </div>
            </TableCell>
            <TableCell>
                <div className="flex items-center gap-1">
                    <Input
                        value={rule.replacement}
                        onChange={(e) =>
                            onChange({ replacement: e.target.value })
                        }
                        placeholder={
                            isPhoneme ? 'wɒrɪŋɡə' : 'for example, Warringer'
                        }
                        className="h-8 text-sm"
                        aria-label={isPhoneme ? 'Phoneme' : 'Respelling'}
                        maxLength={PRONUNCIATION_REPLACEMENT_MAX}
                    />
                    {isPhoneme && (
                        <Select
                            value={rule.alphabet ?? 'ipa'}
                            onValueChange={(v) => onChange({ alphabet: v })}
                        >
                            <SelectTrigger
                                className="h-8 w-[78px]"
                                aria-label="Phoneme alphabet"
                            >
                                <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                                {PHONEME_ALPHABETS.map((alphabet) => (
                                    <SelectItem key={alphabet} value={alphabet}>
                                        {PHONEME_ALPHABET_LABELS[alphabet]}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    )}
                </div>
            </TableCell>
            <TableCell className="text-center">
                <Switch
                    checked={rule.caseSensitive}
                    onCheckedChange={(checked) =>
                        onChange({ caseSensitive: checked })
                    }
                    aria-label="Case sensitive"
                />
            </TableCell>
            <TableCell className="text-center">
                <Switch
                    checked={rule.wordBoundaries}
                    onCheckedChange={(checked) =>
                        onChange({ wordBoundaries: checked })
                    }
                    aria-label="Word boundaries"
                />
            </TableCell>
            <TableCell>
                <RulePreviewPlayButton
                    rule={rule}
                    profile={profile}
                    disabledReason={previewProblem}
                />
            </TableCell>
            <TableCell>
                <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-7"
                    onClick={onRemove}
                    aria-label="Remove rule"
                >
                    <ICON.delete className="size-3.5 text-destructive" />
                </Button>
            </TableCell>
        </TableRow>
    )
}

interface RulePreviewPlayButtonProps {
    readonly rule: RuleInput
    /** The voice to audition in (null while none is selected — disables the button). */
    readonly profile: VoiceProfile | null
    /** The specific reason the row cannot preview, shown on the disabled tooltip. */
    readonly disabledReason: string | null
}

/**
 * Per-row audition: POSTs the candidate rule to the preview-rule endpoint (which validates it
 * through the same domain construction a save uses, so unsaved rules work), then plays the
 * returned clip in its own <audio>. Loading and playing state live on THIS row only.
 */
function RulePreviewPlayButton({
    rule,
    profile,
    disabledReason,
}: RulePreviewPlayButtonProps) {
    const preview = usePreviewPronunciationRule()
    const audioRef = useRef<HTMLAudioElement>(null)
    const [isPlaying, setIsPlaying] = useState(false)
    const [isLoading, setIsLoading] = useState(false)

    const disabled = disabledReason !== null || profile === null

    const play = async () => {
        if (disabled) return
        try {
            setIsLoading(true)
            const res = await preview.mutateAsync({
                type: rule.type,
                stringToReplace: rule.stringToReplace.trim(),
                replacement: rule.replacement.trim(),
                alphabet: rule.alphabet,
                baseVoiceId: profile.baseVoiceId,
                model: currentVersion(profile).model,
            })
            setIsLoading(false)
            const audio = audioRef.current
            if (!audio) return
            audio.src = resolveAudioSrc(res.url)
            await audio.play().catch(() => setIsPlaying(false))
            setIsPlaying(true)
        } catch (err) {
            setIsLoading(false)
            // The backend's 400s are the actionable ones (which model ignores the tag, which
            // part of the rule is unfinished) — surface them verbatim, like doSave does, so
            // the operator learns the fix instead of "preview failed".
            toast.error('Could not preview rule', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Preview failed. Try again.',
            })
        }
    }

    const toggle = () => {
        if (isPlaying) {
            audioRef.current?.pause()
            setIsPlaying(false)
            return
        }
        void play()
    }

    const icon = isLoading ? (
        <Loader2 className="size-3.5 animate-spin" />
    ) : isPlaying ? (
        <ICON.pause className="size-3.5" />
    ) : (
        <ICON.play className="size-3.5" />
    )

    return (
        <>
            {/* The span keeps the tooltip reachable while the button is disabled (a disabled
                button swallows pointer events). The row's phoneme warning uses the same trick. */}
            <Tooltip>
                <TooltipTrigger asChild>
                    <span className="inline-flex">
                        <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="size-7"
                            disabled={disabled}
                            onClick={toggle}
                            aria-label={
                                isLoading
                                    ? 'Preparing preview'
                                    : isPlaying
                                      ? 'Stop preview'
                                      : 'Preview rule'
                            }
                        >
                            {icon}
                        </Button>
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top" className="max-w-65">
                    {disabledReason ??
                        'Preview this rule in the selected voice.'}
                </TooltipContent>
            </Tooltip>
            <audio
                ref={audioRef}
                onEnded={() => setIsPlaying(false)}
                preload="none"
            />
        </>
    )
}
