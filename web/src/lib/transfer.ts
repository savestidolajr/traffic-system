// =============================================================================
// Shared export (data transfer) helpers.
//
// Conventions for every export surface in the app:
//   - File names: arn-<slug>-YYYY-MM-DD.<ext>  (datedFilename)
//   - Button: <ExportButton> in components/TransferButtons
//   - Placement: right-aligned in the toolbar of the content it acts on
//
// Imports are NOT here: configuration enters through the Data transfer page
// alone, which validates and diffs a bundle before anything changes.
// =============================================================================

/** Local calendar date stamp (YYYY-MM-DD) for export file names. */
function dateStamp(): string {
    const now = new Date()
    const month = String(now.getMonth() + 1).padStart(2, '0')
    const day = String(now.getDate()).padStart(2, '0')
    return `${now.getFullYear()}-${month}-${day}`
}

/** `arn-<slug>-YYYY-MM-DD.<ext>` — the app-wide export file-name convention. */
export function datedFilename(slug: string, ext: string): string {
    return `arn-${slug}-${dateStamp()}.${ext}`
}

/** Triggers a browser download of `content` under `filename`. */
function downloadBlob(content: string, filename: string, mimeType: string) {
    const blob = new Blob([content], { type: mimeType })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = filename
    anchor.click()
    URL.revokeObjectURL(url)
}

/** Triggers a browser download for a URL through the shared transfer path.
 * Audio players previously bypassed this helper with hand-rolled anchors. */
export function downloadUrl(url: string, filename: string) {
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = filename
    anchor.rel = 'noopener noreferrer'
    anchor.click()
}

/** Downloads `data` as pretty-printed JSON. */
export function downloadJsonFile(data: unknown, filename: string) {
    downloadBlob(JSON.stringify(data, null, 2), filename, 'application/json')
}

/** Downloads a prebuilt CSV string. */
export function downloadCsvFile(csv: string, filename: string) {
    downloadBlob(csv, filename, 'text/csv;charset=utf-8;')
}

/** RFC-4180 style cell escape: quote-wrap when the value carries `"` `,` or newlines. */
function escapeCsvCell(
    value: string | number | boolean | null | undefined
): string {
    if (value == null) return ''
    const text = String(value)
    return /[",\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

/** Builds a CSV document (trailing newline included) from rows of cells. */
export function toCsv(
    rows: (string | number | boolean | null | undefined)[][]
): string {
    return rows.map((row) => row.map(escapeCsvCell).join(',')).join('\n') + '\n'
}
