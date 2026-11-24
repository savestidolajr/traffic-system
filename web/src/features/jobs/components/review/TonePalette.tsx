import { useState } from 'react'
import { Input } from '@/components/ui/input'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectSeparator,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { TONE_PRESETS, normaliseTag, toneLabel } from '../../lib/tone-marks'
import { cn } from '@/lib/utils'

/** A tone the voice cannot act on is worse than no tone, so say why up front. */
export interface ToneCapability {
    /** Resolved TTS model for this bulletin; tags are only honoured on v3. */
    readonly model: string
}

interface TonePaletteProps {
    /** Tags this operator has used before, most recent first. */
    readonly recent: readonly string[]
    /** Insert the chosen tag into the script at the caret. */
    readonly onInsert: (tag: string) => void
}

/** Radix Select needs a non-empty string per item; custom travels as a sentinel. */
const CUSTOM = 'action:custom'

/**
 * The delivery-tone palette for the script editor. Unlike a per-line control,
 * it inserts the tag INTO the script at the caret, so the operator sees the
 * tag exactly where it will sit among the words — and the mirror renders it as
 * an inline chip. Typing `[slow]` by hand works identically; the palette is
 * for discoverability, not the only path.
 */
export function TonePalette({ recent, onInsert }: TonePaletteProps) {
    const [typing, setTyping] = useState(false)
    const [draft, setDraft] = useState('')

    const parsed = draft.trim() ? normaliseTag(draft) : null

    const close = (next?: string) => {
        if (next !== undefined) onInsert(next)
        setTyping(false)
        setDraft('')
    }

    const WIDTH = 'h-8 w-[7.25rem] shrink-0 text-xs'

    if (typing) {
        return (
            <Input
                autoFocus
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
                onBlur={() =>
                    close(parsed?.ok === true ? parsed.tag : undefined)
                }
                onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                        event.preventDefault()
                        if (parsed?.ok === true) close(parsed.tag)
                    }
                    if (event.key === 'Escape') {
                        event.preventDefault()
                        setDraft('')
                        setTyping(false)
                    }
                }}
                placeholder="type a tone"
                aria-label="Type a delivery tone"
                aria-invalid={parsed?.ok === false}
                title={parsed?.ok === false ? parsed.error : undefined}
                className={cn(
                    WIDTH,
                    'px-2 font-sans',
                    parsed?.ok === false && 'border-destructive'
                )}
            />
        )
    }

    const options = [...recent, ...TONE_PRESETS].filter(
        (t, i, all) => all.indexOf(t) === i
    )

    return (
        <Select
            onValueChange={(next) => {
                if (next !== CUSTOM) {
                    onInsert(next)
                    return
                }
                setDraft('')
                // Radix closes the list BEFORE this fires and then restores
                // focus to the trigger. Mounting the input inside that
                // sequence gets it blurred straight back out, so hand over on
                // the next frame, once focus has settled.
                requestAnimationFrame(() => setTyping(true))
            }}
        >
            <SelectTrigger
                className={cn(
                    WIDTH,
                    'gap-1.5 border-dashed border-input/70 px-2 font-normal text-muted-foreground'
                )}
                aria-label="Add a delivery tone at the cursor"
            >
                <SelectValue placeholder="Add tone" />
            </SelectTrigger>
            <SelectContent align="end" className="min-w-36">
                {options.map((preset) => (
                    <SelectItem key={preset} value={preset}>
                        {toneLabel(preset)}
                    </SelectItem>
                ))}
                <SelectSeparator />
                <SelectItem value={CUSTOM}>Custom…</SelectItem>
            </SelectContent>
        </Select>
    )
}
