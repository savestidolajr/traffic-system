import { Lock, RotateCcw, Trash2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import {
    PLAYBOOK_SECTION_LABELS,
    PLAYBOOK_SECTIONS,
    type BulletinPlaybook,
    type PlaybookBlock,
    type PlaybookOverlay,
    type PlaybookSection,
} from '../../types'

export interface EditableBlock extends PlaybookBlock {
    readonly originalBody: string
    readonly isAdded: boolean
}
export interface PromptAgent {
    readonly role: 'writeIncidents' | 'writeFlow' | 'compose2'
    readonly title: string
    readonly blurb: string
}

export const PROMPT_AGENTS: readonly PromptAgent[] = [
    {
        role: 'writeIncidents',
        title: 'Incidents Desk',
        blurb: 'Briefs what has happened across the network: crashes, closures, hazards, and roadworks.',
    },
    {
        role: 'writeFlow',
        title: 'Flow Desk',
        blurb: 'Briefs how traffic is moving: congestion queues, clear runs, and monitored drive times.',
    },
    {
        role: 'compose2',
        title: 'Composer',
        blurb: 'Assembles the spoken radio bulletin: pacing, phrasing variety, presenter style, and length targets.',
    },
]

export interface RuleMeasure {
    readonly field: string
    readonly note: string
}

export const RULE_MEASURES: readonly RuleMeasure[] = [
    {
        field: 'speed fraction',
        note: 'Fraction of normal speed (for example, 0.20 is a fifth of normal speed).',
    },
    {
        field: 'delay minutes',
        note: 'Measured minutes delayed compared to typical free-flow conditions.',
    },
    {
        field: 'jam factor',
        note: 'HERE congestion index from 0 (clear) to 10 (standstill).',
    },
]

export const CONDITION_BLOCK_LABELS: Readonly<Record<string, string>> = {
    'voice.crash': 'Crashes and Collisions',
    'voice.congestion': 'Heavy Congestion and Queues',
    'voice.roadworks': 'Roadworks and Construction',
    'voice.closure': 'Road Closures',
    'voice.hazard': 'Hazards and Debris',
    'voice.breakdown': 'Breakdowns and Stalled Vehicles',
    'voice.specialevent': 'Special Events and Operations',
    'voice.clear-runs': 'Clear Runs and Flowing Arterials',
    'voice.drive-times': 'Monitored Drive Times',
    'voice.unclassified': 'Unclassified Disruptions',
}

export function isConditionBlock(id: string): boolean {
    return (
        id.startsWith('voice.') &&
        (id in CONDITION_BLOCK_LABELS ||
            id === 'voice.clear-runs' ||
            id === 'voice.crash' ||
            id === 'voice.congestion')
    )
}

export interface Preset {
    readonly value: string
    readonly label: string
    readonly rewrites: Readonly<Record<string, string>>
}
export const PRESETS: readonly Preset[] = [
    { value: 'arn-default', label: 'ARN default', rewrites: {} },
    {
        value: 'breakfast-energy',
        label: 'Breakfast energy',
        rewrites: {
            'role.standard':
                'This is high-energy breakfast radio. Punchy, quick, bright - you are the jolt of caffeine between two songs. Short sentences that land hard. Big story gets drama, everything else gets pace. Never sleepy, never a list.',
            'voice.variety':
                'Keep the energy up: exclaim where the news earns it, ask the listener a question at most once, and never let two sentences share a shape.',
        },
    },
    {
        value: 'formal-news',
        label: 'Formal news',
        rewrites: {
            'role.standard':
                "This is a formal news bulletin. Measured, precise, third person throughout. No contractions anywhere. No slang. Prefer constructions like 'Motorists are advised', 'delays are expected', 'remains closed'. Complete sentences of even, unhurried length.",
            'voice.variety':
                'Formal register permits repetition of structure; clarity outranks variety.',
            'voice.one-person':
                'A newsreader reading prepared copy, calm and impersonal.',
        },
    },
    {
        value: 'laconic',
        label: 'Laconic',
        rewrites: {
            'role.standard':
                'Laconic, dry, unhurried. Clipped sentences. No drama - the road is what it is. Say it plain, like a farmer telling a neighbour over a fence.',
            'voice.variety':
                'Short lines. Vary them a little. Never sound like a machine.',
        },
    },
    {
        value: 'telegraph',
        label: 'Telegraph',
        rewrites: {
            'role.standard':
                "Telegraph style. Clipped fragments, not essays: 'Railway Terrace - closed citybound, about a kay.' Every word earns its place or goes.",
            'length.fill':
                'Fill the slot with MORE ROADS, not longer sentences. Density is the product: if there is time left, voice another story.',
        },
    },
] as const

/**
 * Editorial titles for every shipped block id. Operators never see a raw id:
 * unknown ids fall back to a humanized form of the id's tail.
 */
export const BLOCK_LABELS: Readonly<Record<string, string>> = {
    'role.identity': 'The writer\u2019s job',
    'role.standard': 'Register and tone',
    'role.audience': 'Speak to the listener',
    'role.purpose': 'What the bulletin does',
    'voice.name-roads': 'Always name the road',
    'voice.variety': 'Vary the delivery',
    'voice.one-person': 'One clear voice',
    'voice.pronunciation': 'Pronounce names clearly',
    'voice.no-advice': 'Report, never advise',
    'voice.no-bookend': 'No intros or sign-offs',
    'length.fill': 'Fill the available slot',
    'length.target': 'Aim for the target length',
    'length.density': 'Keep the bulletin moving',
    'length.ladder': 'Where extra words come from',
    'length.rewording-is-free': 'Reword freely, facts stay fixed',
    'length.no-padding': 'Never pad',
    'material.facts-only': 'Facts come from the briefs',
    'material.ownership': 'Every fact stays with its story',
    'material.values-are-exact': 'Quantities are exact',
    'material.selection': 'Story selection',
    'material.reasoning': 'Reasoning stays private',
    'selection.overshoot': 'Running order and required content',
    'output.format': 'Reply format',
}

export function labelForBlock(id: string): string {
    const known = BLOCK_LABELS[id] ?? CONDITION_BLOCK_LABELS[id]
    if (known) return known
    const tail = id.split('.').slice(1).join(' ').replace(/-/g, ' ').trim()
    if (!tail) return id
    return tail.charAt(0).toUpperCase() + tail.slice(1)
}

export function isProtectedBlock(id: string): boolean {
    return id.startsWith('selection.') || id.startsWith('material.')
}

export function isReadOnlySection(section: PlaybookSection): boolean {
    return section === 'output' || section === 'selection'
}

export function buildBlocks(
    defaults: readonly PlaybookBlock[],
    playbook: BulletinPlaybook | undefined,
    role = 'compose2'
): EditableBlock[] {
    const overlay = playbook?.currentOverlay
    const disabled = new Set(overlay?.disable ?? [])
    const rewrites = overlay?.rewrite ?? {}
    const rolePrefix = `${role}/`
    const blocks: EditableBlock[] = defaults.map((block) => {
        const locked =
            block.locked ||
            isProtectedBlock(block.id) ||
            isReadOnlySection(block.section)
        const qualifiedId = `${rolePrefix}${block.id}`
        const rewriteBody = rewrites[qualifiedId] ?? rewrites[block.id]
        const isBlockDisabled =
            disabled.has(qualifiedId) || disabled.has(block.id)
        return {
            ...block,
            locked,
            body: locked ? block.body : (rewriteBody ?? block.body),
            enabled: locked || !isBlockDisabled,
            originalBody: block.body,
            isAdded: false,
        }
    })
    for (const added of overlay?.add ?? []) {
        const idWithoutRole = added.id.startsWith(rolePrefix)
            ? added.id.slice(rolePrefix.length)
            : added.id
        if (added.id.includes('/') && !added.id.startsWith(rolePrefix)) {
            continue
        }
        const locked =
            isProtectedBlock(idWithoutRole) || isReadOnlySection(added.section)
        blocks.push({
            ...added,
            id: idWithoutRole,
            locked,
            enabled: locked || added.enabled,
            originalBody: '',
            isAdded: true,
        })
    }
    return blocks.sort((a, b) => {
        const sectionOrder =
            PLAYBOOK_SECTIONS.indexOf(a.section) -
            PLAYBOOK_SECTIONS.indexOf(b.section)
        return sectionOrder || a.order - b.order
    })
}

export function statusFor(
    block: EditableBlock
): 'Default' | 'Edited' | 'Off' | 'Protected' {
    if (block.locked) return 'Protected'
    if (!block.enabled) return 'Off'
    if (block.isAdded || block.body !== block.originalBody) return 'Edited'
    return 'Default'
}

export function buildOverlay(
    blocks: readonly EditableBlock[],
    role = 'compose2',
    existingOverlay?: PlaybookOverlay
): PlaybookOverlay {
    const rolePrefix = `${role}/`
    const rewrite: Record<string, string> = {
        ...(existingOverlay?.rewrite ?? {}),
    }
    // Clean up existing entries for this specific role
    for (const key of Object.keys(rewrite)) {
        if (key.startsWith(rolePrefix)) {
            delete rewrite[key]
        }
    }
    const disable: string[] = (existingOverlay?.disable ?? []).filter(
        (id) => !id.startsWith(rolePrefix)
    )
    const add: PlaybookBlock[] = (existingOverlay?.add ?? []).filter(
        (block) => !block.id.startsWith(rolePrefix)
    )

    for (const block of blocks) {
        const qualifiedId = `${rolePrefix}${block.id}`
        if (block.isAdded) {
            if (block.locked || isReadOnlySection(block.section)) continue
            if (block.body.trim()) {
                add.push({
                    id: qualifiedId,
                    section: block.section,
                    body: block.body,
                    order: block.order,
                    locked: false,
                    enabled: block.enabled,
                })
            }
            continue
        }
        if (block.locked) continue
        if (!block.enabled) {
            disable.push(qualifiedId)
            continue
        }
        if (block.body.trim() && block.body !== block.originalBody) {
            rewrite[qualifiedId] = block.body
        }
    }
    return { rewrite, disable, reorder: existingOverlay?.reorder ?? {}, add }
}
export function lockedBlockReason(id: string): string {
    if (id === 'output.format')
        return 'Locked because changing it would make the reply unparseable.'
    if (id.startsWith('selection.')) {
        return 'Protected: running order and required content are station configuration'
    }
    return 'Protected because code enforces this invariant; editing the prompt could make prompt and code disagree.'
}

export function lockedSectionReason(section: PlaybookSection): string {
    return section === 'selection'
        ? 'Protected: running order and required content are station configuration'
        : 'Locked because changing it would make the reply unparseable.'
}

/** Renders one playbook section in the Studio dialog. */
export function SectionBlock({
    block,
    onChange,
    onRemove,
}: {
    readonly block: EditableBlock
    readonly onChange: (next: Partial<EditableBlock>) => void
    readonly onRemove: () => void
}) {
    const status = statusFor(block)
    return (
        <div className="space-y-3 rounded-xl border border-border/70 bg-card/40 p-4">
            <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                        <code className="truncate text-xs text-muted-foreground">
                            {block.id}
                        </code>
                        <Badge
                            variant={
                                status === 'Off'
                                    ? 'destructive'
                                    : status === 'Edited'
                                      ? 'default'
                                      : status === 'Protected'
                                        ? 'warning'
                                        : 'muted'
                            }
                        >
                            {status === 'Protected' && (
                                <Lock className="mr-1 size-3" aria-hidden />
                            )}
                            {status}
                        </Badge>
                    </div>
                    {block.locked && (
                        <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                            <Lock className="size-3.5" aria-hidden />
                            {lockedBlockReason(block.id)}
                        </p>
                    )}
                </div>
                {!block.locked && (
                    <div className="flex shrink-0 items-center gap-2">
                        <Label
                            htmlFor={`enabled-${block.id}`}
                            className="text-xs text-muted-foreground"
                        >
                            Enabled
                        </Label>
                        <Switch
                            id={`enabled-${block.id}`}
                            checked={block.enabled}
                            onCheckedChange={(enabled) => onChange({ enabled })}
                        />
                        {block.isAdded && (
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-8"
                                onClick={onRemove}
                                aria-label={`Remove ${block.id}`}
                            >
                                <Trash2
                                    className="size-4 text-destructive"
                                    aria-hidden
                                />
                            </Button>
                        )}
                    </div>
                )}
            </div>
            <Textarea
                value={block.body}
                disabled={block.locked || !block.enabled}
                onChange={(event) =>
                    onChange({
                        body:
                            event.target.value.trim().length === 0
                                ? block.originalBody
                                : event.target.value,
                    })
                }
                className="min-h-24 resize-y font-mono text-xs leading-relaxed"
                aria-label={`${PLAYBOOK_SECTION_LABELS[block.section]} instruction ${block.id}`}
            />
            {!block.isAdded &&
                !block.locked &&
                block.body !== block.originalBody && (
                    <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-7 gap-1.5 text-xs"
                        onClick={() => onChange({ body: block.originalBody })}
                    >
                        <RotateCcw className="size-3.5" aria-hidden /> Revert to
                        shipped text
                    </Button>
                )}
        </div>
    )
}
