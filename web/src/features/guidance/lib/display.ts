import type { GuidanceTypeName } from '../api/schemas'

// =============================================================================
// Display constants for guidance types — labels, hints, and the tinted pill
// classes. Colors are semantic tokens only (design-system compliant).
// =============================================================================

/**
 * Short, plain-English labels for the composer's type select and the row
 * pills. These are DISPLAY strings only — the record keys (the wire
 * GuidanceTypeName values) and TYPE_BADGE below are unchanged.
 */
export const TYPE_LABEL: Record<GuidanceTypeName, string> = {
    Style: 'Tone',
    Editorial: 'Order',
    Lexicon: 'Naming',
    Prohibition: 'Avoid',
    HouseStyle: 'House style',
}

/** One-line explanation shown as the SelectItem description in the composer. */
export const TYPE_HINT: Record<GuidanceTypeName, string> = {
    Style: 'Slang, energy, formality of the read',
    Editorial: 'Lead item, emphasis, running order',
    Lexicon: 'What to call roads, suburbs, landmarks',
    Prohibition: 'Words or claims the script must not use',
    HouseStyle: 'Fixed opening lines, read exactly as written',
}

/** Tinted pill classes per type — semantic tokens only. */
export const TYPE_BADGE: Record<GuidanceTypeName, string> = {
    Style: 'bg-info/10 text-info border-info/20',
    Editorial: 'bg-primary/10 text-primary-on-tint border-primary/20',
    Lexicon: 'bg-success/10 text-success border-success/20',
    Prohibition:
        'bg-destructive/10 text-destructive-on-tint border-destructive/20',
    HouseStyle: 'bg-muted text-muted-foreground border-border',
}

/** Max guidance-note length, mirrors the backend MaxTextLength. */
export const MAX_TEXT = 600
