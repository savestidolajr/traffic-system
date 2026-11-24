import { useState } from 'react'
import { ICON } from '@/lib/design/icons'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { CUE_GROUPS, cueLabel, isCuratedTag, normalizeTag } from '../cues'

interface PermittedCueSelectorProps {
    readonly value: string[]
    readonly onChange: (value: string[]) => void
}

/**
 * The `allowedTags` guardrail as human-labelled toggle chips grouped by kind.
 * Raw `[tag]` strings never surface for the curated set. Each group has an
 * "Others" input so an operator can add their own v3 cue (normalised to a tag);
 * effectiveness then depends on the chosen voice. At least one cue must stay
 * selected, otherwise the director no-ops to a neutral read.
 */
export function PermittedCueSelector({
    value,
    onChange,
}: PermittedCueSelectorProps) {
    const [openGroup, setOpenGroup] = useState<string | null>(null)
    const [draft, setDraft] = useState('')
    const [addError, setAddError] = useState<string | null>(null)
    const [blockedEmpty, setBlockedEmpty] = useState(false)

    const customTags = value.filter((t) => !isCuratedTag(t))

    const toggle = (tag: string) => {
        if (value.includes(tag)) {
            if (value.length === 1) {
                setBlockedEmpty(true)
                return
            }
            onChange(value.filter((t) => t !== tag))
        } else {
            setBlockedEmpty(false)
            onChange([...value, tag])
        }
    }

    const removeTag = (tag: string) => {
        if (value.length === 1) {
            setBlockedEmpty(true)
            return
        }
        onChange(value.filter((t) => t !== tag))
    }

    const submitCustom = () => {
        const normalized = normalizeTag(draft)
        if (!normalized) {
            setAddError('Use 1 or 2 words, letters only.')
            return
        }
        if (!value.includes(normalized)) {
            setBlockedEmpty(false)
            onChange([...value, normalized])
        }
        setDraft('')
        setAddError(null)
        setOpenGroup(null)
    }

    return (
        <div className="flex flex-col gap-3">
            {CUE_GROUPS.map((group) => {
                const adding = openGroup === group.name
                return (
                    <div key={group.name} className="flex flex-col gap-1.5">
                        <span className="text-xs font-medium text-muted-foreground">
                            {group.name}
                        </span>
                        <div className="flex flex-wrap items-center gap-1.5">
                            {group.cues.map((cue) => {
                                const selected = value.includes(cue.tag)
                                return (
                                    <button
                                        key={cue.tag}
                                        type="button"
                                        aria-pressed={selected}
                                        onClick={() => toggle(cue.tag)}
                                        className={cn(
                                            'h-8 rounded-full border px-3 text-xs transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                                            selected
                                                ? 'border-primary bg-primary/10 text-primary-on-tint'
                                                : 'border-border/60 hover:bg-muted/40'
                                        )}
                                    >
                                        {cue.label}
                                    </button>
                                )
                            })}
                            {!adding && (
                                <button
                                    type="button"
                                    onClick={() => {
                                        setOpenGroup(group.name)
                                        setDraft('')
                                        setAddError(null)
                                    }}
                                    className="flex h-8 items-center gap-1 rounded-full border border-dashed border-border/60 px-3 text-xs text-muted-foreground hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                >
                                    <ICON.add className="size-3.5" />
                                    Others
                                </button>
                            )}
                        </div>
                        {adding && (
                            <div className="flex flex-col gap-1">
                                <div className="flex items-center gap-2">
                                    <Input
                                        autoFocus
                                        value={draft}
                                        placeholder="for example, deadpan"
                                        aria-label={`Add a custom ${group.name.toLowerCase()} cue`}
                                        className="h-8 max-w-48 text-xs"
                                        onChange={(e) => {
                                            setDraft(e.target.value)
                                            setAddError(null)
                                        }}
                                        onKeyDown={(e) => {
                                            if (e.key === 'Enter') {
                                                e.preventDefault()
                                                submitCustom()
                                            }
                                        }}
                                    />
                                    <Button
                                        type="button"
                                        size="sm"
                                        variant="outline"
                                        onClick={submitCustom}
                                    >
                                        Add
                                    </Button>
                                    <Button
                                        type="button"
                                        size="sm"
                                        variant="ghost"
                                        onClick={() => {
                                            setOpenGroup(null)
                                            setAddError(null)
                                        }}
                                    >
                                        Cancel
                                    </Button>
                                </div>
                                <p className="text-xs text-muted-foreground">
                                    {group.hint} 1 or 2 words; effect depends on
                                    the voice, so audition to check.
                                </p>
                                {addError && (
                                    <p className="text-xs text-destructive">
                                        {addError}
                                    </p>
                                )}
                            </div>
                        )}
                    </div>
                )
            })}

            {customTags.length > 0 && (
                <div className="flex flex-col gap-1.5">
                    <span className="text-xs font-medium text-muted-foreground">
                        Your added cues
                    </span>
                    <div className="flex flex-wrap gap-1.5">
                        {customTags.map((tag) => (
                            <span
                                key={tag}
                                className="flex h-8 items-center gap-1 rounded-full border border-primary bg-primary/10 px-3 text-xs text-primary-on-tint"
                            >
                                {cueLabel(tag)}
                                <button
                                    type="button"
                                    aria-label={`Remove ${cueLabel(tag)}`}
                                    onClick={() => removeTag(tag)}
                                    className="rounded-full hover:bg-primary/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                >
                                    <ICON.close className="size-3.5" />
                                </button>
                            </span>
                        ))}
                    </div>
                </div>
            )}

            {blockedEmpty && (
                <p className="text-xs text-warning" role="alert">
                    At least one cue is required so this style can sound
                    different from Neutral.
                </p>
            )}
        </div>
    )
}
