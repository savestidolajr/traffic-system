/**
 * Turns the backend's refusal string into something an operator can act on.
 *
 * A grounding refusal arrives as one long sentence-stream — the prefix, then the reviewer's
 * per-claim verdicts run together:
 *
 *   Rewrite refused: not grounded against the bulletin's evidence. "Roadworks … council website."
 *   — GLOBAL — this is unsourced content, not a traffic fact from the supplied data. "Download the
 *   iHeartRadio app …" — GLOBAL — this is promotional content not sourced from the supplied data.
 *
 * Rendered raw that is a wall of 10px red text mixing machine tokens with prose, and it never says
 * what to do next. Parsed, each rejected claim is its own row: the quoted line, the reviewer's
 * scope token as a chip, and the reason. Anything that does not match the shape falls back to the
 * raw text verbatim — a refusal must never be swallowed by a parser that guessed wrong.
 */
export interface RefusedClaim {
    /** The exact script text the reviewer objected to, when it quoted one. */
    readonly quote: string | null
    /** The reviewer's scope token ("GLOBAL", a road name); shown as a chip. */
    readonly scope: string | null
    /** Why it was rejected, in the reviewer's own words. */
    readonly reason: string
}

export interface RewriteRefusal {
    /** One short line naming the gate that refused, not the whole message. */
    readonly headline: string
    /** Per-claim rows; empty when the message had no quoted claims. */
    readonly claims: readonly RefusedClaim[]
    /** The remaining detail when there are no claims to itemise. */
    readonly detail: string
    /** What the operator can actually do about it, when the gate implies one. */
    readonly remedy: string | null
}

/** The gates that produce a refusal, each with a headline and a next step. */
const GATES: readonly {
    readonly match: RegExp
    readonly headline: string
    readonly remedy: string | null
}[] = [
    {
        match: /^Rewrite refused: not grounded against the bulletin's evidence\.\s*/i,
        headline: "Not grounded in this bulletin's evidence",
        remedy: 'Ask for a change to something the bulletin already reports, or edit the script directly.',
    },
    {
        match: /^Content safety:\s*/i,
        headline: 'Blocked by content safety',
        remedy: 'Reword the instruction so the result avoids the flagged wording.',
    },
    {
        match: /^Grounding check unavailable:\s*/i,
        headline: 'Grounding check unavailable',
        remedy: 'The reviewer could not be reached, so nothing was returned. Try again in a moment.',
    },
]

/**
 * `"quoted claim" — SCOPE — reason`, repeated. The scope segment is optional; the reason runs to
 * the next quote or the end. Straight and curly quotes both appear in reviewer output.
 */
const CLAIM =
    /["“]([^"”]+)["”]\s*[—–-]\s*(?:([A-Z][A-Z0-9 _/-]*?)\s*[—–-]\s*)?([^"“]+)/g

export function parseRewriteRefusal(message: string): RewriteRefusal {
    const text = message.trim()
    const gate = GATES.find((g) => g.match.test(text))
    const headline = gate?.headline ?? 'Rewrite refused'
    const body = gate ? text.replace(gate.match, '').trim() : text

    const claims: RefusedClaim[] = []
    for (const match of body.matchAll(CLAIM)) {
        const reason = match[3].trim().replace(/^[—–-]\s*/, '')
        if (!reason) continue
        claims.push({
            quote: match[1].trim(),
            scope: match[2]?.trim() ?? null,
            reason,
        })
    }

    return {
        headline,
        claims,
        // With claims itemised the prose is already on screen; without them the whole body is the
        // only thing the operator has, so it is never dropped.
        detail: claims.length > 0 ? '' : body,
        remedy: gate?.remedy ?? null,
    }
}
