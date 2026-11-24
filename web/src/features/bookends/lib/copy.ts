import { BookOpen, type LucideIcon } from 'lucide-react'

export interface BookendCopy {
    /** Lowercase noun for mid-sentence use ("Save bookend", "Live assigned bookend."). */
    readonly singular: string
    /** Sentence-cased noun for labels and headings ("Bookend name"). */
    readonly singularTitle: string
    readonly plural: string
    readonly title: string
    readonly description: string
    readonly placeholder: string
    readonly createLabel: string
    readonly emptyMessage: string
    readonly emptyDetail: string
    readonly searchPlaceholder: string
}

/** The single source of user-facing copy — one noun now: a bookend is a pair. */
export const BOOKEND_COPY: BookendCopy = {
    singular: 'bookend',
    singularTitle: 'Bookend',
    plural: 'bookends',
    title: 'Bookends',
    description: 'The shared library of intro and outro pairs.',
    placeholder: "e.g. Looking over {city} today, here's your traffic.",
    createLabel: 'New bookend',
    emptyMessage: 'No bookends yet',
    emptyDetail: 'Write an intro and outro pair, then assign it to a station.',
    searchPlaceholder: 'Search bookends, tokens, or stations...',
}

/** The gallery/studio icon — the pair wrapping the bulletin read. */
export const BOOKEND_ICON: LucideIcon = BookOpen
