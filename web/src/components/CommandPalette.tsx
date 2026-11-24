import { useEffect, useMemo, useState } from 'react'
import { useNavigate, type NavigateFunction } from 'react-router'
import { AudioLines, Drama, BookText } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import {
    CommandDialog,
    CommandEmpty,
    CommandGroup,
    CommandInput,
    CommandItem,
    CommandList,
    CommandShortcut,
} from '@/components/ui/command'
import {
    ROUTE_STATIONS,
    ROUTE_VOICES,
    ROUTE_DELIVERY,
    ROUTE_PRONUNCIATION,
    ROUTE_OPERATORS,
    NAV_SHORTCUT_PREFIX,
    visibleRoutes,
} from '@/lib/routes'
import { shouldIgnoreKeyboardEvent } from '@/lib/dom'
import { useNavShortcuts } from '@/hooks/use-nav-shortcuts'
import { useIsAdmin, useIsOperator } from '@/hooks/use-current-user'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { useDeliveryStyles } from '@/features/delivery/api/delivery-style-hooks'
import { usePronunciationDictionaries } from '@/features/pronunciation/api/pronunciation-hooks'
import { useOperatorAssignments } from '@/features/operators/api/operator-hooks'
import type { MarketSchedule } from '@/features/schedules/types'

/** Dispatch on `document` to open the palette from anywhere (sidebar). */
export const OPEN_COMMAND_PALETTE_EVENT = 'arn:open-command-palette'

export function CommandPalette() {
    const [open, setOpen] = useState(false)
    const navigate = useNavigate()

    // `g` then a digit/letter — bound here so it works app-wide; the palette
    // advertises the same chord on each page row below.
    useNavShortcuts()

    // Cmd+K / Ctrl+K — useGlobalKeydown handles single-key shortcuts only,
    // so we keep a direct listener for the modifier combo. The sidebar's
    // search button opens the palette via a custom event so mouse-first
    // users can discover it too.
    useEffect(() => {
        const down = (e: KeyboardEvent) => {
            if (shouldIgnoreKeyboardEvent(e)) return
            if (e.key === 'k' && (e.metaKey || e.ctrlKey)) {
                e.preventDefault()
                setOpen((prev) => !prev)
            }
        }
        const openFromEvent = () => setOpen(true)
        document.addEventListener('keydown', down)
        document.addEventListener(OPEN_COMMAND_PALETTE_EVENT, openFromEvent)
        return () => {
            document.removeEventListener('keydown', down)
            document.removeEventListener(
                OPEN_COMMAND_PALETTE_EVENT,
                openFromEvent
            )
        }
    }, [])

    const runCommand = (command: () => void) => {
        setOpen(false)
        command()
    }

    return (
        <CommandDialog open={open} onOpenChange={setOpen}>
            <CommandInput placeholder="Search pages, stations, voices…" />
            <CommandList>
                <CommandEmpty>No results found.</CommandEmpty>
                {/* Mounted only while open so entity lists fetch on demand. */}
                {open && (
                    <PaletteResults
                        runCommand={runCommand}
                        navigate={navigate}
                    />
                )}
            </CommandList>
        </CommandDialog>
    )
}

/**
 * The searchable result set. Pages plus the unique, configurable entities an
 * operator might jump to — stations, voice profiles, delivery styles,
 * pronunciation dictionaries, operators. Individual bulletins are
 * deliberately excluded: they aren't unique (many share the same
 * station/market/status) and the set is unbounded, so they'd flood the list
 * without being reliably distinguishable — History is the right place to
 * browse them. cmdk filters items client-side by each item's `value`, so
 * every label an operator might search by (station call sign, schedule name,
 * market id, assigned road names, dictionary rule text) is folded into it.
 */
function PaletteResults({
    runCommand,
    navigate,
}: {
    readonly runCommand: (command: () => void) => void
    readonly navigate: NavigateFunction
}) {
    const isAdmin = useIsAdmin()
    const isOperator = useIsOperator()
    const routes = visibleRoutes({ isAdmin, isOperator })

    // Stations and the three shared libraries (voices, delivery, pronunciation) are
    // operator routes, so their jump lists must not be admin-only; the Operators
    // register below stays Admin with its page.
    const canSeeStations = isAdmin || isOperator
    const { data: schedulesData } = useSchedules({ enabled: canSeeStations })
    const { data: profilesData } = useVoiceProfiles({ enabled: canSeeStations })
    const { data: stylesData } = useDeliveryStyles({ enabled: canSeeStations })
    const { data: dictionariesData } = usePronunciationDictionaries({
        enabled: canSeeStations,
    })
    const { data: operators } = useOperatorAssignments({ enabled: isAdmin })

    const schedules = useMemo(
        () => schedulesData?.schedules ?? [],
        [schedulesData]
    )
    const profiles = profilesData?.profiles ?? []
    const styles = stylesData?.styles ?? []
    const dictionaries = dictionariesData?.items ?? []
    const assignments = operators?.operators ?? []

    // Resolve a station's schedule by the same label precedence its call-sign
    // chips already use elsewhere (station -> name -> marketId), so "3AW" and
    // "Melbourne Morning" both resolve to the one schedule, and folding in its
    // name/marketId makes market/city searches ("Melbourne") hit every voice,
    // delivery style, and dictionary assigned to that station — not just
    // Stations itself.
    const scheduleByLabel = useMemo(() => {
        const map = new Map<string, MarketSchedule>()
        for (const s of schedules) {
            map.set(s.station ?? s.name ?? s.marketId, s)
        }
        return map
    }, [schedules])

    const searchTextForStations = (stations: readonly string[]): string =>
        stations
            .flatMap((label) => {
                const s = scheduleByLabel.get(label)
                return s ? [label, s.name ?? '', s.marketId] : [label]
            })
            .join(' ')

    return (
        <>
            <CommandGroup heading="Pages">
                {routes.map((route) => (
                    <CommandItem
                        key={route.path}
                        value={`${route.label} ${route.path}`}
                        onSelect={() => runCommand(() => navigate(route.path))}
                    >
                        <route.icon className="mr-2 size-4" />
                        <span>{route.label}</span>
                        {route.shortcut && (
                            <CommandShortcut>
                                {NAV_SHORTCUT_PREFIX} {route.shortcut}
                            </CommandShortcut>
                        )}
                    </CommandItem>
                ))}
            </CommandGroup>

            {canSeeStations && schedules.length > 0 && (
                <CommandGroup heading="Stations">
                    {schedules.map((s) => (
                        <CommandItem
                            key={s.marketId}
                            value={`${s.station ?? ''} ${s.name ?? ''} ${s.marketId}`}
                            onSelect={() =>
                                runCommand(() =>
                                    navigate(
                                        `${ROUTE_STATIONS}?open=${s.marketId}`
                                    )
                                )
                            }
                        >
                            <ICON.settings className="mr-2 size-4 text-muted-foreground" />
                            <span className="font-medium">
                                {s.station ?? s.name ?? s.marketId}
                            </span>
                            <span className="ml-1.5 text-muted-foreground">
                                {s.name ?? s.marketId}
                            </span>
                        </CommandItem>
                    ))}
                </CommandGroup>
            )}

            {canSeeStations && profiles.length > 0 && (
                <CommandGroup heading="Voices">
                    {profiles.map((p) => (
                        <CommandItem
                            key={p.id}
                            value={`${p.name} ${p.baseVoiceName} ${searchTextForStations(p.assignedStations)}`}
                            onSelect={() =>
                                runCommand(() =>
                                    navigate(`${ROUTE_VOICES}?open=${p.id}`)
                                )
                            }
                        >
                            <AudioLines className="mr-2 size-4 text-muted-foreground" />
                            <span className="font-medium">{p.name}</span>
                            <span className="ml-1.5 text-muted-foreground">
                                {p.assignedStations.length > 0
                                    ? p.assignedStations.join(', ')
                                    : p.baseVoiceName}
                            </span>
                        </CommandItem>
                    ))}
                </CommandGroup>
            )}

            {canSeeStations && styles.length > 0 && (
                <CommandGroup heading="Delivery styles">
                    {styles.map((s) => (
                        <CommandItem
                            key={s.id}
                            value={`${s.name} ${searchTextForStations(s.assignedStations)}`}
                            onSelect={() =>
                                runCommand(() =>
                                    navigate(`${ROUTE_DELIVERY}?open=${s.id}`)
                                )
                            }
                        >
                            <Drama className="mr-2 size-4 text-muted-foreground" />
                            <span className="font-medium">{s.name}</span>
                            {s.assignedStations.length > 0 && (
                                <span className="ml-1.5 text-muted-foreground">
                                    {s.assignedStations.join(', ')}
                                </span>
                            )}
                        </CommandItem>
                    ))}
                </CommandGroup>
            )}

            {canSeeStations && dictionaries.length > 0 && (
                <CommandGroup heading="Pronunciation">
                    {dictionaries.map((d) => {
                        // Rule text (e.g. "Hoddle St" -> "Hoddle Street") is
                        // folded in so a road/suburb search finds the
                        // dictionary that fixes its pronunciation, not just
                        // dictionaries matched by name.
                        const ruleText = d.rules
                            .flatMap((r) => [r.stringToReplace, r.replacement])
                            .join(' ')
                        return (
                            <CommandItem
                                key={d.id}
                                value={`${d.name} ${searchTextForStations(d.assignedStations)} ${ruleText}`}
                                onSelect={() =>
                                    runCommand(() =>
                                        navigate(
                                            `${ROUTE_PRONUNCIATION}?open=${d.id}`
                                        )
                                    )
                                }
                            >
                                <BookText className="mr-2 size-4 text-muted-foreground" />
                                <span className="font-medium">{d.name}</span>
                                {d.assignedStations.length > 0 && (
                                    <span className="ml-1.5 text-muted-foreground">
                                        {d.assignedStations.join(', ')}
                                    </span>
                                )}
                            </CommandItem>
                        )
                    })}
                </CommandGroup>
            )}

            {isAdmin && assignments.length > 0 && (
                <CommandGroup heading="Operators">
                    {assignments.map((o) => (
                        <CommandItem
                            key={o.objectId}
                            value={`${o.displayName ?? ''} ${o.objectId} ${o.markets.join(' ')}`}
                            onSelect={() =>
                                runCommand(() =>
                                    navigate(
                                        `${ROUTE_OPERATORS}?open=${o.objectId}`
                                    )
                                )
                            }
                        >
                            <ICON.safety className="mr-2 size-4 text-muted-foreground" />
                            <span className="font-medium">
                                {o.displayName || o.objectId}
                            </span>
                            {o.markets.length > 0 && (
                                <span className="ml-1.5 text-muted-foreground">
                                    {o.markets.join(', ')}
                                </span>
                            )}
                        </CommandItem>
                    ))}
                </CommandGroup>
            )}
        </>
    )
}
