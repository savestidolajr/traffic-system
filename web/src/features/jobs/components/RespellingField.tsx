import { useState, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { InfoHint } from '@/components/InfoHint'
import { Input } from '@/components/ui/input'
import { PRONUNCIATION_REPLACEMENT_MAX } from '@/features/pronunciation/lib/pronunciation-form'

interface RespellingFieldProps {
    /** The word or phrase the rule will target; drives the accessible name. */
    readonly word: string
    readonly dictionaryName: string
    /** Whether a rule already rewrites this target. Omit when the caller
     *  cannot know — the hint then states the outcome without claiming which it is. */
    readonly hasExistingRule?: boolean
    /** A rejected promise leaves the typed value in place to retry. */
    readonly onSave: (replacement: string) => Promise<void>
    /** Slotted between the heading and the respelling row (e.g. a target field). */
    readonly targetField?: ReactNode
    /** Appended to the hint by surfaces where a flag is also in play. */
    readonly note?: string
}

function outcome(dictionaryName: string, hasExistingRule?: boolean): string {
    if (hasExistingRule === true)
        return ` Replaces the current rule in ${dictionaryName}: one rule per word or phrase.`
    if (hasExistingRule === false) return ` Adds a rule to ${dictionaryName}.`
    return ` Saves the rule in ${dictionaryName}: one rule per word or phrase.`
}

/**
 * The one place an operator types how a word or phrase should sound. Shared by
 * the marked-word popover and the hover-any-word popover so a respelling looks
 * and behaves identically wherever the operator reaches it.
 */
export function RespellingField({
    word,
    dictionaryName,
    hasExistingRule,
    onSave,
    targetField,
    note,
}: RespellingFieldProps) {
    const [respelling, setRespelling] = useState('')
    const [saving, setSaving] = useState(false)

    const save = async () => {
        const replacement = respelling.trim()
        if (!replacement || !word || saving) return
        setSaving(true)
        try {
            await onSave(replacement)
            setRespelling('')
        } finally {
            setSaving(false)
        }
    }

    return (
        <div className="flex flex-col gap-1.5">
            <div className="flex items-center gap-1.5">
                <span className="text-2xs font-medium text-muted-foreground">
                    Say it differently
                </span>
                <InfoHint label="What does changing the pronunciation do?">
                    Changes what listeners hear, from the next bulletin.
                    {outcome(dictionaryName, hasExistingRule)}
                    {note ? ` ${note}` : ''}
                </InfoHint>
            </div>
            {targetField}
            <div className="flex items-center gap-1.5">
                <Input
                    value={respelling}
                    onChange={(e) => setRespelling(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                            e.preventDefault()
                            void save()
                        }
                    }}
                    placeholder="how it should sound"
                    aria-label={word ? `Respelling for ${word}` : 'Respelling'}
                    maxLength={PRONUNCIATION_REPLACEMENT_MAX}
                    className="h-8 text-xs"
                />
                <Button
                    size="sm"
                    disabled={!respelling.trim() || !word || saving}
                    onClick={() => void save()}
                >
                    Save
                </Button>
            </div>
        </div>
    )
}
