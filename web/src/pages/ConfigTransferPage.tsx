import { useSearchParams } from 'react-router'
import { Header, PageContainer } from '@/components/layout'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { Badge } from '@/components/ui/badge'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import { ICON } from '@/lib/design/icons'
import { ExportPanel } from '@/features/config-transfer/components/ExportPanel'
import { ImportPanel } from '@/features/config-transfer/components/ImportPanel'
import { PendingApprovalsCard } from '@/features/config-transfer/components/PendingApprovalsCard'
import { usePendingImports } from '@/features/config-transfer/api/config-transfer-hooks'

const DownloadIcon = ICON.download
const UploadIcon = ICON.upload

type Mode = 'export' | 'import'

export function ConfigTransferPage() {
    useDocumentTitle('Data transfer')
    const [searchParams, setSearchParams] = useSearchParams()
    const pendingImports = usePendingImports()
    const pendingCount = pendingImports.data?.items.length ?? 0

    const mode: Mode =
        searchParams.get('tab') === 'import' ? 'import' : 'export'
    const setMode = (next: string) => {
        const params = new URLSearchParams(searchParams)
        params.set('tab', next)
        setSearchParams(params, { replace: true })
    }

    return (
        <PageContainer>
            <Header
                title="Data transfer"
                description="One JSON file for the whole config, with validation and a field-level diff."
            />
            <Tabs value={mode} onValueChange={setMode} className="mt-1">
                <TabsList>
                    <TabsTrigger value="export">
                        <DownloadIcon className="size-3.5" />
                        Export
                    </TabsTrigger>
                    <TabsTrigger value="import">
                        <UploadIcon className="size-3.5" />
                        Import
                        {pendingCount > 0 && (
                            <Badge
                                variant="warning"
                                className="px-1.5 py-0 text-3xs"
                            >
                                {pendingCount}
                            </Badge>
                        )}
                    </TabsTrigger>
                </TabsList>

                <TabsContent value="export" className="mt-4">
                    <ExportPanel />
                </TabsContent>
                <TabsContent
                    value="import"
                    className="mt-4 flex flex-col gap-4"
                >
                    <PendingApprovalsCard />
                    <ImportPanel />
                </TabsContent>
            </Tabs>
        </PageContainer>
    )
}
