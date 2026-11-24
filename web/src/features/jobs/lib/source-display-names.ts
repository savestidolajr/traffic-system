const SOURCE_NAMES: Record<string, string> = {
    here: 'HERE',
    'here-weather': 'HERE Weather',
    'here-fuel': 'HERE Fuel',
    'livetraffic-nsw': 'Live Traffic NSW',
    qldtraffic: 'QLDTraffic',
    victraffic: 'VicTraffic',
    'mainroads-wa': 'Main Roads WA',
    trafficsa: 'Traffic SA',
    tasalert: 'TasALERT',
}

/** Stable operator-facing name for an upstream source id. */
export const sourceDisplayName = (id: string): string =>
    SOURCE_NAMES[id.toLowerCase()] ?? id
