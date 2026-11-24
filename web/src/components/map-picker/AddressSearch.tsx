import { useRef, useCallback, useState } from 'react'
import { Search, Loader2, MapPin } from 'lucide-react'
import { useGeocoding } from './use-geocoding'
import { Input } from '@/components/ui/input'
import type { GeocodeResult } from './types'

const RESULT_ID_PREFIX = 'addr-result-'

export function AddressSearch({
    onCenterChange,
    disabled = false,
}: {
    onCenterChange: (lat: number, lng: number) => void
    disabled?: boolean
}) {
    const {
        searchQuery,
        setSearchQuery,
        searchResults,
        isSearching,
        searchOpen,
        setSearchOpen,
        handleSearchSelect: select,
        available,
    } = useGeocoding(onCenterChange)

    const inputRef = useRef<HTMLInputElement>(null)
    const [activeIndex, setActiveIndex] = useState(-1)
    const resultsRef = useRef<HTMLUListElement>(null)

    const handleSearchSelect = useCallback(
        (result: GeocodeResult) => {
            select(result)
            inputRef.current?.blur()
        },
        [select]
    )

    const handleKeyDown = useCallback(
        (e: React.KeyboardEvent) => {
            if (!searchOpen || searchResults.length === 0) return

            if (e.key === 'ArrowDown') {
                e.preventDefault()
                const next = Math.min(activeIndex + 1, searchResults.length - 1)
                setActiveIndex(next)
                scrollIntoView(next)
            } else if (e.key === 'ArrowUp') {
                e.preventDefault()
                const next = Math.max(activeIndex - 1, 0)
                setActiveIndex(next)
                scrollIntoView(next)
            } else if (e.key === 'Enter' && activeIndex >= 0) {
                e.preventDefault()
                handleSearchSelect(searchResults[activeIndex])
            } else if (e.key === 'Escape') {
                setSearchOpen(false)
                setActiveIndex(-1)
            }
        },
        [
            searchOpen,
            searchResults,
            handleSearchSelect,
            setSearchOpen,
            activeIndex,
        ]
    )

    return (
        <div className="relative flex flex-col gap-2">
            <label
                htmlFor="address-search"
                className="text-xs text-muted-foreground"
            >
                Search address
            </label>
            <div className="relative">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                    ref={inputRef}
                    id="address-search"
                    type="text"
                    role="combobox"
                    aria-expanded={searchOpen}
                    aria-controls="address-results"
                    aria-autocomplete="list"
                    aria-activedescendant={
                        activeIndex >= 0
                            ? `${RESULT_ID_PREFIX}${activeIndex}`
                            : undefined
                    }
                    placeholder={
                        available
                            ? 'Type an address'
                            : 'Address search unavailable'
                    }
                    value={searchQuery}
                    onChange={(e) => {
                        setSearchQuery(e.target.value)
                        setActiveIndex(-1)
                        if (e.target.value.trim()) setSearchOpen(true)
                    }}
                    onFocus={() => {
                        if (searchQuery.trim()) setSearchOpen(true)
                    }}
                    onBlur={() => {
                        // Delay so click on a result item registers first
                        setTimeout(() => setSearchOpen(false), 200)
                    }}
                    onKeyDown={handleKeyDown}
                    disabled={disabled || !available}
                    className="pl-9"
                />
                {isSearching && (
                    <Loader2 className="absolute right-2.5 top-1/2 size-4 -translate-y-1/2 animate-spin text-muted-foreground" />
                )}
            </div>

            {searchOpen && searchResults.length > 0 && (
                <ul
                    ref={resultsRef}
                    id="address-results"
                    role="listbox"
                    className="absolute left-0 right-0 top-full z-20 mt-1 max-h-60 overflow-auto rounded-md border bg-popover p-1 shadow-md"
                    onMouseDown={(e) => {
                        // Prevent the blur handler from closing before click registers
                        e.preventDefault()
                    }}
                >
                    {searchResults.map((result, i) => (
                        <li
                            key={result.id}
                            id={`${RESULT_ID_PREFIX}${i}`}
                            role="option"
                            aria-selected={i === activeIndex}
                            onMouseDown={() => handleSearchSelect(result)}
                            onMouseEnter={() => {
                                setActiveIndex(i)
                            }}
                            className={`flex cursor-pointer items-start gap-2 rounded-sm px-2 py-1.5 text-sm outline-none hover:bg-accent hover:text-accent-foreground aria-selected:bg-accent aria-selected:text-accent-foreground`}
                        >
                            <MapPin className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                            <div className="flex min-w-0 flex-col">
                                <span className="truncate font-medium">
                                    {result.label.split(',')[0]}
                                </span>
                                <span className="truncate text-xs text-muted-foreground">
                                    {result.label}
                                </span>
                            </div>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    )
}

function scrollIntoView(index: number) {
    // Schedules microtask to ensure DOM is in sync before scrolling
    requestAnimationFrame(() => {
        const el = document.getElementById(`${RESULT_ID_PREFIX}${index}`)
        el?.scrollIntoView({ block: 'nearest' })
    })
}
