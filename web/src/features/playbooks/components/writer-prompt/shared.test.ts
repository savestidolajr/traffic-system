import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import {
    CONDITION_BLOCK_LABELS,
    PROMPT_AGENTS,
    RULE_MEASURES,
    buildBlocks,
    buildOverlay,
    isConditionBlock,
    labelForBlock,
    type EditableBlock,
} from './shared'

describe('writer prompt agents', () => {
    it('has one grid column per agent', () => {
        // The selector's column count is a static Tailwind class, so it cannot be derived from the
        // array at runtime and the two silently drift. They did: dropping the reviewer left
        // `sm:grid-cols-4` behind three agents and a dead fourth slot, which reads as a missing
        // option rather than an absent one. This is the tripwire — change the array, change the class.
        const source = readFileSync(
            join(__dirname, '..', 'WriterPromptManager.tsx'),
            'utf8'
        )
        const match = source.match(/sm:grid-cols-(\d+)/)

        expect(match, 'agent selector grid class not found').not.toBeNull()
        expect(Number(match?.[1])).toBe(PROMPT_AGENTS.length)
    })

    it('omits the reviewer, whose prompt carries no editable blocks', () => {
        // edit.md ships with zero block markers, so a reviewer tab would render an empty column.
        // The reviewer is also the wrong place for station config: it would become a second copy of
        // the voice rules the desks already own, and the two would drift.
        expect(PROMPT_AGENTS.map((a) => a.role)).toEqual([
            'writeIncidents',
            'writeFlow',
            'compose2',
        ])
    })

    it('gives every agent a job description an operator can act on', () => {
        for (const agent of PROMPT_AGENTS) {
            expect(agent.title.length).toBeGreaterThan(0)
            expect(agent.blurb.length).toBeGreaterThan(20)
        }
    })
})

describe('condition blocks', () => {
    it('labels each condition with the name the priority sliders use', () => {
        // Same ten classes, same words, in the console and in the prompt. A label that drifts here
        // makes the two screens look like they configure different things.
        expect(labelForBlock('voice.crash')).toBe('Crashes and Collisions')
        expect(labelForBlock('voice.congestion')).toBe(
            'Heavy Congestion and Queues'
        )
        expect(labelForBlock('voice.specialevent')).toBe(
            'Special Events and Operations'
        )
    })

    it('recognises condition blocks and nothing else', () => {
        expect(isConditionBlock('voice.crash')).toBe(true)
        expect(isConditionBlock('voice.clear-runs')).toBe(true)
        expect(isConditionBlock('voice.variety')).toBe(false)
        expect(isConditionBlock('role.identity')).toBe(false)
    })

    it('names a measure a threshold rule can be written against', () => {
        // Without this the operator is guessing at what the desk can see, and "say chockers above
        // jam factor 5" has nowhere to bind.
        const fields = RULE_MEASURES.map((m) => m.field)

        expect(fields).toContain('speed fraction')
        expect(
            RULE_MEASURES.find((m) => m.field === 'speed fraction')?.note
        ).toMatch(/fraction of normal/i)
    })

    it('covers every condition label with a block id under voice', () => {
        for (const id of Object.keys(CONDITION_BLOCK_LABELS))
            expect(id.startsWith('voice.')).toBe(true)
    })
})

describe('overlay is role-qualified and merges', () => {
    const block = (
        id: string,
        body: string,
        originalBody: string
    ): EditableBlock => ({
        id,
        section: 'voice',
        body,
        originalBody,
        order: 0,
        locked: false,
        enabled: true,
        isAdded: false,
    })

    it('qualifies every key with the agent it was edited for', () => {
        // Unqualified keys would collide: role.identity and voice.condition-is-yours exist in more
        // than one agent's prompt, so an edit to one desk would silently apply to the other.
        const overlay = buildOverlay(
            [block('voice.roadworks', 'cone country', 'house default')],
            'writeIncidents'
        )

        expect(Object.keys(overlay.rewrite)).toEqual([
            'writeIncidents/voice.roadworks',
        ])
    })

    it('keeps other agents edits when saving one agent', () => {
        // The editor only holds one agent's blocks, so a naive save would drop the rest of the
        // playbook — the station would lose its presenter edits by touching a desk.
        const existing = {
            rewrite: { 'compose2/voice.variety': 'presenter edit' },
            disable: ['writeFlow/voice.clear-runs'],
            reorder: {},
            add: [],
        }

        const overlay = buildOverlay(
            [block('voice.roadworks', 'cone country', 'house default')],
            'writeIncidents',
            existing
        )

        expect(overlay.rewrite['compose2/voice.variety']).toBe('presenter edit')
        expect(overlay.rewrite['writeIncidents/voice.roadworks']).toBe(
            'cone country'
        )
        expect(overlay.disable).toContain('writeFlow/voice.clear-runs')
    })

    it('replaces this agents own edits rather than accumulating them', () => {
        const existing = {
            rewrite: { 'writeIncidents/voice.roadworks': 'stale wording' },
            disable: [],
            reorder: {},
            add: [],
        }

        const overlay = buildOverlay(
            [block('voice.roadworks', 'cone country', 'house default')],
            'writeIncidents',
            existing
        )

        expect(overlay.rewrite['writeIncidents/voice.roadworks']).toBe(
            'cone country'
        )
    })

    it('round-trips through buildBlocks for the agent that owns the edit', () => {
        const overlay = buildOverlay(
            [block('voice.roadworks', 'cone country', 'house default')],
            'writeIncidents'
        )
        const defaults = [
            {
                id: 'voice.roadworks',
                section: 'voice' as const,
                body: 'house default',
                order: 0,
                locked: false,
                enabled: true,
            },
        ]
        const playbook = { currentOverlay: overlay } as never

        expect(buildBlocks(defaults, playbook, 'writeIncidents')[0].body).toBe(
            'cone country'
        )
        // ...and the other agent still sees the house default.
        expect(buildBlocks(defaults, playbook, 'compose2')[0].body).toBe(
            'house default'
        )
    })
})
