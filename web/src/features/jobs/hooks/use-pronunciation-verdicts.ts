import { useCallback, useMemo } from 'react'
import { toastAction } from '@/lib/toast-action'
import { toast } from 'sonner'
import {
    useVerifiedPronunciations,
    useSetVerifiedPronunciations,
    pronunciationKey,
    type VerifiedPronunciationEntry,
} from '@/features/pronunciation/api/verified-pronunciation-hooks'
import { useAddJobPronunciationRule } from '@/features/pronunciation/api/pronunciation-hooks'
import { useIsAdmin, useIsOperator } from '@/hooks/use-current-user'
import type { Dictionary } from '@/features/pronunciation/types'

export interface PronunciationVerdicts {
    readonly verified: readonly VerifiedPronunciationEntry[]
    /**
     * Whether this user may record verdicts. Deliberately NOT the bulletin's
     * `canEdit`: a verdict is a standing library decision, not a script edit,
     * and the recurring false flags an operator most wants to silence are
     * usually noticed on an already-approved bulletin. Mirrors the backend,
     * which gates the route on Operator rather than job state.
     */
    readonly canManage: boolean
    /** Records a standing "sounded fine" verdict. Success toast offers an immediate undo. */
    readonly verify: (word: string) => void
    /** Drops the verdict so the word flags again — on every bulletin, past and future. */
    readonly unverify: (word: string) => void
    /** Saves/replaces the word's alias rule; a rule change mints a new version. */
    readonly addRule: (word: string, replacement: string) => Promise<void>
}

/**
 * The two verdicts an operator can record against a flagged pronunciation, and
 * the standing verdict list they are read against.
 *
 * `verify`/`unverify` are whole-list PUTs (the backend resource is per-market
 * and replace-only), so both compute the next list from the current one. Undo
 * is therefore just the inverse call — no compensating-transaction bookkeeping.
 */
export function usePronunciationVerdicts(
    jobId: string,
    market: string,
    dictionary: Dictionary | null,
    /** Reports a saved rule so it queues as a pending change on this bulletin. */
    onEdit?: (word: string, replacement: string) => void
): PronunciationVerdicts {
    const { data } = useVerifiedPronunciations(market)
    const setVerified = useSetVerifiedPronunciations()
    const addJobRule = useAddJobPronunciationRule()
    // Admin satisfies every Operator route server-side
    // (AuthorizationMiddleware.HasRequiredRole), and `useIsOperator` alone is
    // false for an explicit admin — so pair them, as every other caller does.
    // Bound to locals first: `useIsAdmin() || useIsOperator()` short-circuits,
    // which makes the second call conditional and breaks the hook order.
    const isAdmin = useIsAdmin()
    const isOperator = useIsOperator()
    const canManage = isAdmin || isOperator

    const verified = useMemo(() => data?.words ?? [], [data])

    const save = useCallback(
        (words: readonly VerifiedPronunciationEntry[]) =>
            setVerified.mutateAsync({ marketId: market, words }),
        [setVerified, market]
    )

    const verify = useCallback(
        (word: string) => {
            const key = pronunciationKey(word)
            if (verified.some((w) => pronunciationKey(w.word) === key)) return
            const next: VerifiedPronunciationEntry[] = [
                ...verified,
                {
                    word,
                    note: null,
                    enabled: true,
                    verifiedBy: null,
                    verifiedAt: null,
                },
            ]
            void toastAction(
                () => save(next),
                `"${word}" won't be flagged again`,
                'Could not save the verdict',
                undefined,
                { onUndo: () => void save(verified) }
            )
        },
        [verified, save]
    )

    const unverify = useCallback(
        (word: string) => {
            const key = pronunciationKey(word)
            const next = verified.filter(
                (w) => pronunciationKey(w.word) !== key
            )
            if (next.length === verified.length) return
            void toastAction(
                () => save(next),
                `"${word}" will be flagged again`,
                'Could not remove the verdict',
                undefined,
                { onUndo: () => void save(verified) }
            )
        },
        [verified, save]
    )

    const addRule = useCallback(
        async (word: string, replacement: string) => {
            if (!dictionary) return
            const target = word.trim().toLowerCase()
            const existing = dictionary.rules.find(
                (r) =>
                    r.type === 'alias' &&
                    r.stringToReplace.trim().toLowerCase() === target
            )
            if (existing?.replacement === replacement) {
                toast.info(`${dictionary.name} already says "${replacement}"`)
                return
            }

            // One job-addressed call. The server resolves the dictionary this
            // bulletin aired with, replaces the word-or-phrase rule (never
            // appends a second — the provider applies one per target) and
            // replays the edit if the dictionary moved underneath, so there is
            // no whole-list payload, no ETag and no 409 dance to run from the
            // browser.
            //
            // Undefined means the save failed and already surfaced its own
            // error toast — do NOT queue a pending change for a rule that was
            // never written, or the operator would re-voice into nothing.
            const saved = await toastAction(
                () => addJobRule.mutateAsync({ jobId, word, replacement }),
                // `onEdit` is absent on a bulletin that can no longer be
                // re-voiced (already approved/published), so promising a
                // re-voice there would send the operator looking for a button
                // that isn't offered.
                onEdit
                    ? 'Saved — re-voice to use it on this bulletin'
                    : 'Saved — applies from the next bulletin',
                'Could not update the dictionary'
            )
            if (saved !== undefined) onEdit?.(word, replacement)
        },
        [dictionary, addJobRule, jobId, onEdit]
    )

    return { verified, canManage, verify, unverify, addRule }
}
