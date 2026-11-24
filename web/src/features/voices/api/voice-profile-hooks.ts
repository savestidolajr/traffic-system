import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { pascalizeKeys } from '@/lib/api-utils'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import {
    VoiceProfileApiSchema,
    VoiceProfilesListResponseSchema,
    VoicePreviewResponseSchema,
    VoiceoverResponseSchema,
} from './profile-schemas'
import type {
    VoiceProfile,
    VoiceProfilesListResponse,
    VoicePreviewResponse,
    CreateVoiceProfileRequest,
    UpdateVoiceProfileRequest,
    PreviewVoiceRequest,
    VoiceoverRequest,
    VoiceoverResponse,
} from '../profile-types'
import {
    VOICE_PROFILES,
    VOICE_PROFILE_PREVIEW,
    voiceProfileDetail,
    voiceProfileVersionRestore,
    voiceProfileArchive,
    voiceProfileUnarchive,
    voiceProfileDelete,
    voiceProfileVoiceover,
} from '@/lib/api/paths'

/**
 * Mirrors the backend's ConfigValidationLimits.MaxSynthesisScriptLength — roughly six
 * minutes of speech. The cap exists so one request cannot bill an arbitrarily long read.
 */
export const VOICEOVER_SCRIPT_MAX = 5000

const VOICE_PROFILE_QUERY_KEYS = createQueryKeys('voice-profiles')

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------
const fetchVoiceProfiles = async (): Promise<VoiceProfilesListResponse> => {
    const { data } = await apiClient.get(VOICE_PROFILES)
    return VoiceProfilesListResponseSchema.parse(data)
}

const fetchVoiceProfile = async (id: string): Promise<VoiceProfile> => {
    const { data } = await apiClient.get(voiceProfileDetail(id))
    return VoiceProfileApiSchema.parse(data)
}

const createVoiceProfile = async (
    body: CreateVoiceProfileRequest
): Promise<VoiceProfile> => {
    const { data } = await apiClient.post(VOICE_PROFILES, pascalizeKeys(body))
    return VoiceProfileApiSchema.parse(data)
}

const updateVoiceProfile = async ({
    id,
    body,
}: {
    id: string
    body: UpdateVoiceProfileRequest
}): Promise<VoiceProfile> => {
    const { data } = await apiClient.put(
        voiceProfileDetail(id),
        pascalizeKeys(body)
    )
    return VoiceProfileApiSchema.parse(data)
}

const restoreVoiceProfileVersion = async ({
    id,
    version,
}: {
    id: string
    version: number
}): Promise<VoiceProfile> => {
    const { data } = await apiClient.post(
        voiceProfileVersionRestore(id, version)
    )
    return VoiceProfileApiSchema.parse(data)
}

const deleteVoiceProfile = async (id: string): Promise<void> => {
    await apiClient.delete(voiceProfileDelete(id))
}

const archiveVoiceProfile = async (id: string): Promise<VoiceProfile> => {
    const { data } = await apiClient.post(voiceProfileArchive(id))
    return VoiceProfileApiSchema.parse(data)
}

const unarchiveVoiceProfile = async (id: string): Promise<VoiceProfile> => {
    const { data } = await apiClient.post(voiceProfileUnarchive(id))
    return VoiceProfileApiSchema.parse(data)
}

const previewVoice = async (
    body: PreviewVoiceRequest
): Promise<VoicePreviewResponse> => {
    const { data } = await apiClient.post(
        VOICE_PROFILE_PREVIEW,
        pascalizeKeys(body)
    )
    return VoicePreviewResponseSchema.parse(data)
}

const renderVoiceover = async ({
    profileId,
    script,
}: VoiceoverRequest): Promise<VoiceoverResponse> => {
    const { data } = await apiClient.post(voiceProfileVoiceover(profileId), {
        Script: script,
    })
    return VoiceoverResponseSchema.parse(data)
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------
export const useVoiceProfiles = (options?: { readonly enabled?: boolean }) =>
    useQueryFactory(VOICE_PROFILE_QUERY_KEYS.lists(), fetchVoiceProfiles, {
        staleTime: 1000 * 60,
        enabled: options?.enabled ?? true,
    })

export const useVoiceProfile = (id: string | null) =>
    useQueryFactory(
        VOICE_PROFILE_QUERY_KEYS.detail(id ?? ''),
        () => fetchVoiceProfile(id as string),
        { enabled: Boolean(id) }
    )

export const useCreateVoiceProfile = () =>
    useMutationFactory(createVoiceProfile, () => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
    ])

export const useUpdateVoiceProfile = () =>
    useMutationFactory(updateVoiceProfile, (vars) => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
        VOICE_PROFILE_QUERY_KEYS.detail(vars.id),
    ])

export const useRestoreVoiceProfileVersion = () =>
    useMutationFactory(restoreVoiceProfileVersion, (vars) => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
        VOICE_PROFILE_QUERY_KEYS.detail(vars.id),
    ])

export const useDeleteVoiceProfile = () =>
    useMutationFactory(deleteVoiceProfile, () => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
    ])

export const useArchiveVoiceProfile = () =>
    useMutationFactory(archiveVoiceProfile, (id) => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
        VOICE_PROFILE_QUERY_KEYS.detail(id),
    ])

export const useUnarchiveVoiceProfile = () =>
    useMutationFactory(unarchiveVoiceProfile, (id) => [
        VOICE_PROFILE_QUERY_KEYS.lists(),
        VOICE_PROFILE_QUERY_KEYS.detail(id),
    ])

export const usePreviewVoice = () => useMutationFactory(previewVoice, () => [])

/**
 * Renders arbitrary copy on a profile's CURRENT configuration and returns a same-origin
 * url plus the file name the browser should save it as. Invalidates nothing: a voiceover
 * is a deliverable, not a change to the library.
 */
export const useVoiceover = () => useMutationFactory(renderVoiceover, () => [])
