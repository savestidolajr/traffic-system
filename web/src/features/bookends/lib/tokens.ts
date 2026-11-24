/**
 * Token helpers mirroring the backend BookendTokenResolver: a bookend template
 * may carry {token} placeholders, filled per station from a small built-in set
 * plus per-station overrides (MarketSchedule.BookendTokens). The web keeps a
 * small mirror so the studio can flag custom tokens — which need a per-station
 * value the operator supplies in the schedule studio — and preview the fully
 * resolved sentence before an author saves.
 */

/** Token names resolved automatically from schedule data — no station value needed. */
export const BUILT_IN_TOKENS: readonly string[] = ['city', 'market', 'station']

/** Matches the backend's TokenPattern: {name} with a letter-first name. */
const TOKEN_PATTERN = /\{([A-Za-z][A-Za-z0-9_]*)\}/g

/**
 * Distinct token names a template references, in first-appearance order,
 * matched case-insensitively (mirrors BookendTokenResolver.ExtractTokens).
 */
export function extractTokens(text: string): string[] {
    const names: string[] = []
    for (const match of text.matchAll(TOKEN_PATTERN)) {
        const name = match[1]
        if (!names.some((n) => n.toLowerCase() === name.toLowerCase())) {
            names.push(name)
        }
    }
    return names
}

/**
 * Distinct token names across a bookend pair's lines (intro then outro), in
 * first-appearance order — mirrors the backend's union of ExtractTokens over
 * both template lines when it computes BookendDto.Tokens.
 */
export function extractTokensAcross(lines: readonly string[]): string[] {
    const names: string[] = []
    for (const line of lines) {
        for (const name of extractTokens(line)) {
            if (!names.some((n) => n.toLowerCase() === name.toLowerCase())) {
                names.push(name)
            }
        }
    }
    return names
}
