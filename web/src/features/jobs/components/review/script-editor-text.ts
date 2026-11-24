const sentenceSegmenter = new Intl.Segmenter('en-AU', {
    granularity: 'sentence',
})

/** Formats canonical prose as one editable sentence per visual line. */
export function formatScriptForEditing(value: string): string {
    return Array.from(sentenceSegmenter.segment(value), (entry) =>
        entry.segment.trim()
    )
        .filter(Boolean)
        .join('\n')
}

/** Restores the editor's visual line breaks to canonical script spacing. */
export function canonicalizeEditedScript(value: string): string {
    return value
        .replace(/\s*\n+\s*/g, ' ')
        .replace(/[\t ]+/g, ' ')
        .trim()
}
