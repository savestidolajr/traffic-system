// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'

afterEach(() => {
    cleanup()
})
import { EvidenceDialog } from './EvidenceDialog'
import type { JobIncident, OfficialEvidence } from '../../../types'

describe('EvidenceDialog', () => {
    const mockIncident: JobIncident = {
        key: 'here-1',
        priority: 1,
        headline: 'Crash on M4',
        detail: 'At Church Street',
        direction: 'westbound',
        roadClosed: true,
        entryTimeUtc: '2026-08-19T07:54:00Z',
        startTimeUtc: '2026-08-19T07:50:00Z',
        endTimeUtc: '2026-08-19T09:00:00Z',
        shape: [
            { lat: -33.8, lng: 151.0 },
            { lat: -33.81, lng: 151.01 },
        ],
    }

    const mockQldEvidence: OfficialEvidence = {
        sourceId: 'qldtraffic',
        headline: 'Roadworks: Between Arnold Street and Airport Avenue',
        isIncidentClass: false,
        direction: 'northbound',
        roadName: 'Captain Cook Highway',
        crossStreet: 'Arnold Street',
        suburb: 'Aeroglen',
        description: 'Traffic control on scene',
        startTime: '2026-07-13T06:00:00+10:00',
        endTime: '2026-08-31T05:00:00+10:00',
        publishedTime: '2026-07-13T06:00:00+10:00',
        lastUpdated: '2026-08-19T17:54:00+10:00',
        authorityPriority: 'Medium',
        category: 'Roadworks',
        subCategory: 'Planned roadworks',
        impactType: 'Lanes affected',
        impactSubtype: 'Lane or lanes reduced',
        qualitativeDelay: 'Delays expected',
        matchBasis: 'road-name',
        sourceRecordId: '812066',
        shapePointCount: 57,
    }

    it('renders unified canonical truth summary at the top and individual source payloads beneath', () => {
        render(
            <EvidenceDialog
                corridor={{
                    roadName: 'Captain Cook Highway',
                    sources: ['here', 'qldtraffic'],
                    officialEvidences: [mockQldEvidence],
                }}
                incidents={[mockIncident]}
                open={true}
                onOpenChange={() => {}}
            />
        )

        const dialog = screen.getByTestId('evidence-dialog')
        expect(dialog).toBeInTheDocument()
        expect(
            screen.getByText('Evidence: Captain Cook Highway')
        ).toBeInTheDocument()
        expect(screen.getByText('Matched by road name')).toBeInTheDocument()

        // Check Unified Pipeline Truth section
        expect(dialog).toHaveTextContent(
            'Unified Corridor Truth (Pipeline Consensus)'
        )
        expect(dialog).toHaveTextContent('Sources: HERE + QLDTraffic')
        expect(dialog).toHaveTextContent('At Church Street')
        expect(dialog).toHaveTextContent('westbound')

        // Check Individual Provider Payloads
        expect(dialog).toHaveTextContent('Individual Source Payloads')
        expect(dialog).toHaveTextContent('HERE (Telematics & Incidents)')
        expect(dialog).toHaveTextContent('QLDTraffic')
        expect(dialog).toHaveTextContent('Medium')
        expect(dialog).toHaveTextContent('Lanes affected')
    })

    it('summarizes geometry point counts cleanly in parsed records instead of raw coordinate dump', () => {
        render(
            <EvidenceDialog
                corridor={{
                    roadName: 'Captain Cook Highway',
                    sources: ['here', 'qldtraffic'],
                    officialEvidences: [mockQldEvidence],
                }}
                incidents={[mockIncident]}
                open={true}
                onOpenChange={() => {}}
            />
        )

        const dialog = screen.getByTestId('evidence-dialog')
        expect(dialog).toHaveTextContent('2 points geometry')
        expect(dialog).not.toHaveTextContent('{"lat":-33.8,"lng":151}')
    })
})
