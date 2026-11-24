import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { pascalizeKeys } from '@/lib/api-utils'
import {
    BulletinPlaybookApiSchema,
    BulletinPlaybooksListResponseSchema,
    DefaultPlaybookResponseSchema,
    PlaybookPreviewApiSchema,
} from './playbook-schemas'
import type {
    BulletinPlaybook,
    BulletinPlaybooksListResponse,
    CreatePlaybookRequest,
    DefaultPlaybookResponse,
    PlaybookOverlay,
    PlaybookPreview,
    PlaybookPreviewRequest,
    UpdatePlaybookRequest,
} from '../types'
import {
    PLAYBOOKS,
    PLAYBOOK_PREVIEW,
    playbookArchive,
    playbookDefault,
    playbookDelete,
    playbookDetail,
    playbookRestoreVersion,
    playbookUnarchive,
} from '@/lib/api/paths'

const PLAYBOOK_QUERY_KEYS = createQueryKeys('playbooks')

/**
 * Overlay maps are keyed by BLOCK IDS (`role.standard`), which must reach the API
 * verbatim. `pascalizeKeys` recurses into records and would mangle them into
 * `Role.standard` — measured live on dev 2026-08-27: every preset rewrite came back
 * "no such block". Serialize overlays explicitly; pascalize only true DTO fields.
 */
const serializeOverlay = (overlay: PlaybookOverlay) => ({
    Rewrite: { ...overlay.rewrite },
    Disable: [...overlay.disable],
    Reorder: { ...overlay.reorder },
    Add: overlay.add.map((block) => ({
        Id: block.id,
        Section: block.section,
        Body: block.body,
        Order: block.order,
        Locked: block.locked,
        Enabled: block.enabled,
    })),
})

const serializeWithOverlay = <T extends { readonly overlay: PlaybookOverlay }>(
    body: T
) => {
    const { overlay, ...rest } = body
    return { ...pascalizeKeys(rest), Overlay: serializeOverlay(overlay) }
}

const fetchPlaybooks = async (): Promise<BulletinPlaybooksListResponse> => {
    const { data } = await apiClient.get(PLAYBOOKS)
    return BulletinPlaybooksListResponseSchema.parse(data)
}

const fetchPlaybook = async (id: string): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.get(playbookDetail(id))
    return BulletinPlaybookApiSchema.parse(data)
}

const fetchDefaultPlaybook = async (
    role: string
): Promise<DefaultPlaybookResponse> => {
    const { data } = await apiClient.get(playbookDefault(role))
    return DefaultPlaybookResponseSchema.parse(data)
}

const createPlaybook = async (
    body: CreatePlaybookRequest
): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.post(PLAYBOOKS, serializeWithOverlay(body))
    return BulletinPlaybookApiSchema.parse(data)
}

const previewPlaybook = async (
    request: PlaybookPreviewRequest
): Promise<PlaybookPreview> => {
    const { data } = await apiClient.post(
        PLAYBOOK_PREVIEW,
        serializeWithOverlay(request)
    )
    return PlaybookPreviewApiSchema.parse(data)
}

const updatePlaybook = async ({
    id,
    body,
}: {
    readonly id: string
    readonly body: UpdatePlaybookRequest
}): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.put(
        playbookDetail(id),
        serializeWithOverlay(body)
    )
    return BulletinPlaybookApiSchema.parse(data)
}

const restorePlaybookVersion = async ({
    id,
    version,
}: {
    readonly id: string
    readonly version: number
}): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.post(playbookRestoreVersion(id, version))
    return BulletinPlaybookApiSchema.parse(data)
}

const archivePlaybook = async (id: string): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.post(playbookArchive(id))
    return BulletinPlaybookApiSchema.parse(data)
}

const unarchivePlaybook = async (id: string): Promise<BulletinPlaybook> => {
    const { data } = await apiClient.post(playbookUnarchive(id))
    return BulletinPlaybookApiSchema.parse(data)
}

const deletePlaybook = async (id: string): Promise<void> => {
    await apiClient.delete(playbookDelete(id))
}

export const usePlaybooks = (options?: { readonly enabled?: boolean }) =>
    useQueryFactory(PLAYBOOK_QUERY_KEYS.lists(), fetchPlaybooks, {
        staleTime: 1000 * 60,
        enabled: options?.enabled ?? true,
    })

export const usePlaybook = (id: string | null) =>
    useQueryFactory(
        PLAYBOOK_QUERY_KEYS.detail(id ?? ''),
        () => fetchPlaybook(id as string),
        { enabled: Boolean(id) }
    )

export const useDefaultPlaybook = (role: string, enabled = true) =>
    useQueryFactory(
        PLAYBOOK_QUERY_KEYS.detail(`default:${role}`),
        () => fetchDefaultPlaybook(role),
        { staleTime: 1000 * 60, enabled }
    )

export const useCreatePlaybook = () =>
    useMutationFactory(createPlaybook, () => [PLAYBOOK_QUERY_KEYS.lists()])

export const useUpdatePlaybook = () =>
    useMutationFactory(updatePlaybook, (vars) => [
        PLAYBOOK_QUERY_KEYS.lists(),
        PLAYBOOK_QUERY_KEYS.detail(vars.id),
    ])

export const usePreviewPlaybook = () =>
    useMutationFactory(previewPlaybook, () => [])

export const useRestorePlaybookVersion = () =>
    useMutationFactory(restorePlaybookVersion, (vars) => [
        PLAYBOOK_QUERY_KEYS.lists(),
        PLAYBOOK_QUERY_KEYS.detail(vars.id),
    ])

export const useArchivePlaybook = () =>
    useMutationFactory(archivePlaybook, (id) => [
        PLAYBOOK_QUERY_KEYS.lists(),
        PLAYBOOK_QUERY_KEYS.detail(id),
    ])

export const useUnarchivePlaybook = () =>
    useMutationFactory(unarchivePlaybook, (id) => [
        PLAYBOOK_QUERY_KEYS.lists(),
        PLAYBOOK_QUERY_KEYS.detail(id),
    ])

export const useDeletePlaybook = () =>
    useMutationFactory(deletePlaybook, () => [PLAYBOOK_QUERY_KEYS.lists()])
