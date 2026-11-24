// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReviewContextLens } from './ReviewContextLens'
import type { Job, JobIncident, TrafficCorridor } from '../../types'
import type { ProvenanceResult } from '../../lib/verification/provenance'
import { makeCorridor, makeIncident } from '@/test/factories'
import { TooltipProvider } from '@/components/ui/tooltip'

afterEach(() => {
    cleanup()
})

// ---------------------------------------------------------------------------
// Minimal job — ReviewContextLens only reads `job.status` (the FALLBACK_USED
// banner check) and `job.traffic.{incidents,corridors}`; every other JobBase
// field is irrelevant to this component's rendering.
// ---------------------------------------------------------------------------
function makeJob(
    incidents: readonly JobIncident[],
    corridors: readonly TrafficCorridor[] = []
): Job {
    return {
        status: 'WAITING_APPROVAL',
        traffic: { incidents, corridors },
    } as unknown as Job
}

// Two distinct roads, each cited by the script — render as plain rows.
const citedCrash = makeIncident(
    'inc-cited-crash',
    'accident on Pacific Highway',
    { priority: 2, detail: 'At Smith Street - Multi vehicle crash' }
)
const citedRoadworks = makeIncident(
    'inc-cited-roadworks',
    'construction on Bruce Highway',
    { priority: 3, detail: 'At Exit 5 - Road construction' }
)

// Same road, two HERE segments — selected but never voiced (unused), and
// must collapse under one "Smith Street" road group rather than list twice.
const unusedA = makeIncident('inc-unused-a', 'construction on Smith Street', {
    priority: 3,
    detail: 'At King Street - Road construction',
})
const unusedB = makeIncident('inc-unused-b', 'construction on Smith Street', {
    priority: 3,
    detail: 'At Queen Street - Road construction',
})

// Fetched from HERE but never selected for the bulletin at all.
const belowCutIncident = makeIncident(
    'inc-below-cut',
    'roadHazard on Anzac Parade',
    { priority: 4, detail: 'Debris on roadway' }
)

// Flow/congestion corridors: no discrete incident behind them, so the corridor
// row IS the evidence. citedFlow is voiced, unusedFlow is selected but never
// voiced, belowCutFlow was fetched and never selected.
const citedFlow = makeCorridor('Stanmore Road')
const unusedFlow = makeCorridor('Wigram Road')
const belowCutFlow = makeCorridor('Goulding Road', { isSelected: false })
// An incident-backed corridor must NOT also render as a corridor row — its
// incidents already stand for it, and two rows would double-count one fact.
const incidentBackedCorridor = makeCorridor('Pacific Highway', {
    primaryType: 'accident',
    incidentKeys: ['inc-cited-crash'],
})

const job = makeJob(
    [citedCrash, citedRoadworks, unusedA, unusedB, belowCutIncident],
    [incidentBackedCorridor, citedFlow, unusedFlow, belowCutFlow]
)
const includedKeys = [
    citedCrash.key,
    citedRoadworks.key,
    unusedA.key,
    unusedB.key,
]

// Model-attributed lineage: citedCrash cited once, citedRoadworks cited
// twice (drives the S1 / S1 +1 badge assertions). unusedA/unusedB carry no
// lineage entry, which is what makes them "unused selected".
const provenance: ProvenanceResult = {
    sentences: [
        {
            index: 0,
            text: 'Crash on Pacific Highway.',
            startWord: 0,
            endWord: 4,
        },
        {
            index: 1,
            text: 'Roadworks on Bruce Highway.',
            startWord: 4,
            endWord: 8,
        },
        {
            index: 2,
            text: "Stanmore Road's moving slowly.",
            startWord: 8,
            endWord: 12,
        },
    ],
    bySentence: [
        { incidentKeys: [citedCrash.key, citedRoadworks.key] },
        { incidentKeys: [citedRoadworks.key] },
        { incidentKeys: [] },
    ],
    byIncidentKey: {
        [citedCrash.key]: { sentenceIndexes: [0], matchedBy: 'model' },
        [citedRoadworks.key]: { sentenceIndexes: [0, 1], matchedBy: 'model' },
    },
    byCorridorRoad: {
        'pacific highway': { sentenceIndexes: [0], matchedBy: 'model' },
        'stanmore road': { sentenceIndexes: [2], matchedBy: 'model' },
        'wigram road': { sentenceIndexes: [], matchedBy: 'model' },
        'goulding road': { sentenceIndexes: [], matchedBy: 'model' },
    },
}
function renderLens() {
    return render(
        <TooltipProvider>
            <ReviewContextLens
                job={job}
                focusKey={null}
                provenance={provenance}
                includedKeys={includedKeys}
            />
        </TooltipProvider>
    )
}

describe('ReviewContextLens — evidence rail', () => {
    it('counts flow corridors alongside grouped stories in the tab triggers', () => {
        renderLens()
        // Cited = 2 cited incidents + Stanmore Road (flow).
        expect(
            screen.getByRole('button', { name: /^Cited\s*3$/ })
        ).toBeInTheDocument()
        // Unused = 1 Smith Street story (2 raw segments) + Wigram Road.
        expect(
            screen.getByRole('button', { name: /^Unused\s*2$/ })
        ).toBeInTheDocument()
        // Below cut = 1 unselected incident + Goulding Road.
        expect(
            screen.getByRole('button', { name: /^Below cut\s*2$/ })
        ).toBeInTheDocument()
    })

    it('explains grouped row counts separately from source-record counts', () => {
        renderLens()

        expect(screen.getByTestId('evidence-summary')).toHaveTextContent(
            '3 voiced road/story rows · 3 source records'
        )
    })

    it('distinguishes one voiced story from its raw source records', () => {
        const churchIncidents = ['church-a', 'church-b', 'church-c'].map(
            (key) => makeIncident(key, 'roadworks on Church Street')
        )
        const churchJob = makeJob(churchIncidents, [
            makeCorridor('Church Street', {
                incidentKeys: churchIncidents.map((incident) => incident.key),
            }),
        ])
        const churchProvenance: ProvenanceResult = {
            sentences: [],
            bySentence: [],
            byIncidentKey: Object.fromEntries(
                churchIncidents.map((incident) => [
                    incident.key,
                    { sentenceIndexes: [0], matchedBy: 'model' as const },
                ])
            ),
            byCorridorRoad: {},
        }

        render(
            <TooltipProvider>
                <ReviewContextLens
                    job={churchJob}
                    focusKey={null}
                    provenance={churchProvenance}
                    includedKeys={churchIncidents.map(
                        (incident) => incident.key
                    )}
                />
            </TooltipProvider>
        )

        expect(screen.getByTestId('evidence-summary')).toHaveTextContent(
            '1 voiced road/story rows · 3 source records'
        )
    })

    it('lists a cited flow corridor as its own evidence row', () => {
        renderLens()
        expect(
            screen.getByText('Congestion on Stanmore Road')
        ).toBeInTheDocument()
        // Cited in sentence 3 — the badge proves the corridor carries lineage,
        // not just presence in the traffic snapshot.
        expect(screen.getByText('S3')).toBeInTheDocument()
    })

    it('never renders an incident-backed corridor as a second corridor row', () => {
        renderLens()
        // Pacific Highway is represented by its incident row, once.
        expect(
            screen.getByText('accident on Pacific Highway')
        ).toBeInTheDocument()
        expect(
            screen.queryByText('Accident on Pacific Highway')
        ).not.toBeInTheDocument()
    })

    it('buckets flow corridors by selection and citation', async () => {
        const user = userEvent.setup()
        renderLens()

        expect(
            screen.queryByText('Congestion on Wigram Road')
        ).not.toBeInTheDocument()
        await user.click(screen.getByRole('button', { name: /Unused/ }))
        expect(
            screen.getByText('Congestion on Wigram Road')
        ).toBeInTheDocument()
        expect(
            screen.queryByText('Congestion on Goulding Road')
        ).not.toBeInTheDocument()

        await user.click(screen.getByRole('button', { name: /Below cut/ }))
        expect(
            screen.getByText('Congestion on Goulding Road')
        ).toBeInTheDocument()
    })

    it('shows only cited evidence by default, switching tabs reveals the others', async () => {
        const user = userEvent.setup()
        renderLens()

        // Cited tab is active by default.
        expect(
            screen.getByText('accident on Pacific Highway')
        ).toBeInTheDocument()
        expect(
            screen.getByText('construction on Bruce Highway')
        ).toBeInTheDocument()

        // Unused and below-cut content not in the DOM until their tab is clicked.
        expect(screen.queryByText('Smith Street')).not.toBeInTheDocument()
        expect(
            screen.queryByText('roadHazard on Anzac Parade')
        ).not.toBeInTheDocument()

        // Click the Below cut tab — surfaces its content.
        await user.click(screen.getByRole('button', { name: /Below cut/ }))
        expect(
            screen.getByText('roadHazard on Anzac Parade')
        ).toBeInTheDocument()

        // Click the Unused tab — surfaces its content.
        await user.click(screen.getByRole('button', { name: /Unused/ }))
        expect(screen.getByText('Smith Street')).toBeInTheDocument()
    })

    it('groups duplicate unused-selected roadworks under one road group', async () => {
        const user = userEvent.setup()
        renderLens()

        await user.click(screen.getByRole('button', { name: /Unused/ }))

        // One road group summarizing both segments, not two individual rows.
        const roadGroupToggle = screen.getByRole('button', {
            name: /Smith Street/,
        })
        expect(roadGroupToggle).toHaveAttribute('aria-expanded', 'false')
        expect(
            screen.queryByText('Roadworks · At King Street')
        ).not.toBeInTheDocument()
        expect(
            screen.queryByText('Roadworks · At Queen Street')
        ).not.toBeInTheDocument()

        // Expanding the group reveals the rows.
        await user.click(roadGroupToggle)
        expect(
            screen.getByText('Roadworks · At King Street')
        ).toBeInTheDocument()
        expect(
            screen.getByText('Roadworks · At Queen Street')
        ).toBeInTheDocument()
    })

    it('renders sentence badges for cited rows', () => {
        renderLens()
        expect(screen.getByText('S1')).toBeInTheDocument()
        expect(screen.getByText('S1 +1')).toBeInTheDocument()
    })
})
