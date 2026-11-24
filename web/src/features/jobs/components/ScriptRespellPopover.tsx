import { useState } from 'react'
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover'
import { Input } from '@/components/ui/input'
import { PRONUNCIATION_WORD_MAX } from '@/features/pronunciation/lib/pronunciation-form'
import { RespellingField } from './RespellingField'

export interface RespellTarget {
    /** Prefilled rule target, taken from the word or known road phrase under the pointer. */
    readonly text: string
    /** The target's own element — what the popover is positioned against. */
    readonly anchor: HTMLElement
}

interface ScriptRespellPopoverProps {
    readonly target: RespellTarget
    readonly dictionaryName: string
    readonly onSave: (word: string, replacement: string) => Promise<void>
    readonly onClose: () => void
    /** Pointer entered the panel — cancel the pending hover-close. */
    readonly onKeepOpen: () => void
    /** Pointer left the panel — start the hover-close countdown. */
    readonly onScheduleClose: () => void
}

/**
 * Teach the voice any word in the script, flagged or not.
 *
 * The marked-word popover only reaches words the pre-review or the dictionary
 * already touched. This one is opened by resting the pointer on ANY word, so a
 * mispronunciation nobody flagged takes the same gesture to fix — and clicking a
 * word stays a plain seek, which is what the operator does all day.
 *
 * The target stays editable because the operator often hears a phrase ("Bell St")
 * where they can only rest on one word.
 */
export function ScriptRespellPopover({
    target,
    dictionaryName,
    onSave,
    onClose,
    onKeepOpen,
    onScheduleClose,
}: ScriptRespellPopoverProps) {
    const [word, setWord] = useState(target.text)

    return (
        <Popover
            open
            onOpenChange={(open) => {
                if (!open) onClose()
            }}
        >
            <PopoverAnchor virtualRef={{ current: target.anchor }} />
            <PopoverContent
                align="start"
                sideOffset={6}
                className="w-64"
                onMouseEnter={onKeepOpen}
                onMouseLeave={onScheduleClose}
                // Hover opened this, so it must never take focus: the operator is
                // still reading, and the audio player's space/arrow keys only work
                // while focus is inside it. They click a field when they mean to type.
                onOpenAutoFocus={(event) => event.preventDefault()}
                onCloseAutoFocus={(event) => event.preventDefault()}
                // The hovered word is the ANCHOR, which sits outside the content, so
                // Radix would treat clicking it (to seek) as a dismissal and the
                // panel would vanish under the cursor. Exempt it only.
                onInteractOutside={(event) => {
                    if (target.anchor.contains(event.target as Node))
                        event.preventDefault()
                }}
            >
                <RespellingField
                    word={word.trim()}
                    dictionaryName={dictionaryName}
                    onSave={async (replacement) => {
                        await onSave(word.trim(), replacement)
                        onClose()
                    }}
                    targetField={
                        <Input
                            value={word}
                            onChange={(e) => setWord(e.target.value)}
                            placeholder="word or phrase"
                            aria-label="Word or phrase to respell"
                            maxLength={PRONUNCIATION_WORD_MAX}
                            className="h-8 text-xs"
                        />
                    }
                />
            </PopoverContent>
        </Popover>
    )
}
