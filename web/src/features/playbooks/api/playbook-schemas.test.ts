import { describe, expect, it } from 'vitest'
import {
    BulletinPlaybookApiSchema,
    DefaultPlaybookResponseSchema,
    PlaybookPreviewApiSchema,
} from './playbook-schemas'

const block = {
    Id: 'material.facts-only',
    Section: 'Material',
    Body: 'Use supplied facts only.',
    Order: 0,
    Locked: true,
    Enabled: true,
}

describe('playbook API schemas', () => {
    it('camelizes default blocks and preserves locked metadata', () => {
        const parsed = DefaultPlaybookResponseSchema.parse({
            Role: 'compose2',
            DefaultAvailable: true,
            Blocks: [
                block,
                {
                    Id: 'length.target',
                    Section: 'Length',
                    Body: 'Fit the read.',
                    Order: 0,
                    Locked: false,
                    Enabled: true,
                },
            ],
        })

        expect(parsed).toEqual({
            role: 'compose2',
            defaultAvailable: true,
            blocks: [
                {
                    id: 'material.facts-only',
                    section: 'material',
                    body: 'Use supplied facts only.',
                    order: 0,
                    locked: true,
                    enabled: true,
                },
                {
                    id: 'length.target',
                    section: 'length',
                    body: 'Fit the read.',
                    order: 0,
                    locked: false,
                    enabled: true,
                },
            ],
        })
    })

    it('accepts an unavailable role without inventing blocks', () => {
        expect(
            DefaultPlaybookResponseSchema.parse({
                Role: 'edit',
                DefaultAvailable: false,
            })
        ).toEqual({ role: 'edit', defaultAvailable: false, blocks: [] })
    })

    it('parses a playbook overlay as a camelCase diff', () => {
        const parsed = BulletinPlaybookApiSchema.parse({
            Id: 'pb-1',
            Name: 'Sydney compose',
            Description: null,
            Role: 'compose2',
            Status: 'active',
            CurrentVersion: 2,
            CurrentOverlay: {
                Rewrite: { 'role.compose': 'Write for Sydney listeners.' },
                Disable: ['selection.secondary'],
                Reorder: {},
                Add: [],
            },
            CreatedAt: '2026-08-27T00:00:00Z',
            UpdatedAt: '2026-08-27T00:00:00Z',
            CreatedBy: null,
            AssignedStations: [],
            Versions: [],
        })

        expect(parsed.currentOverlay).toEqual({
            rewrite: { 'role.compose': 'Write for Sydney listeners.' },
            disable: ['selection.secondary'],
            reorder: {},
            add: [],
        })
    })
    it('parses a preview response including refusals and duration', () => {
        expect(
            PlaybookPreviewApiSchema.parse({
                Script: 'Traffic first, then weather.',
                BaselineScript: 'Traffic only.',
                Refused: ['selection.required'],
                Notes: ['Applied station rules.'],
                PredictedSeconds: 18.5,
            })
        ).toEqual({
            script: 'Traffic first, then weather.',
            baselineScript: 'Traffic only.',
            refused: ['selection.required'],
            notes: ['Applied station rules.'],
            predictedSeconds: 18.5,
        })
    })
})
