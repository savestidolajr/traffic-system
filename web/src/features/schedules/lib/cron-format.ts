import cronstrue from 'cronstrue'

/**
 * Compress cronstrue's verbose English for narrow card rows. Exported for
 * tests that pin the cronstrue output format these regexes rely on.
 */
export function abbreviate(text: string): string {
    // "At 0, 15, 30, and 45 minutes past the hour" → "Every 15 min":
    // evenly-stepped minute lists are intervals, and the verbose form
    // truncates to a meaningless leading "0" in narrow card rows.
    const minuteList = text.match(
        /^At ((?:\d+, )+(?:and )?\d+) minutes past the hour/i
    )
    if (minuteList) {
        const mins = minuteList[1].replace(/and /g, '').split(', ').map(Number)
        const step = mins[1] - mins[0]
        if (
            step > 0 &&
            mins.every((m, i) => i === 0 || m - mins[i - 1] === step)
        ) {
            text = text.replace(minuteList[0], `Every ${step} min`)
        }
    }
    return text
        .replace(/^At /i, '')
        .replace(/Every day/i, 'Daily')
        .replace(/Monday through Friday/i, 'Mon–Fri')
        .replace(/Monday/g, 'Mon')
        .replace(/Tuesday/g, 'Tue')
        .replace(/Wednesday/g, 'Wed')
        .replace(/Thursday/g, 'Thu')
        .replace(/Friday/g, 'Fri')
        .replace(/Saturday/g, 'Sat')
        .replace(/Sunday/g, 'Sun')
        .replace(/, and /g, ', ')
        .replace(/minutes past the hour/g, 'min past')
        .replace(/every (\d+) minutes/i, 'every $1 min')
}

/**
 * Convert a cron expression to human-readable text safely.
 * Returns null on parse failure so callers can render a fallback.
 */
export function cronToText(expression: string): string | null {
    try {
        const lines = expression.includes('\n')
            ? expression.split('\n').filter(Boolean)
            : [expression]
        return lines
            .map((l) =>
                cronstrue.toString(l.trim(), { use24HourTimeFormat: true })
            )
            .join('; ')
    } catch {
        return null
    }
}
