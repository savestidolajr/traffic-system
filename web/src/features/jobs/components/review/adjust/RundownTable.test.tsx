// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RundownTable } from './RundownTable'
import type { TrafficCorridor, JobIncident, FlowRoad } from '../../../types'
import type {
    IncidentRow,
    CorridorCuration,
} from '../../../hooks/use-bulletin-review'
import type { ProvenanceResult } from '../../../lib/verification/provenance'
import { googleMapsTrafficUrl } from '../../../lib/map/map-links'
import { TooltipProvider } from '@/components/ui/tooltip'

afterEach(() => {
    cleanup()
})

// ---------------------------------------------------------------------------
// Mock @dnd-kit — we only care about rendering, not drag-and-drop.
// ---------------------------------------------------------------------------
vi.mock('@dnd-kit/core', () => ({
    DndContext: ({ children }: { children: React.ReactNode }) => (
        <>{children}</>
    ),
    PointerSensor: class {},
    useSensor: () => ({}),
    useSensors: () => [],
    pointerWithin: () => null,
    useDraggable: () => ({
        attributes: { role: 'button' },
        listeners: {},
        setNodeRef: () => {},
        transform: null,
        isDragging: false,
    }),
    useDroppable: () => ({
        setNodeRef: () => {},
        isOver: false,
    }),
}))

vi.mock('@dnd-kit/utilities', () => ({
    CSS: {
        Translate: {
            toString: () => '',
        },
    },
}))

// ---------------------------------------------------------------------------
// Minimal fixture helpers
// ---------------------------------------------------------------------------
const makeIncident = (
    key: string,
    over: Partial<JobIncident> = {}
): JobIncident => ({
    key,
    priority: 1,
    headline: 'Traffic incident',
    detail: 'Traffic incident detail',
    ...over,
})

const makeCorridor = (
    over: Partial<TrafficCorridor> = {}
): TrafficCorridor => ({
    roadName: 'M1',
    severity: 2,
    impactScore: 5,
    primaryType: 'CRASH',
    description: 'Multi-vehicle crash',
    incidentCount: 2,
    totalLengthKm: 1.2,
    isSelected: true,
    functionalClass: 1,
    corroborationCount: 1,
    ...over,
})
const makeRow = (
    incident: JobIncident,
    included: boolean,
    roadName = 'M1',
    rank = 1
): IncidentRow => ({
    incident,
    rank: included ? 1 : null,
    included,
    isFirst: false,
    isLast: false,
    corridor: { roadName, rank, isSelected: true },
})

const noop = () => {}

// ---------------------------------------------------------------------------
// Minimal props — only what RundownTable needs to render without crashing.
// ---------------------------------------------------------------------------
const baseProps = {
    flowRoads: [],
    canEdit: false,
    isFlowSelected: () => false,
    toggleFlowRoad: noop,
    onToggleInclude: noop,
    provenance: null,
}

// ---------------------------------------------------------------------------
// Shared fixtures
// ---------------------------------------------------------------------------
const incidentWithCoords = makeIncident('inc-with-coords', {
    headline: 'Crash on M1 near Parramatta',
    latitude: -33.8151,
    longitude: 151.0014,
})

const incidentWithoutCoords = makeIncident('inc-no-coords', {
    headline: 'Stall on M1 near Granville',
})

const corridor = makeCorridor({ incidentCount: 2 })

const includedRow = makeRow(incidentWithCoords, true)
const availableRow = makeRow(incidentWithoutCoords, false)

const defaultProps = {
    ...baseProps,
    corridors: [corridor],
    includedRows: [includedRow],
    availableRows: [availableRow],
}

/** Expand the M1 corridor row so IncidentDetailRow instances render. */
async function expandM1(user: ReturnType<typeof userEvent.setup>) {
    const headings = screen.getAllByText('M1')
    await user.click(headings[0])
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe('RundownTable — Google Maps incident link', () => {
    it('renders a Google Maps link for an incident that has coordinates', async () => {
        const user = userEvent.setup()
        render(<RundownTable {...defaultProps} />)

        await expandM1(user)

        const link = screen.getByRole('link', {
            name: 'Open traffic view in Google Maps',
        })
        expect(link).toHaveAttribute(
            'href',
            googleMapsTrafficUrl(-33.8151, 151.0014)
        )
        expect(link).toHaveAttribute('target', '_blank')
        expect(link).toHaveAttribute('rel', 'noopener noreferrer')
        expect(link).toHaveAttribute(
            'title',
            'Open traffic view in Google Maps'
        )
    })

    it('omits the Google Maps link for an incident without coordinates', async () => {
        const user = userEvent.setup()
        render(<RundownTable {...defaultProps} />)

        await expandM1(user)

        // Exactly one Google Maps link — only the incident with coords gets one.
        const links = screen.getAllByRole('link', {
            name: 'Open traffic view in Google Maps',
        })
        expect(links).toHaveLength(1)
    })
})
describe('RundownTable — provenance badges', () => {
    it('marks incident stories and flow roads voiced from either lineage index', () => {
        const churchIncidents = ['church-a', 'church-b', 'church-c'].map(
            (key) =>
                makeIncident(key, {
                    headline: 'Roadworks on Church Street',
                })
        )
        const warringahIncident = makeIncident('warringah-a', {
            headline: 'Crash on Warringah Road',
        })
        const church = makeCorridor({
            roadName: 'Church Street',
            incidentCount: 3,
            incidentKeys: churchIncidents.map((incident) => incident.key),
        })
        const warringah = makeCorridor({
            roadName: 'Warringah Road',
            incidentCount: 1,
            incidentKeys: [warringahIncident.key],
        })
        const flowRoad = (roadName: string): FlowRoad => ({
            roadName,
            jamFactor: 1,
            lengthKm: 8,
            isWeighted: false,
            isSelected: true,
            score: 2,
            priorityBoosted: false,
            functionalClass: 1,
        })
        const rows = [
            ...churchIncidents.map((incident) =>
                makeRow(incident, true, 'Church Street')
            ),
            makeRow(warringahIncident, true, 'Warringah Road', 2),
        ]
        const provenance: ProvenanceResult = {
            sentences: [],
            bySentence: [],
            byIncidentKey: {
                'church-a': { sentenceIndexes: [0], matchedBy: 'model' },
            },
            byCorridorRoad: {
                'warringah road': {
                    sentenceIndexes: [1],
                    matchedBy: 'model',
                },
                m5: { sentenceIndexes: [2], matchedBy: 'model' },
                m7: { sentenceIndexes: [3], matchedBy: 'model' },
            },
        }

        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[church, warringah]}
                    includedRows={rows}
                    availableRows={[]}
                    flowRoads={[flowRoad('M5'), flowRoad('M7')]}
                    isFlowSelected={() => true}
                    provenance={provenance}
                />
            </TooltipProvider>
        )

        expect(screen.getAllByText('VOICED')).toHaveLength(4)
        expect(screen.getByText('Church Street')).toBeInTheDocument()
        expect(screen.getByText('Warringah Road')).toBeInTheDocument()
        expect(screen.getByText('M5')).toBeInTheDocument()
        expect(screen.getByText('M7')).toBeInTheDocument()
    })
})

describe('RundownTable — road-closed tag', () => {
    it('renders a "closed" tag when roadClosed is true', async () => {
        const user = userEvent.setup()
        const closedIncident = makeIncident('inc-closed', {
            headline: 'Crash on M1 near Auburn',
            roadClosed: true,
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(closedIncident, true)],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        await expandM1(user)
        expect(screen.getByText('closed')).toBeInTheDocument()
    })

    it('does not render a "closed" tag when roadClosed is absent', async () => {
        const user = userEvent.setup()
        const openIncident = makeIncident('inc-open', {
            headline: 'Crash on M1 near Auburn',
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(openIncident, true)],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        await expandM1(user)
        expect(screen.queryByText('closed')).not.toBeInTheDocument()
    })
})

describe('RundownTable — fact line', () => {
    it('composes detail, direction, lane state, and window inline on one line', async () => {
        const user = userEvent.setup()
        const incident = makeIncident('inc-ctx', {
            headline: 'Incident on M1',
            direction: 'Northbound',
            laneDetail: 'Right lane blocked',
            endTimeUtc: '2099-08-22T06:31:00Z',
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(incident, true)],
            availableRows: [],
        }
        render(
            <TooltipProvider>
                <RundownTable {...props} />
            </TooltipProvider>
        )
        await expandM1(user)

        // Everything the record carries rides the single muted line — nothing
        // is hidden behind a hover.
        expect(
            screen.getByText(/detail · Northbound · Right lane blocked · until/)
        ).toBeInTheDocument()
    })

    it('tags a row with its contributing feeds when a road authority contributed', async () => {
        const user = userEvent.setup()
        const incident = makeIncident('inc-src', {
            headline: 'Incident on M1',
            sources: ['here', 'livetraffic-nsw'],
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(incident, true)],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        await expandM1(user)
        expect(screen.getByText('HERE + Live Traffic NSW')).toBeInTheDocument()
    })

    it('does not render a fact line or source tag when the record carries none', async () => {
        const user = userEvent.setup()
        const incident = makeIncident('inc-no-ctx', {
            headline: 'Incident on M1',
            detail: '',
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(incident, true)],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        await expandM1(user)
        const mutedSpans = document.querySelectorAll(
            '.text-muted-foreground.line-clamp-1'
        )
        expect(mutedSpans).toHaveLength(0)
    })
})

describe('RundownTable — inclusion toggle (keyboard path)', () => {
    it('toggles a corridor road out of the bulletin via the drag handle button', async () => {
        const user = userEvent.setup()
        const toggle = vi.fn()
        const curation: CorridorCuration = {
            order: null,
            effective: ['M1'],
            isOn: () => true,
            toggle,
            moveUp: noop,
            moveDown: noop,
            reset: noop,
        }
        const props = {
            ...defaultProps,
            canEdit: true,
            curation,
        }
        render(
            <TooltipProvider>
                <RundownTable {...props} />
            </TooltipProvider>
        )
        await user.click(
            screen.getByRole('button', {
                name: 'Remove M1 from the bulletin',
            })
        )
        expect(toggle).toHaveBeenCalledWith('M1')
    })

    it('toggles a flowing road out of the bulletin via the drag handle button', async () => {
        const user = userEvent.setup()
        const toggleFlowRoad = vi.fn()
        const flow: FlowRoad = {
            roadName: 'Princes Freeway',
            jamFactor: 2,
            lengthKm: 8,
            isWeighted: true,
            isSelected: true,
            score: 4,
            priorityBoosted: false,
            functionalClass: 1,
        }
        const props = {
            ...baseProps,
            corridors: [],
            includedRows: [],
            availableRows: [],
            canEdit: true,
            flowRoads: [flow],
            isFlowSelected: () => true,
            toggleFlowRoad,
        }
        render(
            <TooltipProvider>
                <RundownTable {...props} />
            </TooltipProvider>
        )
        await user.click(
            screen.getByRole('button', {
                name: 'Remove Princes Freeway from the bulletin',
            })
        )
        expect(toggleFlowRoad).toHaveBeenCalledWith('Princes Freeway')
    })
})

describe('RundownTable — entry-time tooltip', () => {
    it('renders the tooltip trigger and reveals "Reported" text on hover when entryTimeUtc is set', async () => {
        const user = userEvent.setup()
        const incident = makeIncident('inc-time', {
            headline: 'Incident on M1',
            entryTimeUtc: '2026-06-29T01:00:00Z',
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(incident, true)],
            availableRows: [],
        }
        render(
            <TooltipProvider>
                <RundownTable {...props} />
            </TooltipProvider>
        )
        await expandM1(user)

        // The clock trigger span is the only inline-flex muted-foreground span
        // inside the headline row that isn't a link.
        const clockTrigger = document.querySelector(
            'span.translate-y-px:not(a)'
        ) as HTMLElement
        expect(clockTrigger).toBeInTheDocument()

        await user.hover(clockTrigger)
        const tooltip = await screen.findByRole('tooltip')
        expect(tooltip.textContent).toMatch(/Reported/)
        expect(tooltip).toBeInTheDocument()
    })

    it('does not render the tooltip trigger when entryTimeUtc is absent', async () => {
        const user = userEvent.setup()
        const incident = makeIncident('inc-no-time', {
            headline: 'Incident on M1',
        })
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [makeRow(incident, true)],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        await expandM1(user)

        // No clock trigger span present.
        const clockTrigger = document.querySelector(
            'span.translate-y-px:not(a)'
        )
        expect(clockTrigger).not.toBeInTheDocument()
    })
})

describe('RundownTable — priority-road marker accessibility', () => {
    it('announces a priority-boosted corridor via boosted score in the table', () => {
        const priorityCorridor = makeCorridor({
            roadName: 'M2',
            priorityBoosted: true,
            incidentCount: 0,
            impactScore: 120,
        })
        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[priorityCorridor]}
                    includedRows={[]}
                    availableRows={[]}
                />
            </TooltipProvider>
        )
        expect(screen.getByText(/120/)).toBeInTheDocument()
        expect(screen.getByText(/120/)).toHaveClass('text-warning')
    })

    it('announces a priority-boosted flowing road via boosted score in the table', () => {
        const flow: FlowRoad = {
            roadName: 'Princes Freeway',
            jamFactor: 2,
            lengthKm: 8,
            isWeighted: true,
            isSelected: true,
            score: 45,
            priorityBoosted: true,
            functionalClass: 1,
        }
        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[]}
                    includedRows={[]}
                    availableRows={[]}
                    flowRoads={[flow]}
                    isFlowSelected={() => true}
                />
            </TooltipProvider>
        )
        expect(screen.getByText(/45/)).toBeInTheDocument()
        expect(screen.getByText(/45/)).toHaveClass('text-warning')
    })

    // The reported defect (dev job e4dae3b1): every boosted flow row showed a bare
    // "(+)" with no amount, because AudioFinalizer never persisted BoostAmount. The
    // bare marker is correct ONLY when the amount is genuinely unknown.
    it('shows the actual boost amount when the backend supplied one', () => {
        const flow: FlowRoad = {
            roadName: 'M5 South Western Motorway',
            jamFactor: 2,
            lengthKm: 8,
            isWeighted: true,
            isSelected: true,
            score: 112.5,
            baseScore: 3,
            boostAmount: 109.5,
            priorityBoosted: true,
            functionalClass: 1,
        }
        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[]}
                    includedRows={[]}
                    availableRows={[]}
                    flowRoads={[flow]}
                    isFlowSelected={() => true}
                />
            </TooltipProvider>
        )
        expect(screen.getByText('(+110)')).toBeInTheDocument()
        expect(screen.queryByText('(+)')).not.toBeInTheDocument()
    })

    it('never reports a known sub-unit boost as an unknown amount', () => {
        // Math.round(0.4) is 0, which is falsy — testing truthiness rendered a real
        // boost as the bare "(+)", the same symptom as a missing amount.
        const flow: FlowRoad = {
            roadName: 'Victoria Road',
            jamFactor: 2,
            lengthKm: 4,
            isWeighted: true,
            isSelected: true,
            score: 40.4,
            baseScore: 40,
            boostAmount: 0.4,
            priorityBoosted: true,
            functionalClass: 2,
        }
        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[]}
                    includedRows={[]}
                    availableRows={[]}
                    flowRoads={[flow]}
                    isFlowSelected={() => true}
                />
            </TooltipProvider>
        )
        expect(screen.queryByText('(+)')).not.toBeInTheDocument()
        expect(screen.getByText('(+1)')).toBeInTheDocument()
    })

    it('falls back to a bare marker only when the amount is genuinely unknown', () => {
        // Legacy jobs persisted before the AudioFinalizer fix carry no BoostAmount.
        const flow: FlowRoad = {
            roadName: 'M1 Eastern Distributor',
            jamFactor: 2,
            lengthKm: 6,
            isWeighted: true,
            isSelected: true,
            score: 112.5,
            priorityBoosted: true,
            functionalClass: 1,
        }
        render(
            <TooltipProvider>
                <RundownTable
                    {...baseProps}
                    corridors={[]}
                    includedRows={[]}
                    availableRows={[]}
                    flowRoads={[flow]}
                    isFlowSelected={() => true}
                />
            </TooltipProvider>
        )
        expect(screen.getByText('(+)')).toBeInTheDocument()
    })
})

describe('RundownTable — corridor row keyboard expansion', () => {
    // Regression: the corridor row expanded on click only (a plain div
    // onClick with no tabIndex/onKeyDown), so a keyboard-only operator could
    // never reveal the per-incident mention/drop checkboxes nested inside it.
    it('expands a corridor row with Enter', async () => {
        const user = userEvent.setup()
        render(<RundownTable {...defaultProps} />)

        // The per-incident checkbox is inside the collapsed row.
        expect(
            screen.queryByRole('checkbox', { name: 'Drop this incident' })
        ).not.toBeInTheDocument()

        // With canEdit false the row is the only tabbable element; tab to it
        // and press Enter — the same toggle a mouse click performs.
        await user.tab()
        await user.keyboard('{Enter}')

        expect(
            screen.getByRole('checkbox', { name: 'Drop this incident' })
        ).toBeInTheDocument()
    })

    it('expands a corridor row with Space', async () => {
        const user = userEvent.setup()
        render(<RundownTable {...defaultProps} />)

        await user.tab()
        await user.keyboard(' ')

        expect(
            screen.getByRole('checkbox', { name: 'Drop this incident' })
        ).toBeInTheDocument()
    })
})

describe('RundownTable — cleared section', () => {
    it('renders cleared events under their own divider, struck on the road name only', () => {
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [
                makeRow(
                    makeIncident('inc-live', { headline: 'Incident on M1' }),
                    true
                ),
            ],
            availableRows: [],
            clearedEvents: [
                {
                    key: 'gone-1',
                    roadName: 'Mitchell Road',
                    headline: 'construction on Mitchell Rd',
                },
            ],
        }
        render(<RundownTable {...props} />)

        expect(
            screen.getByText('cleared since last bulletin')
        ).toBeInTheDocument()
        const row = screen.getByTestId('cleared-row')
        expect(row).toHaveTextContent('Mitchell Rd')
        expect(row).toHaveTextContent('construction on Mitchell Rd')
        // The strike marks the road name, not the whole line.
        expect(row.querySelector('.line-through')?.textContent).toBe(
            'Mitchell Road'
        )
    })

    it('renders no cleared section when nothing cleared', () => {
        const props = {
            ...defaultProps,
            corridors: [makeCorridor({ incidentCount: 1 })],
            includedRows: [
                makeRow(
                    makeIncident('inc-live', { headline: 'Incident on M1' }),
                    true
                ),
            ],
            availableRows: [],
        }
        render(<RundownTable {...props} />)
        expect(
            screen.queryByText('cleared since last bulletin')
        ).not.toBeInTheDocument()
    })
})
