// =============================================================================
// Spoken-number normalization — broadcast scripts voice quantities as words
// ("delays of around fifteen minutes", "about twenty-five kays"), which
// digit-only checks silently skip. Normalizing to digits lets the claim
// checker and the advice guard see the figure a listener hears.
// =============================================================================

const UNITS: Record<string, number> = {
    one: 1,
    two: 2,
    three: 3,
    four: 4,
    five: 5,
    six: 6,
    seven: 7,
    eight: 8,
    nine: 9,
    ten: 10,
    eleven: 11,
    twelve: 12,
    thirteen: 13,
    fourteen: 14,
    fifteen: 15,
    sixteen: 16,
    seventeen: 17,
    eighteen: 18,
    nineteen: 19,
}

const TENS: Record<string, number> = {
    twenty: 20,
    thirty: 30,
    forty: 40,
    fifty: 50,
    sixty: 60,
    seventy: 70,
    eighty: 80,
    ninety: 90,
}

const TENS_ALT = 'twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety'
const DIGIT_UNIT_ALT = 'one|two|three|four|five|six|seven|eight|nine'
const UNIT_ALT = `${DIGIT_UNIT_ALT}|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen`

// Compound first ("twenty-five" / "twenty five"), then standalone words.
const COMPOUND_PATTERN = new RegExp(
    `\\b(${TENS_ALT})[\\s-](${DIGIT_UNIT_ALT})\\b`,
    'gi'
)
const STANDALONE_PATTERN = new RegExp(`\\b(${UNIT_ALT}|${TENS_ALT})\\b`, 'gi')

/**
 * Replace spoken number words with digits: "twenty-five" → "25",
 * "fifteen" → "15". Only cardinal 1–99 — the range broadcast quantities
 * (minutes, kays, degrees) actually use. Output is for CHECKING, never
 * for display.
 */
export function normalizeSpokenNumbers(text: string): string {
    return text
        .replace(COMPOUND_PATTERN, (_, tens: string, unit: string) =>
            String(TENS[tens.toLowerCase()] + UNITS[unit.toLowerCase()])
        )
        .replace(STANDALONE_PATTERN, (word: string) => {
            const lower = word.toLowerCase()
            return String(UNITS[lower] ?? TENS[lower])
        })
}
