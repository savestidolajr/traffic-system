// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { SourceChips } from './SourceChips'
import type { JobIncident, TrafficCorridor } from '../../../types'

const corridor = (overrides: Partial<TrafficCorridor>): TrafficCorridor =>
    ({
        roadName: 'M4',
        severity: 2,
        impactScore: 50,
        primaryType: 'accident',
        description: 'crash',
        incidentCount: 1,
        totalLengthKm: 2,
        rank: 1,
        functionalClass: 1,
        corroborationCount: 1,
        ...overrides,
    }) as TrafficCorridor

const hereIncident: JobIncident = {
    key: 'inc-1',
    priority: 1,
    headline: 'accident on M4',
    detail: 'At Church Street - Crash',
    type: 'accident',
    roadClosed: false,
    entryTimeUtc: '2026-08-18T02:00:00Z',
} as JobIncident

afterEach(cleanup)

describe('SourceChips', () => {
    it('renders a quiet chip for HERE-only corridors — the finding is openable, not hidden', () => {
        render(<SourceChips corridor={corridor({ sources: ['here'] })} />)
        expect(screen.getByTestId('source-chips-here-only')).toHaveTextContent(
            'HERE'
        )
    })

    it('names every source on a multi-source corridor', () => {
        render(
            <SourceChips
                corridor={corridor({ sources: ['here', 'livetraffic-nsw'] })}
            />
        )
        expect(
            screen.getByTestId('source-chips-corroborated')
        ).toHaveTextContent('HERE + Live Traffic NSW')
    })

    it('marks an official-only corridor as an alert', () => {
        render(<SourceChips corridor={corridor({ sources: ['qldtraffic'] })} />)
        expect(
            screen.getByTestId('source-chips-official-only')
        ).toHaveTextContent('QLDTraffic')
    })

    it('opens the evidence dialog with the match receipt and both records', () => {
        render(
            <SourceChips
                corridor={corridor({
                    officialEvidences: [
                        {
                            sourceId: 'qldtraffic',
                            headline:
                                'Roadworks: Reservoir Road and Pease street',
                            isIncidentClass: false,
                            matchBasis: 'road-name',
                            suburb: 'Manoora',
                        },
                    ],
                })}
                incidents={[hereIncident]}
            />
        )

        fireEvent.click(screen.getByTestId('source-chips-corroborated'))

        const dialog = screen.getByTestId('evidence-dialog')
        expect(dialog).toHaveTextContent('Evidence: M4')
        expect(dialog).toHaveTextContent('Matched by road name')
        expect(dialog).toHaveTextContent(
            'Roadworks: Reservoir Road and Pease street'
        )
        expect(dialog).toHaveTextContent('QLDTraffic')
        expect(dialog).not.toHaveTextContent('says')
        expect(dialog).toHaveTextContent('At Church Street - Crash')
        expect(dialog).toHaveTextContent(
            'Unified Corridor Truth (Pipeline Consensus)'
        )
    })

    it('states the finding plainly when HERE is the only source', () => {
        render(
            <SourceChips
                corridor={corridor({ sources: ['here'] })}
                incidents={[hereIncident]}
            />
        )

        fireEvent.click(screen.getByTestId('source-chips-here-only'))

        expect(screen.getByTestId('evidence-dialog')).toHaveTextContent(
            'only HERE reported this'
        )
    })

    it('renders the proximity match basis with the metres', () => {
        render(
            <SourceChips
                corridor={corridor({
                    officialEvidences: [
                        {
                            sourceId: 'livetraffic-nsw',
                            headline: 'Crash on M4',
                            isIncidentClass: true,
                            matchBasis: 'distance-212m',
                        },
                    ],
                })}
                incidents={[hereIncident]}
            />
        )

        fireEvent.click(screen.getByTestId('source-chips-corroborated'))

        expect(screen.getByTestId('evidence-dialog')).toHaveTextContent(
            'Matched by proximity: 212m apart'
        )
    })

    it('renders flow congestion metrics in HERE column when corridor has 0 incidents', () => {
        render(
            <SourceChips
                corridor={corridor({
                    roadName: 'Old Windsor Rd',
                    primaryType: 'congestion',
                    description: 'slow-moving',
                    incidentCount: 0,
                    delayMinutes: 6,
                    totalLengthKm: 1.6,
                    sources: ['here'],
                })}
                incidents={[]}
            />
        )

        fireEvent.click(screen.getByTestId('source-chips-here-only'))

        const dialog = screen.getByTestId('evidence-dialog')
        expect(dialog).toHaveTextContent('Evidence: Old Windsor Rd')
        expect(dialog).toHaveTextContent('slow-moving')
        expect(dialog).toHaveTextContent('6 min delay')
        expect(dialog).toHaveTextContent('1.6')
        expect(dialog).not.toHaveTextContent('no record')
    })

    it('renders single officialEvidence when present on corridor', () => {
        render(
            <SourceChips
                corridor={corridor({
                    roadName: 'Mamre Road',
                    sources: ['here', 'livetraffic-nsw'],
                    officialEvidence: {
                        sourceId: 'livetraffic-nsw',
                        headline:
                            'CHANGED TRAFFIC CONDITIONS Mamre Road upgrade',
                        isIncidentClass: true,
                        matchBasis: 'road-name',
                        crossStreet: 'M4 Motorway',
                        suburb: 'St Clair',
                    },
                })}
                incidents={[]}
            />
        )

        fireEvent.click(screen.getByTestId('source-chips-corroborated'))

        const dialog = screen.getByTestId('evidence-dialog')
        expect(dialog).toHaveTextContent(
            'CHANGED TRAFFIC CONDITIONS Mamre Road upgrade'
        )
        expect(dialog).toHaveTextContent('M4 Motorway')
        expect(dialog).toHaveTextContent('St Clair')
    })
})
