import { z } from 'zod'
import {
    BulletinPlaybookApiSchema,
    BulletinPlaybooksListResponseSchema,
    DefaultPlaybookResponseSchema,
} from './api/playbook-schemas'

export type PlaybookSection =
    'role' | 'material' | 'selection' | 'voice' | 'length' | 'output'
export const PLAYBOOK_SECTIONS: readonly PlaybookSection[] = [
    'role',
    'material',
    'selection',
    'voice',
    'length',
    'output',
]

export const PLAYBOOK_SECTION_LABELS: Record<PlaybookSection, string> = {
    role: 'Role',
    material: 'Material',
    selection: 'Selection',
    voice: 'Voice',
    length: 'Length',
    output: 'Output',
}

export interface PlaybookBlock {
    readonly id: string
    readonly section: PlaybookSection
    readonly body: string
    readonly order: number
    readonly locked: boolean
    readonly enabled: boolean
}

export interface PlaybookOverlay {
    readonly rewrite: Readonly<Record<string, string>>
    readonly disable: readonly string[]
    readonly reorder: Readonly<Record<string, number>>
    readonly add: readonly PlaybookBlock[]
}

export type BulletinPlaybook = z.infer<typeof BulletinPlaybookApiSchema>
export type BulletinPlaybooksListResponse = z.infer<
    typeof BulletinPlaybooksListResponseSchema
>
export type DefaultPlaybookResponse = z.infer<
    typeof DefaultPlaybookResponseSchema
>

export interface CreatePlaybookRequest {
    readonly name: string
    readonly description: string | null
    readonly role: string
    readonly overlay: PlaybookOverlay
    readonly changeNote: string | null
    readonly id?: string
}

export interface UpdatePlaybookRequest {
    readonly name: string
    readonly description: string | null
    readonly overlay: PlaybookOverlay
    readonly changeNote: string | null
}
export interface PlaybookPreviewRequest {
    readonly role: string
    readonly market: string
    readonly overlay: PlaybookOverlay
}

export interface PlaybookPreview {
    readonly script: string
    readonly baselineScript: string | null
    readonly refused: readonly string[]
    readonly notes: readonly string[]
    readonly predictedSeconds: number | null
}
