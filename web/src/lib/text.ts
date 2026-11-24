/** SCREAMING_SNAKE or snake_case → Sentence case, as a last resort. */
export function prettify(raw: string): string {
    const words = raw
        .toLowerCase()
        .split(/[_\s]+/)
        .filter(Boolean)
    if (words.length === 0) return raw
    return [
        words[0].charAt(0).toUpperCase() + words[0].slice(1),
        ...words.slice(1),
    ].join(' ')
}
