/** Format the optional alias/phoneme breakdown shown beside a rule count. */
export function formatRuleCountSummary(
    aliasCount: number,
    phonemeCount: number
): string {
    const ruleCount = aliasCount + phonemeCount
    // When every rule is one type the breakdown is a redundant echo —
    // "15 rules (15 aliases)" adds nothing — so show it only for mixed sets.
    if (
        ruleCount > 0 &&
        (aliasCount === ruleCount || phonemeCount === ruleCount)
    ) {
        return ''
    }
    const parts: string[] = []
    if (aliasCount > 0) {
        parts.push(`${aliasCount} alias${aliasCount === 1 ? '' : 'es'}`)
    }
    if (phonemeCount > 0) {
        parts.push(`${phonemeCount} phoneme${phonemeCount === 1 ? '' : 's'}`)
    }
    return parts.length > 0 ? ` (${parts.join(', ')})` : ''
}
