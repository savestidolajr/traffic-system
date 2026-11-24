// =============================================================================
// Tiny JSON-shape guards shared by anything that walks untyped `unknown` data
// (an uploaded file, a raw API error body, ...).
// =============================================================================

export function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * Reads a property, falling back to a case-insensitive match. Some backend
 * parsers (`System.Text.Json` default) are case-insensitive on the way in,
 * so an AI-edited or hand-typed payload that drifts from PascalCase must
 * still resolve here.
 */
export function getProp(obj: Record<string, unknown>, name: string): unknown {
    if (name in obj) return obj[name]
    const lower = name.toLowerCase()
    const match = Object.keys(obj).find((k) => k.toLowerCase() === lower)
    return match === undefined ? undefined : obj[match]
}

/** Best-effort parse — a dropped (oversized), missing, or malformed body renders nothing, never a crash. */
export function tryParseJson(
    raw: string | null | undefined
): Record<string, unknown> | null {
    if (!raw) return null
    try {
        const parsed: unknown = JSON.parse(raw)
        return isRecord(parsed) ? parsed : null
    } catch {
        return null
    }
}
