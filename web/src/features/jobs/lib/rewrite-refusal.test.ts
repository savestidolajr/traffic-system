import { describe, it, expect } from 'vitest'
import { parseRewriteRefusal } from './rewrite-refusal'

// The refusal is the only thing an operator has to work from when a rewrite is rejected, so the
// two things that must hold are: every rejected claim is itemised, and nothing the backend said is
// ever silently dropped when the shape is unexpected.
describe('parseRewriteRefusal', () => {
    it('itemises each rejected claim with its quote, scope and reason', () => {
        const refusal = parseRewriteRefusal(
            "Rewrite refused: not grounded against the bulletin's evidence. " +
                '"Roadworks and changed traffic conditions are updated weekly on the Cairns Regional Council website." ' +
                '— GLOBAL — this is unsourced content, not a traffic fact from the supplied data. ' +
                '"Download the iHeartRadio app to take us anywhere — search KIIS or 4CA Cairns." ' +
                '— GLOBAL — this is promotional content not sourced from the supplied data.'
        )

        expect(refusal.headline).toBe(
            "Not grounded in this bulletin's evidence"
        )
        expect(refusal.claims).toHaveLength(2)
        expect(refusal.claims[0].quote).toContain('Cairns Regional Council')
        expect(refusal.claims[0].scope).toBe('GLOBAL')
        expect(refusal.claims[0].reason).toBe(
            'this is unsourced content, not a traffic fact from the supplied data.'
        )
        expect(refusal.claims[1].quote).toContain('iHeartRadio')
        // A remedy is the point of parsing at all: the raw string never says what to do next.
        expect(refusal.remedy).toMatch(/already reports/)
    })

    it('keeps a scope-less claim rather than dropping it', () => {
        const refusal = parseRewriteRefusal(
            "Rewrite refused: not grounded against the bulletin's evidence. " +
                '"The Hume Freeway is closed outbound." — no such road in the source data.'
        )

        expect(refusal.claims).toHaveLength(1)
        expect(refusal.claims[0].scope).toBeNull()
        expect(refusal.claims[0].reason).toBe(
            'no such road in the source data.'
        )
    })

    it('names the content-safety gate separately from grounding', () => {
        const refusal = parseRewriteRefusal(
            'Content safety: the script names a deceased person.'
        )

        expect(refusal.headline).toBe('Blocked by content safety')
        expect(refusal.detail).toBe('the script names a deceased person.')
    })

    it('falls back to the raw message when it matches no known shape', () => {
        const refusal = parseRewriteRefusal(
            'Rewrite failed: upstream timed out.'
        )

        expect(refusal.headline).toBe('Rewrite refused')
        expect(refusal.claims).toHaveLength(0)
        expect(refusal.detail).toBe('Rewrite failed: upstream timed out.')
    })

    it('keeps unquoted prose from a grounded refusal instead of showing an empty panel', () => {
        const refusal = parseRewriteRefusal(
            "Rewrite refused: not grounded against the bulletin's evidence. " +
                'It dropped or altered these facts: Toorak Road.'
        )

        expect(refusal.claims).toHaveLength(0)
        expect(refusal.detail).toBe(
            'It dropped or altered these facts: Toorak Road.'
        )
    })
})
