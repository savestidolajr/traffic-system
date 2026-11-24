import { useState } from 'react'
import { RuleComposer } from '@/components/RuleComposer'
import { ComposerField } from '@/components/ComposerField'
import { Input } from '@/components/ui/input'
import {
    Select,
    SelectTrigger,
    SelectValue,
    SelectContent,
    SelectItem,
} from '@/components/ui/select'
import { ICON } from '@/lib/design/icons'
import { TYPE_LABEL, TYPE_HINT, MAX_TEXT } from '../lib/display'
import { GUIDANCE_TYPES, type GuidanceTypeName } from '../api/schemas'

const AddIcon = ICON.add
const EditIcon = ICON.edit
const PromoteIcon = ICON.ai

export type ComposerMode = 'create' | 'edit' | 'promote'

export interface ComposerSubmit {
    id?: string
    /** Scope value: GLOBAL_GROUP_ID sentinel or a marketId. */
    scope: string
    type: GuidanceTypeName
    text: string
    enabled: boolean
}

const TITLE: Record<ComposerMode, string> = {
    create: 'Add a guidance note',
    edit: 'Edit a guidance note',
    promote: 'Promote an operator note',
}

const SAVE_LABEL: Record<ComposerMode, string> = {
    create: 'Add',
    edit: 'Save changes',
    promote: 'Promote',
}

interface GuidanceComposerProps {
    readonly mode: ComposerMode
    readonly initial: ComposerSubmit
    readonly submitting: boolean
    readonly onSubmit: (values: ComposerSubmit) => void
    readonly onCancel: () => void
}

/**
 * Inline add/edit/promote composer — one field row (scope · type · guidance)
 * ending in the primary action, mirroring the banned-words and priority-roads
 * composers. The page remounts this via `key` when the edit/promote target
 * changes, so internal state seeds cleanly from `initial`. `enabled` passes
 * through unchanged (new guidance defaults on; toggle it in the list row).
 */
export function GuidanceComposer({
    mode,
    initial,
    submitting,
    onSubmit,
    onCancel,
}: GuidanceComposerProps) {
    const scope = initial.scope
    const [type, setType] = useState<GuidanceTypeName>(initial.type)
    const [text, setText] = useState(initial.text)

    const trimmed = text.trim()
    const valid =
        trimmed.length > 0 && trimmed.length <= MAX_TEXT && scope !== ''

    const submit = () => {
        if (!valid || submitting) return
        onSubmit({
            id: initial.id,
            scope,
            type,
            text: trimmed,
            enabled: initial.enabled,
        })
    }

    const SaveIcon =
        mode === 'promote' ? PromoteIcon : mode === 'edit' ? EditIcon : AddIcon
    return (
        <RuleComposer
            title={mode === 'create' ? undefined : TITLE[mode]}
            onCancel={onCancel}
            submit={{
                label: SAVE_LABEL[mode],
                icon: SaveIcon,
                onClick: submit,
                disabled: !valid || submitting,
                ready: valid,
                submitting,
                tooltip:
                    mode === 'create' && trimmed.length === 0
                        ? 'Enter guidance text to enable the Add button'
                        : undefined,
            }}
        >
            <ComposerField label="Type" width="sm:w-44">
                <Select
                    value={type}
                    onValueChange={(v) => setType(v as GuidanceTypeName)}
                >
                    <SelectTrigger aria-label="Type">
                        <SelectValue placeholder="Choose a type" />
                    </SelectTrigger>
                    <SelectContent>
                        {GUIDANCE_TYPES.map((t) => (
                            /* Even a plain one-word label ("Order"? "Avoid"?)
                               benefits from one more line of context, so
                               TYPE_HINT rides along as the item description.
                               It must stay in `description` — inside the
                               label it lands in the trigger and overflows it. */
                            <SelectItem
                                key={t}
                                value={t}
                                description={TYPE_HINT[t]}
                            >
                                {TYPE_LABEL[t]}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            </ComposerField>
            <ComposerField
                label="Guidance"
                htmlFor="composer-text"
                width="flex-1"
            >
                <Input
                    id="composer-text"
                    maxLength={MAX_TEXT}
                    placeholder="for example, Lead with the worst delay, then work outwards"
                    value={text}
                    onChange={(e) => setText(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter') submit()
                    }}
                />
            </ComposerField>
        </RuleComposer>
    )
}
