import { describe, it, expect } from 'vitest'
import { buildCoverageGroups } from './coverage-grouping'
import type { TrafficCorridor, JobIncident } from '../../types'
import type {
    IncidentRow,
    IncidentCorridorRef,
} from '../../hooks/use-bulletin-review'

const corridor = (over: Partial<TrafficCorridor> = {}): TrafficCorridor => ({
    roadName: 'M1',
    severity: 2,
    impactScore: 5,
    primaryType: 'CRASH',
    description: 'desc',
    incidentCount: 1,
    totalLengthKm: 1,
    rank: 1,
    isSelected: true,
    functionalClass: 1,
    priorityBoosted: false,
    ...over,
})

const incident = (key: string, over: Partial<JobIncident> = {}): JobIncident =>
    ({
        key,
        priority: 3,
        headline: `Incident ${key}`,
        detail: 'detail',
        functionalClass: 1,
        isNew: false,
        ...over,
    }) as JobIncident

const row = (
    key: string,
    included: boolean,
    corridorRef: IncidentCorridorRef | null
): IncidentRow => ({
    incident: incident(key),
    rank: included ? 1 : null,
    included,
    isFirst: false,
    isLast: false,
    corridor: corridorRef,
})

const ref = (
    roadName: string,
    rank = 1,
    isSelected = true
): IncidentCorridorRef => ({ roadName, rank, isSelected })

describe('buildCoverageGroups', () => {
    const westGate = corridor({
        roadName: 'West Gate Fwy',
        rank: 1,
        incidentKeys: ['a'],
    })
    const monash = corridor({
        roadName: 'Monash Fwy',
        rank: 2,
        incidentKeys: ['b', 'c'],
    })
    const princes = corridor({
        roadName: 'Princes Hwy',
        rank: 3,
        isSelected: false,
    })

    it('groups included incidents under their road in machine order', () => {
        const groups = buildCoverageGroups(
            [monash, westGate, princes],
            [
                row('a', true, ref('West Gate Fwy')),
                row('b', true, ref('Monash Fwy', 2)),
            ],
            []
        )
        expect(groups.degraded).toBe(false)
        expect(groups.onAir.map((g) => g.corridor.roadName)).toEqual([
            'West Gate Fwy',
            'Monash Fwy',
        ])
        expect(groups.onAir[0].position).toBe(1)
        expect(groups.onAir[0].rows.map((r) => r.incident.key)).toEqual(['a'])
        expect(groups.belowCut.map((g) => g.corridor.roadName)).toEqual([
            'Princes Hwy',
        ])
        expect(groups.belowCut[0].position).toBeNull()
    })

    it('respects a curated on-air override, including dropped roads', () => {
        const groups = buildCoverageGroups(
            [westGate, monash, princes],
            [
                row('a', true, ref('West Gate Fwy')),
                row('b', true, ref('Monash Fwy', 2)),
            ],
            [],
            ['Monash Fwy', 'Princes Hwy'] // West Gate curated off, order flipped
        )
        expect(groups.onAir.map((g) => g.corridor.roadName)).toEqual([
            'Monash Fwy',
            'Princes Hwy',
        ])
        expect(groups.onAir[1].position).toBe(2)
        // The curated-off road keeps its still-included rows (this fetch's data)
        const off = groups.belowCut.find(
            (g) => g.corridor.roadName === 'West Gate Fwy'
        )
        expect(off?.rows.map((r) => r.incident.key)).toEqual(['a'])
        expect(off?.emptied).toBe(false)
    })
    it('deduplicates repeated same-road entries in a curated override', () => {
        const aeroglen = corridor({
            roadName: 'Captain Cook Highway',
            suburb: 'Aeroglen',
            rank: 1,
            impactScore: 10,
        })
        const palmCove = corridor({
            roadName: 'Captain Cook Highway',
            suburb: 'Palm Cove',
            rank: 2,
            impactScore: 8,
        })

        const groups = buildCoverageGroups(
            [aeroglen, palmCove],
            [],
            [],
            ['Captain Cook Highway', 'Captain Cook Highway']
        )

        expect(groups.onAir).toHaveLength(1)
        expect(groups.onAir[0].corridor.suburb).toBe('Aeroglen')
    })

    it('nests available incidents under their on-air road', () => {
        const groups = buildCoverageGroups(
            [monash],
            [row('b', true, ref('Monash Fwy'))],
            [row('c', false, ref('Monash Fwy'))]
        )
        expect(groups.onAir[0].alsoOnRoad.map((r) => r.incident.key)).toEqual([
            'c',
        ])
        expect(groups.loose).toHaveLength(0)
    })

    it('collects loose incidents no corridor claims, included first', () => {
        const groups = buildCoverageGroups(
            [westGate],
            [row('x', true, null)],
            [row('y', false, ref('Unranked Rd'))] // ref to a road not in the job
        )
        expect(groups.loose.map((r) => r.incident.key)).toEqual(['x', 'y'])
    })

    it('flags an emptied road only when it HAD linked incidents', () => {
        const groups = buildCoverageGroups(
            [westGate, corridor({ roadName: 'Unlinked Rd', rank: 4 })],
            [],
            [row('a', false, ref('West Gate Fwy'))] // operator dropped it
        )
        const emptiedRoad = groups.onAir.find(
            (g) => g.corridor.roadName === 'West Gate Fwy'
        )
        const unlinked = groups.onAir.find(
            (g) => g.corridor.roadName === 'Unlinked Rd'
        )
        expect(emptiedRoad?.emptied).toBe(true)
        expect(unlinked?.emptied).toBe(false) // no incidentKeys = just unlinked
    })

    it('degrades to a flat loose list when the job has no corridors', () => {
        const groups = buildCoverageGroups(
            [],
            [row('a', true, null)],
            [row('b', false, null)]
        )
        expect(groups.degraded).toBe(true)
        expect(groups.onAir).toHaveLength(0)
        expect(groups.loose.map((r) => r.incident.key)).toEqual(['a', 'b'])
    })
})
