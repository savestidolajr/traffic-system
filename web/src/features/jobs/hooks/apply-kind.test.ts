import { describe, it, expect } from 'vitest'
import { applyKindFor } from './use-bulletin-review'
import type { ReviewChange } from './use-bulletin-review'

function change(kind: ReviewChange['kind'], id = kind): ReviewChange {
    return { id, kind, label: id, removable: true }
}

/**
 * The apply action must name the CHEAPEST pipeline that satisfies everything
 * queued. Getting this wrong is expensive in both directions: calling a
 * pronunciation fix a "regenerate" burns ~45 extra seconds of a tight on-air
 * window AND rewrites a script the operator already read, while calling an
 * evidence change a "re-voice" would silently not apply it at all.
 */
describe('applyKindFor', () => {
    it('has nothing to apply when nothing is queued', () => {
        expect(applyKindFor([])).toBeNull()
    })

    it('re-voices for a single pronunciation change', () => {
        expect(applyKindFor([change('pronunciation')])).toBe('revoice')
    })

    it('re-voices for a single voice change', () => {
        // Switching voices is TTS-only — it never pays the LLM, because the
        // words the operator already read are exactly the words that air.
        expect(applyKindFor([change('voice')])).toBe('revoice')
    })

    it('re-voices once for voice and pronunciation changes queued together', () => {
        expect(
            applyKindFor([
                change('voice', 'voice'),
                change('pronunciation', 'pron-toorak'),
            ])
        ).toBe('revoice')
    })

    it('re-voices ONCE for several pronunciation changes', () => {
        // The batching requirement: N fixes must not mean N re-voices.
        expect(
            applyKindFor([
                change('pronunciation', 'pron-toorak'),
                change('pronunciation', 'pron-keilor'),
                change('pronunciation', 'pron-prahran'),
            ])
        ).toBe('revoice')
    })

    it.each([
        'incident-removed',
        'incident-added',
        'corridors-curated',
        'flow-curated',
    ] as const)('regenerates for a %s change', (kind) => {
        expect(applyKindFor([change(kind)])).toBe('regenerate')
    })

    it('regenerates when a pronunciation change is mixed with evidence', () => {
        // The most expensive queued change wins, and regenerate subsumes
        // re-voice, so the pronunciation still lands in the same pass.
        expect(
            applyKindFor([
                change('pronunciation', 'pron-toorak'),
                change('incident-removed', 'excl-1'),
            ])
        ).toBe('regenerate')
    })

    it('regenerates when a voice change is mixed with evidence changes', () => {
        // The voice still lands — regenerate subsumes the re-voice — but the
        // words get rebuilt from the selected evidence, so the batch cannot be
        // a TTS-only pass.
        expect(
            applyKindFor([
                change('voice', 'voice'),
                change('incident-removed', 'excl-1'),
            ])
        ).toBe('regenerate')
    })
})
