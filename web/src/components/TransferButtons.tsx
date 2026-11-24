import { Button } from '@/components/ui/button'
import { ICON } from '@/lib/design/icons'

// =============================================================================
// Shared export toolbar button.
//
// Placement convention: right-aligned in the toolbar of the content it acts on
// (see lib/transfer.ts).
//
// There is deliberately no import counterpart. Configuration enters the system
// through ONE door — the Data transfer page — because that door dry-run
// validates the bundle, shows a field-level diff, and (in production) requires a
// second admin to approve. A one-click file picker in a rule toolbar can do none
// of that, and the pronunciation-rules CSV that used to live here could not even
// carry phoneme rules. Exports stay per-surface only where the artefact is a
// REPORT rather than configuration (the broadcast-log CSV).
// =============================================================================

interface ExportButtonProps {
    readonly label?: string
    readonly onExport: () => void
    readonly size?: 'sm' | 'default'
    readonly disabled?: boolean
}

export function ExportButton({
    label = 'Export JSON',
    onExport,
    size = 'sm',
    disabled,
}: ExportButtonProps) {
    const DownloadIcon = ICON.download
    return (
        <Button
            type="button"
            variant="outline"
            size={size}
            disabled={disabled}
            onClick={() => void onExport()}
        >
            <DownloadIcon className="size-3.5" />
            {label}
        </Button>
    )
}
