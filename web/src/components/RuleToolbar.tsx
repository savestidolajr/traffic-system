import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { InfoHint } from '@/components/InfoHint'
import { ICON } from '@/lib/design/icons'
import { StickyFilterRail } from '@/components/StickyFilterRail'

const SearchIcon = ICON.search
const AddIcon = ICON.add

export interface RuleToolbarAdd {
    /**
     * The thing being added, lower case, with its article: `a priority road`.
     *
     * NOT the button text. The button always reads `Add`, so the toolbar's
     * leading control is the same size and in the same place on every tab —
     * it used to carry the full phrase and jumped between 100px and 194px as
     * you switched tabs, shoving the search box with it. This prop feeds the
     * accessible name (`Add a priority road`, which still contains the visible
     * `Add`, so Label-in-Name holds) and the `?` beside it.
     */
    readonly what: string
    readonly onClick: () => void
    /** True while the composer is open — the button reads as pressed. */
    readonly active: boolean
    /**
     * What this kind of rule does to a bulletin, behind the `?`. It lives here
     * rather than in the composer because the composer is closed most of the
     * time and the explanation has to stay reachable.
     */
    readonly hint: ReactNode
}

interface RuleToolbarProps {
    /** Opens the tab's composer. Omit only for a tab with no create action. */
    readonly add?: RuleToolbarAdd
    /**
     * Omit both `search` and `onSearchChange` for a list that cannot be
     * filtered safely — the input is then not rendered at all.
     */
    readonly search?: string
    readonly onSearchChange?: (value: string) => void
    readonly placeholder?: string
    /** Filter `<Select>`s and export actions — rendered after the search box. */
    readonly children?: ReactNode
}
/**
 * The single row of chrome above every Bulletin-rules list: find, narrow,
 * create — in that order, in the same place, on every tab.
 *
 * Search LEADS because it is the stable left anchor: it is the only control
 * every tab has, so the row starts at the same place and with the same thing
 * no matter which tab you are on. `Add` is the neutral-outline pair at the far
 * right, where the eye finishes — the GitHub/Linear/Notion arrangement. It is
 * deliberately NOT a filled primary: this page's job is reading the rules that
 * exist, so a solid block leading the row overstated a secondary action.
 *
 * There is no export slot. Config leaves and enters through the Data transfer
 * page, which covers every bundle section in both directions and adds a dry-run
 * diff and an approval step that a one-click download cannot. That now includes
 * pronunciation rules: their alias-only CSV pair was retired in favour of the
 * one door, which also carries phoneme rules.
 */
export function RuleToolbar({
    add,
    search,
    onSearchChange,
    placeholder = 'Search…',
    children,
}: RuleToolbarProps) {
    return (
        <StickyFilterRail className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
            {onSearchChange && (
                /* `min-w-0` because an <input>'s intrinsic min-width defeats
                   `flex-1`: without it the row cannot shrink and the rail
                   overflows its own container. `basis-56` keeps the search
                   usable when the row wraps — but both are `sm:`, because
                   flex-basis sizes the MAIN axis and this rail is a COLUMN
                   below sm, where a 224px basis made the search wrapper 224px
                   TALL and opened a dead gap above the filters. */
                <div className="relative w-full min-w-0 sm:flex-1 sm:basis-56">
                    <SearchIcon className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                        value={search ?? ''}
                        onChange={(e) => onSearchChange(e.target.value)}
                        placeholder={placeholder}
                        className="pl-9"
                    />
                </div>
            )}
            {children}
            {add && (
                <div className="flex shrink-0 items-center gap-1 sm:ml-auto">
                    <Button
                        variant={add.active ? 'secondary' : 'outline'}
                        onClick={add.onClick}
                        aria-expanded={add.active}
                        aria-label={`Add ${add.what}`}
                    >
                        <AddIcon />
                        Add
                    </Button>
                    <InfoHint label={`What is ${add.what}?`}>
                        {add.hint}
                    </InfoHint>
                </div>
            )}
        </StickyFilterRail>
    )
}
