import { findPronunciationMatches } from '@/features/jobs/lib/pronunciation-matches'
import type { Dictionary } from '@/features/pronunciation/types'

export interface PronunciationDisplay {
    readonly label: string
    readonly title: string | undefined
}

export const resolvePronunciationDisplay = (
    providerDictionaryId: string | null | undefined,
    providerVersionId: string | null | undefined,
    voiceModel: string,
    dictionaries: readonly Dictionary[]
): PronunciationDisplay => {
    const result = findPronunciationMatches(
        '',
        providerDictionaryId,
        providerVersionId,
        voiceModel,
        dictionaries
    )

    const title =
        providerDictionaryId != null || providerVersionId != null
            ? `Provider dictionary ID: ${
                  providerDictionaryId ?? 'none'
              }; provider version ID: ${providerVersionId ?? 'none'}`
            : undefined

    switch (result.resolution) {
        case 'exact':
            return {
                label: `${
                    result.dictionaryName ?? 'Unknown dictionary'
                } · v${result.versionNumber}`,
                title,
            }
        case 'missing-version':
        case 'missing-dictionary':
            return {
                label: `${
                    result.dictionaryName ?? 'Unknown dictionary'
                } · version unavailable`,
                title,
            }
        case 'none':
            return { label: 'None', title }
    }
}
