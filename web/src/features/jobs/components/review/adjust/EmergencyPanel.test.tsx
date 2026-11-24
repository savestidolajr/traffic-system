// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { EmergencyEvent, SourceFetchDiagnostic } from '../../../types'
import { EmergencyPanel } from './EmergencyPanel'

const diagnostic = (
    outcome: SourceFetchDiagnostic['outcome'],
    detail: string | null = null
): SourceFetchDiagnostic => ({
    sourceId: 'dataquoll',
    outcome,
    detail,
    dataAsOfUtc: outcome === 'Ok' ? '2026-08-31T03:53:00Z' : null,
    staleSeconds: null,
})

const event = (
    id: string,
    overrides: Partial<EmergencyEvent> = {}
): EmergencyEvent => ({
    id,
    sourceFeedId: 'vic-osom',
    agency: 'EMV',
    state: 'vic',
    title: 'Incident',
    warningLevel: 'None',
    eventType: 'other',
    eventCategory: 'other',
    suburb: 'Richmond',
    distanceKm: 4.25,
    licence: 'CC BY 4.0',
    airEligible: false,
    ...overrides,
})

afterEach(cleanup)

describe('EmergencyPanel', () => {
    // An empty list has four different meanings. Reporting "no incidents in the coverage area"
    // for a feed that was never asked is a factual claim about the world that happens to be a
    // guess, and it reads to an operator as reassurance. Each case must say what it means.
    it('reports a genuinely clear coverage area only when the feed actually answered', () => {
        render(<EmergencyPanel events={[]} diagnostic={diagnostic('Ok')} />)

        expect(
            screen.getByText('No emergency incidents in the coverage area.')
        ).toBeInTheDocument()
        expect(screen.getByText('DataQuoll')).toBeInTheDocument()
    })

    it('says the feed is switched off rather than claiming the area is clear', () => {
        render(
            <EmergencyPanel events={[]} diagnostic={diagnostic('Skipped')} />
        )

        expect(
            screen.getByText(/not switched on for this environment/i)
        ).toBeInTheDocument()
        expect(
            screen.queryByText('No emergency incidents in the coverage area.')
        ).not.toBeInTheDocument()
    })

    it('distinguishes a broken feed from a switched-off one, and names the reason', () => {
        render(
            <EmergencyPanel
                events={[]}
                diagnostic={diagnostic('Failed', 'HTTP 401 (unauthorised)')}
            />
        )

        expect(
            screen.getByText(
                /could not be reached \(HTTP 401 \(unauthorised\)\)/i
            )
        ).toBeInTheDocument()
        expect(
            screen.queryByText('No emergency incidents in the coverage area.')
        ).not.toBeInTheDocument()
    })

    it('admits it does not know when the bulletin carries no diagnostic at all', () => {
        render(<EmergencyPanel events={[]} />)

        expect(
            screen.getByText(
                'No emergency data was captured for this bulletin.'
            )
        ).toBeInTheDocument()
    })

    it('shows suppression categories and read-only rows', () => {
        render(
            <EmergencyPanel
                events={[
                    event('eligible', {
                        eventCategory: 'fire',
                        airEligible: true,
                    }),
                    event('medical', { eventCategory: 'medical' }),
                    event('planned', { eventCategory: 'planned' }),
                ]}
            />
        )

        expect(
            screen.getByText('2 not airable (medical, planned burns)')
        ).toBeInTheDocument()
        expect(screen.getAllByRole('checkbox')).toHaveLength(3)
        expect(
            screen
                .getAllByRole('checkbox')
                .every((box) => box.hasAttribute('disabled'))
        ).toBe(true)
        expect(
            screen.getByText(
                'Observation only. These incidents are not yet wired to the script.'
            )
        ).toBeInTheDocument()
    })

    it('elevates emergency warnings and checks them as forced', () => {
        render(
            <EmergencyPanel
                events={[
                    event('warning', {
                        title: 'Bushfire near Bendigo',
                        warningLevel: 'EmergencyWarning',
                        eventCategory: 'fire',
                        airEligible: true,
                        distanceKm: 18.1,
                    }),
                ]}
            />
        )

        expect(screen.getByText('1 MUST AIR')).toBeInTheDocument()
        expect(screen.getByText('FORCED')).toBeInTheDocument()
        expect(screen.getByText(/Emergency Warning/)).toBeInTheDocument()
        expect(screen.getByRole('checkbox')).toBeChecked()
        expect(screen.getByRole('checkbox')).toBeDisabled()
    })
})
