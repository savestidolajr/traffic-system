export interface IncidentPermutation {
    readonly provider: string
    readonly eventType: string
    readonly details: string
    readonly impact: string
    readonly codes?: string
    readonly count: number
}

export interface CanonicalIncidentItem {
    readonly id: string
    readonly name: string
    readonly defaultBonus: number
    readonly description: string
}

export const CANONICAL_INCIDENT_CLASSES: readonly CanonicalIncidentItem[] = [
    {
        id: 'Crash',
        name: 'Crashes and Collisions',
        defaultBonus: 20,
        description:
            'Single and multi-vehicle collisions, rollovers, and emergency incidents.',
    },
    {
        id: 'Closure',
        name: 'Road Closures',
        defaultBonus: 20,
        description:
            'Full carriageway and arterial shutdowns with detour routes.',
    },
    {
        id: 'Fire',
        name: 'Vehicle and Bushfires',
        defaultBonus: 15,
        description: 'Active vehicle fires and emergency smoke hazard zones.',
    },
    {
        id: 'Flooding',
        name: 'Flooding and Water Over Road',
        defaultBonus: 15,
        description:
            'Submerged causeways, flash flooding, and weather disruptions.',
    },
    {
        id: 'Breakdown',
        name: 'Breakdowns and Stalls',
        defaultBonus: 10,
        description:
            'Disabled vehicles, heavy vehicle tow aways, and lane stalls.',
    },
    {
        id: 'Hazard',
        name: 'Hazards and Road Damage',
        defaultBonus: 5,
        description:
            'Debris, traffic signal faults, bridge/culvert damage, and cautionary notices.',
    },
    {
        id: 'Congestion',
        name: 'Heavy Congestion and Queues',
        defaultBonus: 5,
        description:
            'Queuing traffic, bottleneck delays, and changed traffic conditions.',
    },
    {
        id: 'Roadworks',
        name: 'Roadworks and Construction',
        defaultBonus: 0,
        description:
            'Maintenance, resurfacing, upgrades, utility works, and lane reductions.',
    },
    {
        id: 'SpecialEvent',
        name: 'Special Events and Operations',
        defaultBonus: 0,
        description:
            'Community events, races, and scheduled police operations.',
    },
    {
        id: 'Ferry',
        name: 'Ferry Closures',
        defaultBonus: 0,
        description: 'Vehicular river ferry service maintenance and closures.',
    },
] as const
