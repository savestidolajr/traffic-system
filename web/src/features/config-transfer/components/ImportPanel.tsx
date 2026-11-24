import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { MovingBorder } from '@/components/ui/aceternity/moving-border'
import { MetricCard } from '@/components/MetricCard'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import {
    fetchExportConfig,
    tryImportConfig,
    usePendingImports,
} from '../api/config-transfer-hooks'
import type {
    ConfigImportReport,
    ConfigImportAccepted,
} from '../api/config-transfer-schemas'
import { ImportReviewTabs } from './ImportReviewTabs'

const UploadIcon = ICON.upload
const CheckIcon = ICON.success
const WarningIcon = ICON.warning

/**
 * Drag-and-drop config bundle import. A dry-run always runs first; applying
 * is a deliberate second step once the reviewer has read the diff.
 */
export function ImportPanel() {
    const [report, setReport] = useState<ConfigImportReport | null>(null)
    const [uploadedBundle, setUploadedBundle] = useState<unknown>(null)
    const [currentBundle, setCurrentBundle] = useState<unknown>(null)
    const [importing, setImporting] = useState(false)
    const [applying, setApplying] = useState(false)
    const [dragActive, setDragActive] = useState(false)
    const inputRef = useRef<HTMLInputElement>(null)
    const pendingImports = usePendingImports()

    const reset = () => {
        setReport(null)
        setUploadedBundle(null)
        setCurrentBundle(null)
    }

    const handleFile = async (file: File) => {
        reset()
        let parsed: unknown
        try {
            parsed = JSON.parse(await file.text())
        } catch {
            toast.error('Could not parse file as JSON')
            return
        }
        setUploadedBundle(parsed)
        setImporting(true)
        try {
            const [outcome, before] = await Promise.all([
                tryImportConfig({ bundle: parsed, dryRun: true }),
                fetchExportConfig().catch(() => null),
            ])
            setCurrentBundle(before)
            if (outcome.ok) {
                // Dry-run never parks for approval — it is always a plain report.
                setReport(outcome.result as ConfigImportReport)
            } else if ('report' in outcome) {
                setReport(outcome.report)
            } else {
                toast.error(
                    outcome.error instanceof Error
                        ? outcome.error.message
                        : 'Import failed'
                )
            }
        } finally {
            setImporting(false)
        }
    }

    const handleApply = async () => {
        if (!uploadedBundle) return
        setApplying(true)
        try {
            const outcome = await tryImportConfig({
                bundle: uploadedBundle,
                dryRun: false,
            })
            if (outcome.ok && 'pendingImportId' in outcome.result) {
                const accepted = outcome.result as ConfigImportAccepted
                setReport(accepted.report)
                toast.info('Sent for second-admin approval')
                pendingImports.refetch()
            } else if (outcome.ok) {
                const applied = outcome.result as ConfigImportReport
                setReport(applied)
                toast.success(
                    applied.errors > 0
                        ? `Applied with ${applied.errors} error${applied.errors === 1 ? '' : 's'}`
                        : 'Import applied successfully'
                )
            } else if ('report' in outcome) {
                setReport(outcome.report)
                toast.error(
                    'Bundle no longer validates: review the errors below'
                )
            } else {
                toast.error(
                    outcome.error instanceof Error
                        ? outcome.error.message
                        : 'Import failed'
                )
            }
        } finally {
            setApplying(false)
        }
    }

    if (!report) {
        return (
            <Dropzone
                importing={importing}
                dragActive={dragActive}
                setDragActive={setDragActive}
                inputRef={inputRef}
                onFile={handleFile}
            />
        )
    }

    const canApply = !report.applied && report.errors === 0 && report.dryRun
    const totalChanges = report.creates + report.updates + report.errors

    return (
        <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                <MetricCard label="Create" icon={ICON.add}>
                    {report.creates}
                </MetricCard>
                <MetricCard label="Update" icon={ICON.edit}>
                    {report.updates}
                </MetricCard>
                <MetricCard label="Unchanged">{report.unchanged}</MetricCard>
                <MetricCard label="Errors" icon={WarningIcon}>
                    {report.errors}
                </MetricCard>
            </div>

            {report.errors > 0 && (
                <MovingBorder tone="destructive">
                    <div className="flex items-center gap-2 rounded-[calc(0.5rem-1px)] bg-background px-4 py-2.5 text-sm">
                        <WarningIcon className="size-4 shrink-0 text-destructive" />
                        Fix the {report.errors} error
                        {report.errors === 1 ? '' : 's'} below before applying:
                        an invalid bundle blocks the whole import.
                    </div>
                </MovingBorder>
            )}
            {report.applied && (
                <div className="flex items-center gap-2 rounded-lg border border-success/30 bg-success/5 px-4 py-2.5 text-sm text-success">
                    <CheckIcon className="size-4 shrink-0" />
                    Applied. Re-import the same file any time. Unchanged
                    entities are always a no-op.
                </div>
            )}
            {report.requiresApproval &&
                !report.applied &&
                report.errors === 0 && (
                    <MovingBorder tone="warning">
                        <div className="flex items-center gap-2 rounded-[calc(0.5rem-1px)] bg-background px-4 py-2.5 text-sm">
                            <WarningIcon className="size-4 shrink-0 text-warning" />
                            This environment requires a second admin to approve
                            before applying.
                        </div>
                    </MovingBorder>
                )}

            <ImportReviewTabs
                report={report}
                beforeBundle={currentBundle}
                afterBundle={uploadedBundle}
            />

            <div className="flex flex-wrap gap-2">
                <Button
                    type="button"
                    onClick={handleApply}
                    disabled={!canApply || applying || totalChanges === 0}
                >
                    {applying ? 'Applying...' : 'Apply import'}
                </Button>
                <Button type="button" variant="outline" onClick={reset}>
                    Choose a different file
                </Button>
            </div>
        </div>
    )
}

function Dropzone({
    importing,
    dragActive,
    setDragActive,
    inputRef,
    onFile,
}: {
    readonly importing: boolean
    readonly dragActive: boolean
    readonly setDragActive: (active: boolean) => void
    readonly inputRef: React.RefObject<HTMLInputElement | null>
    readonly onFile: (file: File) => void | Promise<void>
}) {
    return (
        <SpotlightCard
            className={cn(
                'border-2 border-dashed transition-colors',
                dragActive && 'border-primary bg-primary/5'
            )}
        >
            <div
                onClick={() => inputRef.current?.click()}
                onDragOver={(e) => {
                    e.preventDefault()
                    setDragActive(true)
                }}
                onDragLeave={() => setDragActive(false)}
                onDrop={(e) => {
                    e.preventDefault()
                    setDragActive(false)
                    const file = e.dataTransfer.files?.[0]
                    if (file) void onFile(file)
                }}
                className="flex cursor-pointer flex-col items-center gap-3 px-6 py-14 text-center"
            >
                <input
                    ref={inputRef}
                    type="file"
                    accept="application/json"
                    className="hidden"
                    aria-label="Import JSON"
                    onChange={(e) => {
                        const file = e.target.files?.[0]
                        if (file) void onFile(file)
                        e.target.value = ''
                    }}
                />
                <button
                    type="button"
                    aria-label="Import a config bundle"
                    onClick={(e) => {
                        e.stopPropagation()
                        inputRef.current?.click()
                    }}
                    className="flex cursor-pointer flex-col items-center gap-3"
                >
                    <UploadIcon
                        className={cn(
                            'size-8 text-muted-foreground',
                            importing && 'animate-bounce'
                        )}
                    />
                    <span className="flex flex-col gap-1">
                        <span className="text-sm font-semibold">
                            {importing
                                ? 'Validating...'
                                : 'Drag & drop a config bundle, or click to browse'}
                        </span>
                        <span className="text-xs text-muted-foreground">
                            JSON only. Always dry-run validated first - nothing
                            changes until you apply.
                        </span>
                    </span>
                </button>
            </div>
        </SpotlightCard>
    )
}
