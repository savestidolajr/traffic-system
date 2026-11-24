import { useState, useCallback } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useDebounce } from '@/hooks/use-debounce'
import { useMapConfig } from '@/features/jobs/api/map-config-hooks'
import type { GeocodeResult } from './types'

interface HereGeocodeItem {
    id: string
    title: string
    address?: { label?: string }
    position?: { lat: number; lng: number }
}

// HERE Geocoding & Search v7, authenticated with the same browser map key the
// basemap uses (served by GET /api/map-config, never bundled).
async function geocode(
    query: string,
    apiKey: string,
    signal: AbortSignal
): Promise<GeocodeResult[]> {
    const response = await fetch(
        `https://geocode.search.hereapi.com/v1/geocode?q=${encodeURIComponent(query)}&limit=5&apiKey=${encodeURIComponent(apiKey)}`,
        { signal }
    )
    if (!response.ok) {
        throw new Error(`Geocoding request failed: ${response.status}`)
    }
    const body: { items?: HereGeocodeItem[] } = await response.json()
    return (body.items ?? []).flatMap((item) =>
        item.position
            ? [
                  {
                      id: item.id,
                      label: item.address?.label ?? item.title,
                      lat: item.position.lat,
                      lng: item.position.lng,
                  },
              ]
            : []
    )
}

export function useGeocoding(
    onCenterChange: (lat: number, lng: number) => void
) {
    const [searchQuery, setSearchQuery] = useState('')
    const [searchOpen, setSearchOpen] = useState(false)
    const { data: mapConfig } = useMapConfig()
    const apiKey = mapConfig?.hereApiKey ?? ''

    const debouncedQuery = useDebounce(searchQuery, 300)
    const trimmed = debouncedQuery.trim()

    // React Query owns the fetch lifecycle: superseded requests are cancelled
    // via the AbortSignal (so a slow old response can't overwrite newer
    // results) and the previous results stay visible while the next query
    // loads. Replaces a manual fetch-in-effect that set state synchronously.
    const { data, isFetching: isSearching } = useQuery({
        queryKey: ['geocoding', trimmed],
        queryFn: ({ signal }) => geocode(trimmed, apiKey, signal),
        enabled: trimmed.length > 0 && apiKey.length > 0,
        placeholderData: keepPreviousData,
        staleTime: 5 * 60 * 1000,
    })

    // Empty query shows nothing; otherwise the latest (or kept-previous) hits.
    const searchResults = trimmed.length > 0 ? (data ?? []) : []

    const handleSearchSelect = useCallback(
        (result: GeocodeResult) => {
            onCenterChange(result.lat, result.lng)
            setSearchQuery(result.label)
            setSearchOpen(false)
        },
        [onCenterChange]
    )

    return {
        searchQuery,
        setSearchQuery,
        searchResults,
        isSearching,
        searchOpen,
        setSearchOpen,
        handleSearchSelect,
        available: apiKey.length > 0,
    }
}
