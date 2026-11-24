import { useMemo, useState } from 'react'
import { Check } from 'lucide-react'
import { useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Skeleton } from '@/components/ui/skeleton'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import { datedFilename, downloadJsonFile } from '@/lib/transfer'
import {
    fetchExportConfig,
    fetchConfigSchema,
    useConfigExportPreview,
    useConfigSections,
} from '../api/config-transfer-hooks'
import {
    sectionIcon,
    sectionLabel,
    sectionsFromQuery,
    sectionCount,
} from '../lib/section-meta'

const DownloadIcon = ICON.download
const FileJsonIcon = ICON.fileJson

/**
 * Section picker as a bento grid of spotlight cards — each tile is the
 * bundle section's own icon, its live entity count (from a prefetched
 * current-config preview), and a check-glyph toggle. Replaces the old pill
 * chips: same selection semantics, but a tile shows enough to decide
 * whether a section is worth including before ever clicking Export.
 */
export function ExportPanel() {
    const [searchParams] = useSearchParams()
    // The section list comes from the SERVER's own published schema, not from a
    // list compiled into this build: `ConfigSections.All` has grown before, and
    // a hardcoded picker silently omitted the new section from every UI export,
    // so "download the whole config" was not a whole config.
    const { sections: available } = useConfigSections()
    const [exporting, setExporting] = useState(false)
    const [downloadingSchema, setDownloadingSchema] = useState(false)
    const preview = useConfigExportPreview()

    // Selection is DERIVED, never synced by an effect: the baseline is the
    // `?sections=…` deep link (absent = everything the server offers) and the
    // only state is the toggles the operator has flipped since. Seeding a Set
    // from `available` in an effect would also have to re-seed whenever the
    // schema query resolved, silently discarding those toggles.
    const [overrides, setOverrides] = useState<ReadonlyMap<string, boolean>>(
        () => new Map()
    )
    const linked = useMemo(
        () => sectionsFromQuery(searchParams, available),
        [searchParams, available]
    )
    const isOn = (section: string) =>
        overrides.get(section) ?? linked.has(section)
    const selected = available.filter(isOn)

    const toggle = (section: string) =>
        setOverrides((prev) => {
            const next = new Map(prev)
            next.set(section, !(prev.get(section) ?? linked.has(section)))
            return next
        })

    const handleExport = async () => {
        setExporting(true)
        try {
            const bundle = await fetchExportConfig(
                selected.length < available.length ? selected : undefined
            )
            downloadJsonFile(bundle, datedFilename('config', 'json'))
            toast.success('Config bundle downloaded')
        } catch {
            toast.error('Failed to export config')
        } finally {
            setExporting(false)
        }
    }

    const handleDownloadSchema = async () => {
        setDownloadingSchema(true)
        try {
            const schema = await fetchConfigSchema()
            downloadJsonFile(schema, 'arn-config.schema.json')
            toast.success('Schema downloaded')
        } catch {
            toast.error('Failed to download schema')
        } finally {
            setDownloadingSchema(false)
        }
    }

    return (
        <div className="flex flex-col gap-4">
            <div
                className="grid grid-cols-2 gap-3 sm:grid-cols-3"
                role="group"
                aria-label="Sections to include"
            >
                {available.map((section) => {
                    const on = isOn(section)
                    const Icon = sectionIcon(section)
                    const count = preview.data
                        ? sectionCount(preview.data, section)
                        : null
                    return (
                        <button
                            key={section}
                            type="button"
                            role="checkbox"
                            aria-checked={on}
                            title={`Bundle section: ${section}`}
                            onClick={() => toggle(section)}
                            className="text-left"
                        >
                            <SpotlightCard
                                className={cn(
                                    'flex h-full flex-col p-4 transition-colors',
                                    on
                                        ? 'border-primary/60 bg-primary/5'
                                        : 'hover:border-primary/30'
                                )}
                            >
                                <div className="flex h-full flex-col gap-3">
                                    <div className="flex items-start justify-between">
                                        <Icon
                                            className="size-5 text-muted-foreground"
                                            aria-hidden
                                        />
                                        <div
                                            className={cn(
                                                'flex size-5 shrink-0 items-center justify-center rounded-full border transition-colors',
                                                on
                                                    ? 'border-primary bg-primary text-primary-foreground'
                                                    : 'border-border text-transparent'
                                            )}
                                        >
                                            <Check
                                                className="size-3"
                                                aria-hidden
                                            />
                                        </div>
                                    </div>
                                    <div className="flex flex-col gap-0.5">
                                        <span className="text-sm font-semibold">
                                            {sectionLabel(section)}
                                        </span>
                                        {count === null ? (
                                            <Skeleton className="h-3.5 w-16" />
                                        ) : (
                                            <span className="font-mono text-xs text-muted-foreground">
                                                {count}{' '}
                                                {count === 1 ? 'item' : 'items'}
                                            </span>
                                        )}
                                    </div>
                                </div>
                            </SpotlightCard>
                        </button>
                    )
                })}
            </div>
            <div className="flex flex-wrap gap-2">
                <Button
                    type="button"
                    onClick={handleExport}
                    disabled={exporting || selected.length === 0}
                >
                    <DownloadIcon className="size-4" />
                    {exporting ? 'Exporting...' : 'Export JSON'}
                </Button>
                <Button
                    type="button"
                    variant="outline"
                    onClick={handleDownloadSchema}
                    disabled={downloadingSchema}
                >
                    <FileJsonIcon className="size-4" />
                    {downloadingSchema ? 'Downloading...' : 'Download schema'}
                </Button>
            </div>
        </div>
    )
}
