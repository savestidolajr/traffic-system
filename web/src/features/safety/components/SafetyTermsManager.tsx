import { useMemo, useState } from 'react'
import { toast } from 'sonner'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { RuleComposer } from '@/components/RuleComposer'
import { ComposerField } from '@/components/ComposerField'
import { RuleRow } from '@/components/RuleRow'
import { ScopeGroupedList } from '@/components/ScopeGroupedList'
import { RuleToolbar } from '@/components/RuleToolbar'
import { EmptyState } from '@/components/EmptyState'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
    Select,
    SelectTrigger,
    SelectValue,
    SelectContent,
    SelectItem,
} from '@/components/ui/select'
import { ICON } from '@/lib/design/icons'
import { RuleTab } from '@/components/RuleTab'
import {
    filterAndGroupByScope,
    appliesToStation,
    scopeKey,
} from '@/features/guidance/lib/scope-grouping'
import {
    useSafetyTerms,
    useUpsertSafetyTerm,
    useDeleteSafetyTerm,
} from '../api/safety-hooks'
import type {
    SafetyTerm,
    SafetyKindName,
    SafetyScopeName,
} from '../api/schemas'

const AddIcon = ICON.add
const ResetIcon = ICON.reset

const MAX_TERM = 120

const KIND_BADGE: Record<SafetyKindName, string> = {
    Banned: 'bg-destructive/10 text-destructive-on-tint border-destructive/20',
    Allowed: 'bg-success/10 text-success border-success/20',
}
const KIND_LABEL: Record<SafetyKindName, string> = {
    Banned: 'Banned',
    Allowed: 'Exception',
}

interface MarketOption {
    readonly marketId: string
    readonly label: string
}

export function SafetyTermsManager({
    markets,
    station,
}: {
    readonly markets: readonly MarketOption[]
    readonly station: string
}) {
    const { data: terms, isLoading } = useSafetyTerms()
    const upsert = useUpsertSafetyTerm()
    const remove = useDeleteSafetyTerm()

    const [kind, setKind] = useState<SafetyKindName>('Banned')
    const [term, setTerm] = useState('')
    const [composerOpen, setComposerOpen] = useState(false)
    const [scope, setScope] = useState(station)

    const [search, setSearch] = useState('')
    const [kindFilter, setKindFilter] = useState('all')

    const trimmed = term.trim()
    const valid = trimmed.length > 0 && trimmed.length <= MAX_TERM
    const submitting = upsert.isPending

    const labelFor = (scopeId: string) =>
        markets.find((m) => m.marketId === scopeId)?.label ?? scopeId

    const active = search.trim() !== '' || kindFilter !== 'all'

    const groups = useMemo(
        () =>
            filterAndGroupByScope(
                terms ?? [],
                scopeKey,
                (scopeId) =>
                    markets.find((m) => m.marketId === scopeId)?.label ??
                    scopeId,
                {
                    include: (t) => {
                        if (!appliesToStation(t, station)) return false
                        const q = search.trim().toLowerCase()
                        if (q && !t.term.toLowerCase().includes(q)) return false
                        if (kindFilter !== 'all' && t.kind !== kindFilter)
                            return false
                        return true
                    },
                    compareItems: (a, b) =>
                        b.updatedAtUtc.localeCompare(a.updatedAtUtc),
                }
            ),
        [terms, markets, search, station, kindFilter]
    )

    const handleAdd = async () => {
        if (!valid || submitting) return
        try {
            await upsert.mutateAsync({
                kind,
                scope: 'Market',
                scopeId: scope,
                term: trimmed,
            })
            setTerm('')
            setComposerOpen(false)
            toast.success(
                kind === 'Banned' ? 'Banned term added' : 'Exception added'
            )
        } catch {
            toast.error('Failed to save term')
        }
    }
    const handleToggle = async (t: SafetyTerm) => {
        try {
            await upsert.mutateAsync({
                id: t.id,
                kind: t.kind as SafetyKindName,
                scope: t.scope as SafetyScopeName,
                scopeId: t.scopeId,
                term: t.term,
                enabled: !t.enabled,
            })
        } catch {
            toast.error('Failed to update term')
        }
    }

    const handleDelete = async (t: SafetyTerm) => {
        try {
            await remove.mutateAsync({
                id: t.id,
                scope: t.scope as SafetyScopeName,
                scopeId: t.scopeId,
            })
            toast.success('Term removed')
        } catch {
            toast.error('Failed to remove term')
        }
    }

    return (
        <RuleTab>
            <RuleToolbar
                add={{
                    what: 'a banned word or exception',
                    onClick: () => {
                        if (!composerOpen) {
                            setScope(station)
                        }
                        setComposerOpen((open) => !open)
                    },
                    active: composerOpen,
                    hint: 'A banned word forces the bulletin to be rewritten. An exception lets a real place name like Cockle Bay through.',
                }}
                search={search}
                onSearchChange={setSearch}
                placeholder="Search terms…"
            >
                <Select value={kindFilter} onValueChange={setKindFilter}>
                    <SelectTrigger
                        aria-label="Filter by type"
                        className="sm:w-40"
                    >
                        <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="all">All types</SelectItem>
                        <SelectItem value="Banned">Banned</SelectItem>
                        <SelectItem value="Allowed">Exception</SelectItem>
                    </SelectContent>
                </Select>
            </RuleToolbar>

            {composerOpen && (
                <RuleComposer
                    onCancel={() => setComposerOpen(false)}
                    submit={{
                        label: 'Add',
                        icon: AddIcon,
                        onClick: handleAdd,
                        disabled: !valid || submitting,
                        ready: valid,
                        submitting,
                    }}
                >
                    <ComposerField label="Type" width="sm:w-44">
                        <Select
                            value={kind}
                            onValueChange={(v) => setKind(v as SafetyKindName)}
                        >
                            <SelectTrigger aria-label="Type">
                                <SelectValue placeholder="Choose a type" />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value="Banned">Banned</SelectItem>
                                <SelectItem value="Allowed">
                                    Exception
                                </SelectItem>
                            </SelectContent>
                        </Select>
                    </ComposerField>
                    <ComposerField
                        label="Word or phrase"
                        htmlFor="safety-term"
                        width="flex-1"
                    >
                        <Input
                            id="safety-term"
                            maxLength={MAX_TERM}
                            placeholder="for example, gridlock or Gun Club Road"
                            value={term}
                            onChange={(e) => setTerm(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === 'Enter') handleAdd()
                            }}
                        />
                    </ComposerField>
                </RuleComposer>
            )}

            <ScopedActivityHistory
                resource="safety-terms"
                entityId={station}
                label={`History for ${labelFor(station)}`}
            />

            <ScopeGroupedList<SafetyTerm>
                groups={groups}
                isLoading={isLoading}
                empty={
                    <EmptyState
                        action={
                            active ? (
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={() => {
                                        setSearch('')
                                        setKindFilter('all')
                                    }}
                                >
                                    <ResetIcon />
                                    Clear filters
                                </Button>
                            ) : undefined
                        }
                    >
                        {active
                            ? 'No terms match these filters.'
                            : 'No terms yet. Add a banned word or an exception.'}
                    </EmptyState>
                }
                rowKey={(t) => t.id}
                renderRow={(t) => {
                    const k = t.kind as SafetyKindName
                    const term = t as SafetyTerm
                    return (
                        <RuleRow
                            enabled={term.enabled}
                            onToggle={() => handleToggle(term)}
                            label={term.term}
                            // The kind is a CATEGORY, the same shape of thing as Editorial
                            // guidance's type pill — so it belongs in RuleRow's `badge` slot
                            // (a fixed `sm:w-28` column that keeps pills aligned down the
                            // list), not hand-widened to `w-24` on the right where the other
                            // tabs put per-row controls and state.
                            badge={
                                <Badge
                                    variant="outline"
                                    className={KIND_BADGE[k]}
                                >
                                    {KIND_LABEL[k] ?? term.kind}
                                </Badge>
                            }
                            onDelete={() => handleDelete(term)}
                            deleteLabel={`Delete ${term.term}`}
                            itemLabel="safety term"
                            disabled={submitting}
                        />
                    )
                }}
            />
        </RuleTab>
    )
}
