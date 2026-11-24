// =============================================================================
// GENERATED FILE — real HERE Traffic API snapshot. Do not edit by hand.
// Regenerate with:  node scripts/fetch-here-traffic.mjs
// Fetched: 2026-06-25T04:10:15.113Z
// =============================================================================

import type { IncidentFixture, CorridorFixture } from './fixtures'

interface LiveTrafficSentence {
    Text: string
    CorridorKeys: string[]
}

export interface LiveTrafficFixture {
    FetchedAtIso: string
    Bbox: number[]
    Incidents: IncidentFixture[]
    Corridors: (CorridorFixture & { IncidentKeys: string[] })[]
    SelectedIncidents: IncidentFixture[]
    /** Broadcast-style traffic sentences with corridor lineage. */
    TrafficSentences: LiveTrafficSentence[]
    /** True when exactly one delay contradiction was planted (demo guard). */
    ContradictionPlanted: boolean
}

export const LIVE_TRAFFIC: Record<
    'sydney' | 'melbourne' | 'brisbane' | 'goldcoast' | 'perth' | 'adelaide',
    LiveTrafficFixture
> = {
    "sydney": {
        "FetchedAtIso": "2026-06-25T04:10:10.218Z",
        "Bbox": [
            150.5,
            -34.1,
            151.5,
            -33.6
        ],
        "Incidents": [
            {
                "Key": "inc-syd-01",
                "Priority": 4,
                "Headline": "South Street — roadworks",
                "Detail": "Between South Street and President Avenue - Construction work",
                "FunctionalClass": 1,
                "LengthMeters": 1475,
                "Latitude": -33.9643,
                "Longitude": 151.13761,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.9643,
                        "Lng": 151.13761
                    },
                    {
                        "Lat": -33.96435,
                        "Lng": 151.13787
                    },
                    {
                        "Lat": -33.96435,
                        "Lng": 151.13787
                    },
                    {
                        "Lat": -33.96438,
                        "Lng": 151.13808
                    },
                    {
                        "Lat": -33.96438,
                        "Lng": 151.13808
                    },
                    {
                        "Lat": -33.96443,
                        "Lng": 151.13838
                    },
                    {
                        "Lat": -33.96443,
                        "Lng": 151.13838
                    },
                    {
                        "Lat": -33.9645,
                        "Lng": 151.13877
                    },
                    {
                        "Lat": -33.9645,
                        "Lng": 151.13877
                    },
                    {
                        "Lat": -33.96455,
                        "Lng": 151.13903
                    },
                    {
                        "Lat": -33.96455,
                        "Lng": 151.13903
                    },
                    {
                        "Lat": -33.96462,
                        "Lng": 151.1395
                    },
                    {
                        "Lat": -33.96462,
                        "Lng": 151.1395
                    },
                    {
                        "Lat": -33.96464,
                        "Lng": 151.13965
                    },
                    {
                        "Lat": -33.96464,
                        "Lng": 151.13965
                    },
                    {
                        "Lat": -33.96468,
                        "Lng": 151.14007
                    },
                    {
                        "Lat": -33.96468,
                        "Lng": 151.14007
                    },
                    {
                        "Lat": -33.96477,
                        "Lng": 151.14085
                    },
                    {
                        "Lat": -33.96477,
                        "Lng": 151.14085
                    },
                    {
                        "Lat": -33.9648,
                        "Lng": 151.14102
                    },
                    {
                        "Lat": -33.9648,
                        "Lng": 151.14102
                    },
                    {
                        "Lat": -33.96487,
                        "Lng": 151.14145
                    },
                    {
                        "Lat": -33.96487,
                        "Lng": 151.14145
                    },
                    {
                        "Lat": -33.9649,
                        "Lng": 151.14166
                    },
                    {
                        "Lat": -33.9649,
                        "Lng": 151.14166
                    },
                    {
                        "Lat": -33.96497,
                        "Lng": 151.1421
                    },
                    {
                        "Lat": -33.965,
                        "Lng": 151.14241
                    },
                    {
                        "Lat": -33.965,
                        "Lng": 151.14241
                    },
                    {
                        "Lat": -33.96502,
                        "Lng": 151.14272
                    },
                    {
                        "Lat": -33.96502,
                        "Lng": 151.14272
                    },
                    {
                        "Lat": -33.9651,
                        "Lng": 151.14348
                    },
                    {
                        "Lat": -33.9651,
                        "Lng": 151.14348
                    },
                    {
                        "Lat": -33.96515,
                        "Lng": 151.14392
                    },
                    {
                        "Lat": -33.96522,
                        "Lng": 151.14433
                    },
                    {
                        "Lat": -33.96522,
                        "Lng": 151.14433
                    },
                    {
                        "Lat": -33.96528,
                        "Lng": 151.14467
                    },
                    {
                        "Lat": -33.96528,
                        "Lng": 151.14467
                    },
                    {
                        "Lat": -33.96535,
                        "Lng": 151.14503
                    },
                    {
                        "Lat": -33.96535,
                        "Lng": 151.14503
                    },
                    {
                        "Lat": -33.96538,
                        "Lng": 151.14523
                    },
                    {
                        "Lat": -33.96538,
                        "Lng": 151.14523
                    },
                    {
                        "Lat": -33.96547,
                        "Lng": 151.14577
                    },
                    {
                        "Lat": -33.96547,
                        "Lng": 151.14577
                    },
                    {
                        "Lat": -33.9655,
                        "Lng": 151.14597
                    },
                    {
                        "Lat": -33.9655,
                        "Lng": 151.14597
                    },
                    {
                        "Lat": -33.96553,
                        "Lng": 151.14616
                    },
                    {
                        "Lat": -33.96553,
                        "Lng": 151.14616
                    },
                    {
                        "Lat": -33.96561,
                        "Lng": 151.14681
                    },
                    {
                        "Lat": -33.96561,
                        "Lng": 151.14681
                    },
                    {
                        "Lat": -33.96571,
                        "Lng": 151.14735
                    },
                    {
                        "Lat": -33.96571,
                        "Lng": 151.14735
                    },
                    {
                        "Lat": -33.96582,
                        "Lng": 151.14794
                    },
                    {
                        "Lat": -33.96582,
                        "Lng": 151.14794
                    },
                    {
                        "Lat": -33.9659,
                        "Lng": 151.14837
                    },
                    {
                        "Lat": -33.9659,
                        "Lng": 151.14837
                    },
                    {
                        "Lat": -33.966,
                        "Lng": 151.14891
                    },
                    {
                        "Lat": -33.966,
                        "Lng": 151.14891
                    },
                    {
                        "Lat": -33.96604,
                        "Lng": 151.14915
                    },
                    {
                        "Lat": -33.96604,
                        "Lng": 151.14915
                    },
                    {
                        "Lat": -33.96607,
                        "Lng": 151.14936
                    },
                    {
                        "Lat": -33.96607,
                        "Lng": 151.14936
                    },
                    {
                        "Lat": -33.96621,
                        "Lng": 151.15032
                    },
                    {
                        "Lat": -33.96626,
                        "Lng": 151.1504
                    },
                    {
                        "Lat": -33.96626,
                        "Lng": 151.1504
                    },
                    {
                        "Lat": -33.96644,
                        "Lng": 151.15166
                    },
                    {
                        "Lat": -33.96644,
                        "Lng": 151.15166
                    },
                    {
                        "Lat": -33.96658,
                        "Lng": 151.15269
                    },
                    {
                        "Lat": -33.96658,
                        "Lng": 151.15269
                    },
                    {
                        "Lat": -33.96661,
                        "Lng": 151.15292
                    },
                    {
                        "Lat": -33.96661,
                        "Lng": 151.15292
                    },
                    {
                        "Lat": -33.96666,
                        "Lng": 151.1533
                    },
                    {
                        "Lat": -33.96666,
                        "Lng": 151.1533
                    },
                    {
                        "Lat": -33.96669,
                        "Lng": 151.15349
                    }
                ]
            },
            {
                "Key": "inc-syd-02",
                "Priority": 4,
                "Headline": "Brook Street — roadworks",
                "Detail": "At Brook Street - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 122,
                "Latitude": -33.82257,
                "Longitude": 151.20616,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.82257,
                        "Lng": 151.20616
                    },
                    {
                        "Lat": -33.82273,
                        "Lng": 151.20638
                    },
                    {
                        "Lat": -33.82298,
                        "Lng": 151.20677
                    },
                    {
                        "Lat": -33.8231,
                        "Lng": 151.20698
                    },
                    {
                        "Lat": -33.82322,
                        "Lng": 151.20723
                    }
                ]
            },
            {
                "Key": "inc-syd-03",
                "Priority": 3,
                "Headline": "Flynn Avenue — roadworks",
                "Detail": "Between Flynn Avenue and Mannow Avenue - Road construction",
                "FunctionalClass": 3,
                "LengthMeters": 963,
                "Latitude": -33.92112,
                "Longitude": 150.85079,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.92112,
                        "Lng": 150.85079
                    },
                    {
                        "Lat": -33.92138,
                        "Lng": 150.85056
                    },
                    {
                        "Lat": -33.92138,
                        "Lng": 150.85056
                    },
                    {
                        "Lat": -33.92173,
                        "Lng": 150.85025
                    },
                    {
                        "Lat": -33.92173,
                        "Lng": 150.85025
                    },
                    {
                        "Lat": -33.92192,
                        "Lng": 150.85008
                    },
                    {
                        "Lat": -33.92234,
                        "Lng": 150.84977
                    },
                    {
                        "Lat": -33.92234,
                        "Lng": 150.84977
                    },
                    {
                        "Lat": -33.92244,
                        "Lng": 150.8497
                    },
                    {
                        "Lat": -33.92244,
                        "Lng": 150.8497
                    },
                    {
                        "Lat": -33.92277,
                        "Lng": 150.84947
                    },
                    {
                        "Lat": -33.92297,
                        "Lng": 150.84936
                    },
                    {
                        "Lat": -33.92297,
                        "Lng": 150.84936
                    },
                    {
                        "Lat": -33.92332,
                        "Lng": 150.84915
                    },
                    {
                        "Lat": -33.92332,
                        "Lng": 150.84915
                    },
                    {
                        "Lat": -33.92353,
                        "Lng": 150.84903
                    },
                    {
                        "Lat": -33.92353,
                        "Lng": 150.84903
                    },
                    {
                        "Lat": -33.92368,
                        "Lng": 150.84895
                    },
                    {
                        "Lat": -33.92368,
                        "Lng": 150.84895
                    },
                    {
                        "Lat": -33.92417,
                        "Lng": 150.8487
                    },
                    {
                        "Lat": -33.92417,
                        "Lng": 150.8487
                    },
                    {
                        "Lat": -33.92447,
                        "Lng": 150.84856
                    },
                    {
                        "Lat": -33.92447,
                        "Lng": 150.84856
                    },
                    {
                        "Lat": -33.92464,
                        "Lng": 150.84849
                    },
                    {
                        "Lat": -33.92464,
                        "Lng": 150.84849
                    },
                    {
                        "Lat": -33.92489,
                        "Lng": 150.84839
                    },
                    {
                        "Lat": -33.92489,
                        "Lng": 150.84839
                    },
                    {
                        "Lat": -33.92503,
                        "Lng": 150.84833
                    },
                    {
                        "Lat": -33.92503,
                        "Lng": 150.84833
                    },
                    {
                        "Lat": -33.92517,
                        "Lng": 150.84827
                    },
                    {
                        "Lat": -33.92517,
                        "Lng": 150.84827
                    },
                    {
                        "Lat": -33.92547,
                        "Lng": 150.84814
                    },
                    {
                        "Lat": -33.92547,
                        "Lng": 150.84814
                    },
                    {
                        "Lat": -33.92638,
                        "Lng": 150.84776
                    },
                    {
                        "Lat": -33.92638,
                        "Lng": 150.84776
                    },
                    {
                        "Lat": -33.92659,
                        "Lng": 150.84769
                    },
                    {
                        "Lat": -33.92659,
                        "Lng": 150.84769
                    },
                    {
                        "Lat": -33.92695,
                        "Lng": 150.84758
                    },
                    {
                        "Lat": -33.92772,
                        "Lng": 150.84741
                    },
                    {
                        "Lat": -33.92772,
                        "Lng": 150.84741
                    },
                    {
                        "Lat": -33.92804,
                        "Lng": 150.84735
                    },
                    {
                        "Lat": -33.92804,
                        "Lng": 150.84735
                    },
                    {
                        "Lat": -33.92913,
                        "Lng": 150.84714
                    }
                ]
            },
            {
                "Key": "inc-syd-04",
                "Priority": 3,
                "Headline": "Huntley Street — roadworks",
                "Detail": "At Huntley Street - Closed due to road construction",
                "FunctionalClass": 3,
                "LengthMeters": 45,
                "Latitude": -33.90738,
                "Longitude": 151.18719,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.90738,
                        "Lng": 151.18719
                    },
                    {
                        "Lat": -33.90705,
                        "Lng": 151.18747
                    }
                ]
            },
            {
                "Key": "inc-syd-05",
                "Priority": 3,
                "Headline": "Johnston Street — roadworks",
                "Detail": "At Johnston Street - Road construction",
                "FunctionalClass": 3,
                "LengthMeters": 27,
                "Latitude": -33.87173,
                "Longitude": 151.17376,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.87173,
                        "Lng": 151.17376
                    },
                    {
                        "Lat": -33.87188,
                        "Lng": 151.17399
                    }
                ]
            },
            {
                "Key": "inc-syd-06",
                "Priority": 3,
                "Headline": "Day Street — planned event",
                "Detail": "At Day Street - Major event",
                "FunctionalClass": 3,
                "LengthMeters": 16,
                "Latitude": -33.8759,
                "Longitude": 151.20346,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.8759,
                        "Lng": 151.20346
                    },
                    {
                        "Lat": -33.87576,
                        "Lng": 151.20348
                    }
                ]
            },
            {
                "Key": "inc-syd-07",
                "Priority": 2,
                "Headline": "Construction work",
                "Detail": "Construction work",
                "FunctionalClass": 4,
                "LengthMeters": 314,
                "Latitude": -33.83121,
                "Longitude": 151.08641,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.83121,
                        "Lng": 151.08641
                    },
                    {
                        "Lat": -33.8312,
                        "Lng": 151.08654
                    },
                    {
                        "Lat": -33.83115,
                        "Lng": 151.08662
                    },
                    {
                        "Lat": -33.83112,
                        "Lng": 151.08664
                    },
                    {
                        "Lat": -33.83103,
                        "Lng": 151.08668
                    },
                    {
                        "Lat": -33.83072,
                        "Lng": 151.08673
                    },
                    {
                        "Lat": -33.83072,
                        "Lng": 151.08673
                    },
                    {
                        "Lat": -33.83055,
                        "Lng": 151.08675
                    },
                    {
                        "Lat": -33.83055,
                        "Lng": 151.08675
                    },
                    {
                        "Lat": -33.8304,
                        "Lng": 151.08678
                    },
                    {
                        "Lat": -33.8304,
                        "Lng": 151.08678
                    },
                    {
                        "Lat": -33.83001,
                        "Lng": 151.08689
                    },
                    {
                        "Lat": -33.82952,
                        "Lng": 151.08699
                    },
                    {
                        "Lat": -33.82952,
                        "Lng": 151.08699
                    },
                    {
                        "Lat": -33.82929,
                        "Lng": 151.08703
                    },
                    {
                        "Lat": -33.82929,
                        "Lng": 151.08703
                    },
                    {
                        "Lat": -33.82873,
                        "Lng": 151.08714
                    },
                    {
                        "Lat": -33.82873,
                        "Lng": 151.08714
                    },
                    {
                        "Lat": -33.82856,
                        "Lng": 151.08718
                    }
                ]
            },
            {
                "Key": "inc-syd-08",
                "Priority": 2,
                "Headline": "to road — roadworks",
                "Detail": "Closed due to road construction",
                "FunctionalClass": 4,
                "LengthMeters": 223,
                "Latitude": -33.62716,
                "Longitude": 150.75004,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.62716,
                        "Lng": 150.75004
                    },
                    {
                        "Lat": -33.62645,
                        "Lng": 150.74777
                    }
                ]
            },
            {
                "Key": "inc-syd-09",
                "Priority": 2,
                "Headline": "At Monorail Station access South",
                "Detail": "At Monorail Station access South - Major event",
                "FunctionalClass": 4,
                "LengthMeters": 116,
                "Latitude": -33.87505,
                "Longitude": 151.19874,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.87505,
                        "Lng": 151.19874
                    },
                    {
                        "Lat": -33.87602,
                        "Lng": 151.1992
                    }
                ]
            },
            {
                "Key": "inc-syd-10",
                "Priority": 2,
                "Headline": "Road construction",
                "Detail": "Road construction",
                "FunctionalClass": 4,
                "LengthMeters": 20,
                "Latitude": -33.86154,
                "Longitude": 151.04216,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.86154,
                        "Lng": 151.04216
                    },
                    {
                        "Lat": -33.86168,
                        "Lng": 151.04229
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "South Street",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 1.5,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 3,
                "DelayMinutes": null,
                "Latitude": -33.9643,
                "Longitude": 151.13761,
                "IncidentKeys": [
                    "inc-syd-01"
                ]
            },
            {
                "RoadName": "Brook Street",
                "Severity": 1,
                "ImpactScore": 200.9,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -33.82257,
                "Longitude": 151.20616,
                "IncidentKeys": [
                    "inc-syd-02"
                ]
            },
            {
                "RoadName": "Flynn Avenue",
                "Severity": 2,
                "ImpactScore": 145,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 1,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 3,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 2,
                "DelayMinutes": null,
                "Latitude": -33.92112,
                "Longitude": 150.85079,
                "IncidentKeys": [
                    "inc-syd-03"
                ]
            },
            {
                "RoadName": "Huntley Street",
                "Severity": 2,
                "ImpactScore": 136.5,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 3,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -33.90738,
                "Longitude": 151.18719,
                "IncidentKeys": [
                    "inc-syd-04"
                ]
            },
            {
                "RoadName": "Johnston Street",
                "Severity": 2,
                "ImpactScore": 134.3,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0,
                "Rank": 5,
                "IsSelected": false,
                "FunctionalClass": 3,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -33.87173,
                "Longitude": 151.17376,
                "IncidentKeys": [
                    "inc-syd-05"
                ]
            },
            {
                "RoadName": "Day Street",
                "Severity": 2,
                "ImpactScore": 132,
                "PrimaryType": "plannedEvent",
                "Description": "planned event",
                "IncidentCount": 1,
                "TotalLengthKm": 0,
                "Rank": 6,
                "IsSelected": false,
                "FunctionalClass": 3,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -33.8759,
                "Longitude": 151.20346,
                "IncidentKeys": [
                    "inc-syd-06"
                ]
            },
            {
                "RoadName": "to road",
                "Severity": 3,
                "ImpactScore": 103.5,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.2,
                "Rank": 7,
                "IsSelected": false,
                "FunctionalClass": 4,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -33.62716,
                "Longitude": 150.75004,
                "IncidentKeys": [
                    "inc-syd-08"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-syd-01",
                "Priority": 4,
                "Headline": "South Street — roadworks",
                "Detail": "Between South Street and President Avenue - Construction work",
                "FunctionalClass": 1,
                "LengthMeters": 1475,
                "Latitude": -33.9643,
                "Longitude": 151.13761,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.9643,
                        "Lng": 151.13761
                    },
                    {
                        "Lat": -33.96435,
                        "Lng": 151.13787
                    },
                    {
                        "Lat": -33.96435,
                        "Lng": 151.13787
                    },
                    {
                        "Lat": -33.96438,
                        "Lng": 151.13808
                    },
                    {
                        "Lat": -33.96438,
                        "Lng": 151.13808
                    },
                    {
                        "Lat": -33.96443,
                        "Lng": 151.13838
                    },
                    {
                        "Lat": -33.96443,
                        "Lng": 151.13838
                    },
                    {
                        "Lat": -33.9645,
                        "Lng": 151.13877
                    },
                    {
                        "Lat": -33.9645,
                        "Lng": 151.13877
                    },
                    {
                        "Lat": -33.96455,
                        "Lng": 151.13903
                    },
                    {
                        "Lat": -33.96455,
                        "Lng": 151.13903
                    },
                    {
                        "Lat": -33.96462,
                        "Lng": 151.1395
                    },
                    {
                        "Lat": -33.96462,
                        "Lng": 151.1395
                    },
                    {
                        "Lat": -33.96464,
                        "Lng": 151.13965
                    },
                    {
                        "Lat": -33.96464,
                        "Lng": 151.13965
                    },
                    {
                        "Lat": -33.96468,
                        "Lng": 151.14007
                    },
                    {
                        "Lat": -33.96468,
                        "Lng": 151.14007
                    },
                    {
                        "Lat": -33.96477,
                        "Lng": 151.14085
                    },
                    {
                        "Lat": -33.96477,
                        "Lng": 151.14085
                    },
                    {
                        "Lat": -33.9648,
                        "Lng": 151.14102
                    },
                    {
                        "Lat": -33.9648,
                        "Lng": 151.14102
                    },
                    {
                        "Lat": -33.96487,
                        "Lng": 151.14145
                    },
                    {
                        "Lat": -33.96487,
                        "Lng": 151.14145
                    },
                    {
                        "Lat": -33.9649,
                        "Lng": 151.14166
                    },
                    {
                        "Lat": -33.9649,
                        "Lng": 151.14166
                    },
                    {
                        "Lat": -33.96497,
                        "Lng": 151.1421
                    },
                    {
                        "Lat": -33.965,
                        "Lng": 151.14241
                    },
                    {
                        "Lat": -33.965,
                        "Lng": 151.14241
                    },
                    {
                        "Lat": -33.96502,
                        "Lng": 151.14272
                    },
                    {
                        "Lat": -33.96502,
                        "Lng": 151.14272
                    },
                    {
                        "Lat": -33.9651,
                        "Lng": 151.14348
                    },
                    {
                        "Lat": -33.9651,
                        "Lng": 151.14348
                    },
                    {
                        "Lat": -33.96515,
                        "Lng": 151.14392
                    },
                    {
                        "Lat": -33.96522,
                        "Lng": 151.14433
                    },
                    {
                        "Lat": -33.96522,
                        "Lng": 151.14433
                    },
                    {
                        "Lat": -33.96528,
                        "Lng": 151.14467
                    },
                    {
                        "Lat": -33.96528,
                        "Lng": 151.14467
                    },
                    {
                        "Lat": -33.96535,
                        "Lng": 151.14503
                    },
                    {
                        "Lat": -33.96535,
                        "Lng": 151.14503
                    },
                    {
                        "Lat": -33.96538,
                        "Lng": 151.14523
                    },
                    {
                        "Lat": -33.96538,
                        "Lng": 151.14523
                    },
                    {
                        "Lat": -33.96547,
                        "Lng": 151.14577
                    },
                    {
                        "Lat": -33.96547,
                        "Lng": 151.14577
                    },
                    {
                        "Lat": -33.9655,
                        "Lng": 151.14597
                    },
                    {
                        "Lat": -33.9655,
                        "Lng": 151.14597
                    },
                    {
                        "Lat": -33.96553,
                        "Lng": 151.14616
                    },
                    {
                        "Lat": -33.96553,
                        "Lng": 151.14616
                    },
                    {
                        "Lat": -33.96561,
                        "Lng": 151.14681
                    },
                    {
                        "Lat": -33.96561,
                        "Lng": 151.14681
                    },
                    {
                        "Lat": -33.96571,
                        "Lng": 151.14735
                    },
                    {
                        "Lat": -33.96571,
                        "Lng": 151.14735
                    },
                    {
                        "Lat": -33.96582,
                        "Lng": 151.14794
                    },
                    {
                        "Lat": -33.96582,
                        "Lng": 151.14794
                    },
                    {
                        "Lat": -33.9659,
                        "Lng": 151.14837
                    },
                    {
                        "Lat": -33.9659,
                        "Lng": 151.14837
                    },
                    {
                        "Lat": -33.966,
                        "Lng": 151.14891
                    },
                    {
                        "Lat": -33.966,
                        "Lng": 151.14891
                    },
                    {
                        "Lat": -33.96604,
                        "Lng": 151.14915
                    },
                    {
                        "Lat": -33.96604,
                        "Lng": 151.14915
                    },
                    {
                        "Lat": -33.96607,
                        "Lng": 151.14936
                    },
                    {
                        "Lat": -33.96607,
                        "Lng": 151.14936
                    },
                    {
                        "Lat": -33.96621,
                        "Lng": 151.15032
                    },
                    {
                        "Lat": -33.96626,
                        "Lng": 151.1504
                    },
                    {
                        "Lat": -33.96626,
                        "Lng": 151.1504
                    },
                    {
                        "Lat": -33.96644,
                        "Lng": 151.15166
                    },
                    {
                        "Lat": -33.96644,
                        "Lng": 151.15166
                    },
                    {
                        "Lat": -33.96658,
                        "Lng": 151.15269
                    },
                    {
                        "Lat": -33.96658,
                        "Lng": 151.15269
                    },
                    {
                        "Lat": -33.96661,
                        "Lng": 151.15292
                    },
                    {
                        "Lat": -33.96661,
                        "Lng": 151.15292
                    },
                    {
                        "Lat": -33.96666,
                        "Lng": 151.1533
                    },
                    {
                        "Lat": -33.96666,
                        "Lng": 151.1533
                    },
                    {
                        "Lat": -33.96669,
                        "Lng": 151.15349
                    }
                ]
            },
            {
                "Key": "inc-syd-02",
                "Priority": 4,
                "Headline": "Brook Street — roadworks",
                "Detail": "At Brook Street - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 122,
                "Latitude": -33.82257,
                "Longitude": 151.20616,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.82257,
                        "Lng": 151.20616
                    },
                    {
                        "Lat": -33.82273,
                        "Lng": 151.20638
                    },
                    {
                        "Lat": -33.82298,
                        "Lng": 151.20677
                    },
                    {
                        "Lat": -33.8231,
                        "Lng": 151.20698
                    },
                    {
                        "Lat": -33.82322,
                        "Lng": 151.20723
                    }
                ]
            },
            {
                "Key": "inc-syd-03",
                "Priority": 3,
                "Headline": "Flynn Avenue — roadworks",
                "Detail": "Between Flynn Avenue and Mannow Avenue - Road construction",
                "FunctionalClass": 3,
                "LengthMeters": 963,
                "Latitude": -33.92112,
                "Longitude": 150.85079,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.92112,
                        "Lng": 150.85079
                    },
                    {
                        "Lat": -33.92138,
                        "Lng": 150.85056
                    },
                    {
                        "Lat": -33.92138,
                        "Lng": 150.85056
                    },
                    {
                        "Lat": -33.92173,
                        "Lng": 150.85025
                    },
                    {
                        "Lat": -33.92173,
                        "Lng": 150.85025
                    },
                    {
                        "Lat": -33.92192,
                        "Lng": 150.85008
                    },
                    {
                        "Lat": -33.92234,
                        "Lng": 150.84977
                    },
                    {
                        "Lat": -33.92234,
                        "Lng": 150.84977
                    },
                    {
                        "Lat": -33.92244,
                        "Lng": 150.8497
                    },
                    {
                        "Lat": -33.92244,
                        "Lng": 150.8497
                    },
                    {
                        "Lat": -33.92277,
                        "Lng": 150.84947
                    },
                    {
                        "Lat": -33.92297,
                        "Lng": 150.84936
                    },
                    {
                        "Lat": -33.92297,
                        "Lng": 150.84936
                    },
                    {
                        "Lat": -33.92332,
                        "Lng": 150.84915
                    },
                    {
                        "Lat": -33.92332,
                        "Lng": 150.84915
                    },
                    {
                        "Lat": -33.92353,
                        "Lng": 150.84903
                    },
                    {
                        "Lat": -33.92353,
                        "Lng": 150.84903
                    },
                    {
                        "Lat": -33.92368,
                        "Lng": 150.84895
                    },
                    {
                        "Lat": -33.92368,
                        "Lng": 150.84895
                    },
                    {
                        "Lat": -33.92417,
                        "Lng": 150.8487
                    },
                    {
                        "Lat": -33.92417,
                        "Lng": 150.8487
                    },
                    {
                        "Lat": -33.92447,
                        "Lng": 150.84856
                    },
                    {
                        "Lat": -33.92447,
                        "Lng": 150.84856
                    },
                    {
                        "Lat": -33.92464,
                        "Lng": 150.84849
                    },
                    {
                        "Lat": -33.92464,
                        "Lng": 150.84849
                    },
                    {
                        "Lat": -33.92489,
                        "Lng": 150.84839
                    },
                    {
                        "Lat": -33.92489,
                        "Lng": 150.84839
                    },
                    {
                        "Lat": -33.92503,
                        "Lng": 150.84833
                    },
                    {
                        "Lat": -33.92503,
                        "Lng": 150.84833
                    },
                    {
                        "Lat": -33.92517,
                        "Lng": 150.84827
                    },
                    {
                        "Lat": -33.92517,
                        "Lng": 150.84827
                    },
                    {
                        "Lat": -33.92547,
                        "Lng": 150.84814
                    },
                    {
                        "Lat": -33.92547,
                        "Lng": 150.84814
                    },
                    {
                        "Lat": -33.92638,
                        "Lng": 150.84776
                    },
                    {
                        "Lat": -33.92638,
                        "Lng": 150.84776
                    },
                    {
                        "Lat": -33.92659,
                        "Lng": 150.84769
                    },
                    {
                        "Lat": -33.92659,
                        "Lng": 150.84769
                    },
                    {
                        "Lat": -33.92695,
                        "Lng": 150.84758
                    },
                    {
                        "Lat": -33.92772,
                        "Lng": 150.84741
                    },
                    {
                        "Lat": -33.92772,
                        "Lng": 150.84741
                    },
                    {
                        "Lat": -33.92804,
                        "Lng": 150.84735
                    },
                    {
                        "Lat": -33.92804,
                        "Lng": 150.84735
                    },
                    {
                        "Lat": -33.92913,
                        "Lng": 150.84714
                    }
                ]
            },
            {
                "Key": "inc-syd-04",
                "Priority": 3,
                "Headline": "Huntley Street — roadworks",
                "Detail": "At Huntley Street - Closed due to road construction",
                "FunctionalClass": 3,
                "LengthMeters": 45,
                "Latitude": -33.90738,
                "Longitude": 151.18719,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -33.90738,
                        "Lng": 151.18719
                    },
                    {
                        "Lat": -33.90705,
                        "Lng": 151.18747
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on South Street have lanes closed over 2 kilometres.",
                "CorridorKeys": [
                    "South Street"
                ]
            },
            {
                "Text": "Roadworks on Brook Street have lanes closed.",
                "CorridorKeys": [
                    "Brook Street"
                ]
            },
            {
                "Text": "Roadworks on Flynn Avenue have lanes closed.",
                "CorridorKeys": [
                    "Flynn Avenue"
                ]
            },
            {
                "Text": "Roadworks on Huntley Street have lanes closed.",
                "CorridorKeys": [
                    "Huntley Street"
                ]
            }
        ],
        "ContradictionPlanted": false
    },
    "melbourne": {
        "FetchedAtIso": "2026-06-25T04:10:11.703Z",
        "Bbox": [
            144.5,
            -38.2,
            145.6,
            -37.4
        ],
        "Incidents": [
            {
                "Key": "inc-mel-01",
                "Priority": 4,
                "Headline": "Donnybrook Road — roadworks",
                "Detail": "Between Donnybrook Road and Main Road - Maintenance work",
                "FunctionalClass": 1,
                "LengthMeters": 39768,
                "Latitude": -37.45584,
                "Longitude": 144.97487,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.45584,
                        "Lng": 144.97487
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45033,
                        "Lng": 144.97875
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44866,
                        "Lng": 144.97978
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44755,
                        "Lng": 144.98031
                    },
                    {
                        "Lat": -37.44701,
                        "Lng": 144.98052
                    },
                    {
                        "Lat": -37.44628,
                        "Lng": 144.98077
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44501,
                        "Lng": 144.9811
                    },
                    {
                        "Lat": -37.44435,
                        "Lng": 144.98122
                    },
                    {
                        "Lat": -37.44375,
                        "Lng": 144.9813
                    },
                    {
                        "Lat": -37.44315,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44281,
                        "Lng": 144.98136
                    },
                    {
                        "Lat": -37.44159,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.43919,
                        "Lng": 144.98124
                    },
                    {
                        "Lat": -37.438,
                        "Lng": 144.98123
                    },
                    {
                        "Lat": -37.43715,
                        "Lng": 144.98127
                    },
                    {
                        "Lat": -37.43657,
                        "Lng": 144.98134
                    },
                    {
                        "Lat": -37.43614,
                        "Lng": 144.9814
                    },
                    {
                        "Lat": -37.43556,
                        "Lng": 144.9815
                    },
                    {
                        "Lat": -37.43459,
                        "Lng": 144.98172
                    },
                    {
                        "Lat": -37.43368,
                        "Lng": 144.98198
                    },
                    {
                        "Lat": -37.43322,
                        "Lng": 144.98214
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.43229,
                        "Lng": 144.9825
                    },
                    {
                        "Lat": -37.4319,
                        "Lng": 144.98267
                    },
                    {
                        "Lat": -37.43116,
                        "Lng": 144.98304
                    },
                    {
                        "Lat": -37.43041,
                        "Lng": 144.98344
                    },
                    {
                        "Lat": -37.43019,
                        "Lng": 144.98357
                    },
                    {
                        "Lat": -37.42888,
                        "Lng": 144.98443
                    },
                    {
                        "Lat": -37.4279,
                        "Lng": 144.98518
                    },
                    {
                        "Lat": -37.42706,
                        "Lng": 144.9859
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41668,
                        "Lng": 144.99576
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41541,
                        "Lng": 144.99712
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41406,
                        "Lng": 144.99881
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41272,
                        "Lng": 145.0008
                    },
                    {
                        "Lat": -37.41211,
                        "Lng": 145.00186
                    },
                    {
                        "Lat": -37.41147,
                        "Lng": 145.0031
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41055,
                        "Lng": 145.00508
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.40954,
                        "Lng": 145.0073
                    },
                    {
                        "Lat": -37.40935,
                        "Lng": 145.00768
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40869,
                        "Lng": 145.00889
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40781,
                        "Lng": 145.01033
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40627,
                        "Lng": 145.01245
                    },
                    {
                        "Lat": -37.40601,
                        "Lng": 145.01277
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40523,
                        "Lng": 145.01366
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.4041,
                        "Lng": 145.01482
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.39825,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.39825,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.39782,
                        "Lng": 145.02096
                    },
                    {
                        "Lat": -37.39689,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.3963,
                        "Lng": 145.02233
                    },
                    {
                        "Lat": -37.39603,
                        "Lng": 145.02254
                    },
                    {
                        "Lat": -37.39524,
                        "Lng": 145.02312
                    },
                    {
                        "Lat": -37.39481,
                        "Lng": 145.02341
                    },
                    {
                        "Lat": -37.39444,
                        "Lng": 145.02364
                    },
                    {
                        "Lat": -37.39338,
                        "Lng": 145.02424
                    },
                    {
                        "Lat": -37.39338,
                        "Lng": 145.02424
                    },
                    {
                        "Lat": -37.39313,
                        "Lng": 145.02436
                    },
                    {
                        "Lat": -37.39313,
                        "Lng": 145.02436
                    },
                    {
                        "Lat": -37.39247,
                        "Lng": 145.02468
                    },
                    {
                        "Lat": -37.39196,
                        "Lng": 145.0249
                    },
                    {
                        "Lat": -37.39126,
                        "Lng": 145.02516
                    },
                    {
                        "Lat": -37.3904,
                        "Lng": 145.02544
                    },
                    {
                        "Lat": -37.38925,
                        "Lng": 145.02572
                    },
                    {
                        "Lat": -37.38862,
                        "Lng": 145.02584
                    },
                    {
                        "Lat": -37.38823,
                        "Lng": 145.02589
                    },
                    {
                        "Lat": -37.38711,
                        "Lng": 145.02599
                    },
                    {
                        "Lat": -37.38711,
                        "Lng": 145.02599
                    },
                    {
                        "Lat": -37.38686,
                        "Lng": 145.026
                    },
                    {
                        "Lat": -37.38686,
                        "Lng": 145.026
                    },
                    {
                        "Lat": -37.38637,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38603,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38603,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38511,
                        "Lng": 145.02598
                    },
                    {
                        "Lat": -37.3846,
                        "Lng": 145.02593
                    },
                    {
                        "Lat": -37.38367,
                        "Lng": 145.02579
                    },
                    {
                        "Lat": -37.38312,
                        "Lng": 145.02568
                    },
                    {
                        "Lat": -37.3825,
                        "Lng": 145.02553
                    },
                    {
                        "Lat": -37.38147,
                        "Lng": 145.02523
                    },
                    {
                        "Lat": -37.38101,
                        "Lng": 145.02507
                    },
                    {
                        "Lat": -37.38041,
                        "Lng": 145.02483
                    },
                    {
                        "Lat": -37.3798,
                        "Lng": 145.02456
                    },
                    {
                        "Lat": -37.37919,
                        "Lng": 145.02426
                    },
                    {
                        "Lat": -37.37867,
                        "Lng": 145.02398
                    },
                    {
                        "Lat": -37.37839,
                        "Lng": 145.02382
                    },
                    {
                        "Lat": -37.37761,
                        "Lng": 145.02333
                    },
                    {
                        "Lat": -37.37599,
                        "Lng": 145.02225
                    },
                    {
                        "Lat": -37.37514,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.37514,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.37448,
                        "Lng": 145.02138
                    },
                    {
                        "Lat": -37.37395,
                        "Lng": 145.02114
                    },
                    {
                        "Lat": -37.37336,
                        "Lng": 145.02091
                    },
                    {
                        "Lat": -37.37284,
                        "Lng": 145.02073
                    },
                    {
                        "Lat": -37.37243,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.37195,
                        "Lng": 145.02049
                    },
                    {
                        "Lat": -37.37094,
                        "Lng": 145.0203
                    },
                    {
                        "Lat": -37.37053,
                        "Lng": 145.02026
                    },
                    {
                        "Lat": -37.37053,
                        "Lng": 145.02026
                    },
                    {
                        "Lat": -37.36994,
                        "Lng": 145.02021
                    },
                    {
                        "Lat": -37.36921,
                        "Lng": 145.0202
                    },
                    {
                        "Lat": -37.36882,
                        "Lng": 145.02022
                    },
                    {
                        "Lat": -37.36796,
                        "Lng": 145.0203
                    },
                    {
                        "Lat": -37.36719,
                        "Lng": 145.02043
                    },
                    {
                        "Lat": -37.36635,
                        "Lng": 145.02064
                    },
                    {
                        "Lat": -37.36546,
                        "Lng": 145.02092
                    },
                    {
                        "Lat": -37.363,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.36237,
                        "Lng": 145.02192
                    },
                    {
                        "Lat": -37.36186,
                        "Lng": 145.02205
                    },
                    {
                        "Lat": -37.36102,
                        "Lng": 145.02224
                    },
                    {
                        "Lat": -37.36102,
                        "Lng": 145.02224
                    },
                    {
                        "Lat": -37.35981,
                        "Lng": 145.02245
                    },
                    {
                        "Lat": -37.35865,
                        "Lng": 145.02258
                    },
                    {
                        "Lat": -37.35865,
                        "Lng": 145.02258
                    },
                    {
                        "Lat": -37.35725,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35725,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35672,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35672,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35605,
                        "Lng": 145.02263
                    },
                    {
                        "Lat": -37.35605,
                        "Lng": 145.02263
                    },
                    {
                        "Lat": -37.35568,
                        "Lng": 145.02262
                    },
                    {
                        "Lat": -37.35568,
                        "Lng": 145.02262
                    },
                    {
                        "Lat": -37.35526,
                        "Lng": 145.02259
                    },
                    {
                        "Lat": -37.35526,
                        "Lng": 145.02259
                    },
                    {
                        "Lat": -37.35433,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.35433,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.3534,
                        "Lng": 145.02236
                    },
                    {
                        "Lat": -37.3534,
                        "Lng": 145.02236
                    },
                    {
                        "Lat": -37.353,
                        "Lng": 145.02229
                    },
                    {
                        "Lat": -37.353,
                        "Lng": 145.02229
                    },
                    {
                        "Lat": -37.35211,
                        "Lng": 145.0221
                    },
                    {
                        "Lat": -37.35145,
                        "Lng": 145.02193
                    },
                    {
                        "Lat": -37.35111,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.35111,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.35052,
                        "Lng": 145.02166
                    },
                    {
                        "Lat": -37.35052,
                        "Lng": 145.02166
                    },
                    {
                        "Lat": -37.34841,
                        "Lng": 145.02097
                    },
                    {
                        "Lat": -37.34841,
                        "Lng": 145.02097
                    },
                    {
                        "Lat": -37.34811,
                        "Lng": 145.02088
                    },
                    {
                        "Lat": -37.34745,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34745,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34674,
                        "Lng": 145.02058
                    },
                    {
                        "Lat": -37.34674,
                        "Lng": 145.02058
                    },
                    {
                        "Lat": -37.34639,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.34639,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.34584,
                        "Lng": 145.02051
                    },
                    {
                        "Lat": -37.34554,
                        "Lng": 145.02051
                    },
                    {
                        "Lat": -37.34492,
                        "Lng": 145.02055
                    },
                    {
                        "Lat": -37.34492,
                        "Lng": 145.02055
                    },
                    {
                        "Lat": -37.34444,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.34444,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.34395,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34346,
                        "Lng": 145.02084
                    },
                    {
                        "Lat": -37.34276,
                        "Lng": 145.02108
                    },
                    {
                        "Lat": -37.34217,
                        "Lng": 145.02134
                    },
                    {
                        "Lat": -37.34217,
                        "Lng": 145.02134
                    },
                    {
                        "Lat": -37.34201,
                        "Lng": 145.02143
                    },
                    {
                        "Lat": -37.34201,
                        "Lng": 145.02143
                    },
                    {
                        "Lat": -37.34156,
                        "Lng": 145.02168
                    },
                    {
                        "Lat": -37.34156,
                        "Lng": 145.02168
                    },
                    {
                        "Lat": -37.34135,
                        "Lng": 145.02181
                    },
                    {
                        "Lat": -37.34135,
                        "Lng": 145.02181
                    },
                    {
                        "Lat": -37.34082,
                        "Lng": 145.02216
                    },
                    {
                        "Lat": -37.34037,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.33995,
                        "Lng": 145.02285
                    },
                    {
                        "Lat": -37.33946,
                        "Lng": 145.02331
                    },
                    {
                        "Lat": -37.33764,
                        "Lng": 145.02524
                    },
                    {
                        "Lat": -37.33696,
                        "Lng": 145.02591
                    },
                    {
                        "Lat": -37.33641,
                        "Lng": 145.02642
                    },
                    {
                        "Lat": -37.33595,
                        "Lng": 145.02681
                    },
                    {
                        "Lat": -37.33539,
                        "Lng": 145.02725
                    },
                    {
                        "Lat": -37.33539,
                        "Lng": 145.02725
                    },
                    {
                        "Lat": -37.33514,
                        "Lng": 145.02743
                    },
                    {
                        "Lat": -37.33514,
                        "Lng": 145.02743
                    },
                    {
                        "Lat": -37.33449,
                        "Lng": 145.0279
                    },
                    {
                        "Lat": -37.3337,
                        "Lng": 145.02839
                    },
                    {
                        "Lat": -37.33315,
                        "Lng": 145.02871
                    },
                    {
                        "Lat": -37.33243,
                        "Lng": 145.02909
                    },
                    {
                        "Lat": -37.33243,
                        "Lng": 145.02909
                    },
                    {
                        "Lat": -37.33172,
                        "Lng": 145.02942
                    },
                    {
                        "Lat": -37.33064,
                        "Lng": 145.02984
                    },
                    {
                        "Lat": -37.33064,
                        "Lng": 145.02984
                    },
                    {
                        "Lat": -37.33016,
                        "Lng": 145.03001
                    },
                    {
                        "Lat": -37.33016,
                        "Lng": 145.03001
                    },
                    {
                        "Lat": -37.32954,
                        "Lng": 145.03019
                    },
                    {
                        "Lat": -37.32954,
                        "Lng": 145.03019
                    },
                    {
                        "Lat": -37.32928,
                        "Lng": 145.03026
                    },
                    {
                        "Lat": -37.32928,
                        "Lng": 145.03026
                    },
                    {
                        "Lat": -37.32793,
                        "Lng": 145.03058
                    },
                    {
                        "Lat": -37.3276,
                        "Lng": 145.03067
                    },
                    {
                        "Lat": -37.32701,
                        "Lng": 145.03086
                    },
                    {
                        "Lat": -37.32651,
                        "Lng": 145.03105
                    },
                    {
                        "Lat": -37.32651,
                        "Lng": 145.03105
                    },
                    {
                        "Lat": -37.32606,
                        "Lng": 145.03125
                    },
                    {
                        "Lat": -37.3256,
                        "Lng": 145.0315
                    },
                    {
                        "Lat": -37.32527,
                        "Lng": 145.03171
                    },
                    {
                        "Lat": -37.32474,
                        "Lng": 145.03209
                    },
                    {
                        "Lat": -37.32474,
                        "Lng": 145.03209
                    },
                    {
                        "Lat": -37.32414,
                        "Lng": 145.03261
                    },
                    {
                        "Lat": -37.32414,
                        "Lng": 145.03261
                    },
                    {
                        "Lat": -37.32383,
                        "Lng": 145.03291
                    },
                    {
                        "Lat": -37.32383,
                        "Lng": 145.03291
                    },
                    {
                        "Lat": -37.32343,
                        "Lng": 145.03336
                    },
                    {
                        "Lat": -37.32343,
                        "Lng": 145.03336
                    },
                    {
                        "Lat": -37.32311,
                        "Lng": 145.03375
                    },
                    {
                        "Lat": -37.32279,
                        "Lng": 145.03419
                    },
                    {
                        "Lat": -37.32248,
                        "Lng": 145.03468
                    },
                    {
                        "Lat": -37.32233,
                        "Lng": 145.03494
                    },
                    {
                        "Lat": -37.32193,
                        "Lng": 145.03571
                    },
                    {
                        "Lat": -37.32178,
                        "Lng": 145.03604
                    },
                    {
                        "Lat": -37.32155,
                        "Lng": 145.03663
                    },
                    {
                        "Lat": -37.32133,
                        "Lng": 145.0373
                    },
                    {
                        "Lat": -37.32116,
                        "Lng": 145.03792
                    },
                    {
                        "Lat": -37.32116,
                        "Lng": 145.03792
                    },
                    {
                        "Lat": -37.32102,
                        "Lng": 145.03858
                    },
                    {
                        "Lat": -37.32093,
                        "Lng": 145.03908
                    },
                    {
                        "Lat": -37.32093,
                        "Lng": 145.03908
                    },
                    {
                        "Lat": -37.32086,
                        "Lng": 145.03949
                    },
                    {
                        "Lat": -37.32086,
                        "Lng": 145.03949
                    },
                    {
                        "Lat": -37.32066,
                        "Lng": 145.04079
                    },
                    {
                        "Lat": -37.32053,
                        "Lng": 145.0415
                    },
                    {
                        "Lat": -37.32046,
                        "Lng": 145.04182
                    },
                    {
                        "Lat": -37.32034,
                        "Lng": 145.04231
                    },
                    {
                        "Lat": -37.32019,
                        "Lng": 145.04283
                    },
                    {
                        "Lat": -37.31991,
                        "Lng": 145.04363
                    },
                    {
                        "Lat": -37.31991,
                        "Lng": 145.04363
                    },
                    {
                        "Lat": -37.31966,
                        "Lng": 145.04422
                    },
                    {
                        "Lat": -37.31934,
                        "Lng": 145.04485
                    },
                    {
                        "Lat": -37.31895,
                        "Lng": 145.0455
                    },
                    {
                        "Lat": -37.31875,
                        "Lng": 145.0458
                    },
                    {
                        "Lat": -37.31846,
                        "Lng": 145.04619
                    },
                    {
                        "Lat": -37.31846,
                        "Lng": 145.04619
                    },
                    {
                        "Lat": -37.31826,
                        "Lng": 145.04644
                    },
                    {
                        "Lat": -37.31796,
                        "Lng": 145.04678
                    },
                    {
                        "Lat": -37.31759,
                        "Lng": 145.04716
                    },
                    {
                        "Lat": -37.31703,
                        "Lng": 145.04765
                    },
                    {
                        "Lat": -37.31654,
                        "Lng": 145.04801
                    },
                    {
                        "Lat": -37.31654,
                        "Lng": 145.04801
                    },
                    {
                        "Lat": -37.31616,
                        "Lng": 145.04826
                    },
                    {
                        "Lat": -37.31575,
                        "Lng": 145.04849
                    },
                    {
                        "Lat": -37.3152,
                        "Lng": 145.04874
                    },
                    {
                        "Lat": -37.31464,
                        "Lng": 145.04894
                    },
                    {
                        "Lat": -37.31464,
                        "Lng": 145.04894
                    },
                    {
                        "Lat": -37.314,
                        "Lng": 145.04912
                    },
                    {
                        "Lat": -37.31363,
                        "Lng": 145.0492
                    },
                    {
                        "Lat": -37.31363,
                        "Lng": 145.0492
                    },
                    {
                        "Lat": -37.31341,
                        "Lng": 145.04924
                    },
                    {
                        "Lat": -37.31341,
                        "Lng": 145.04924
                    },
                    {
                        "Lat": -37.31193,
                        "Lng": 145.04947
                    },
                    {
                        "Lat": -37.31193,
                        "Lng": 145.04947
                    },
                    {
                        "Lat": -37.31124,
                        "Lng": 145.0496
                    },
                    {
                        "Lat": -37.3105,
                        "Lng": 145.04979
                    },
                    {
                        "Lat": -37.30954,
                        "Lng": 145.05011
                    },
                    {
                        "Lat": -37.30885,
                        "Lng": 145.05038
                    },
                    {
                        "Lat": -37.30855,
                        "Lng": 145.05051
                    },
                    {
                        "Lat": -37.30855,
                        "Lng": 145.05051
                    },
                    {
                        "Lat": -37.30796,
                        "Lng": 145.05079
                    },
                    {
                        "Lat": -37.30763,
                        "Lng": 145.05096
                    },
                    {
                        "Lat": -37.30706,
                        "Lng": 145.05127
                    },
                    {
                        "Lat": -37.30624,
                        "Lng": 145.05174
                    },
                    {
                        "Lat": -37.30551,
                        "Lng": 145.05213
                    },
                    {
                        "Lat": -37.30489,
                        "Lng": 145.05242
                    },
                    {
                        "Lat": -37.30384,
                        "Lng": 145.05284
                    },
                    {
                        "Lat": -37.30384,
                        "Lng": 145.05284
                    },
                    {
                        "Lat": -37.30334,
                        "Lng": 145.05301
                    },
                    {
                        "Lat": -37.30248,
                        "Lng": 145.05326
                    },
                    {
                        "Lat": -37.30171,
                        "Lng": 145.05343
                    },
                    {
                        "Lat": -37.30086,
                        "Lng": 145.05357
                    },
                    {
                        "Lat": -37.30023,
                        "Lng": 145.05365
                    },
                    {
                        "Lat": -37.29917,
                        "Lng": 145.05376
                    },
                    {
                        "Lat": -37.29339,
                        "Lng": 145.0543
                    },
                    {
                        "Lat": -37.29339,
                        "Lng": 145.0543
                    },
                    {
                        "Lat": -37.29306,
                        "Lng": 145.05433
                    },
                    {
                        "Lat": -37.29306,
                        "Lng": 145.05433
                    },
                    {
                        "Lat": -37.2914,
                        "Lng": 145.0545
                    },
                    {
                        "Lat": -37.2914,
                        "Lng": 145.0545
                    },
                    {
                        "Lat": -37.29117,
                        "Lng": 145.05452
                    },
                    {
                        "Lat": -37.29117,
                        "Lng": 145.05452
                    },
                    {
                        "Lat": -37.29058,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.29058,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.289,
                        "Lng": 145.05472
                    },
                    {
                        "Lat": -37.289,
                        "Lng": 145.05472
                    },
                    {
                        "Lat": -37.28717,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.28665,
                        "Lng": 145.05494
                    },
                    {
                        "Lat": -37.28665,
                        "Lng": 145.05494
                    },
                    {
                        "Lat": -37.28641,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.28641,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.28619,
                        "Lng": 145.05499
                    },
                    {
                        "Lat": -37.28619,
                        "Lng": 145.05499
                    },
                    {
                        "Lat": -37.28295,
                        "Lng": 145.05529
                    },
                    {
                        "Lat": -37.28295,
                        "Lng": 145.05529
                    },
                    {
                        "Lat": -37.28262,
                        "Lng": 145.05532
                    },
                    {
                        "Lat": -37.28262,
                        "Lng": 145.05532
                    },
                    {
                        "Lat": -37.28129,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.28129,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.28092,
                        "Lng": 145.05548
                    },
                    {
                        "Lat": -37.28092,
                        "Lng": 145.05548
                    },
                    {
                        "Lat": -37.28069,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.28069,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.27919,
                        "Lng": 145.05565
                    },
                    {
                        "Lat": -37.27919,
                        "Lng": 145.05565
                    },
                    {
                        "Lat": -37.27848,
                        "Lng": 145.05571
                    },
                    {
                        "Lat": -37.27848,
                        "Lng": 145.05571
                    },
                    {
                        "Lat": -37.27804,
                        "Lng": 145.05576
                    },
                    {
                        "Lat": -37.27703,
                        "Lng": 145.05582
                    },
                    {
                        "Lat": -37.27638,
                        "Lng": 145.05584
                    },
                    {
                        "Lat": -37.27638,
                        "Lng": 145.05584
                    },
                    {
                        "Lat": -37.27605,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27605,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27535,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27535,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27463,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27392,
                        "Lng": 145.05583
                    },
                    {
                        "Lat": -37.27293,
                        "Lng": 145.05578
                    },
                    {
                        "Lat": -37.27152,
                        "Lng": 145.05566
                    },
                    {
                        "Lat": -37.27015,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.27015,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.26924,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.2682,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.26765,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.26668,
                        "Lng": 145.05546
                    },
                    {
                        "Lat": -37.26527,
                        "Lng": 145.05559
                    },
                    {
                        "Lat": -37.26527,
                        "Lng": 145.05559
                    },
                    {
                        "Lat": -37.26425,
                        "Lng": 145.05575
                    },
                    {
                        "Lat": -37.26338,
                        "Lng": 145.05592
                    },
                    {
                        "Lat": -37.26219,
                        "Lng": 145.05618
                    },
                    {
                        "Lat": -37.26121,
                        "Lng": 145.05638
                    },
                    {
                        "Lat": -37.26068,
                        "Lng": 145.05645
                    },
                    {
                        "Lat": -37.26068,
                        "Lng": 145.05645
                    },
                    {
                        "Lat": -37.26034,
                        "Lng": 145.05649
                    },
                    {
                        "Lat": -37.2596,
                        "Lng": 145.05653
                    },
                    {
                        "Lat": -37.25919,
                        "Lng": 145.05653
                    },
                    {
                        "Lat": -37.25876,
                        "Lng": 145.05651
                    },
                    {
                        "Lat": -37.25822,
                        "Lng": 145.05647
                    },
                    {
                        "Lat": -37.25779,
                        "Lng": 145.05642
                    },
                    {
                        "Lat": -37.25717,
                        "Lng": 145.05631
                    },
                    {
                        "Lat": -37.25681,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.25681,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.25451,
                        "Lng": 145.05564
                    },
                    {
                        "Lat": -37.25335,
                        "Lng": 145.05541
                    },
                    {
                        "Lat": -37.25335,
                        "Lng": 145.05541
                    },
                    {
                        "Lat": -37.25282,
                        "Lng": 145.05533
                    },
                    {
                        "Lat": -37.25207,
                        "Lng": 145.05525
                    },
                    {
                        "Lat": -37.25127,
                        "Lng": 145.05519
                    },
                    {
                        "Lat": -37.25078,
                        "Lng": 145.05517
                    },
                    {
                        "Lat": -37.25078,
                        "Lng": 145.05517
                    },
                    {
                        "Lat": -37.24967,
                        "Lng": 145.05519
                    },
                    {
                        "Lat": -37.2484,
                        "Lng": 145.05524
                    },
                    {
                        "Lat": -37.2484,
                        "Lng": 145.05524
                    },
                    {
                        "Lat": -37.24767,
                        "Lng": 145.05526
                    },
                    {
                        "Lat": -37.24701,
                        "Lng": 145.05526
                    },
                    {
                        "Lat": -37.24637,
                        "Lng": 145.05523
                    },
                    {
                        "Lat": -37.2456,
                        "Lng": 145.05516
                    },
                    {
                        "Lat": -37.24484,
                        "Lng": 145.05505
                    },
                    {
                        "Lat": -37.24431,
                        "Lng": 145.05495
                    },
                    {
                        "Lat": -37.24343,
                        "Lng": 145.05474
                    },
                    {
                        "Lat": -37.24234,
                        "Lng": 145.05441
                    },
                    {
                        "Lat": -37.24157,
                        "Lng": 145.05416
                    },
                    {
                        "Lat": -37.24157,
                        "Lng": 145.05416
                    },
                    {
                        "Lat": -37.24142,
                        "Lng": 145.05411
                    },
                    {
                        "Lat": -37.24142,
                        "Lng": 145.05411
                    },
                    {
                        "Lat": -37.24128,
                        "Lng": 145.05406
                    },
                    {
                        "Lat": -37.24128,
                        "Lng": 145.05406
                    },
                    {
                        "Lat": -37.24033,
                        "Lng": 145.05378
                    },
                    {
                        "Lat": -37.23953,
                        "Lng": 145.0536
                    },
                    {
                        "Lat": -37.23916,
                        "Lng": 145.05354
                    },
                    {
                        "Lat": -37.23877,
                        "Lng": 145.0535
                    },
                    {
                        "Lat": -37.23833,
                        "Lng": 145.05347
                    },
                    {
                        "Lat": -37.23774,
                        "Lng": 145.05345
                    },
                    {
                        "Lat": -37.23726,
                        "Lng": 145.05346
                    },
                    {
                        "Lat": -37.23666,
                        "Lng": 145.05351
                    },
                    {
                        "Lat": -37.23577,
                        "Lng": 145.05366
                    },
                    {
                        "Lat": -37.23553,
                        "Lng": 145.05371
                    },
                    {
                        "Lat": -37.23487,
                        "Lng": 145.05388
                    },
                    {
                        "Lat": -37.23451,
                        "Lng": 145.054
                    },
                    {
                        "Lat": -37.23397,
                        "Lng": 145.0542
                    },
                    {
                        "Lat": -37.23344,
                        "Lng": 145.05443
                    },
                    {
                        "Lat": -37.23315,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.23242,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.23202,
                        "Lng": 145.0552
                    },
                    {
                        "Lat": -37.23169,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.23128,
                        "Lng": 145.05573
                    },
                    {
                        "Lat": -37.23104,
                        "Lng": 145.05592
                    },
                    {
                        "Lat": -37.23044,
                        "Lng": 145.05644
                    },
                    {
                        "Lat": -37.22856,
                        "Lng": 145.05821
                    },
                    {
                        "Lat": -37.22773,
                        "Lng": 145.05894
                    },
                    {
                        "Lat": -37.22647,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.22607,
                        "Lng": 145.06027
                    },
                    {
                        "Lat": -37.22539,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.22539,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.22526,
                        "Lng": 145.06086
                    },
                    {
                        "Lat": -37.22526,
                        "Lng": 145.06086
                    },
                    {
                        "Lat": -37.22462,
                        "Lng": 145.0613
                    },
                    {
                        "Lat": -37.22381,
                        "Lng": 145.06182
                    },
                    {
                        "Lat": -37.22222,
                        "Lng": 145.06274
                    },
                    {
                        "Lat": -37.22104,
                        "Lng": 145.06334
                    },
                    {
                        "Lat": -37.22037,
                        "Lng": 145.06366
                    },
                    {
                        "Lat": -37.21946,
                        "Lng": 145.06406
                    },
                    {
                        "Lat": -37.21874,
                        "Lng": 145.06434
                    },
                    {
                        "Lat": -37.21762,
                        "Lng": 145.06473
                    },
                    {
                        "Lat": -37.21657,
                        "Lng": 145.06505
                    },
                    {
                        "Lat": -37.21451,
                        "Lng": 145.06561
                    },
                    {
                        "Lat": -37.21387,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.21387,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.21325,
                        "Lng": 145.066
                    },
                    {
                        "Lat": -37.21249,
                        "Lng": 145.06627
                    },
                    {
                        "Lat": -37.21249,
                        "Lng": 145.06627
                    },
                    {
                        "Lat": -37.21163,
                        "Lng": 145.0666
                    },
                    {
                        "Lat": -37.21163,
                        "Lng": 145.0666
                    },
                    {
                        "Lat": -37.21137,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.21137,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.21058,
                        "Lng": 145.06705
                    },
                    {
                        "Lat": -37.21058,
                        "Lng": 145.06705
                    },
                    {
                        "Lat": -37.21028,
                        "Lng": 145.06719
                    },
                    {
                        "Lat": -37.21028,
                        "Lng": 145.06719
                    },
                    {
                        "Lat": -37.20957,
                        "Lng": 145.06753
                    },
                    {
                        "Lat": -37.20957,
                        "Lng": 145.06753
                    },
                    {
                        "Lat": -37.20802,
                        "Lng": 145.06831
                    },
                    {
                        "Lat": -37.20802,
                        "Lng": 145.06831
                    },
                    {
                        "Lat": -37.20741,
                        "Lng": 145.06861
                    },
                    {
                        "Lat": -37.20668,
                        "Lng": 145.06894
                    },
                    {
                        "Lat": -37.20668,
                        "Lng": 145.06894
                    },
                    {
                        "Lat": -37.20587,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.20495,
                        "Lng": 145.06959
                    },
                    {
                        "Lat": -37.20411,
                        "Lng": 145.06985
                    },
                    {
                        "Lat": -37.20255,
                        "Lng": 145.07024
                    },
                    {
                        "Lat": -37.20176,
                        "Lng": 145.07039
                    },
                    {
                        "Lat": -37.20086,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.20032,
                        "Lng": 145.0706
                    },
                    {
                        "Lat": -37.19922,
                        "Lng": 145.0707
                    },
                    {
                        "Lat": -37.19831,
                        "Lng": 145.07073
                    },
                    {
                        "Lat": -37.19735,
                        "Lng": 145.07073
                    },
                    {
                        "Lat": -37.19651,
                        "Lng": 145.07069
                    },
                    {
                        "Lat": -37.19554,
                        "Lng": 145.0706
                    },
                    {
                        "Lat": -37.19498,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.19498,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.1943,
                        "Lng": 145.07043
                    },
                    {
                        "Lat": -37.19357,
                        "Lng": 145.07029
                    },
                    {
                        "Lat": -37.19357,
                        "Lng": 145.07029
                    },
                    {
                        "Lat": -37.19221,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.19221,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.19163,
                        "Lng": 145.06987
                    },
                    {
                        "Lat": -37.19163,
                        "Lng": 145.06987
                    },
                    {
                        "Lat": -37.19089,
                        "Lng": 145.06971
                    },
                    {
                        "Lat": -37.19089,
                        "Lng": 145.06971
                    },
                    {
                        "Lat": -37.19048,
                        "Lng": 145.06963
                    },
                    {
                        "Lat": -37.19048,
                        "Lng": 145.06963
                    },
                    {
                        "Lat": -37.18996,
                        "Lng": 145.06954
                    },
                    {
                        "Lat": -37.18996,
                        "Lng": 145.06954
                    },
                    {
                        "Lat": -37.18946,
                        "Lng": 145.06946
                    },
                    {
                        "Lat": -37.18946,
                        "Lng": 145.06946
                    },
                    {
                        "Lat": -37.18798,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.18798,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.18665,
                        "Lng": 145.06913
                    },
                    {
                        "Lat": -37.18513,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18513,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18339,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18339,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18233,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18233,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18204,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18204,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18151,
                        "Lng": 145.069
                    },
                    {
                        "Lat": -37.18151,
                        "Lng": 145.069
                    },
                    {
                        "Lat": -37.18093,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18038,
                        "Lng": 145.06914
                    },
                    {
                        "Lat": -37.18007,
                        "Lng": 145.06921
                    },
                    {
                        "Lat": -37.17959,
                        "Lng": 145.06936
                    },
                    {
                        "Lat": -37.17939,
                        "Lng": 145.06944
                    },
                    {
                        "Lat": -37.17939,
                        "Lng": 145.06944
                    },
                    {
                        "Lat": -37.17909,
                        "Lng": 145.06956
                    },
                    {
                        "Lat": -37.17884,
                        "Lng": 145.06968
                    },
                    {
                        "Lat": -37.17825,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.17784,
                        "Lng": 145.07026
                    },
                    {
                        "Lat": -37.17747,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.17747,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.17671,
                        "Lng": 145.07112
                    },
                    {
                        "Lat": -37.17671,
                        "Lng": 145.07112
                    },
                    {
                        "Lat": -37.17637,
                        "Lng": 145.07138
                    },
                    {
                        "Lat": -37.17637,
                        "Lng": 145.07138
                    },
                    {
                        "Lat": -37.17594,
                        "Lng": 145.07172
                    },
                    {
                        "Lat": -37.17594,
                        "Lng": 145.07172
                    },
                    {
                        "Lat": -37.17574,
                        "Lng": 145.07188
                    },
                    {
                        "Lat": -37.17574,
                        "Lng": 145.07188
                    },
                    {
                        "Lat": -37.17409,
                        "Lng": 145.07318
                    },
                    {
                        "Lat": -37.17409,
                        "Lng": 145.07318
                    },
                    {
                        "Lat": -37.17249,
                        "Lng": 145.07442
                    },
                    {
                        "Lat": -37.1719,
                        "Lng": 145.07484
                    },
                    {
                        "Lat": -37.17139,
                        "Lng": 145.07518
                    },
                    {
                        "Lat": -37.17089,
                        "Lng": 145.07548
                    },
                    {
                        "Lat": -37.17025,
                        "Lng": 145.07583
                    },
                    {
                        "Lat": -37.16935,
                        "Lng": 145.07625
                    },
                    {
                        "Lat": -37.16895,
                        "Lng": 145.07642
                    },
                    {
                        "Lat": -37.16845,
                        "Lng": 145.0766
                    },
                    {
                        "Lat": -37.16845,
                        "Lng": 145.0766
                    },
                    {
                        "Lat": -37.16842,
                        "Lng": 145.07661
                    },
                    {
                        "Lat": -37.16842,
                        "Lng": 145.07661
                    },
                    {
                        "Lat": -37.16772,
                        "Lng": 145.07684
                    },
                    {
                        "Lat": -37.16716,
                        "Lng": 145.07698
                    },
                    {
                        "Lat": -37.16658,
                        "Lng": 145.07712
                    },
                    {
                        "Lat": -37.16563,
                        "Lng": 145.07731
                    },
                    {
                        "Lat": -37.16495,
                        "Lng": 145.07746
                    },
                    {
                        "Lat": -37.16495,
                        "Lng": 145.07746
                    },
                    {
                        "Lat": -37.16346,
                        "Lng": 145.07778
                    },
                    {
                        "Lat": -37.16246,
                        "Lng": 145.07795
                    },
                    {
                        "Lat": -37.16064,
                        "Lng": 145.07821
                    },
                    {
                        "Lat": -37.16024,
                        "Lng": 145.07825
                    },
                    {
                        "Lat": -37.16024,
                        "Lng": 145.07825
                    },
                    {
                        "Lat": -37.15614,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.15614,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.15523,
                        "Lng": 145.07886
                    },
                    {
                        "Lat": -37.15476,
                        "Lng": 145.07896
                    },
                    {
                        "Lat": -37.15376,
                        "Lng": 145.07924
                    },
                    {
                        "Lat": -37.15376,
                        "Lng": 145.07924
                    },
                    {
                        "Lat": -37.15302,
                        "Lng": 145.07948
                    },
                    {
                        "Lat": -37.15302,
                        "Lng": 145.07948
                    },
                    {
                        "Lat": -37.15232,
                        "Lng": 145.07972
                    },
                    {
                        "Lat": -37.14678,
                        "Lng": 145.08152
                    },
                    {
                        "Lat": -37.14678,
                        "Lng": 145.08152
                    },
                    {
                        "Lat": -37.14578,
                        "Lng": 145.08186
                    },
                    {
                        "Lat": -37.14282,
                        "Lng": 145.08283
                    },
                    {
                        "Lat": -37.14282,
                        "Lng": 145.08283
                    },
                    {
                        "Lat": -37.14111,
                        "Lng": 145.0834
                    },
                    {
                        "Lat": -37.13576,
                        "Lng": 145.08515
                    },
                    {
                        "Lat": -37.13506,
                        "Lng": 145.08544
                    },
                    {
                        "Lat": -37.13467,
                        "Lng": 145.08564
                    },
                    {
                        "Lat": -37.13396,
                        "Lng": 145.08606
                    },
                    {
                        "Lat": -37.13247,
                        "Lng": 145.08703
                    },
                    {
                        "Lat": -37.13247,
                        "Lng": 145.08703
                    },
                    {
                        "Lat": -37.13153,
                        "Lng": 145.08764
                    },
                    {
                        "Lat": -37.13082,
                        "Lng": 145.08806
                    },
                    {
                        "Lat": -37.13082,
                        "Lng": 145.08806
                    },
                    {
                        "Lat": -37.13058,
                        "Lng": 145.0882
                    },
                    {
                        "Lat": -37.13058,
                        "Lng": 145.0882
                    },
                    {
                        "Lat": -37.13037,
                        "Lng": 145.08831
                    },
                    {
                        "Lat": -37.13037,
                        "Lng": 145.08831
                    },
                    {
                        "Lat": -37.12997,
                        "Lng": 145.0885
                    },
                    {
                        "Lat": -37.12997,
                        "Lng": 145.0885
                    },
                    {
                        "Lat": -37.12982,
                        "Lng": 145.08857
                    },
                    {
                        "Lat": -37.12955,
                        "Lng": 145.08867
                    }
                ]
            },
            {
                "Key": "inc-mel-02",
                "Priority": 4,
                "Headline": "Sunshine Avenue — roadworks",
                "Detail": "Between Sunshine Avenue and Keilor Park Drive - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 22563,
                "Latitude": -37.57194,
                "Longitude": 144.67614,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.57194,
                        "Lng": 144.67614
                    },
                    {
                        "Lat": -37.57221,
                        "Lng": 144.67631
                    },
                    {
                        "Lat": -37.57221,
                        "Lng": 144.67631
                    },
                    {
                        "Lat": -37.57259,
                        "Lng": 144.67659
                    },
                    {
                        "Lat": -37.57259,
                        "Lng": 144.67659
                    },
                    {
                        "Lat": -37.57404,
                        "Lng": 144.67768
                    },
                    {
                        "Lat": -37.57404,
                        "Lng": 144.67768
                    },
                    {
                        "Lat": -37.57578,
                        "Lng": 144.67903
                    },
                    {
                        "Lat": -37.57578,
                        "Lng": 144.67903
                    },
                    {
                        "Lat": -37.5763,
                        "Lng": 144.67946
                    },
                    {
                        "Lat": -37.5763,
                        "Lng": 144.67946
                    },
                    {
                        "Lat": -37.57731,
                        "Lng": 144.68027
                    },
                    {
                        "Lat": -37.57731,
                        "Lng": 144.68027
                    },
                    {
                        "Lat": -37.57878,
                        "Lng": 144.68144
                    },
                    {
                        "Lat": -37.57878,
                        "Lng": 144.68144
                    },
                    {
                        "Lat": -37.57937,
                        "Lng": 144.68192
                    },
                    {
                        "Lat": -37.57937,
                        "Lng": 144.68192
                    },
                    {
                        "Lat": -37.57969,
                        "Lng": 144.68218
                    },
                    {
                        "Lat": -37.57982,
                        "Lng": 144.68227
                    },
                    {
                        "Lat": -37.57982,
                        "Lng": 144.68227
                    },
                    {
                        "Lat": -37.58045,
                        "Lng": 144.68272
                    },
                    {
                        "Lat": -37.58045,
                        "Lng": 144.68272
                    },
                    {
                        "Lat": -37.58084,
                        "Lng": 144.683
                    },
                    {
                        "Lat": -37.58084,
                        "Lng": 144.683
                    },
                    {
                        "Lat": -37.58143,
                        "Lng": 144.68342
                    },
                    {
                        "Lat": -37.58143,
                        "Lng": 144.68342
                    },
                    {
                        "Lat": -37.58212,
                        "Lng": 144.68392
                    },
                    {
                        "Lat": -37.58212,
                        "Lng": 144.68392
                    },
                    {
                        "Lat": -37.58238,
                        "Lng": 144.68412
                    },
                    {
                        "Lat": -37.58238,
                        "Lng": 144.68412
                    },
                    {
                        "Lat": -37.58264,
                        "Lng": 144.68431
                    },
                    {
                        "Lat": -37.58371,
                        "Lng": 144.68501
                    },
                    {
                        "Lat": -37.58465,
                        "Lng": 144.6856
                    },
                    {
                        "Lat": -37.58662,
                        "Lng": 144.6868
                    },
                    {
                        "Lat": -37.58771,
                        "Lng": 144.68749
                    },
                    {
                        "Lat": -37.58771,
                        "Lng": 144.68749
                    },
                    {
                        "Lat": -37.59027,
                        "Lng": 144.68919
                    },
                    {
                        "Lat": -37.59133,
                        "Lng": 144.68994
                    },
                    {
                        "Lat": -37.59222,
                        "Lng": 144.69058
                    },
                    {
                        "Lat": -37.59345,
                        "Lng": 144.69149
                    },
                    {
                        "Lat": -37.59542,
                        "Lng": 144.693
                    },
                    {
                        "Lat": -37.60382,
                        "Lng": 144.69984
                    },
                    {
                        "Lat": -37.60492,
                        "Lng": 144.7008
                    },
                    {
                        "Lat": -37.60492,
                        "Lng": 144.7008
                    },
                    {
                        "Lat": -37.60562,
                        "Lng": 144.70144
                    },
                    {
                        "Lat": -37.60562,
                        "Lng": 144.70144
                    },
                    {
                        "Lat": -37.60637,
                        "Lng": 144.70215
                    },
                    {
                        "Lat": -37.60667,
                        "Lng": 144.70245
                    },
                    {
                        "Lat": -37.60667,
                        "Lng": 144.70245
                    },
                    {
                        "Lat": -37.60749,
                        "Lng": 144.70325
                    },
                    {
                        "Lat": -37.60749,
                        "Lng": 144.70325
                    },
                    {
                        "Lat": -37.60836,
                        "Lng": 144.70412
                    },
                    {
                        "Lat": -37.60836,
                        "Lng": 144.70412
                    },
                    {
                        "Lat": -37.61077,
                        "Lng": 144.70648
                    },
                    {
                        "Lat": -37.61077,
                        "Lng": 144.70648
                    },
                    {
                        "Lat": -37.61121,
                        "Lng": 144.70692
                    },
                    {
                        "Lat": -37.61121,
                        "Lng": 144.70692
                    },
                    {
                        "Lat": -37.6122,
                        "Lng": 144.70789
                    },
                    {
                        "Lat": -37.6122,
                        "Lng": 144.70789
                    },
                    {
                        "Lat": -37.61302,
                        "Lng": 144.7087
                    },
                    {
                        "Lat": -37.61302,
                        "Lng": 144.7087
                    },
                    {
                        "Lat": -37.61316,
                        "Lng": 144.70884
                    },
                    {
                        "Lat": -37.61316,
                        "Lng": 144.70884
                    },
                    {
                        "Lat": -37.61325,
                        "Lng": 144.70892
                    },
                    {
                        "Lat": -37.61325,
                        "Lng": 144.70892
                    },
                    {
                        "Lat": -37.6138,
                        "Lng": 144.70946
                    },
                    {
                        "Lat": -37.61426,
                        "Lng": 144.70988
                    },
                    {
                        "Lat": -37.61426,
                        "Lng": 144.70988
                    },
                    {
                        "Lat": -37.61471,
                        "Lng": 144.71029
                    },
                    {
                        "Lat": -37.61471,
                        "Lng": 144.71029
                    },
                    {
                        "Lat": -37.61537,
                        "Lng": 144.71088
                    },
                    {
                        "Lat": -37.61537,
                        "Lng": 144.71088
                    },
                    {
                        "Lat": -37.61601,
                        "Lng": 144.71142
                    },
                    {
                        "Lat": -37.61601,
                        "Lng": 144.71142
                    },
                    {
                        "Lat": -37.61631,
                        "Lng": 144.71167
                    },
                    {
                        "Lat": -37.61631,
                        "Lng": 144.71167
                    },
                    {
                        "Lat": -37.61688,
                        "Lng": 144.71214
                    },
                    {
                        "Lat": -37.61688,
                        "Lng": 144.71214
                    },
                    {
                        "Lat": -37.618,
                        "Lng": 144.71305
                    },
                    {
                        "Lat": -37.618,
                        "Lng": 144.71305
                    },
                    {
                        "Lat": -37.61968,
                        "Lng": 144.71441
                    },
                    {
                        "Lat": -37.61968,
                        "Lng": 144.71441
                    },
                    {
                        "Lat": -37.62009,
                        "Lng": 144.71478
                    },
                    {
                        "Lat": -37.62044,
                        "Lng": 144.71513
                    },
                    {
                        "Lat": -37.62044,
                        "Lng": 144.71513
                    },
                    {
                        "Lat": -37.6206,
                        "Lng": 144.71529
                    },
                    {
                        "Lat": -37.62099,
                        "Lng": 144.71575
                    },
                    {
                        "Lat": -37.62099,
                        "Lng": 144.71575
                    },
                    {
                        "Lat": -37.62152,
                        "Lng": 144.71647
                    },
                    {
                        "Lat": -37.62186,
                        "Lng": 144.71701
                    },
                    {
                        "Lat": -37.62215,
                        "Lng": 144.71756
                    },
                    {
                        "Lat": -37.62247,
                        "Lng": 144.71821
                    },
                    {
                        "Lat": -37.6227,
                        "Lng": 144.71874
                    },
                    {
                        "Lat": -37.6227,
                        "Lng": 144.71874
                    },
                    {
                        "Lat": -37.62279,
                        "Lng": 144.71893
                    },
                    {
                        "Lat": -37.62279,
                        "Lng": 144.71893
                    },
                    {
                        "Lat": -37.6229,
                        "Lng": 144.71916
                    },
                    {
                        "Lat": -37.6229,
                        "Lng": 144.71916
                    },
                    {
                        "Lat": -37.62308,
                        "Lng": 144.71957
                    },
                    {
                        "Lat": -37.62349,
                        "Lng": 144.72043
                    },
                    {
                        "Lat": -37.62388,
                        "Lng": 144.72119
                    },
                    {
                        "Lat": -37.62417,
                        "Lng": 144.7217
                    },
                    {
                        "Lat": -37.62455,
                        "Lng": 144.72228
                    },
                    {
                        "Lat": -37.6249,
                        "Lng": 144.72276
                    },
                    {
                        "Lat": -37.62525,
                        "Lng": 144.7232
                    },
                    {
                        "Lat": -37.62565,
                        "Lng": 144.72365
                    },
                    {
                        "Lat": -37.62605,
                        "Lng": 144.72406
                    },
                    {
                        "Lat": -37.62644,
                        "Lng": 144.72442
                    },
                    {
                        "Lat": -37.62685,
                        "Lng": 144.72476
                    },
                    {
                        "Lat": -37.62781,
                        "Lng": 144.72542
                    },
                    {
                        "Lat": -37.62837,
                        "Lng": 144.72577
                    },
                    {
                        "Lat": -37.62837,
                        "Lng": 144.72577
                    },
                    {
                        "Lat": -37.62964,
                        "Lng": 144.72655
                    },
                    {
                        "Lat": -37.62964,
                        "Lng": 144.72655
                    },
                    {
                        "Lat": -37.63064,
                        "Lng": 144.72716
                    },
                    {
                        "Lat": -37.63064,
                        "Lng": 144.72716
                    },
                    {
                        "Lat": -37.63089,
                        "Lng": 144.72731
                    },
                    {
                        "Lat": -37.63089,
                        "Lng": 144.72731
                    },
                    {
                        "Lat": -37.63176,
                        "Lng": 144.72784
                    },
                    {
                        "Lat": -37.63176,
                        "Lng": 144.72784
                    },
                    {
                        "Lat": -37.63215,
                        "Lng": 144.72808
                    },
                    {
                        "Lat": -37.63215,
                        "Lng": 144.72808
                    },
                    {
                        "Lat": -37.63275,
                        "Lng": 144.72845
                    },
                    {
                        "Lat": -37.63275,
                        "Lng": 144.72845
                    },
                    {
                        "Lat": -37.63779,
                        "Lng": 144.73154
                    },
                    {
                        "Lat": -37.63779,
                        "Lng": 144.73154
                    },
                    {
                        "Lat": -37.6385,
                        "Lng": 144.73199
                    },
                    {
                        "Lat": -37.6385,
                        "Lng": 144.73199
                    },
                    {
                        "Lat": -37.63899,
                        "Lng": 144.73229
                    },
                    {
                        "Lat": -37.63899,
                        "Lng": 144.73229
                    },
                    {
                        "Lat": -37.63916,
                        "Lng": 144.73239
                    },
                    {
                        "Lat": -37.63916,
                        "Lng": 144.73239
                    },
                    {
                        "Lat": -37.63954,
                        "Lng": 144.73262
                    },
                    {
                        "Lat": -37.63954,
                        "Lng": 144.73262
                    },
                    {
                        "Lat": -37.63982,
                        "Lng": 144.73279
                    },
                    {
                        "Lat": -37.63982,
                        "Lng": 144.73279
                    },
                    {
                        "Lat": -37.6407,
                        "Lng": 144.73332
                    },
                    {
                        "Lat": -37.6407,
                        "Lng": 144.73332
                    },
                    {
                        "Lat": -37.64256,
                        "Lng": 144.73438
                    },
                    {
                        "Lat": -37.64455,
                        "Lng": 144.7354
                    },
                    {
                        "Lat": -37.64455,
                        "Lng": 144.7354
                    },
                    {
                        "Lat": -37.64877,
                        "Lng": 144.73747
                    },
                    {
                        "Lat": -37.64877,
                        "Lng": 144.73747
                    },
                    {
                        "Lat": -37.65454,
                        "Lng": 144.7403
                    },
                    {
                        "Lat": -37.65454,
                        "Lng": 144.7403
                    },
                    {
                        "Lat": -37.65616,
                        "Lng": 144.7411
                    },
                    {
                        "Lat": -37.65616,
                        "Lng": 144.7411
                    },
                    {
                        "Lat": -37.65682,
                        "Lng": 144.74143
                    },
                    {
                        "Lat": -37.65682,
                        "Lng": 144.74143
                    },
                    {
                        "Lat": -37.65711,
                        "Lng": 144.74158
                    },
                    {
                        "Lat": -37.65711,
                        "Lng": 144.74158
                    },
                    {
                        "Lat": -37.65784,
                        "Lng": 144.74197
                    },
                    {
                        "Lat": -37.65868,
                        "Lng": 144.7425
                    },
                    {
                        "Lat": -37.6591,
                        "Lng": 144.7428
                    },
                    {
                        "Lat": -37.65948,
                        "Lng": 144.7431
                    },
                    {
                        "Lat": -37.65989,
                        "Lng": 144.74345
                    },
                    {
                        "Lat": -37.66063,
                        "Lng": 144.74415
                    },
                    {
                        "Lat": -37.66063,
                        "Lng": 144.74415
                    },
                    {
                        "Lat": -37.66111,
                        "Lng": 144.74464
                    },
                    {
                        "Lat": -37.66162,
                        "Lng": 144.74524
                    },
                    {
                        "Lat": -37.66205,
                        "Lng": 144.7458
                    },
                    {
                        "Lat": -37.66238,
                        "Lng": 144.74626
                    },
                    {
                        "Lat": -37.66238,
                        "Lng": 144.74626
                    },
                    {
                        "Lat": -37.66263,
                        "Lng": 144.74664
                    },
                    {
                        "Lat": -37.66323,
                        "Lng": 144.74764
                    },
                    {
                        "Lat": -37.66323,
                        "Lng": 144.74764
                    },
                    {
                        "Lat": -37.66359,
                        "Lng": 144.74832
                    },
                    {
                        "Lat": -37.66359,
                        "Lng": 144.74832
                    },
                    {
                        "Lat": -37.66386,
                        "Lng": 144.74885
                    },
                    {
                        "Lat": -37.66432,
                        "Lng": 144.74981
                    },
                    {
                        "Lat": -37.66432,
                        "Lng": 144.74981
                    },
                    {
                        "Lat": -37.66442,
                        "Lng": 144.75002
                    },
                    {
                        "Lat": -37.66442,
                        "Lng": 144.75002
                    },
                    {
                        "Lat": -37.66547,
                        "Lng": 144.75221
                    },
                    {
                        "Lat": -37.66547,
                        "Lng": 144.75221
                    },
                    {
                        "Lat": -37.6665,
                        "Lng": 144.75437
                    },
                    {
                        "Lat": -37.6665,
                        "Lng": 144.75437
                    },
                    {
                        "Lat": -37.6667,
                        "Lng": 144.75479
                    },
                    {
                        "Lat": -37.6667,
                        "Lng": 144.75479
                    },
                    {
                        "Lat": -37.66699,
                        "Lng": 144.75541
                    },
                    {
                        "Lat": -37.66699,
                        "Lng": 144.75541
                    },
                    {
                        "Lat": -37.66716,
                        "Lng": 144.75576
                    },
                    {
                        "Lat": -37.66716,
                        "Lng": 144.75576
                    },
                    {
                        "Lat": -37.6673,
                        "Lng": 144.75605
                    },
                    {
                        "Lat": -37.6673,
                        "Lng": 144.75605
                    },
                    {
                        "Lat": -37.66757,
                        "Lng": 144.75663
                    },
                    {
                        "Lat": -37.66757,
                        "Lng": 144.75663
                    },
                    {
                        "Lat": -37.66774,
                        "Lng": 144.75698
                    },
                    {
                        "Lat": -37.66774,
                        "Lng": 144.75698
                    },
                    {
                        "Lat": -37.66813,
                        "Lng": 144.7578
                    },
                    {
                        "Lat": -37.66813,
                        "Lng": 144.7578
                    },
                    {
                        "Lat": -37.6685,
                        "Lng": 144.75858
                    },
                    {
                        "Lat": -37.6685,
                        "Lng": 144.75858
                    },
                    {
                        "Lat": -37.66892,
                        "Lng": 144.75946
                    },
                    {
                        "Lat": -37.66892,
                        "Lng": 144.75946
                    },
                    {
                        "Lat": -37.66912,
                        "Lng": 144.75988
                    },
                    {
                        "Lat": -37.66912,
                        "Lng": 144.75988
                    },
                    {
                        "Lat": -37.66992,
                        "Lng": 144.76152
                    },
                    {
                        "Lat": -37.66992,
                        "Lng": 144.76152
                    },
                    {
                        "Lat": -37.67067,
                        "Lng": 144.76289
                    },
                    {
                        "Lat": -37.67067,
                        "Lng": 144.76289
                    },
                    {
                        "Lat": -37.67096,
                        "Lng": 144.7634
                    },
                    {
                        "Lat": -37.67096,
                        "Lng": 144.7634
                    },
                    {
                        "Lat": -37.67124,
                        "Lng": 144.76383
                    },
                    {
                        "Lat": -37.67124,
                        "Lng": 144.76383
                    },
                    {
                        "Lat": -37.67139,
                        "Lng": 144.76402
                    },
                    {
                        "Lat": -37.67139,
                        "Lng": 144.76402
                    },
                    {
                        "Lat": -37.67151,
                        "Lng": 144.76421
                    },
                    {
                        "Lat": -37.67151,
                        "Lng": 144.76421
                    },
                    {
                        "Lat": -37.67188,
                        "Lng": 144.76476
                    },
                    {
                        "Lat": -37.67188,
                        "Lng": 144.76476
                    },
                    {
                        "Lat": -37.67236,
                        "Lng": 144.76548
                    },
                    {
                        "Lat": -37.67236,
                        "Lng": 144.76548
                    },
                    {
                        "Lat": -37.67288,
                        "Lng": 144.76625
                    },
                    {
                        "Lat": -37.67288,
                        "Lng": 144.76625
                    },
                    {
                        "Lat": -37.67317,
                        "Lng": 144.76668
                    },
                    {
                        "Lat": -37.67317,
                        "Lng": 144.76668
                    },
                    {
                        "Lat": -37.67331,
                        "Lng": 144.76689
                    },
                    {
                        "Lat": -37.67331,
                        "Lng": 144.76689
                    },
                    {
                        "Lat": -37.67434,
                        "Lng": 144.76843
                    },
                    {
                        "Lat": -37.67434,
                        "Lng": 144.76843
                    },
                    {
                        "Lat": -37.67505,
                        "Lng": 144.76949
                    },
                    {
                        "Lat": -37.67505,
                        "Lng": 144.76949
                    },
                    {
                        "Lat": -37.67527,
                        "Lng": 144.76981
                    },
                    {
                        "Lat": -37.67527,
                        "Lng": 144.76981
                    },
                    {
                        "Lat": -37.67569,
                        "Lng": 144.77044
                    },
                    {
                        "Lat": -37.67569,
                        "Lng": 144.77044
                    },
                    {
                        "Lat": -37.67584,
                        "Lng": 144.77066
                    },
                    {
                        "Lat": -37.67584,
                        "Lng": 144.77066
                    },
                    {
                        "Lat": -37.67607,
                        "Lng": 144.771
                    },
                    {
                        "Lat": -37.67607,
                        "Lng": 144.771
                    },
                    {
                        "Lat": -37.67735,
                        "Lng": 144.77292
                    },
                    {
                        "Lat": -37.67735,
                        "Lng": 144.77292
                    },
                    {
                        "Lat": -37.67786,
                        "Lng": 144.77371
                    },
                    {
                        "Lat": -37.67786,
                        "Lng": 144.77371
                    },
                    {
                        "Lat": -37.67808,
                        "Lng": 144.77406
                    },
                    {
                        "Lat": -37.67808,
                        "Lng": 144.77406
                    },
                    {
                        "Lat": -37.6787,
                        "Lng": 144.77507
                    },
                    {
                        "Lat": -37.6787,
                        "Lng": 144.77507
                    },
                    {
                        "Lat": -37.68044,
                        "Lng": 144.77788
                    },
                    {
                        "Lat": -37.68044,
                        "Lng": 144.77788
                    },
                    {
                        "Lat": -37.68162,
                        "Lng": 144.7798
                    },
                    {
                        "Lat": -37.68162,
                        "Lng": 144.7798
                    },
                    {
                        "Lat": -37.68196,
                        "Lng": 144.78036
                    },
                    {
                        "Lat": -37.68196,
                        "Lng": 144.78036
                    },
                    {
                        "Lat": -37.68279,
                        "Lng": 144.78171
                    },
                    {
                        "Lat": -37.68279,
                        "Lng": 144.78171
                    },
                    {
                        "Lat": -37.68307,
                        "Lng": 144.78217
                    },
                    {
                        "Lat": -37.68307,
                        "Lng": 144.78217
                    },
                    {
                        "Lat": -37.68336,
                        "Lng": 144.78263
                    },
                    {
                        "Lat": -37.68336,
                        "Lng": 144.78263
                    },
                    {
                        "Lat": -37.68358,
                        "Lng": 144.78297
                    },
                    {
                        "Lat": -37.68358,
                        "Lng": 144.78297
                    },
                    {
                        "Lat": -37.68623,
                        "Lng": 144.78727
                    },
                    {
                        "Lat": -37.68623,
                        "Lng": 144.78727
                    },
                    {
                        "Lat": -37.68647,
                        "Lng": 144.78765
                    },
                    {
                        "Lat": -37.68647,
                        "Lng": 144.78765
                    },
                    {
                        "Lat": -37.68666,
                        "Lng": 144.78794
                    },
                    {
                        "Lat": -37.68666,
                        "Lng": 144.78794
                    },
                    {
                        "Lat": -37.68761,
                        "Lng": 144.78948
                    },
                    {
                        "Lat": -37.68761,
                        "Lng": 144.78948
                    },
                    {
                        "Lat": -37.68796,
                        "Lng": 144.79007
                    },
                    {
                        "Lat": -37.68796,
                        "Lng": 144.79007
                    },
                    {
                        "Lat": -37.68823,
                        "Lng": 144.79052
                    },
                    {
                        "Lat": -37.68823,
                        "Lng": 144.79052
                    },
                    {
                        "Lat": -37.68852,
                        "Lng": 144.79098
                    },
                    {
                        "Lat": -37.68852,
                        "Lng": 144.79098
                    },
                    {
                        "Lat": -37.68869,
                        "Lng": 144.79125
                    },
                    {
                        "Lat": -37.68869,
                        "Lng": 144.79125
                    },
                    {
                        "Lat": -37.68907,
                        "Lng": 144.79185
                    },
                    {
                        "Lat": -37.68907,
                        "Lng": 144.79185
                    },
                    {
                        "Lat": -37.68946,
                        "Lng": 144.79248
                    },
                    {
                        "Lat": -37.68946,
                        "Lng": 144.79248
                    },
                    {
                        "Lat": -37.69114,
                        "Lng": 144.79522
                    },
                    {
                        "Lat": -37.69114,
                        "Lng": 144.79522
                    },
                    {
                        "Lat": -37.69401,
                        "Lng": 144.79985
                    },
                    {
                        "Lat": -37.69401,
                        "Lng": 144.79985
                    },
                    {
                        "Lat": -37.69446,
                        "Lng": 144.80058
                    },
                    {
                        "Lat": -37.69446,
                        "Lng": 144.80058
                    },
                    {
                        "Lat": -37.69506,
                        "Lng": 144.80155
                    },
                    {
                        "Lat": -37.69506,
                        "Lng": 144.80155
                    },
                    {
                        "Lat": -37.6953,
                        "Lng": 144.80192
                    },
                    {
                        "Lat": -37.6953,
                        "Lng": 144.80192
                    },
                    {
                        "Lat": -37.69535,
                        "Lng": 144.802
                    },
                    {
                        "Lat": -37.69535,
                        "Lng": 144.802
                    },
                    {
                        "Lat": -37.69624,
                        "Lng": 144.80343
                    },
                    {
                        "Lat": -37.69624,
                        "Lng": 144.80343
                    },
                    {
                        "Lat": -37.69693,
                        "Lng": 144.80452
                    },
                    {
                        "Lat": -37.69693,
                        "Lng": 144.80452
                    },
                    {
                        "Lat": -37.69807,
                        "Lng": 144.80635
                    },
                    {
                        "Lat": -37.69807,
                        "Lng": 144.80635
                    },
                    {
                        "Lat": -37.69834,
                        "Lng": 144.80676
                    },
                    {
                        "Lat": -37.69834,
                        "Lng": 144.80676
                    },
                    {
                        "Lat": -37.6985,
                        "Lng": 144.80701
                    },
                    {
                        "Lat": -37.6985,
                        "Lng": 144.80701
                    },
                    {
                        "Lat": -37.69898,
                        "Lng": 144.80769
                    },
                    {
                        "Lat": -37.69898,
                        "Lng": 144.80769
                    },
                    {
                        "Lat": -37.69902,
                        "Lng": 144.80775
                    },
                    {
                        "Lat": -37.69902,
                        "Lng": 144.80775
                    },
                    {
                        "Lat": -37.69929,
                        "Lng": 144.80813
                    },
                    {
                        "Lat": -37.70003,
                        "Lng": 144.80912
                    },
                    {
                        "Lat": -37.70003,
                        "Lng": 144.80912
                    },
                    {
                        "Lat": -37.70104,
                        "Lng": 144.8105
                    },
                    {
                        "Lat": -37.70104,
                        "Lng": 144.8105
                    },
                    {
                        "Lat": -37.70224,
                        "Lng": 144.81212
                    },
                    {
                        "Lat": -37.70224,
                        "Lng": 144.81212
                    },
                    {
                        "Lat": -37.7036,
                        "Lng": 144.81395
                    },
                    {
                        "Lat": -37.7036,
                        "Lng": 144.81395
                    },
                    {
                        "Lat": -37.7058,
                        "Lng": 144.81693
                    },
                    {
                        "Lat": -37.7058,
                        "Lng": 144.81693
                    },
                    {
                        "Lat": -37.70605,
                        "Lng": 144.81727
                    },
                    {
                        "Lat": -37.70678,
                        "Lng": 144.81834
                    },
                    {
                        "Lat": -37.70678,
                        "Lng": 144.81834
                    },
                    {
                        "Lat": -37.70718,
                        "Lng": 144.81902
                    },
                    {
                        "Lat": -37.70718,
                        "Lng": 144.81902
                    },
                    {
                        "Lat": -37.7073,
                        "Lng": 144.81925
                    },
                    {
                        "Lat": -37.7073,
                        "Lng": 144.81925
                    },
                    {
                        "Lat": -37.70742,
                        "Lng": 144.81946
                    },
                    {
                        "Lat": -37.70742,
                        "Lng": 144.81946
                    },
                    {
                        "Lat": -37.7079,
                        "Lng": 144.82043
                    },
                    {
                        "Lat": -37.7079,
                        "Lng": 144.82043
                    },
                    {
                        "Lat": -37.70829,
                        "Lng": 144.82131
                    },
                    {
                        "Lat": -37.70829,
                        "Lng": 144.82131
                    },
                    {
                        "Lat": -37.70842,
                        "Lng": 144.82164
                    },
                    {
                        "Lat": -37.70842,
                        "Lng": 144.82164
                    },
                    {
                        "Lat": -37.70859,
                        "Lng": 144.82207
                    },
                    {
                        "Lat": -37.70859,
                        "Lng": 144.82207
                    },
                    {
                        "Lat": -37.70916,
                        "Lng": 144.82355
                    },
                    {
                        "Lat": -37.70954,
                        "Lng": 144.82458
                    },
                    {
                        "Lat": -37.70954,
                        "Lng": 144.82458
                    },
                    {
                        "Lat": -37.70998,
                        "Lng": 144.82575
                    },
                    {
                        "Lat": -37.70998,
                        "Lng": 144.82575
                    },
                    {
                        "Lat": -37.71005,
                        "Lng": 144.82593
                    },
                    {
                        "Lat": -37.71005,
                        "Lng": 144.82593
                    },
                    {
                        "Lat": -37.7102,
                        "Lng": 144.82634
                    },
                    {
                        "Lat": -37.7102,
                        "Lng": 144.82634
                    },
                    {
                        "Lat": -37.71054,
                        "Lng": 144.8272
                    },
                    {
                        "Lat": -37.71054,
                        "Lng": 144.8272
                    },
                    {
                        "Lat": -37.71085,
                        "Lng": 144.82801
                    },
                    {
                        "Lat": -37.71085,
                        "Lng": 144.82801
                    },
                    {
                        "Lat": -37.71122,
                        "Lng": 144.829
                    },
                    {
                        "Lat": -37.71154,
                        "Lng": 144.8298
                    },
                    {
                        "Lat": -37.71154,
                        "Lng": 144.8298
                    },
                    {
                        "Lat": -37.71163,
                        "Lng": 144.83001
                    },
                    {
                        "Lat": -37.71163,
                        "Lng": 144.83001
                    },
                    {
                        "Lat": -37.71182,
                        "Lng": 144.83043
                    },
                    {
                        "Lat": -37.71182,
                        "Lng": 144.83043
                    },
                    {
                        "Lat": -37.71201,
                        "Lng": 144.83083
                    },
                    {
                        "Lat": -37.71201,
                        "Lng": 144.83083
                    },
                    {
                        "Lat": -37.71213,
                        "Lng": 144.83108
                    },
                    {
                        "Lat": -37.71213,
                        "Lng": 144.83108
                    },
                    {
                        "Lat": -37.71219,
                        "Lng": 144.8312
                    },
                    {
                        "Lat": -37.71219,
                        "Lng": 144.8312
                    },
                    {
                        "Lat": -37.71228,
                        "Lng": 144.83138
                    },
                    {
                        "Lat": -37.71228,
                        "Lng": 144.83138
                    },
                    {
                        "Lat": -37.71269,
                        "Lng": 144.83218
                    },
                    {
                        "Lat": -37.71269,
                        "Lng": 144.83218
                    },
                    {
                        "Lat": -37.71304,
                        "Lng": 144.83278
                    },
                    {
                        "Lat": -37.71375,
                        "Lng": 144.83387
                    },
                    {
                        "Lat": -37.71375,
                        "Lng": 144.83387
                    },
                    {
                        "Lat": -37.71431,
                        "Lng": 144.83463
                    },
                    {
                        "Lat": -37.71431,
                        "Lng": 144.83463
                    },
                    {
                        "Lat": -37.71549,
                        "Lng": 144.83602
                    },
                    {
                        "Lat": -37.71549,
                        "Lng": 144.83602
                    },
                    {
                        "Lat": -37.71612,
                        "Lng": 144.83664
                    },
                    {
                        "Lat": -37.71612,
                        "Lng": 144.83664
                    },
                    {
                        "Lat": -37.71693,
                        "Lng": 144.83742
                    },
                    {
                        "Lat": -37.71693,
                        "Lng": 144.83742
                    },
                    {
                        "Lat": -37.71723,
                        "Lng": 144.83768
                    },
                    {
                        "Lat": -37.71723,
                        "Lng": 144.83768
                    },
                    {
                        "Lat": -37.71747,
                        "Lng": 144.83791
                    },
                    {
                        "Lat": -37.71747,
                        "Lng": 144.83791
                    },
                    {
                        "Lat": -37.71837,
                        "Lng": 144.83874
                    },
                    {
                        "Lat": -37.71837,
                        "Lng": 144.83874
                    },
                    {
                        "Lat": -37.71871,
                        "Lng": 144.83905
                    },
                    {
                        "Lat": -37.71871,
                        "Lng": 144.83905
                    },
                    {
                        "Lat": -37.71982,
                        "Lng": 144.84008
                    },
                    {
                        "Lat": -37.71982,
                        "Lng": 144.84008
                    },
                    {
                        "Lat": -37.72,
                        "Lng": 144.84024
                    },
                    {
                        "Lat": -37.72,
                        "Lng": 144.84024
                    },
                    {
                        "Lat": -37.72035,
                        "Lng": 144.84057
                    },
                    {
                        "Lat": -37.72035,
                        "Lng": 144.84057
                    },
                    {
                        "Lat": -37.72055,
                        "Lng": 144.84074
                    },
                    {
                        "Lat": -37.72055,
                        "Lng": 144.84074
                    },
                    {
                        "Lat": -37.72085,
                        "Lng": 144.84102
                    },
                    {
                        "Lat": -37.72085,
                        "Lng": 144.84102
                    },
                    {
                        "Lat": -37.72153,
                        "Lng": 144.84166
                    },
                    {
                        "Lat": -37.72153,
                        "Lng": 144.84166
                    },
                    {
                        "Lat": -37.72174,
                        "Lng": 144.84188
                    }
                ]
            },
            {
                "Key": "inc-mel-03",
                "Priority": 4,
                "Headline": "Donnybrook Road — roadworks",
                "Detail": "Between Donnybrook Road and Epping-Kilmore Road - Maintenance work",
                "FunctionalClass": 1,
                "LengthMeters": 7851,
                "Latitude": -37.45584,
                "Longitude": 144.97487,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.45584,
                        "Lng": 144.97487
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45033,
                        "Lng": 144.97875
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44866,
                        "Lng": 144.97978
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44755,
                        "Lng": 144.98031
                    },
                    {
                        "Lat": -37.44701,
                        "Lng": 144.98052
                    },
                    {
                        "Lat": -37.44628,
                        "Lng": 144.98077
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44501,
                        "Lng": 144.9811
                    },
                    {
                        "Lat": -37.44435,
                        "Lng": 144.98122
                    },
                    {
                        "Lat": -37.44375,
                        "Lng": 144.9813
                    },
                    {
                        "Lat": -37.44315,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44281,
                        "Lng": 144.98136
                    },
                    {
                        "Lat": -37.44159,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.43919,
                        "Lng": 144.98124
                    },
                    {
                        "Lat": -37.438,
                        "Lng": 144.98123
                    },
                    {
                        "Lat": -37.43715,
                        "Lng": 144.98127
                    },
                    {
                        "Lat": -37.43657,
                        "Lng": 144.98134
                    },
                    {
                        "Lat": -37.43614,
                        "Lng": 144.9814
                    },
                    {
                        "Lat": -37.43556,
                        "Lng": 144.9815
                    },
                    {
                        "Lat": -37.43459,
                        "Lng": 144.98172
                    },
                    {
                        "Lat": -37.43368,
                        "Lng": 144.98198
                    },
                    {
                        "Lat": -37.43322,
                        "Lng": 144.98214
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.43229,
                        "Lng": 144.9825
                    },
                    {
                        "Lat": -37.4319,
                        "Lng": 144.98267
                    },
                    {
                        "Lat": -37.43116,
                        "Lng": 144.98304
                    },
                    {
                        "Lat": -37.43041,
                        "Lng": 144.98344
                    },
                    {
                        "Lat": -37.43019,
                        "Lng": 144.98357
                    },
                    {
                        "Lat": -37.42888,
                        "Lng": 144.98443
                    },
                    {
                        "Lat": -37.4279,
                        "Lng": 144.98518
                    },
                    {
                        "Lat": -37.42706,
                        "Lng": 144.9859
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41668,
                        "Lng": 144.99576
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41541,
                        "Lng": 144.99712
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41406,
                        "Lng": 144.99881
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41272,
                        "Lng": 145.0008
                    },
                    {
                        "Lat": -37.41211,
                        "Lng": 145.00186
                    },
                    {
                        "Lat": -37.41147,
                        "Lng": 145.0031
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41055,
                        "Lng": 145.00508
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.40954,
                        "Lng": 145.0073
                    },
                    {
                        "Lat": -37.40935,
                        "Lng": 145.00768
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40869,
                        "Lng": 145.00889
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40781,
                        "Lng": 145.01033
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40627,
                        "Lng": 145.01245
                    },
                    {
                        "Lat": -37.40601,
                        "Lng": 145.01277
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40523,
                        "Lng": 145.01366
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.4041,
                        "Lng": 145.01482
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.39825,
                        "Lng": 145.02054
                    }
                ]
            },
            {
                "Key": "inc-mel-04",
                "Priority": 4,
                "Headline": "Toorak Road — roadworks",
                "Detail": "Between Toorak Road and Warrigal Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 5806,
                "Latitude": -37.84796,
                "Longitude": 145.0429,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.84796,
                        "Lng": 145.0429
                    },
                    {
                        "Lat": -37.84817,
                        "Lng": 145.04351
                    },
                    {
                        "Lat": -37.84831,
                        "Lng": 145.04408
                    },
                    {
                        "Lat": -37.84831,
                        "Lng": 145.04408
                    },
                    {
                        "Lat": -37.8484,
                        "Lng": 145.04448
                    },
                    {
                        "Lat": -37.8484,
                        "Lng": 145.04448
                    },
                    {
                        "Lat": -37.8486,
                        "Lng": 145.04544
                    },
                    {
                        "Lat": -37.8486,
                        "Lng": 145.04544
                    },
                    {
                        "Lat": -37.84872,
                        "Lng": 145.04597
                    },
                    {
                        "Lat": -37.84872,
                        "Lng": 145.04597
                    },
                    {
                        "Lat": -37.84898,
                        "Lng": 145.04693
                    },
                    {
                        "Lat": -37.84898,
                        "Lng": 145.04693
                    },
                    {
                        "Lat": -37.84921,
                        "Lng": 145.04759
                    },
                    {
                        "Lat": -37.84921,
                        "Lng": 145.04759
                    },
                    {
                        "Lat": -37.84944,
                        "Lng": 145.04812
                    },
                    {
                        "Lat": -37.84944,
                        "Lng": 145.04812
                    },
                    {
                        "Lat": -37.84963,
                        "Lng": 145.0485
                    },
                    {
                        "Lat": -37.84983,
                        "Lng": 145.04886
                    },
                    {
                        "Lat": -37.84996,
                        "Lng": 145.04908
                    },
                    {
                        "Lat": -37.85035,
                        "Lng": 145.04967
                    },
                    {
                        "Lat": -37.85035,
                        "Lng": 145.04967
                    },
                    {
                        "Lat": -37.85116,
                        "Lng": 145.05086
                    },
                    {
                        "Lat": -37.85116,
                        "Lng": 145.05086
                    },
                    {
                        "Lat": -37.85141,
                        "Lng": 145.0513
                    },
                    {
                        "Lat": -37.85161,
                        "Lng": 145.05169
                    },
                    {
                        "Lat": -37.85189,
                        "Lng": 145.05234
                    },
                    {
                        "Lat": -37.85189,
                        "Lng": 145.05234
                    },
                    {
                        "Lat": -37.85198,
                        "Lng": 145.05258
                    },
                    {
                        "Lat": -37.85198,
                        "Lng": 145.05258
                    },
                    {
                        "Lat": -37.85244,
                        "Lng": 145.05393
                    },
                    {
                        "Lat": -37.85271,
                        "Lng": 145.05462
                    },
                    {
                        "Lat": -37.85271,
                        "Lng": 145.05462
                    },
                    {
                        "Lat": -37.85283,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.85283,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.85312,
                        "Lng": 145.05546
                    },
                    {
                        "Lat": -37.85343,
                        "Lng": 145.05595
                    },
                    {
                        "Lat": -37.85343,
                        "Lng": 145.05595
                    },
                    {
                        "Lat": -37.85354,
                        "Lng": 145.0561
                    },
                    {
                        "Lat": -37.85354,
                        "Lng": 145.0561
                    },
                    {
                        "Lat": -37.85364,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.85364,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.85373,
                        "Lng": 145.05634
                    },
                    {
                        "Lat": -37.85373,
                        "Lng": 145.05634
                    },
                    {
                        "Lat": -37.85385,
                        "Lng": 145.05648
                    },
                    {
                        "Lat": -37.85385,
                        "Lng": 145.05648
                    },
                    {
                        "Lat": -37.85403,
                        "Lng": 145.05668
                    },
                    {
                        "Lat": -37.85403,
                        "Lng": 145.05668
                    },
                    {
                        "Lat": -37.85449,
                        "Lng": 145.05713
                    },
                    {
                        "Lat": -37.85449,
                        "Lng": 145.05713
                    },
                    {
                        "Lat": -37.85481,
                        "Lng": 145.0574
                    },
                    {
                        "Lat": -37.85481,
                        "Lng": 145.0574
                    },
                    {
                        "Lat": -37.855,
                        "Lng": 145.05754
                    },
                    {
                        "Lat": -37.855,
                        "Lng": 145.05754
                    },
                    {
                        "Lat": -37.85529,
                        "Lng": 145.05775
                    },
                    {
                        "Lat": -37.85579,
                        "Lng": 145.05806
                    },
                    {
                        "Lat": -37.85579,
                        "Lng": 145.05806
                    },
                    {
                        "Lat": -37.85624,
                        "Lng": 145.05829
                    },
                    {
                        "Lat": -37.85663,
                        "Lng": 145.05846
                    },
                    {
                        "Lat": -37.85663,
                        "Lng": 145.05846
                    },
                    {
                        "Lat": -37.85698,
                        "Lng": 145.05858
                    },
                    {
                        "Lat": -37.85916,
                        "Lng": 145.05922
                    },
                    {
                        "Lat": -37.85916,
                        "Lng": 145.05922
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05947
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05947
                    },
                    {
                        "Lat": -37.86096,
                        "Lng": 145.05974
                    },
                    {
                        "Lat": -37.86096,
                        "Lng": 145.05974
                    },
                    {
                        "Lat": -37.86173,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.86173,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.86363,
                        "Lng": 145.06051
                    },
                    {
                        "Lat": -37.86363,
                        "Lng": 145.06051
                    },
                    {
                        "Lat": -37.86514,
                        "Lng": 145.06095
                    },
                    {
                        "Lat": -37.86554,
                        "Lng": 145.06108
                    },
                    {
                        "Lat": -37.86583,
                        "Lng": 145.06119
                    },
                    {
                        "Lat": -37.86583,
                        "Lng": 145.06119
                    },
                    {
                        "Lat": -37.86615,
                        "Lng": 145.06133
                    },
                    {
                        "Lat": -37.86652,
                        "Lng": 145.06153
                    },
                    {
                        "Lat": -37.86671,
                        "Lng": 145.06165
                    },
                    {
                        "Lat": -37.86704,
                        "Lng": 145.06189
                    },
                    {
                        "Lat": -37.86741,
                        "Lng": 145.0622
                    },
                    {
                        "Lat": -37.86741,
                        "Lng": 145.0622
                    },
                    {
                        "Lat": -37.86879,
                        "Lng": 145.06346
                    },
                    {
                        "Lat": -37.86879,
                        "Lng": 145.06346
                    },
                    {
                        "Lat": -37.86912,
                        "Lng": 145.06375
                    },
                    {
                        "Lat": -37.86943,
                        "Lng": 145.064
                    },
                    {
                        "Lat": -37.86966,
                        "Lng": 145.06417
                    },
                    {
                        "Lat": -37.87016,
                        "Lng": 145.0645
                    },
                    {
                        "Lat": -37.8706,
                        "Lng": 145.06475
                    },
                    {
                        "Lat": -37.87091,
                        "Lng": 145.06491
                    },
                    {
                        "Lat": -37.87123,
                        "Lng": 145.06506
                    },
                    {
                        "Lat": -37.87159,
                        "Lng": 145.06521
                    },
                    {
                        "Lat": -37.87159,
                        "Lng": 145.06521
                    },
                    {
                        "Lat": -37.87296,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.87342,
                        "Lng": 145.06601
                    },
                    {
                        "Lat": -37.87342,
                        "Lng": 145.06601
                    },
                    {
                        "Lat": -37.87379,
                        "Lng": 145.06619
                    },
                    {
                        "Lat": -37.87403,
                        "Lng": 145.06633
                    },
                    {
                        "Lat": -37.87429,
                        "Lng": 145.0665
                    },
                    {
                        "Lat": -37.87472,
                        "Lng": 145.06684
                    },
                    {
                        "Lat": -37.87506,
                        "Lng": 145.06716
                    },
                    {
                        "Lat": -37.87506,
                        "Lng": 145.06716
                    },
                    {
                        "Lat": -37.87553,
                        "Lng": 145.06769
                    },
                    {
                        "Lat": -37.87583,
                        "Lng": 145.06811
                    },
                    {
                        "Lat": -37.87611,
                        "Lng": 145.06857
                    },
                    {
                        "Lat": -37.87635,
                        "Lng": 145.06905
                    },
                    {
                        "Lat": -37.87647,
                        "Lng": 145.06932
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.06985
                    },
                    {
                        "Lat": -37.87677,
                        "Lng": 145.07022
                    },
                    {
                        "Lat": -37.87677,
                        "Lng": 145.07022
                    },
                    {
                        "Lat": -37.87691,
                        "Lng": 145.07083
                    },
                    {
                        "Lat": -37.87699,
                        "Lng": 145.07133
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07166
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.07226
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.07254
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07312
                    },
                    {
                        "Lat": -37.87688,
                        "Lng": 145.07487
                    },
                    {
                        "Lat": -37.87688,
                        "Lng": 145.07487
                    },
                    {
                        "Lat": -37.87669,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87669,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.07729
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.07729
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.07767
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.0779
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.0779
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.07811
                    },
                    {
                        "Lat": -37.87667,
                        "Lng": 145.07852
                    },
                    {
                        "Lat": -37.87676,
                        "Lng": 145.07915
                    },
                    {
                        "Lat": -37.87676,
                        "Lng": 145.07915
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07946
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07946
                    },
                    {
                        "Lat": -37.87691,
                        "Lng": 145.07982
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.08027
                    },
                    {
                        "Lat": -37.87715,
                        "Lng": 145.0805
                    },
                    {
                        "Lat": -37.87727,
                        "Lng": 145.08079
                    },
                    {
                        "Lat": -37.87769,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87769,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87794,
                        "Lng": 145.08217
                    },
                    {
                        "Lat": -37.87794,
                        "Lng": 145.08217
                    },
                    {
                        "Lat": -37.87871,
                        "Lng": 145.08366
                    },
                    {
                        "Lat": -37.87871,
                        "Lng": 145.08366
                    },
                    {
                        "Lat": -37.87914,
                        "Lng": 145.08446
                    },
                    {
                        "Lat": -37.87952,
                        "Lng": 145.08512
                    },
                    {
                        "Lat": -37.87952,
                        "Lng": 145.08512
                    },
                    {
                        "Lat": -37.87995,
                        "Lng": 145.08588
                    },
                    {
                        "Lat": -37.88015,
                        "Lng": 145.0862
                    },
                    {
                        "Lat": -37.88015,
                        "Lng": 145.0862
                    },
                    {
                        "Lat": -37.88035,
                        "Lng": 145.08654
                    }
                ]
            },
            {
                "Key": "inc-mel-05",
                "Priority": 4,
                "Headline": "Huntingdale Road — roadworks",
                "Detail": "Between Huntingdale Road and Burke Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 4764,
                "Latitude": -37.88206,
                "Longitude": 145.08889,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.88206,
                        "Lng": 145.08889
                    },
                    {
                        "Lat": -37.88144,
                        "Lng": 145.08799
                    },
                    {
                        "Lat": -37.88095,
                        "Lng": 145.08724
                    },
                    {
                        "Lat": -37.88048,
                        "Lng": 145.08649
                    },
                    {
                        "Lat": -37.88048,
                        "Lng": 145.08649
                    },
                    {
                        "Lat": -37.88012,
                        "Lng": 145.08582
                    },
                    {
                        "Lat": -37.88012,
                        "Lng": 145.08582
                    },
                    {
                        "Lat": -37.87983,
                        "Lng": 145.08535
                    },
                    {
                        "Lat": -37.87954,
                        "Lng": 145.08485
                    },
                    {
                        "Lat": -37.87887,
                        "Lng": 145.08363
                    },
                    {
                        "Lat": -37.87887,
                        "Lng": 145.08363
                    },
                    {
                        "Lat": -37.87805,
                        "Lng": 145.08203
                    },
                    {
                        "Lat": -37.87805,
                        "Lng": 145.08203
                    },
                    {
                        "Lat": -37.87786,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87786,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87776,
                        "Lng": 145.08145
                    },
                    {
                        "Lat": -37.87776,
                        "Lng": 145.08145
                    },
                    {
                        "Lat": -37.87741,
                        "Lng": 145.08069
                    },
                    {
                        "Lat": -37.87721,
                        "Lng": 145.08018
                    },
                    {
                        "Lat": -37.87701,
                        "Lng": 145.0795
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07906
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07906
                    },
                    {
                        "Lat": -37.87687,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.87687,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07821
                    },
                    {
                        "Lat": -37.87681,
                        "Lng": 145.07792
                    },
                    {
                        "Lat": -37.87681,
                        "Lng": 145.07792
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07734
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07734
                    },
                    {
                        "Lat": -37.87685,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87685,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87704,
                        "Lng": 145.07472
                    },
                    {
                        "Lat": -37.8772,
                        "Lng": 145.07302
                    },
                    {
                        "Lat": -37.87722,
                        "Lng": 145.07261
                    },
                    {
                        "Lat": -37.87721,
                        "Lng": 145.07198
                    },
                    {
                        "Lat": -37.87715,
                        "Lng": 145.07128
                    },
                    {
                        "Lat": -37.87708,
                        "Lng": 145.0708
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07055
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07013
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07013
                    },
                    {
                        "Lat": -37.87675,
                        "Lng": 145.06957
                    },
                    {
                        "Lat": -37.87663,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.8764,
                        "Lng": 145.06874
                    },
                    {
                        "Lat": -37.87613,
                        "Lng": 145.06825
                    },
                    {
                        "Lat": -37.87597,
                        "Lng": 145.068
                    },
                    {
                        "Lat": -37.8757,
                        "Lng": 145.06762
                    },
                    {
                        "Lat": -37.87533,
                        "Lng": 145.06718
                    },
                    {
                        "Lat": -37.87512,
                        "Lng": 145.06695
                    },
                    {
                        "Lat": -37.87487,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.87469,
                        "Lng": 145.06656
                    },
                    {
                        "Lat": -37.87431,
                        "Lng": 145.06629
                    },
                    {
                        "Lat": -37.87401,
                        "Lng": 145.0661
                    },
                    {
                        "Lat": -37.87351,
                        "Lng": 145.06583
                    },
                    {
                        "Lat": -37.87351,
                        "Lng": 145.06583
                    },
                    {
                        "Lat": -37.87316,
                        "Lng": 145.06567
                    },
                    {
                        "Lat": -37.87261,
                        "Lng": 145.06545
                    },
                    {
                        "Lat": -37.87261,
                        "Lng": 145.06545
                    },
                    {
                        "Lat": -37.87118,
                        "Lng": 145.06483
                    },
                    {
                        "Lat": -37.87082,
                        "Lng": 145.06466
                    },
                    {
                        "Lat": -37.87056,
                        "Lng": 145.06452
                    },
                    {
                        "Lat": -37.87013,
                        "Lng": 145.06427
                    },
                    {
                        "Lat": -37.8697,
                        "Lng": 145.06398
                    },
                    {
                        "Lat": -37.86928,
                        "Lng": 145.06366
                    },
                    {
                        "Lat": -37.86898,
                        "Lng": 145.0634
                    },
                    {
                        "Lat": -37.86898,
                        "Lng": 145.0634
                    },
                    {
                        "Lat": -37.86775,
                        "Lng": 145.06226
                    },
                    {
                        "Lat": -37.8674,
                        "Lng": 145.06196
                    },
                    {
                        "Lat": -37.8674,
                        "Lng": 145.06196
                    },
                    {
                        "Lat": -37.86719,
                        "Lng": 145.06178
                    },
                    {
                        "Lat": -37.86674,
                        "Lng": 145.06144
                    },
                    {
                        "Lat": -37.86655,
                        "Lng": 145.06131
                    },
                    {
                        "Lat": -37.86619,
                        "Lng": 145.06111
                    },
                    {
                        "Lat": -37.86599,
                        "Lng": 145.06102
                    },
                    {
                        "Lat": -37.86572,
                        "Lng": 145.06092
                    },
                    {
                        "Lat": -37.86523,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.86458,
                        "Lng": 145.06059
                    },
                    {
                        "Lat": -37.86458,
                        "Lng": 145.06059
                    },
                    {
                        "Lat": -37.86097,
                        "Lng": 145.05954
                    },
                    {
                        "Lat": -37.86097,
                        "Lng": 145.05954
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05927
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05927
                    },
                    {
                        "Lat": -37.85921,
                        "Lng": 145.05903
                    },
                    {
                        "Lat": -37.85921,
                        "Lng": 145.05903
                    },
                    {
                        "Lat": -37.85804,
                        "Lng": 145.05869
                    },
                    {
                        "Lat": -37.85804,
                        "Lng": 145.05869
                    },
                    {
                        "Lat": -37.85783,
                        "Lng": 145.05863
                    },
                    {
                        "Lat": -37.85783,
                        "Lng": 145.05863
                    },
                    {
                        "Lat": -37.85723,
                        "Lng": 145.05844
                    },
                    {
                        "Lat": -37.85666,
                        "Lng": 145.05823
                    },
                    {
                        "Lat": -37.85666,
                        "Lng": 145.05823
                    },
                    {
                        "Lat": -37.85612,
                        "Lng": 145.05799
                    },
                    {
                        "Lat": -37.85589,
                        "Lng": 145.05787
                    },
                    {
                        "Lat": -37.85589,
                        "Lng": 145.05787
                    },
                    {
                        "Lat": -37.8555,
                        "Lng": 145.05763
                    },
                    {
                        "Lat": -37.8551,
                        "Lng": 145.05735
                    },
                    {
                        "Lat": -37.8551,
                        "Lng": 145.05735
                    },
                    {
                        "Lat": -37.85486,
                        "Lng": 145.05716
                    },
                    {
                        "Lat": -37.85461,
                        "Lng": 145.05694
                    },
                    {
                        "Lat": -37.85461,
                        "Lng": 145.05694
                    },
                    {
                        "Lat": -37.85417,
                        "Lng": 145.0565
                    },
                    {
                        "Lat": -37.85417,
                        "Lng": 145.0565
                    },
                    {
                        "Lat": -37.85396,
                        "Lng": 145.05626
                    },
                    {
                        "Lat": -37.85396,
                        "Lng": 145.05626
                    },
                    {
                        "Lat": -37.8538,
                        "Lng": 145.05607
                    },
                    {
                        "Lat": -37.8538,
                        "Lng": 145.05607
                    },
                    {
                        "Lat": -37.8537,
                        "Lng": 145.05591
                    }
                ]
            },
            {
                "Key": "inc-mel-06",
                "Priority": 4,
                "Headline": "Northern Highway — roadworks",
                "Detail": "At Northern Highway - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 4300,
                "Latitude": -37.51165,
                "Longitude": 144.9498,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.51165,
                        "Lng": 144.9498
                    },
                    {
                        "Lat": -37.51122,
                        "Lng": 144.94987
                    },
                    {
                        "Lat": -37.51122,
                        "Lng": 144.94987
                    },
                    {
                        "Lat": -37.51115,
                        "Lng": 144.94988
                    },
                    {
                        "Lat": -37.51115,
                        "Lng": 144.94988
                    },
                    {
                        "Lat": -37.5102,
                        "Lng": 144.95004
                    },
                    {
                        "Lat": -37.5102,
                        "Lng": 144.95004
                    },
                    {
                        "Lat": -37.50525,
                        "Lng": 144.95085
                    },
                    {
                        "Lat": -37.50497,
                        "Lng": 144.95091
                    },
                    {
                        "Lat": -37.50451,
                        "Lng": 144.95103
                    },
                    {
                        "Lat": -37.50412,
                        "Lng": 144.95116
                    },
                    {
                        "Lat": -37.50368,
                        "Lng": 144.95134
                    },
                    {
                        "Lat": -37.50324,
                        "Lng": 144.95157
                    },
                    {
                        "Lat": -37.50294,
                        "Lng": 144.95174
                    },
                    {
                        "Lat": -37.50259,
                        "Lng": 144.95197
                    },
                    {
                        "Lat": -37.50205,
                        "Lng": 144.95238
                    },
                    {
                        "Lat": -37.50165,
                        "Lng": 144.95275
                    },
                    {
                        "Lat": -37.50135,
                        "Lng": 144.95306
                    },
                    {
                        "Lat": -37.50135,
                        "Lng": 144.95306
                    },
                    {
                        "Lat": -37.50104,
                        "Lng": 144.95338
                    },
                    {
                        "Lat": -37.5001,
                        "Lng": 144.95445
                    },
                    {
                        "Lat": -37.5001,
                        "Lng": 144.95445
                    },
                    {
                        "Lat": -37.49905,
                        "Lng": 144.95565
                    },
                    {
                        "Lat": -37.49905,
                        "Lng": 144.95565
                    },
                    {
                        "Lat": -37.49881,
                        "Lng": 144.95593
                    },
                    {
                        "Lat": -37.49881,
                        "Lng": 144.95593
                    },
                    {
                        "Lat": -37.49865,
                        "Lng": 144.95611
                    },
                    {
                        "Lat": -37.49865,
                        "Lng": 144.95611
                    },
                    {
                        "Lat": -37.49821,
                        "Lng": 144.95661
                    },
                    {
                        "Lat": -37.49821,
                        "Lng": 144.95661
                    },
                    {
                        "Lat": -37.49784,
                        "Lng": 144.95703
                    },
                    {
                        "Lat": -37.49784,
                        "Lng": 144.95703
                    },
                    {
                        "Lat": -37.49763,
                        "Lng": 144.95726
                    },
                    {
                        "Lat": -37.49763,
                        "Lng": 144.95726
                    },
                    {
                        "Lat": -37.49711,
                        "Lng": 144.95788
                    },
                    {
                        "Lat": -37.49711,
                        "Lng": 144.95788
                    },
                    {
                        "Lat": -37.49572,
                        "Lng": 144.95947
                    },
                    {
                        "Lat": -37.49572,
                        "Lng": 144.95947
                    },
                    {
                        "Lat": -37.4954,
                        "Lng": 144.95983
                    },
                    {
                        "Lat": -37.4954,
                        "Lng": 144.95983
                    },
                    {
                        "Lat": -37.49488,
                        "Lng": 144.96041
                    },
                    {
                        "Lat": -37.49488,
                        "Lng": 144.96041
                    },
                    {
                        "Lat": -37.4944,
                        "Lng": 144.96096
                    },
                    {
                        "Lat": -37.4944,
                        "Lng": 144.96096
                    },
                    {
                        "Lat": -37.49269,
                        "Lng": 144.96292
                    },
                    {
                        "Lat": -37.49222,
                        "Lng": 144.96343
                    },
                    {
                        "Lat": -37.49171,
                        "Lng": 144.96392
                    },
                    {
                        "Lat": -37.49128,
                        "Lng": 144.96429
                    },
                    {
                        "Lat": -37.49075,
                        "Lng": 144.96469
                    },
                    {
                        "Lat": -37.49027,
                        "Lng": 144.96502
                    },
                    {
                        "Lat": -37.48943,
                        "Lng": 144.96551
                    },
                    {
                        "Lat": -37.48943,
                        "Lng": 144.96551
                    },
                    {
                        "Lat": -37.48879,
                        "Lng": 144.96584
                    },
                    {
                        "Lat": -37.48879,
                        "Lng": 144.96584
                    },
                    {
                        "Lat": -37.48698,
                        "Lng": 144.96676
                    },
                    {
                        "Lat": -37.48698,
                        "Lng": 144.96676
                    },
                    {
                        "Lat": -37.48689,
                        "Lng": 144.96681
                    },
                    {
                        "Lat": -37.48689,
                        "Lng": 144.96681
                    },
                    {
                        "Lat": -37.48649,
                        "Lng": 144.96702
                    },
                    {
                        "Lat": -37.48649,
                        "Lng": 144.96702
                    },
                    {
                        "Lat": -37.48566,
                        "Lng": 144.96744
                    },
                    {
                        "Lat": -37.48525,
                        "Lng": 144.96767
                    },
                    {
                        "Lat": -37.483,
                        "Lng": 144.96884
                    },
                    {
                        "Lat": -37.483,
                        "Lng": 144.96884
                    },
                    {
                        "Lat": -37.48165,
                        "Lng": 144.96954
                    },
                    {
                        "Lat": -37.48055,
                        "Lng": 144.97013
                    },
                    {
                        "Lat": -37.48055,
                        "Lng": 144.97013
                    },
                    {
                        "Lat": -37.47859,
                        "Lng": 144.97114
                    },
                    {
                        "Lat": -37.47859,
                        "Lng": 144.97114
                    },
                    {
                        "Lat": -37.47788,
                        "Lng": 144.97151
                    }
                ]
            },
            {
                "Key": "inc-mel-07",
                "Priority": 4,
                "Headline": "Gully Road — roadworks",
                "Detail": "Between Green Gully Road and Keilor Park Drive - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 3356,
                "Latitude": -37.72616,
                "Longitude": 144.85332,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.72616,
                        "Lng": 144.85332
                    },
                    {
                        "Lat": -37.72621,
                        "Lng": 144.85393
                    },
                    {
                        "Lat": -37.72621,
                        "Lng": 144.85393
                    },
                    {
                        "Lat": -37.72629,
                        "Lng": 144.85459
                    },
                    {
                        "Lat": -37.72629,
                        "Lng": 144.85459
                    },
                    {
                        "Lat": -37.72639,
                        "Lng": 144.85522
                    },
                    {
                        "Lat": -37.72639,
                        "Lng": 144.85522
                    },
                    {
                        "Lat": -37.72641,
                        "Lng": 144.85537
                    },
                    {
                        "Lat": -37.72641,
                        "Lng": 144.85537
                    },
                    {
                        "Lat": -37.72656,
                        "Lng": 144.85619
                    },
                    {
                        "Lat": -37.72666,
                        "Lng": 144.85667
                    },
                    {
                        "Lat": -37.72666,
                        "Lng": 144.85667
                    },
                    {
                        "Lat": -37.72672,
                        "Lng": 144.85698
                    },
                    {
                        "Lat": -37.72672,
                        "Lng": 144.85698
                    },
                    {
                        "Lat": -37.72676,
                        "Lng": 144.8572
                    },
                    {
                        "Lat": -37.72676,
                        "Lng": 144.8572
                    },
                    {
                        "Lat": -37.72704,
                        "Lng": 144.85856
                    },
                    {
                        "Lat": -37.72704,
                        "Lng": 144.85856
                    },
                    {
                        "Lat": -37.72714,
                        "Lng": 144.85908
                    },
                    {
                        "Lat": -37.72714,
                        "Lng": 144.85908
                    },
                    {
                        "Lat": -37.7273,
                        "Lng": 144.85989
                    },
                    {
                        "Lat": -37.7273,
                        "Lng": 144.85989
                    },
                    {
                        "Lat": -37.72743,
                        "Lng": 144.86057
                    },
                    {
                        "Lat": -37.72743,
                        "Lng": 144.86057
                    },
                    {
                        "Lat": -37.72768,
                        "Lng": 144.86185
                    },
                    {
                        "Lat": -37.72768,
                        "Lng": 144.86185
                    },
                    {
                        "Lat": -37.72777,
                        "Lng": 144.86229
                    },
                    {
                        "Lat": -37.72777,
                        "Lng": 144.86229
                    },
                    {
                        "Lat": -37.72803,
                        "Lng": 144.86356
                    },
                    {
                        "Lat": -37.72803,
                        "Lng": 144.86356
                    },
                    {
                        "Lat": -37.7281,
                        "Lng": 144.86393
                    },
                    {
                        "Lat": -37.7281,
                        "Lng": 144.86393
                    },
                    {
                        "Lat": -37.7282,
                        "Lng": 144.8644
                    },
                    {
                        "Lat": -37.7282,
                        "Lng": 144.8644
                    },
                    {
                        "Lat": -37.72828,
                        "Lng": 144.86477
                    },
                    {
                        "Lat": -37.72828,
                        "Lng": 144.86477
                    },
                    {
                        "Lat": -37.72859,
                        "Lng": 144.86621
                    },
                    {
                        "Lat": -37.72859,
                        "Lng": 144.86621
                    },
                    {
                        "Lat": -37.72878,
                        "Lng": 144.86712
                    },
                    {
                        "Lat": -37.72878,
                        "Lng": 144.86712
                    },
                    {
                        "Lat": -37.72883,
                        "Lng": 144.8674
                    },
                    {
                        "Lat": -37.72883,
                        "Lng": 144.8674
                    },
                    {
                        "Lat": -37.72897,
                        "Lng": 144.86818
                    },
                    {
                        "Lat": -37.72897,
                        "Lng": 144.86818
                    },
                    {
                        "Lat": -37.72914,
                        "Lng": 144.86906
                    },
                    {
                        "Lat": -37.72914,
                        "Lng": 144.86906
                    },
                    {
                        "Lat": -37.72926,
                        "Lng": 144.86965
                    },
                    {
                        "Lat": -37.72926,
                        "Lng": 144.86965
                    },
                    {
                        "Lat": -37.72956,
                        "Lng": 144.871
                    },
                    {
                        "Lat": -37.72977,
                        "Lng": 144.87187
                    },
                    {
                        "Lat": -37.72977,
                        "Lng": 144.87187
                    },
                    {
                        "Lat": -37.72991,
                        "Lng": 144.87246
                    },
                    {
                        "Lat": -37.72991,
                        "Lng": 144.87246
                    },
                    {
                        "Lat": -37.7301,
                        "Lng": 144.87324
                    },
                    {
                        "Lat": -37.7301,
                        "Lng": 144.87324
                    },
                    {
                        "Lat": -37.73034,
                        "Lng": 144.87421
                    },
                    {
                        "Lat": -37.73034,
                        "Lng": 144.87421
                    },
                    {
                        "Lat": -37.7305,
                        "Lng": 144.87481
                    },
                    {
                        "Lat": -37.7305,
                        "Lng": 144.87481
                    },
                    {
                        "Lat": -37.73063,
                        "Lng": 144.87526
                    },
                    {
                        "Lat": -37.73085,
                        "Lng": 144.87591
                    },
                    {
                        "Lat": -37.73085,
                        "Lng": 144.87591
                    },
                    {
                        "Lat": -37.73094,
                        "Lng": 144.87616
                    },
                    {
                        "Lat": -37.73094,
                        "Lng": 144.87616
                    },
                    {
                        "Lat": -37.73151,
                        "Lng": 144.87765
                    },
                    {
                        "Lat": -37.73151,
                        "Lng": 144.87765
                    },
                    {
                        "Lat": -37.73187,
                        "Lng": 144.8786
                    },
                    {
                        "Lat": -37.73187,
                        "Lng": 144.8786
                    },
                    {
                        "Lat": -37.73201,
                        "Lng": 144.87896
                    },
                    {
                        "Lat": -37.73201,
                        "Lng": 144.87896
                    },
                    {
                        "Lat": -37.73226,
                        "Lng": 144.87959
                    },
                    {
                        "Lat": -37.73226,
                        "Lng": 144.87959
                    },
                    {
                        "Lat": -37.73264,
                        "Lng": 144.88062
                    },
                    {
                        "Lat": -37.73274,
                        "Lng": 144.88092
                    },
                    {
                        "Lat": -37.73274,
                        "Lng": 144.88092
                    },
                    {
                        "Lat": -37.73298,
                        "Lng": 144.88166
                    },
                    {
                        "Lat": -37.73298,
                        "Lng": 144.88166
                    },
                    {
                        "Lat": -37.73313,
                        "Lng": 144.88218
                    },
                    {
                        "Lat": -37.73313,
                        "Lng": 144.88218
                    },
                    {
                        "Lat": -37.73327,
                        "Lng": 144.8827
                    },
                    {
                        "Lat": -37.73327,
                        "Lng": 144.8827
                    },
                    {
                        "Lat": -37.73339,
                        "Lng": 144.88319
                    },
                    {
                        "Lat": -37.73339,
                        "Lng": 144.88319
                    },
                    {
                        "Lat": -37.73346,
                        "Lng": 144.8835
                    },
                    {
                        "Lat": -37.73346,
                        "Lng": 144.8835
                    },
                    {
                        "Lat": -37.73364,
                        "Lng": 144.88441
                    },
                    {
                        "Lat": -37.73374,
                        "Lng": 144.88504
                    },
                    {
                        "Lat": -37.73374,
                        "Lng": 144.88504
                    },
                    {
                        "Lat": -37.73386,
                        "Lng": 144.88602
                    },
                    {
                        "Lat": -37.73386,
                        "Lng": 144.88602
                    },
                    {
                        "Lat": -37.73394,
                        "Lng": 144.88673
                    },
                    {
                        "Lat": -37.73394,
                        "Lng": 144.88673
                    },
                    {
                        "Lat": -37.73406,
                        "Lng": 144.88789
                    },
                    {
                        "Lat": -37.73406,
                        "Lng": 144.88789
                    },
                    {
                        "Lat": -37.73416,
                        "Lng": 144.88884
                    },
                    {
                        "Lat": -37.73416,
                        "Lng": 144.88884
                    },
                    {
                        "Lat": -37.73431,
                        "Lng": 144.8903
                    }
                ]
            },
            {
                "Key": "inc-mel-08",
                "Priority": 4,
                "Headline": "Tullamarine Freeway — roadworks",
                "Detail": "Between Bulla Road and Tullamarine Freeway - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 3239,
                "Latitude": -37.73452,
                "Longitude": 144.89081,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.73452,
                        "Lng": 144.89081
                    },
                    {
                        "Lat": -37.73443,
                        "Lng": 144.89
                    },
                    {
                        "Lat": -37.73443,
                        "Lng": 144.89
                    },
                    {
                        "Lat": -37.73436,
                        "Lng": 144.88931
                    },
                    {
                        "Lat": -37.73436,
                        "Lng": 144.88931
                    },
                    {
                        "Lat": -37.73421,
                        "Lng": 144.88793
                    },
                    {
                        "Lat": -37.73421,
                        "Lng": 144.88793
                    },
                    {
                        "Lat": -37.73408,
                        "Lng": 144.88673
                    },
                    {
                        "Lat": -37.73408,
                        "Lng": 144.88673
                    },
                    {
                        "Lat": -37.73401,
                        "Lng": 144.88607
                    },
                    {
                        "Lat": -37.73401,
                        "Lng": 144.88607
                    },
                    {
                        "Lat": -37.7339,
                        "Lng": 144.88515
                    },
                    {
                        "Lat": -37.73375,
                        "Lng": 144.88423
                    },
                    {
                        "Lat": -37.73363,
                        "Lng": 144.88362
                    },
                    {
                        "Lat": -37.73363,
                        "Lng": 144.88362
                    },
                    {
                        "Lat": -37.73351,
                        "Lng": 144.88305
                    },
                    {
                        "Lat": -37.73341,
                        "Lng": 144.88265
                    },
                    {
                        "Lat": -37.73341,
                        "Lng": 144.88265
                    },
                    {
                        "Lat": -37.7331,
                        "Lng": 144.88154
                    },
                    {
                        "Lat": -37.7331,
                        "Lng": 144.88154
                    },
                    {
                        "Lat": -37.73277,
                        "Lng": 144.88056
                    },
                    {
                        "Lat": -37.73158,
                        "Lng": 144.87745
                    },
                    {
                        "Lat": -37.73158,
                        "Lng": 144.87745
                    },
                    {
                        "Lat": -37.73107,
                        "Lng": 144.8761
                    },
                    {
                        "Lat": -37.73107,
                        "Lng": 144.8761
                    },
                    {
                        "Lat": -37.731,
                        "Lng": 144.8759
                    },
                    {
                        "Lat": -37.731,
                        "Lng": 144.8759
                    },
                    {
                        "Lat": -37.73076,
                        "Lng": 144.87515
                    },
                    {
                        "Lat": -37.7305,
                        "Lng": 144.87416
                    },
                    {
                        "Lat": -37.7305,
                        "Lng": 144.87416
                    },
                    {
                        "Lat": -37.73027,
                        "Lng": 144.87322
                    },
                    {
                        "Lat": -37.73027,
                        "Lng": 144.87322
                    },
                    {
                        "Lat": -37.73003,
                        "Lng": 144.87225
                    },
                    {
                        "Lat": -37.73003,
                        "Lng": 144.87225
                    },
                    {
                        "Lat": -37.72959,
                        "Lng": 144.87044
                    },
                    {
                        "Lat": -37.72959,
                        "Lng": 144.87044
                    },
                    {
                        "Lat": -37.72949,
                        "Lng": 144.87
                    },
                    {
                        "Lat": -37.72949,
                        "Lng": 144.87
                    },
                    {
                        "Lat": -37.72941,
                        "Lng": 144.86959
                    },
                    {
                        "Lat": -37.72941,
                        "Lng": 144.86959
                    },
                    {
                        "Lat": -37.7293,
                        "Lng": 144.86906
                    },
                    {
                        "Lat": -37.7293,
                        "Lng": 144.86906
                    },
                    {
                        "Lat": -37.72921,
                        "Lng": 144.86862
                    },
                    {
                        "Lat": -37.72921,
                        "Lng": 144.86862
                    },
                    {
                        "Lat": -37.72899,
                        "Lng": 144.86752
                    },
                    {
                        "Lat": -37.72899,
                        "Lng": 144.86752
                    },
                    {
                        "Lat": -37.72885,
                        "Lng": 144.8668
                    },
                    {
                        "Lat": -37.72885,
                        "Lng": 144.8668
                    },
                    {
                        "Lat": -37.72873,
                        "Lng": 144.86618
                    },
                    {
                        "Lat": -37.72873,
                        "Lng": 144.86618
                    },
                    {
                        "Lat": -37.72869,
                        "Lng": 144.86596
                    },
                    {
                        "Lat": -37.72869,
                        "Lng": 144.86596
                    },
                    {
                        "Lat": -37.72837,
                        "Lng": 144.86423
                    },
                    {
                        "Lat": -37.72837,
                        "Lng": 144.86423
                    },
                    {
                        "Lat": -37.72833,
                        "Lng": 144.86403
                    },
                    {
                        "Lat": -37.72833,
                        "Lng": 144.86403
                    },
                    {
                        "Lat": -37.72828,
                        "Lng": 144.86378
                    },
                    {
                        "Lat": -37.72828,
                        "Lng": 144.86378
                    },
                    {
                        "Lat": -37.72824,
                        "Lng": 144.86359
                    },
                    {
                        "Lat": -37.72824,
                        "Lng": 144.86359
                    },
                    {
                        "Lat": -37.7282,
                        "Lng": 144.86339
                    },
                    {
                        "Lat": -37.7282,
                        "Lng": 144.86339
                    },
                    {
                        "Lat": -37.72816,
                        "Lng": 144.86319
                    },
                    {
                        "Lat": -37.72816,
                        "Lng": 144.86319
                    },
                    {
                        "Lat": -37.72812,
                        "Lng": 144.86302
                    },
                    {
                        "Lat": -37.72812,
                        "Lng": 144.86302
                    },
                    {
                        "Lat": -37.72807,
                        "Lng": 144.86278
                    },
                    {
                        "Lat": -37.72807,
                        "Lng": 144.86278
                    },
                    {
                        "Lat": -37.72797,
                        "Lng": 144.86224
                    },
                    {
                        "Lat": -37.72797,
                        "Lng": 144.86224
                    },
                    {
                        "Lat": -37.72792,
                        "Lng": 144.86199
                    },
                    {
                        "Lat": -37.72792,
                        "Lng": 144.86199
                    },
                    {
                        "Lat": -37.72788,
                        "Lng": 144.86177
                    },
                    {
                        "Lat": -37.72788,
                        "Lng": 144.86177
                    },
                    {
                        "Lat": -37.72782,
                        "Lng": 144.86144
                    },
                    {
                        "Lat": -37.72782,
                        "Lng": 144.86144
                    },
                    {
                        "Lat": -37.7276,
                        "Lng": 144.86037
                    },
                    {
                        "Lat": -37.7276,
                        "Lng": 144.86037
                    },
                    {
                        "Lat": -37.72755,
                        "Lng": 144.86012
                    },
                    {
                        "Lat": -37.72755,
                        "Lng": 144.86012
                    },
                    {
                        "Lat": -37.72751,
                        "Lng": 144.85991
                    },
                    {
                        "Lat": -37.72751,
                        "Lng": 144.85991
                    },
                    {
                        "Lat": -37.72747,
                        "Lng": 144.8597
                    },
                    {
                        "Lat": -37.72747,
                        "Lng": 144.8597
                    },
                    {
                        "Lat": -37.72732,
                        "Lng": 144.85898
                    },
                    {
                        "Lat": -37.72732,
                        "Lng": 144.85898
                    },
                    {
                        "Lat": -37.72727,
                        "Lng": 144.85874
                    },
                    {
                        "Lat": -37.72727,
                        "Lng": 144.85874
                    },
                    {
                        "Lat": -37.72721,
                        "Lng": 144.85842
                    },
                    {
                        "Lat": -37.72721,
                        "Lng": 144.85842
                    },
                    {
                        "Lat": -37.72699,
                        "Lng": 144.85735
                    },
                    {
                        "Lat": -37.72699,
                        "Lng": 144.85735
                    },
                    {
                        "Lat": -37.72674,
                        "Lng": 144.85614
                    },
                    {
                        "Lat": -37.72658,
                        "Lng": 144.85517
                    }
                ]
            },
            {
                "Key": "inc-mel-09",
                "Priority": 4,
                "Headline": "Princes Highway — roadworks",
                "Detail": "Between Princes Highway and Clyde Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 3159,
                "Latitude": -38.0476,
                "Longitude": 145.34682,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -38.0476,
                        "Lng": 145.34682
                    },
                    {
                        "Lat": -38.04724,
                        "Lng": 145.34593
                    },
                    {
                        "Lat": -38.04696,
                        "Lng": 145.34528
                    },
                    {
                        "Lat": -38.04696,
                        "Lng": 145.34528
                    },
                    {
                        "Lat": -38.0462,
                        "Lng": 145.34361
                    },
                    {
                        "Lat": -38.0462,
                        "Lng": 145.34361
                    },
                    {
                        "Lat": -38.04579,
                        "Lng": 145.34274
                    },
                    {
                        "Lat": -38.04507,
                        "Lng": 145.34126
                    },
                    {
                        "Lat": -38.04391,
                        "Lng": 145.33881
                    },
                    {
                        "Lat": -38.04391,
                        "Lng": 145.33881
                    },
                    {
                        "Lat": -38.04336,
                        "Lng": 145.33765
                    },
                    {
                        "Lat": -38.04336,
                        "Lng": 145.33765
                    },
                    {
                        "Lat": -38.04299,
                        "Lng": 145.33686
                    },
                    {
                        "Lat": -38.04299,
                        "Lng": 145.33686
                    },
                    {
                        "Lat": -38.04276,
                        "Lng": 145.33638
                    },
                    {
                        "Lat": -38.04276,
                        "Lng": 145.33638
                    },
                    {
                        "Lat": -38.04253,
                        "Lng": 145.33592
                    },
                    {
                        "Lat": -38.04253,
                        "Lng": 145.33592
                    },
                    {
                        "Lat": -38.04225,
                        "Lng": 145.33538
                    },
                    {
                        "Lat": -38.04225,
                        "Lng": 145.33538
                    },
                    {
                        "Lat": -38.0421,
                        "Lng": 145.33512
                    },
                    {
                        "Lat": -38.0421,
                        "Lng": 145.33512
                    },
                    {
                        "Lat": -38.04159,
                        "Lng": 145.33427
                    },
                    {
                        "Lat": -38.04126,
                        "Lng": 145.33377
                    },
                    {
                        "Lat": -38.04069,
                        "Lng": 145.33297
                    },
                    {
                        "Lat": -38.04069,
                        "Lng": 145.33297
                    },
                    {
                        "Lat": -38.04042,
                        "Lng": 145.33262
                    },
                    {
                        "Lat": -38.03995,
                        "Lng": 145.33205
                    },
                    {
                        "Lat": -38.03953,
                        "Lng": 145.33158
                    },
                    {
                        "Lat": -38.03953,
                        "Lng": 145.33158
                    },
                    {
                        "Lat": -38.03893,
                        "Lng": 145.33095
                    },
                    {
                        "Lat": -38.03838,
                        "Lng": 145.33041
                    },
                    {
                        "Lat": -38.03757,
                        "Lng": 145.32966
                    },
                    {
                        "Lat": -38.03757,
                        "Lng": 145.32966
                    },
                    {
                        "Lat": -38.03651,
                        "Lng": 145.32867
                    },
                    {
                        "Lat": -38.03651,
                        "Lng": 145.32867
                    },
                    {
                        "Lat": -38.03454,
                        "Lng": 145.32683
                    },
                    {
                        "Lat": -38.03454,
                        "Lng": 145.32683
                    },
                    {
                        "Lat": -38.03441,
                        "Lng": 145.3267
                    },
                    {
                        "Lat": -38.03441,
                        "Lng": 145.3267
                    },
                    {
                        "Lat": -38.03423,
                        "Lng": 145.32652
                    },
                    {
                        "Lat": -38.03423,
                        "Lng": 145.32652
                    },
                    {
                        "Lat": -38.03398,
                        "Lng": 145.3263
                    },
                    {
                        "Lat": -38.03398,
                        "Lng": 145.3263
                    },
                    {
                        "Lat": -38.03349,
                        "Lng": 145.32584
                    },
                    {
                        "Lat": -38.03349,
                        "Lng": 145.32584
                    },
                    {
                        "Lat": -38.03302,
                        "Lng": 145.3254
                    },
                    {
                        "Lat": -38.03302,
                        "Lng": 145.3254
                    },
                    {
                        "Lat": -38.03265,
                        "Lng": 145.32505
                    },
                    {
                        "Lat": -38.03265,
                        "Lng": 145.32505
                    },
                    {
                        "Lat": -38.0324,
                        "Lng": 145.32482
                    },
                    {
                        "Lat": -38.0324,
                        "Lng": 145.32482
                    },
                    {
                        "Lat": -38.0322,
                        "Lng": 145.32463
                    },
                    {
                        "Lat": -38.0322,
                        "Lng": 145.32463
                    },
                    {
                        "Lat": -38.03155,
                        "Lng": 145.324
                    },
                    {
                        "Lat": -38.03155,
                        "Lng": 145.324
                    },
                    {
                        "Lat": -38.0314,
                        "Lng": 145.32386
                    },
                    {
                        "Lat": -38.0314,
                        "Lng": 145.32386
                    },
                    {
                        "Lat": -38.03019,
                        "Lng": 145.32274
                    },
                    {
                        "Lat": -38.03019,
                        "Lng": 145.32274
                    },
                    {
                        "Lat": -38.02873,
                        "Lng": 145.32138
                    },
                    {
                        "Lat": -38.02819,
                        "Lng": 145.32085
                    }
                ]
            },
            {
                "Key": "inc-mel-10",
                "Priority": 4,
                "Headline": "Stud Road — roadworks",
                "Detail": "Between Stud Road and Heatherton Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 2698,
                "Latitude": -37.95699,
                "Longitude": 145.22147,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.95699,
                        "Lng": 145.22147
                    },
                    {
                        "Lat": -37.95806,
                        "Lng": 145.22299
                    },
                    {
                        "Lat": -37.95806,
                        "Lng": 145.22299
                    },
                    {
                        "Lat": -37.95833,
                        "Lng": 145.22336
                    },
                    {
                        "Lat": -37.95833,
                        "Lng": 145.22336
                    },
                    {
                        "Lat": -37.95988,
                        "Lng": 145.22557
                    },
                    {
                        "Lat": -37.96103,
                        "Lng": 145.22718
                    },
                    {
                        "Lat": -37.96103,
                        "Lng": 145.22718
                    },
                    {
                        "Lat": -37.9614,
                        "Lng": 145.2277
                    },
                    {
                        "Lat": -37.9614,
                        "Lng": 145.2277
                    },
                    {
                        "Lat": -37.96166,
                        "Lng": 145.22807
                    },
                    {
                        "Lat": -37.96166,
                        "Lng": 145.22807
                    },
                    {
                        "Lat": -37.96176,
                        "Lng": 145.22821
                    },
                    {
                        "Lat": -37.96176,
                        "Lng": 145.22821
                    },
                    {
                        "Lat": -37.96225,
                        "Lng": 145.22891
                    },
                    {
                        "Lat": -37.96225,
                        "Lng": 145.22891
                    },
                    {
                        "Lat": -37.96252,
                        "Lng": 145.22929
                    },
                    {
                        "Lat": -37.96252,
                        "Lng": 145.22929
                    },
                    {
                        "Lat": -37.96278,
                        "Lng": 145.22966
                    },
                    {
                        "Lat": -37.96278,
                        "Lng": 145.22966
                    },
                    {
                        "Lat": -37.96359,
                        "Lng": 145.2308
                    },
                    {
                        "Lat": -37.96359,
                        "Lng": 145.2308
                    },
                    {
                        "Lat": -37.96379,
                        "Lng": 145.23108
                    },
                    {
                        "Lat": -37.96379,
                        "Lng": 145.23108
                    },
                    {
                        "Lat": -37.96473,
                        "Lng": 145.23242
                    },
                    {
                        "Lat": -37.96473,
                        "Lng": 145.23242
                    },
                    {
                        "Lat": -37.96491,
                        "Lng": 145.23267
                    },
                    {
                        "Lat": -37.96491,
                        "Lng": 145.23267
                    },
                    {
                        "Lat": -37.96523,
                        "Lng": 145.23311
                    },
                    {
                        "Lat": -37.96523,
                        "Lng": 145.23311
                    },
                    {
                        "Lat": -37.96547,
                        "Lng": 145.23341
                    },
                    {
                        "Lat": -37.96547,
                        "Lng": 145.23341
                    },
                    {
                        "Lat": -37.96576,
                        "Lng": 145.23375
                    },
                    {
                        "Lat": -37.96576,
                        "Lng": 145.23375
                    },
                    {
                        "Lat": -37.96604,
                        "Lng": 145.23405
                    },
                    {
                        "Lat": -37.96604,
                        "Lng": 145.23405
                    },
                    {
                        "Lat": -37.96649,
                        "Lng": 145.23451
                    },
                    {
                        "Lat": -37.96649,
                        "Lng": 145.23451
                    },
                    {
                        "Lat": -37.96696,
                        "Lng": 145.23494
                    },
                    {
                        "Lat": -37.96742,
                        "Lng": 145.23531
                    },
                    {
                        "Lat": -37.96742,
                        "Lng": 145.23531
                    },
                    {
                        "Lat": -37.96806,
                        "Lng": 145.23577
                    },
                    {
                        "Lat": -37.96836,
                        "Lng": 145.23595
                    },
                    {
                        "Lat": -37.96836,
                        "Lng": 145.23595
                    },
                    {
                        "Lat": -37.96872,
                        "Lng": 145.23617
                    },
                    {
                        "Lat": -37.96872,
                        "Lng": 145.23617
                    },
                    {
                        "Lat": -37.96903,
                        "Lng": 145.23633
                    },
                    {
                        "Lat": -37.96903,
                        "Lng": 145.23633
                    },
                    {
                        "Lat": -37.96957,
                        "Lng": 145.23659
                    },
                    {
                        "Lat": -37.96989,
                        "Lng": 145.23672
                    },
                    {
                        "Lat": -37.97039,
                        "Lng": 145.2369
                    },
                    {
                        "Lat": -37.97039,
                        "Lng": 145.2369
                    },
                    {
                        "Lat": -37.97092,
                        "Lng": 145.23706
                    },
                    {
                        "Lat": -37.97092,
                        "Lng": 145.23706
                    },
                    {
                        "Lat": -37.97246,
                        "Lng": 145.2374
                    },
                    {
                        "Lat": -37.97246,
                        "Lng": 145.2374
                    },
                    {
                        "Lat": -37.97258,
                        "Lng": 145.23743
                    },
                    {
                        "Lat": -37.97258,
                        "Lng": 145.23743
                    },
                    {
                        "Lat": -37.97306,
                        "Lng": 145.23757
                    },
                    {
                        "Lat": -37.97306,
                        "Lng": 145.23757
                    },
                    {
                        "Lat": -37.9736,
                        "Lng": 145.23776
                    },
                    {
                        "Lat": -37.97391,
                        "Lng": 145.23789
                    },
                    {
                        "Lat": -37.97461,
                        "Lng": 145.23822
                    },
                    {
                        "Lat": -37.97461,
                        "Lng": 145.23822
                    },
                    {
                        "Lat": -37.97523,
                        "Lng": 145.23858
                    },
                    {
                        "Lat": -37.97523,
                        "Lng": 145.23858
                    },
                    {
                        "Lat": -37.97583,
                        "Lng": 145.23898
                    },
                    {
                        "Lat": -37.97604,
                        "Lng": 145.23914
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "Donnybrook Road",
                "Severity": 1,
                "ImpactScore": 220,
                "PrimaryType": "construction",
                "Description": "2 incidents including roadworks",
                "IncidentCount": 2,
                "TotalLengthKm": 47.6,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 5,
                "DelayMinutes": 6,
                "Latitude": -37.45584,
                "Longitude": 144.97487,
                "IncidentKeys": [
                    "inc-mel-01",
                    "inc-mel-03"
                ],
                "DriveTimeMinutes": 11
            },
            {
                "RoadName": "Sunshine Avenue",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 22.6,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 14,
                "DelayMinutes": null,
                "Latitude": -37.57194,
                "Longitude": 144.67614,
                "IncidentKeys": [
                    "inc-mel-02"
                ]
            },
            {
                "RoadName": "Toorak Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 5.8,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 4,
                "DelayMinutes": 1,
                "Latitude": -37.84796,
                "Longitude": 145.0429,
                "IncidentKeys": [
                    "inc-mel-04"
                ]
            },
            {
                "RoadName": "Huntingdale Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 4.8,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 3,
                "DelayMinutes": null,
                "Latitude": -37.88206,
                "Longitude": 145.08889,
                "IncidentKeys": [
                    "inc-mel-05"
                ]
            },
            {
                "RoadName": "Northern Highway",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 4.3,
                "Rank": 5,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 2,
                "DelayMinutes": 1,
                "Latitude": -37.51165,
                "Longitude": 144.9498,
                "IncidentKeys": [
                    "inc-mel-06"
                ]
            },
            {
                "RoadName": "Gully Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 3.4,
                "Rank": 6,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -37.72616,
                "Longitude": 144.85332,
                "IncidentKeys": [
                    "inc-mel-07"
                ]
            },
            {
                "RoadName": "Tullamarine Freeway",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 3.2,
                "Rank": 7,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -37.73452,
                "Longitude": 144.89081,
                "IncidentKeys": [
                    "inc-mel-08"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-mel-01",
                "Priority": 4,
                "Headline": "Donnybrook Road — roadworks",
                "Detail": "Between Donnybrook Road and Main Road - Maintenance work",
                "FunctionalClass": 1,
                "LengthMeters": 39768,
                "Latitude": -37.45584,
                "Longitude": 144.97487,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.45584,
                        "Lng": 144.97487
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45488,
                        "Lng": 144.9755
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45406,
                        "Lng": 144.97606
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45275,
                        "Lng": 144.97697
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.45165,
                        "Lng": 144.97778
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.4513,
                        "Lng": 144.97803
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45111,
                        "Lng": 144.97817
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45061,
                        "Lng": 144.97854
                    },
                    {
                        "Lat": -37.45033,
                        "Lng": 144.97875
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.4497,
                        "Lng": 144.97918
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44917,
                        "Lng": 144.9795
                    },
                    {
                        "Lat": -37.44866,
                        "Lng": 144.97978
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44782,
                        "Lng": 144.98019
                    },
                    {
                        "Lat": -37.44755,
                        "Lng": 144.98031
                    },
                    {
                        "Lat": -37.44701,
                        "Lng": 144.98052
                    },
                    {
                        "Lat": -37.44628,
                        "Lng": 144.98077
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44599,
                        "Lng": 144.98085
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44545,
                        "Lng": 144.981
                    },
                    {
                        "Lat": -37.44501,
                        "Lng": 144.9811
                    },
                    {
                        "Lat": -37.44435,
                        "Lng": 144.98122
                    },
                    {
                        "Lat": -37.44375,
                        "Lng": 144.9813
                    },
                    {
                        "Lat": -37.44315,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44281,
                        "Lng": 144.98136
                    },
                    {
                        "Lat": -37.44159,
                        "Lng": 144.98135
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44016,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.44009,
                        "Lng": 144.98128
                    },
                    {
                        "Lat": -37.43919,
                        "Lng": 144.98124
                    },
                    {
                        "Lat": -37.438,
                        "Lng": 144.98123
                    },
                    {
                        "Lat": -37.43715,
                        "Lng": 144.98127
                    },
                    {
                        "Lat": -37.43657,
                        "Lng": 144.98134
                    },
                    {
                        "Lat": -37.43614,
                        "Lng": 144.9814
                    },
                    {
                        "Lat": -37.43556,
                        "Lng": 144.9815
                    },
                    {
                        "Lat": -37.43459,
                        "Lng": 144.98172
                    },
                    {
                        "Lat": -37.43368,
                        "Lng": 144.98198
                    },
                    {
                        "Lat": -37.43322,
                        "Lng": 144.98214
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.433,
                        "Lng": 144.98223
                    },
                    {
                        "Lat": -37.43229,
                        "Lng": 144.9825
                    },
                    {
                        "Lat": -37.4319,
                        "Lng": 144.98267
                    },
                    {
                        "Lat": -37.43116,
                        "Lng": 144.98304
                    },
                    {
                        "Lat": -37.43041,
                        "Lng": 144.98344
                    },
                    {
                        "Lat": -37.43019,
                        "Lng": 144.98357
                    },
                    {
                        "Lat": -37.42888,
                        "Lng": 144.98443
                    },
                    {
                        "Lat": -37.4279,
                        "Lng": 144.98518
                    },
                    {
                        "Lat": -37.42706,
                        "Lng": 144.9859
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42511,
                        "Lng": 144.98773
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.42102,
                        "Lng": 144.99162
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.4183,
                        "Lng": 144.99421
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41786,
                        "Lng": 144.99462
                    },
                    {
                        "Lat": -37.41668,
                        "Lng": 144.99576
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41617,
                        "Lng": 144.99628
                    },
                    {
                        "Lat": -37.41541,
                        "Lng": 144.99712
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41492,
                        "Lng": 144.9977
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41455,
                        "Lng": 144.99816
                    },
                    {
                        "Lat": -37.41406,
                        "Lng": 144.99881
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41363,
                        "Lng": 144.99941
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41314,
                        "Lng": 145.00014
                    },
                    {
                        "Lat": -37.41272,
                        "Lng": 145.0008
                    },
                    {
                        "Lat": -37.41211,
                        "Lng": 145.00186
                    },
                    {
                        "Lat": -37.41147,
                        "Lng": 145.0031
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41086,
                        "Lng": 145.00442
                    },
                    {
                        "Lat": -37.41055,
                        "Lng": 145.00508
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.41006,
                        "Lng": 145.00619
                    },
                    {
                        "Lat": -37.40954,
                        "Lng": 145.0073
                    },
                    {
                        "Lat": -37.40935,
                        "Lng": 145.00768
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40899,
                        "Lng": 145.00834
                    },
                    {
                        "Lat": -37.40869,
                        "Lng": 145.00889
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40814,
                        "Lng": 145.00981
                    },
                    {
                        "Lat": -37.40781,
                        "Lng": 145.01033
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40744,
                        "Lng": 145.01086
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40705,
                        "Lng": 145.01142
                    },
                    {
                        "Lat": -37.40627,
                        "Lng": 145.01245
                    },
                    {
                        "Lat": -37.40601,
                        "Lng": 145.01277
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.4057,
                        "Lng": 145.01312
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40554,
                        "Lng": 145.01331
                    },
                    {
                        "Lat": -37.40523,
                        "Lng": 145.01366
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.40495,
                        "Lng": 145.01395
                    },
                    {
                        "Lat": -37.4041,
                        "Lng": 145.01482
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40194,
                        "Lng": 145.01694
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40137,
                        "Lng": 145.0175
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40048,
                        "Lng": 145.01836
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.40001,
                        "Lng": 145.01882
                    },
                    {
                        "Lat": -37.39825,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.39825,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.39782,
                        "Lng": 145.02096
                    },
                    {
                        "Lat": -37.39689,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.3963,
                        "Lng": 145.02233
                    },
                    {
                        "Lat": -37.39603,
                        "Lng": 145.02254
                    },
                    {
                        "Lat": -37.39524,
                        "Lng": 145.02312
                    },
                    {
                        "Lat": -37.39481,
                        "Lng": 145.02341
                    },
                    {
                        "Lat": -37.39444,
                        "Lng": 145.02364
                    },
                    {
                        "Lat": -37.39338,
                        "Lng": 145.02424
                    },
                    {
                        "Lat": -37.39338,
                        "Lng": 145.02424
                    },
                    {
                        "Lat": -37.39313,
                        "Lng": 145.02436
                    },
                    {
                        "Lat": -37.39313,
                        "Lng": 145.02436
                    },
                    {
                        "Lat": -37.39247,
                        "Lng": 145.02468
                    },
                    {
                        "Lat": -37.39196,
                        "Lng": 145.0249
                    },
                    {
                        "Lat": -37.39126,
                        "Lng": 145.02516
                    },
                    {
                        "Lat": -37.3904,
                        "Lng": 145.02544
                    },
                    {
                        "Lat": -37.38925,
                        "Lng": 145.02572
                    },
                    {
                        "Lat": -37.38862,
                        "Lng": 145.02584
                    },
                    {
                        "Lat": -37.38823,
                        "Lng": 145.02589
                    },
                    {
                        "Lat": -37.38711,
                        "Lng": 145.02599
                    },
                    {
                        "Lat": -37.38711,
                        "Lng": 145.02599
                    },
                    {
                        "Lat": -37.38686,
                        "Lng": 145.026
                    },
                    {
                        "Lat": -37.38686,
                        "Lng": 145.026
                    },
                    {
                        "Lat": -37.38637,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38603,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38603,
                        "Lng": 145.02602
                    },
                    {
                        "Lat": -37.38511,
                        "Lng": 145.02598
                    },
                    {
                        "Lat": -37.3846,
                        "Lng": 145.02593
                    },
                    {
                        "Lat": -37.38367,
                        "Lng": 145.02579
                    },
                    {
                        "Lat": -37.38312,
                        "Lng": 145.02568
                    },
                    {
                        "Lat": -37.3825,
                        "Lng": 145.02553
                    },
                    {
                        "Lat": -37.38147,
                        "Lng": 145.02523
                    },
                    {
                        "Lat": -37.38101,
                        "Lng": 145.02507
                    },
                    {
                        "Lat": -37.38041,
                        "Lng": 145.02483
                    },
                    {
                        "Lat": -37.3798,
                        "Lng": 145.02456
                    },
                    {
                        "Lat": -37.37919,
                        "Lng": 145.02426
                    },
                    {
                        "Lat": -37.37867,
                        "Lng": 145.02398
                    },
                    {
                        "Lat": -37.37839,
                        "Lng": 145.02382
                    },
                    {
                        "Lat": -37.37761,
                        "Lng": 145.02333
                    },
                    {
                        "Lat": -37.37599,
                        "Lng": 145.02225
                    },
                    {
                        "Lat": -37.37514,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.37514,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.37448,
                        "Lng": 145.02138
                    },
                    {
                        "Lat": -37.37395,
                        "Lng": 145.02114
                    },
                    {
                        "Lat": -37.37336,
                        "Lng": 145.02091
                    },
                    {
                        "Lat": -37.37284,
                        "Lng": 145.02073
                    },
                    {
                        "Lat": -37.37243,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.37195,
                        "Lng": 145.02049
                    },
                    {
                        "Lat": -37.37094,
                        "Lng": 145.0203
                    },
                    {
                        "Lat": -37.37053,
                        "Lng": 145.02026
                    },
                    {
                        "Lat": -37.37053,
                        "Lng": 145.02026
                    },
                    {
                        "Lat": -37.36994,
                        "Lng": 145.02021
                    },
                    {
                        "Lat": -37.36921,
                        "Lng": 145.0202
                    },
                    {
                        "Lat": -37.36882,
                        "Lng": 145.02022
                    },
                    {
                        "Lat": -37.36796,
                        "Lng": 145.0203
                    },
                    {
                        "Lat": -37.36719,
                        "Lng": 145.02043
                    },
                    {
                        "Lat": -37.36635,
                        "Lng": 145.02064
                    },
                    {
                        "Lat": -37.36546,
                        "Lng": 145.02092
                    },
                    {
                        "Lat": -37.363,
                        "Lng": 145.02174
                    },
                    {
                        "Lat": -37.36237,
                        "Lng": 145.02192
                    },
                    {
                        "Lat": -37.36186,
                        "Lng": 145.02205
                    },
                    {
                        "Lat": -37.36102,
                        "Lng": 145.02224
                    },
                    {
                        "Lat": -37.36102,
                        "Lng": 145.02224
                    },
                    {
                        "Lat": -37.35981,
                        "Lng": 145.02245
                    },
                    {
                        "Lat": -37.35865,
                        "Lng": 145.02258
                    },
                    {
                        "Lat": -37.35865,
                        "Lng": 145.02258
                    },
                    {
                        "Lat": -37.35725,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35725,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35672,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35672,
                        "Lng": 145.02265
                    },
                    {
                        "Lat": -37.35605,
                        "Lng": 145.02263
                    },
                    {
                        "Lat": -37.35605,
                        "Lng": 145.02263
                    },
                    {
                        "Lat": -37.35568,
                        "Lng": 145.02262
                    },
                    {
                        "Lat": -37.35568,
                        "Lng": 145.02262
                    },
                    {
                        "Lat": -37.35526,
                        "Lng": 145.02259
                    },
                    {
                        "Lat": -37.35526,
                        "Lng": 145.02259
                    },
                    {
                        "Lat": -37.35433,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.35433,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.3534,
                        "Lng": 145.02236
                    },
                    {
                        "Lat": -37.3534,
                        "Lng": 145.02236
                    },
                    {
                        "Lat": -37.353,
                        "Lng": 145.02229
                    },
                    {
                        "Lat": -37.353,
                        "Lng": 145.02229
                    },
                    {
                        "Lat": -37.35211,
                        "Lng": 145.0221
                    },
                    {
                        "Lat": -37.35145,
                        "Lng": 145.02193
                    },
                    {
                        "Lat": -37.35111,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.35111,
                        "Lng": 145.02183
                    },
                    {
                        "Lat": -37.35052,
                        "Lng": 145.02166
                    },
                    {
                        "Lat": -37.35052,
                        "Lng": 145.02166
                    },
                    {
                        "Lat": -37.34841,
                        "Lng": 145.02097
                    },
                    {
                        "Lat": -37.34841,
                        "Lng": 145.02097
                    },
                    {
                        "Lat": -37.34811,
                        "Lng": 145.02088
                    },
                    {
                        "Lat": -37.34745,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34745,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34674,
                        "Lng": 145.02058
                    },
                    {
                        "Lat": -37.34674,
                        "Lng": 145.02058
                    },
                    {
                        "Lat": -37.34639,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.34639,
                        "Lng": 145.02054
                    },
                    {
                        "Lat": -37.34584,
                        "Lng": 145.02051
                    },
                    {
                        "Lat": -37.34554,
                        "Lng": 145.02051
                    },
                    {
                        "Lat": -37.34492,
                        "Lng": 145.02055
                    },
                    {
                        "Lat": -37.34492,
                        "Lng": 145.02055
                    },
                    {
                        "Lat": -37.34444,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.34444,
                        "Lng": 145.02061
                    },
                    {
                        "Lat": -37.34395,
                        "Lng": 145.02071
                    },
                    {
                        "Lat": -37.34346,
                        "Lng": 145.02084
                    },
                    {
                        "Lat": -37.34276,
                        "Lng": 145.02108
                    },
                    {
                        "Lat": -37.34217,
                        "Lng": 145.02134
                    },
                    {
                        "Lat": -37.34217,
                        "Lng": 145.02134
                    },
                    {
                        "Lat": -37.34201,
                        "Lng": 145.02143
                    },
                    {
                        "Lat": -37.34201,
                        "Lng": 145.02143
                    },
                    {
                        "Lat": -37.34156,
                        "Lng": 145.02168
                    },
                    {
                        "Lat": -37.34156,
                        "Lng": 145.02168
                    },
                    {
                        "Lat": -37.34135,
                        "Lng": 145.02181
                    },
                    {
                        "Lat": -37.34135,
                        "Lng": 145.02181
                    },
                    {
                        "Lat": -37.34082,
                        "Lng": 145.02216
                    },
                    {
                        "Lat": -37.34037,
                        "Lng": 145.02249
                    },
                    {
                        "Lat": -37.33995,
                        "Lng": 145.02285
                    },
                    {
                        "Lat": -37.33946,
                        "Lng": 145.02331
                    },
                    {
                        "Lat": -37.33764,
                        "Lng": 145.02524
                    },
                    {
                        "Lat": -37.33696,
                        "Lng": 145.02591
                    },
                    {
                        "Lat": -37.33641,
                        "Lng": 145.02642
                    },
                    {
                        "Lat": -37.33595,
                        "Lng": 145.02681
                    },
                    {
                        "Lat": -37.33539,
                        "Lng": 145.02725
                    },
                    {
                        "Lat": -37.33539,
                        "Lng": 145.02725
                    },
                    {
                        "Lat": -37.33514,
                        "Lng": 145.02743
                    },
                    {
                        "Lat": -37.33514,
                        "Lng": 145.02743
                    },
                    {
                        "Lat": -37.33449,
                        "Lng": 145.0279
                    },
                    {
                        "Lat": -37.3337,
                        "Lng": 145.02839
                    },
                    {
                        "Lat": -37.33315,
                        "Lng": 145.02871
                    },
                    {
                        "Lat": -37.33243,
                        "Lng": 145.02909
                    },
                    {
                        "Lat": -37.33243,
                        "Lng": 145.02909
                    },
                    {
                        "Lat": -37.33172,
                        "Lng": 145.02942
                    },
                    {
                        "Lat": -37.33064,
                        "Lng": 145.02984
                    },
                    {
                        "Lat": -37.33064,
                        "Lng": 145.02984
                    },
                    {
                        "Lat": -37.33016,
                        "Lng": 145.03001
                    },
                    {
                        "Lat": -37.33016,
                        "Lng": 145.03001
                    },
                    {
                        "Lat": -37.32954,
                        "Lng": 145.03019
                    },
                    {
                        "Lat": -37.32954,
                        "Lng": 145.03019
                    },
                    {
                        "Lat": -37.32928,
                        "Lng": 145.03026
                    },
                    {
                        "Lat": -37.32928,
                        "Lng": 145.03026
                    },
                    {
                        "Lat": -37.32793,
                        "Lng": 145.03058
                    },
                    {
                        "Lat": -37.3276,
                        "Lng": 145.03067
                    },
                    {
                        "Lat": -37.32701,
                        "Lng": 145.03086
                    },
                    {
                        "Lat": -37.32651,
                        "Lng": 145.03105
                    },
                    {
                        "Lat": -37.32651,
                        "Lng": 145.03105
                    },
                    {
                        "Lat": -37.32606,
                        "Lng": 145.03125
                    },
                    {
                        "Lat": -37.3256,
                        "Lng": 145.0315
                    },
                    {
                        "Lat": -37.32527,
                        "Lng": 145.03171
                    },
                    {
                        "Lat": -37.32474,
                        "Lng": 145.03209
                    },
                    {
                        "Lat": -37.32474,
                        "Lng": 145.03209
                    },
                    {
                        "Lat": -37.32414,
                        "Lng": 145.03261
                    },
                    {
                        "Lat": -37.32414,
                        "Lng": 145.03261
                    },
                    {
                        "Lat": -37.32383,
                        "Lng": 145.03291
                    },
                    {
                        "Lat": -37.32383,
                        "Lng": 145.03291
                    },
                    {
                        "Lat": -37.32343,
                        "Lng": 145.03336
                    },
                    {
                        "Lat": -37.32343,
                        "Lng": 145.03336
                    },
                    {
                        "Lat": -37.32311,
                        "Lng": 145.03375
                    },
                    {
                        "Lat": -37.32279,
                        "Lng": 145.03419
                    },
                    {
                        "Lat": -37.32248,
                        "Lng": 145.03468
                    },
                    {
                        "Lat": -37.32233,
                        "Lng": 145.03494
                    },
                    {
                        "Lat": -37.32193,
                        "Lng": 145.03571
                    },
                    {
                        "Lat": -37.32178,
                        "Lng": 145.03604
                    },
                    {
                        "Lat": -37.32155,
                        "Lng": 145.03663
                    },
                    {
                        "Lat": -37.32133,
                        "Lng": 145.0373
                    },
                    {
                        "Lat": -37.32116,
                        "Lng": 145.03792
                    },
                    {
                        "Lat": -37.32116,
                        "Lng": 145.03792
                    },
                    {
                        "Lat": -37.32102,
                        "Lng": 145.03858
                    },
                    {
                        "Lat": -37.32093,
                        "Lng": 145.03908
                    },
                    {
                        "Lat": -37.32093,
                        "Lng": 145.03908
                    },
                    {
                        "Lat": -37.32086,
                        "Lng": 145.03949
                    },
                    {
                        "Lat": -37.32086,
                        "Lng": 145.03949
                    },
                    {
                        "Lat": -37.32066,
                        "Lng": 145.04079
                    },
                    {
                        "Lat": -37.32053,
                        "Lng": 145.0415
                    },
                    {
                        "Lat": -37.32046,
                        "Lng": 145.04182
                    },
                    {
                        "Lat": -37.32034,
                        "Lng": 145.04231
                    },
                    {
                        "Lat": -37.32019,
                        "Lng": 145.04283
                    },
                    {
                        "Lat": -37.31991,
                        "Lng": 145.04363
                    },
                    {
                        "Lat": -37.31991,
                        "Lng": 145.04363
                    },
                    {
                        "Lat": -37.31966,
                        "Lng": 145.04422
                    },
                    {
                        "Lat": -37.31934,
                        "Lng": 145.04485
                    },
                    {
                        "Lat": -37.31895,
                        "Lng": 145.0455
                    },
                    {
                        "Lat": -37.31875,
                        "Lng": 145.0458
                    },
                    {
                        "Lat": -37.31846,
                        "Lng": 145.04619
                    },
                    {
                        "Lat": -37.31846,
                        "Lng": 145.04619
                    },
                    {
                        "Lat": -37.31826,
                        "Lng": 145.04644
                    },
                    {
                        "Lat": -37.31796,
                        "Lng": 145.04678
                    },
                    {
                        "Lat": -37.31759,
                        "Lng": 145.04716
                    },
                    {
                        "Lat": -37.31703,
                        "Lng": 145.04765
                    },
                    {
                        "Lat": -37.31654,
                        "Lng": 145.04801
                    },
                    {
                        "Lat": -37.31654,
                        "Lng": 145.04801
                    },
                    {
                        "Lat": -37.31616,
                        "Lng": 145.04826
                    },
                    {
                        "Lat": -37.31575,
                        "Lng": 145.04849
                    },
                    {
                        "Lat": -37.3152,
                        "Lng": 145.04874
                    },
                    {
                        "Lat": -37.31464,
                        "Lng": 145.04894
                    },
                    {
                        "Lat": -37.31464,
                        "Lng": 145.04894
                    },
                    {
                        "Lat": -37.314,
                        "Lng": 145.04912
                    },
                    {
                        "Lat": -37.31363,
                        "Lng": 145.0492
                    },
                    {
                        "Lat": -37.31363,
                        "Lng": 145.0492
                    },
                    {
                        "Lat": -37.31341,
                        "Lng": 145.04924
                    },
                    {
                        "Lat": -37.31341,
                        "Lng": 145.04924
                    },
                    {
                        "Lat": -37.31193,
                        "Lng": 145.04947
                    },
                    {
                        "Lat": -37.31193,
                        "Lng": 145.04947
                    },
                    {
                        "Lat": -37.31124,
                        "Lng": 145.0496
                    },
                    {
                        "Lat": -37.3105,
                        "Lng": 145.04979
                    },
                    {
                        "Lat": -37.30954,
                        "Lng": 145.05011
                    },
                    {
                        "Lat": -37.30885,
                        "Lng": 145.05038
                    },
                    {
                        "Lat": -37.30855,
                        "Lng": 145.05051
                    },
                    {
                        "Lat": -37.30855,
                        "Lng": 145.05051
                    },
                    {
                        "Lat": -37.30796,
                        "Lng": 145.05079
                    },
                    {
                        "Lat": -37.30763,
                        "Lng": 145.05096
                    },
                    {
                        "Lat": -37.30706,
                        "Lng": 145.05127
                    },
                    {
                        "Lat": -37.30624,
                        "Lng": 145.05174
                    },
                    {
                        "Lat": -37.30551,
                        "Lng": 145.05213
                    },
                    {
                        "Lat": -37.30489,
                        "Lng": 145.05242
                    },
                    {
                        "Lat": -37.30384,
                        "Lng": 145.05284
                    },
                    {
                        "Lat": -37.30384,
                        "Lng": 145.05284
                    },
                    {
                        "Lat": -37.30334,
                        "Lng": 145.05301
                    },
                    {
                        "Lat": -37.30248,
                        "Lng": 145.05326
                    },
                    {
                        "Lat": -37.30171,
                        "Lng": 145.05343
                    },
                    {
                        "Lat": -37.30086,
                        "Lng": 145.05357
                    },
                    {
                        "Lat": -37.30023,
                        "Lng": 145.05365
                    },
                    {
                        "Lat": -37.29917,
                        "Lng": 145.05376
                    },
                    {
                        "Lat": -37.29339,
                        "Lng": 145.0543
                    },
                    {
                        "Lat": -37.29339,
                        "Lng": 145.0543
                    },
                    {
                        "Lat": -37.29306,
                        "Lng": 145.05433
                    },
                    {
                        "Lat": -37.29306,
                        "Lng": 145.05433
                    },
                    {
                        "Lat": -37.2914,
                        "Lng": 145.0545
                    },
                    {
                        "Lat": -37.2914,
                        "Lng": 145.0545
                    },
                    {
                        "Lat": -37.29117,
                        "Lng": 145.05452
                    },
                    {
                        "Lat": -37.29117,
                        "Lng": 145.05452
                    },
                    {
                        "Lat": -37.29058,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.29058,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.289,
                        "Lng": 145.05472
                    },
                    {
                        "Lat": -37.289,
                        "Lng": 145.05472
                    },
                    {
                        "Lat": -37.28717,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.28665,
                        "Lng": 145.05494
                    },
                    {
                        "Lat": -37.28665,
                        "Lng": 145.05494
                    },
                    {
                        "Lat": -37.28641,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.28641,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.28619,
                        "Lng": 145.05499
                    },
                    {
                        "Lat": -37.28619,
                        "Lng": 145.05499
                    },
                    {
                        "Lat": -37.28295,
                        "Lng": 145.05529
                    },
                    {
                        "Lat": -37.28295,
                        "Lng": 145.05529
                    },
                    {
                        "Lat": -37.28262,
                        "Lng": 145.05532
                    },
                    {
                        "Lat": -37.28262,
                        "Lng": 145.05532
                    },
                    {
                        "Lat": -37.28129,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.28129,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.28092,
                        "Lng": 145.05548
                    },
                    {
                        "Lat": -37.28092,
                        "Lng": 145.05548
                    },
                    {
                        "Lat": -37.28069,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.28069,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.27919,
                        "Lng": 145.05565
                    },
                    {
                        "Lat": -37.27919,
                        "Lng": 145.05565
                    },
                    {
                        "Lat": -37.27848,
                        "Lng": 145.05571
                    },
                    {
                        "Lat": -37.27848,
                        "Lng": 145.05571
                    },
                    {
                        "Lat": -37.27804,
                        "Lng": 145.05576
                    },
                    {
                        "Lat": -37.27703,
                        "Lng": 145.05582
                    },
                    {
                        "Lat": -37.27638,
                        "Lng": 145.05584
                    },
                    {
                        "Lat": -37.27638,
                        "Lng": 145.05584
                    },
                    {
                        "Lat": -37.27605,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27605,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27535,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27535,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27463,
                        "Lng": 145.05585
                    },
                    {
                        "Lat": -37.27392,
                        "Lng": 145.05583
                    },
                    {
                        "Lat": -37.27293,
                        "Lng": 145.05578
                    },
                    {
                        "Lat": -37.27152,
                        "Lng": 145.05566
                    },
                    {
                        "Lat": -37.27015,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.27015,
                        "Lng": 145.05551
                    },
                    {
                        "Lat": -37.26924,
                        "Lng": 145.05545
                    },
                    {
                        "Lat": -37.2682,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.26765,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.26668,
                        "Lng": 145.05546
                    },
                    {
                        "Lat": -37.26527,
                        "Lng": 145.05559
                    },
                    {
                        "Lat": -37.26527,
                        "Lng": 145.05559
                    },
                    {
                        "Lat": -37.26425,
                        "Lng": 145.05575
                    },
                    {
                        "Lat": -37.26338,
                        "Lng": 145.05592
                    },
                    {
                        "Lat": -37.26219,
                        "Lng": 145.05618
                    },
                    {
                        "Lat": -37.26121,
                        "Lng": 145.05638
                    },
                    {
                        "Lat": -37.26068,
                        "Lng": 145.05645
                    },
                    {
                        "Lat": -37.26068,
                        "Lng": 145.05645
                    },
                    {
                        "Lat": -37.26034,
                        "Lng": 145.05649
                    },
                    {
                        "Lat": -37.2596,
                        "Lng": 145.05653
                    },
                    {
                        "Lat": -37.25919,
                        "Lng": 145.05653
                    },
                    {
                        "Lat": -37.25876,
                        "Lng": 145.05651
                    },
                    {
                        "Lat": -37.25822,
                        "Lng": 145.05647
                    },
                    {
                        "Lat": -37.25779,
                        "Lng": 145.05642
                    },
                    {
                        "Lat": -37.25717,
                        "Lng": 145.05631
                    },
                    {
                        "Lat": -37.25681,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.25681,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.25451,
                        "Lng": 145.05564
                    },
                    {
                        "Lat": -37.25335,
                        "Lng": 145.05541
                    },
                    {
                        "Lat": -37.25335,
                        "Lng": 145.05541
                    },
                    {
                        "Lat": -37.25282,
                        "Lng": 145.05533
                    },
                    {
                        "Lat": -37.25207,
                        "Lng": 145.05525
                    },
                    {
                        "Lat": -37.25127,
                        "Lng": 145.05519
                    },
                    {
                        "Lat": -37.25078,
                        "Lng": 145.05517
                    },
                    {
                        "Lat": -37.25078,
                        "Lng": 145.05517
                    },
                    {
                        "Lat": -37.24967,
                        "Lng": 145.05519
                    },
                    {
                        "Lat": -37.2484,
                        "Lng": 145.05524
                    },
                    {
                        "Lat": -37.2484,
                        "Lng": 145.05524
                    },
                    {
                        "Lat": -37.24767,
                        "Lng": 145.05526
                    },
                    {
                        "Lat": -37.24701,
                        "Lng": 145.05526
                    },
                    {
                        "Lat": -37.24637,
                        "Lng": 145.05523
                    },
                    {
                        "Lat": -37.2456,
                        "Lng": 145.05516
                    },
                    {
                        "Lat": -37.24484,
                        "Lng": 145.05505
                    },
                    {
                        "Lat": -37.24431,
                        "Lng": 145.05495
                    },
                    {
                        "Lat": -37.24343,
                        "Lng": 145.05474
                    },
                    {
                        "Lat": -37.24234,
                        "Lng": 145.05441
                    },
                    {
                        "Lat": -37.24157,
                        "Lng": 145.05416
                    },
                    {
                        "Lat": -37.24157,
                        "Lng": 145.05416
                    },
                    {
                        "Lat": -37.24142,
                        "Lng": 145.05411
                    },
                    {
                        "Lat": -37.24142,
                        "Lng": 145.05411
                    },
                    {
                        "Lat": -37.24128,
                        "Lng": 145.05406
                    },
                    {
                        "Lat": -37.24128,
                        "Lng": 145.05406
                    },
                    {
                        "Lat": -37.24033,
                        "Lng": 145.05378
                    },
                    {
                        "Lat": -37.23953,
                        "Lng": 145.0536
                    },
                    {
                        "Lat": -37.23916,
                        "Lng": 145.05354
                    },
                    {
                        "Lat": -37.23877,
                        "Lng": 145.0535
                    },
                    {
                        "Lat": -37.23833,
                        "Lng": 145.05347
                    },
                    {
                        "Lat": -37.23774,
                        "Lng": 145.05345
                    },
                    {
                        "Lat": -37.23726,
                        "Lng": 145.05346
                    },
                    {
                        "Lat": -37.23666,
                        "Lng": 145.05351
                    },
                    {
                        "Lat": -37.23577,
                        "Lng": 145.05366
                    },
                    {
                        "Lat": -37.23553,
                        "Lng": 145.05371
                    },
                    {
                        "Lat": -37.23487,
                        "Lng": 145.05388
                    },
                    {
                        "Lat": -37.23451,
                        "Lng": 145.054
                    },
                    {
                        "Lat": -37.23397,
                        "Lng": 145.0542
                    },
                    {
                        "Lat": -37.23344,
                        "Lng": 145.05443
                    },
                    {
                        "Lat": -37.23315,
                        "Lng": 145.05457
                    },
                    {
                        "Lat": -37.23242,
                        "Lng": 145.05496
                    },
                    {
                        "Lat": -37.23202,
                        "Lng": 145.0552
                    },
                    {
                        "Lat": -37.23169,
                        "Lng": 145.05542
                    },
                    {
                        "Lat": -37.23128,
                        "Lng": 145.05573
                    },
                    {
                        "Lat": -37.23104,
                        "Lng": 145.05592
                    },
                    {
                        "Lat": -37.23044,
                        "Lng": 145.05644
                    },
                    {
                        "Lat": -37.22856,
                        "Lng": 145.05821
                    },
                    {
                        "Lat": -37.22773,
                        "Lng": 145.05894
                    },
                    {
                        "Lat": -37.22647,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.22607,
                        "Lng": 145.06027
                    },
                    {
                        "Lat": -37.22539,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.22539,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.22526,
                        "Lng": 145.06086
                    },
                    {
                        "Lat": -37.22526,
                        "Lng": 145.06086
                    },
                    {
                        "Lat": -37.22462,
                        "Lng": 145.0613
                    },
                    {
                        "Lat": -37.22381,
                        "Lng": 145.06182
                    },
                    {
                        "Lat": -37.22222,
                        "Lng": 145.06274
                    },
                    {
                        "Lat": -37.22104,
                        "Lng": 145.06334
                    },
                    {
                        "Lat": -37.22037,
                        "Lng": 145.06366
                    },
                    {
                        "Lat": -37.21946,
                        "Lng": 145.06406
                    },
                    {
                        "Lat": -37.21874,
                        "Lng": 145.06434
                    },
                    {
                        "Lat": -37.21762,
                        "Lng": 145.06473
                    },
                    {
                        "Lat": -37.21657,
                        "Lng": 145.06505
                    },
                    {
                        "Lat": -37.21451,
                        "Lng": 145.06561
                    },
                    {
                        "Lat": -37.21387,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.21387,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.21325,
                        "Lng": 145.066
                    },
                    {
                        "Lat": -37.21249,
                        "Lng": 145.06627
                    },
                    {
                        "Lat": -37.21249,
                        "Lng": 145.06627
                    },
                    {
                        "Lat": -37.21163,
                        "Lng": 145.0666
                    },
                    {
                        "Lat": -37.21163,
                        "Lng": 145.0666
                    },
                    {
                        "Lat": -37.21137,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.21137,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.21058,
                        "Lng": 145.06705
                    },
                    {
                        "Lat": -37.21058,
                        "Lng": 145.06705
                    },
                    {
                        "Lat": -37.21028,
                        "Lng": 145.06719
                    },
                    {
                        "Lat": -37.21028,
                        "Lng": 145.06719
                    },
                    {
                        "Lat": -37.20957,
                        "Lng": 145.06753
                    },
                    {
                        "Lat": -37.20957,
                        "Lng": 145.06753
                    },
                    {
                        "Lat": -37.20802,
                        "Lng": 145.06831
                    },
                    {
                        "Lat": -37.20802,
                        "Lng": 145.06831
                    },
                    {
                        "Lat": -37.20741,
                        "Lng": 145.06861
                    },
                    {
                        "Lat": -37.20668,
                        "Lng": 145.06894
                    },
                    {
                        "Lat": -37.20668,
                        "Lng": 145.06894
                    },
                    {
                        "Lat": -37.20587,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.20495,
                        "Lng": 145.06959
                    },
                    {
                        "Lat": -37.20411,
                        "Lng": 145.06985
                    },
                    {
                        "Lat": -37.20255,
                        "Lng": 145.07024
                    },
                    {
                        "Lat": -37.20176,
                        "Lng": 145.07039
                    },
                    {
                        "Lat": -37.20086,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.20032,
                        "Lng": 145.0706
                    },
                    {
                        "Lat": -37.19922,
                        "Lng": 145.0707
                    },
                    {
                        "Lat": -37.19831,
                        "Lng": 145.07073
                    },
                    {
                        "Lat": -37.19735,
                        "Lng": 145.07073
                    },
                    {
                        "Lat": -37.19651,
                        "Lng": 145.07069
                    },
                    {
                        "Lat": -37.19554,
                        "Lng": 145.0706
                    },
                    {
                        "Lat": -37.19498,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.19498,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.1943,
                        "Lng": 145.07043
                    },
                    {
                        "Lat": -37.19357,
                        "Lng": 145.07029
                    },
                    {
                        "Lat": -37.19357,
                        "Lng": 145.07029
                    },
                    {
                        "Lat": -37.19221,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.19221,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.19163,
                        "Lng": 145.06987
                    },
                    {
                        "Lat": -37.19163,
                        "Lng": 145.06987
                    },
                    {
                        "Lat": -37.19089,
                        "Lng": 145.06971
                    },
                    {
                        "Lat": -37.19089,
                        "Lng": 145.06971
                    },
                    {
                        "Lat": -37.19048,
                        "Lng": 145.06963
                    },
                    {
                        "Lat": -37.19048,
                        "Lng": 145.06963
                    },
                    {
                        "Lat": -37.18996,
                        "Lng": 145.06954
                    },
                    {
                        "Lat": -37.18996,
                        "Lng": 145.06954
                    },
                    {
                        "Lat": -37.18946,
                        "Lng": 145.06946
                    },
                    {
                        "Lat": -37.18946,
                        "Lng": 145.06946
                    },
                    {
                        "Lat": -37.18798,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.18798,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.18665,
                        "Lng": 145.06913
                    },
                    {
                        "Lat": -37.18513,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18513,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18339,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18339,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18233,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18233,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18204,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18204,
                        "Lng": 145.06899
                    },
                    {
                        "Lat": -37.18151,
                        "Lng": 145.069
                    },
                    {
                        "Lat": -37.18151,
                        "Lng": 145.069
                    },
                    {
                        "Lat": -37.18093,
                        "Lng": 145.06904
                    },
                    {
                        "Lat": -37.18038,
                        "Lng": 145.06914
                    },
                    {
                        "Lat": -37.18007,
                        "Lng": 145.06921
                    },
                    {
                        "Lat": -37.17959,
                        "Lng": 145.06936
                    },
                    {
                        "Lat": -37.17939,
                        "Lng": 145.06944
                    },
                    {
                        "Lat": -37.17939,
                        "Lng": 145.06944
                    },
                    {
                        "Lat": -37.17909,
                        "Lng": 145.06956
                    },
                    {
                        "Lat": -37.17884,
                        "Lng": 145.06968
                    },
                    {
                        "Lat": -37.17825,
                        "Lng": 145.06999
                    },
                    {
                        "Lat": -37.17784,
                        "Lng": 145.07026
                    },
                    {
                        "Lat": -37.17747,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.17747,
                        "Lng": 145.07053
                    },
                    {
                        "Lat": -37.17671,
                        "Lng": 145.07112
                    },
                    {
                        "Lat": -37.17671,
                        "Lng": 145.07112
                    },
                    {
                        "Lat": -37.17637,
                        "Lng": 145.07138
                    },
                    {
                        "Lat": -37.17637,
                        "Lng": 145.07138
                    },
                    {
                        "Lat": -37.17594,
                        "Lng": 145.07172
                    },
                    {
                        "Lat": -37.17594,
                        "Lng": 145.07172
                    },
                    {
                        "Lat": -37.17574,
                        "Lng": 145.07188
                    },
                    {
                        "Lat": -37.17574,
                        "Lng": 145.07188
                    },
                    {
                        "Lat": -37.17409,
                        "Lng": 145.07318
                    },
                    {
                        "Lat": -37.17409,
                        "Lng": 145.07318
                    },
                    {
                        "Lat": -37.17249,
                        "Lng": 145.07442
                    },
                    {
                        "Lat": -37.1719,
                        "Lng": 145.07484
                    },
                    {
                        "Lat": -37.17139,
                        "Lng": 145.07518
                    },
                    {
                        "Lat": -37.17089,
                        "Lng": 145.07548
                    },
                    {
                        "Lat": -37.17025,
                        "Lng": 145.07583
                    },
                    {
                        "Lat": -37.16935,
                        "Lng": 145.07625
                    },
                    {
                        "Lat": -37.16895,
                        "Lng": 145.07642
                    },
                    {
                        "Lat": -37.16845,
                        "Lng": 145.0766
                    },
                    {
                        "Lat": -37.16845,
                        "Lng": 145.0766
                    },
                    {
                        "Lat": -37.16842,
                        "Lng": 145.07661
                    },
                    {
                        "Lat": -37.16842,
                        "Lng": 145.07661
                    },
                    {
                        "Lat": -37.16772,
                        "Lng": 145.07684
                    },
                    {
                        "Lat": -37.16716,
                        "Lng": 145.07698
                    },
                    {
                        "Lat": -37.16658,
                        "Lng": 145.07712
                    },
                    {
                        "Lat": -37.16563,
                        "Lng": 145.07731
                    },
                    {
                        "Lat": -37.16495,
                        "Lng": 145.07746
                    },
                    {
                        "Lat": -37.16495,
                        "Lng": 145.07746
                    },
                    {
                        "Lat": -37.16346,
                        "Lng": 145.07778
                    },
                    {
                        "Lat": -37.16246,
                        "Lng": 145.07795
                    },
                    {
                        "Lat": -37.16064,
                        "Lng": 145.07821
                    },
                    {
                        "Lat": -37.16024,
                        "Lng": 145.07825
                    },
                    {
                        "Lat": -37.16024,
                        "Lng": 145.07825
                    },
                    {
                        "Lat": -37.15614,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.15614,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.15523,
                        "Lng": 145.07886
                    },
                    {
                        "Lat": -37.15476,
                        "Lng": 145.07896
                    },
                    {
                        "Lat": -37.15376,
                        "Lng": 145.07924
                    },
                    {
                        "Lat": -37.15376,
                        "Lng": 145.07924
                    },
                    {
                        "Lat": -37.15302,
                        "Lng": 145.07948
                    },
                    {
                        "Lat": -37.15302,
                        "Lng": 145.07948
                    },
                    {
                        "Lat": -37.15232,
                        "Lng": 145.07972
                    },
                    {
                        "Lat": -37.14678,
                        "Lng": 145.08152
                    },
                    {
                        "Lat": -37.14678,
                        "Lng": 145.08152
                    },
                    {
                        "Lat": -37.14578,
                        "Lng": 145.08186
                    },
                    {
                        "Lat": -37.14282,
                        "Lng": 145.08283
                    },
                    {
                        "Lat": -37.14282,
                        "Lng": 145.08283
                    },
                    {
                        "Lat": -37.14111,
                        "Lng": 145.0834
                    },
                    {
                        "Lat": -37.13576,
                        "Lng": 145.08515
                    },
                    {
                        "Lat": -37.13506,
                        "Lng": 145.08544
                    },
                    {
                        "Lat": -37.13467,
                        "Lng": 145.08564
                    },
                    {
                        "Lat": -37.13396,
                        "Lng": 145.08606
                    },
                    {
                        "Lat": -37.13247,
                        "Lng": 145.08703
                    },
                    {
                        "Lat": -37.13247,
                        "Lng": 145.08703
                    },
                    {
                        "Lat": -37.13153,
                        "Lng": 145.08764
                    },
                    {
                        "Lat": -37.13082,
                        "Lng": 145.08806
                    },
                    {
                        "Lat": -37.13082,
                        "Lng": 145.08806
                    },
                    {
                        "Lat": -37.13058,
                        "Lng": 145.0882
                    },
                    {
                        "Lat": -37.13058,
                        "Lng": 145.0882
                    },
                    {
                        "Lat": -37.13037,
                        "Lng": 145.08831
                    },
                    {
                        "Lat": -37.13037,
                        "Lng": 145.08831
                    },
                    {
                        "Lat": -37.12997,
                        "Lng": 145.0885
                    },
                    {
                        "Lat": -37.12997,
                        "Lng": 145.0885
                    },
                    {
                        "Lat": -37.12982,
                        "Lng": 145.08857
                    },
                    {
                        "Lat": -37.12955,
                        "Lng": 145.08867
                    }
                ]
            },
            {
                "Key": "inc-mel-02",
                "Priority": 4,
                "Headline": "Sunshine Avenue — roadworks",
                "Detail": "Between Sunshine Avenue and Keilor Park Drive - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 22563,
                "Latitude": -37.57194,
                "Longitude": 144.67614,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.57194,
                        "Lng": 144.67614
                    },
                    {
                        "Lat": -37.57221,
                        "Lng": 144.67631
                    },
                    {
                        "Lat": -37.57221,
                        "Lng": 144.67631
                    },
                    {
                        "Lat": -37.57259,
                        "Lng": 144.67659
                    },
                    {
                        "Lat": -37.57259,
                        "Lng": 144.67659
                    },
                    {
                        "Lat": -37.57404,
                        "Lng": 144.67768
                    },
                    {
                        "Lat": -37.57404,
                        "Lng": 144.67768
                    },
                    {
                        "Lat": -37.57578,
                        "Lng": 144.67903
                    },
                    {
                        "Lat": -37.57578,
                        "Lng": 144.67903
                    },
                    {
                        "Lat": -37.5763,
                        "Lng": 144.67946
                    },
                    {
                        "Lat": -37.5763,
                        "Lng": 144.67946
                    },
                    {
                        "Lat": -37.57731,
                        "Lng": 144.68027
                    },
                    {
                        "Lat": -37.57731,
                        "Lng": 144.68027
                    },
                    {
                        "Lat": -37.57878,
                        "Lng": 144.68144
                    },
                    {
                        "Lat": -37.57878,
                        "Lng": 144.68144
                    },
                    {
                        "Lat": -37.57937,
                        "Lng": 144.68192
                    },
                    {
                        "Lat": -37.57937,
                        "Lng": 144.68192
                    },
                    {
                        "Lat": -37.57969,
                        "Lng": 144.68218
                    },
                    {
                        "Lat": -37.57982,
                        "Lng": 144.68227
                    },
                    {
                        "Lat": -37.57982,
                        "Lng": 144.68227
                    },
                    {
                        "Lat": -37.58045,
                        "Lng": 144.68272
                    },
                    {
                        "Lat": -37.58045,
                        "Lng": 144.68272
                    },
                    {
                        "Lat": -37.58084,
                        "Lng": 144.683
                    },
                    {
                        "Lat": -37.58084,
                        "Lng": 144.683
                    },
                    {
                        "Lat": -37.58143,
                        "Lng": 144.68342
                    },
                    {
                        "Lat": -37.58143,
                        "Lng": 144.68342
                    },
                    {
                        "Lat": -37.58212,
                        "Lng": 144.68392
                    },
                    {
                        "Lat": -37.58212,
                        "Lng": 144.68392
                    },
                    {
                        "Lat": -37.58238,
                        "Lng": 144.68412
                    },
                    {
                        "Lat": -37.58238,
                        "Lng": 144.68412
                    },
                    {
                        "Lat": -37.58264,
                        "Lng": 144.68431
                    },
                    {
                        "Lat": -37.58371,
                        "Lng": 144.68501
                    },
                    {
                        "Lat": -37.58465,
                        "Lng": 144.6856
                    },
                    {
                        "Lat": -37.58662,
                        "Lng": 144.6868
                    },
                    {
                        "Lat": -37.58771,
                        "Lng": 144.68749
                    },
                    {
                        "Lat": -37.58771,
                        "Lng": 144.68749
                    },
                    {
                        "Lat": -37.59027,
                        "Lng": 144.68919
                    },
                    {
                        "Lat": -37.59133,
                        "Lng": 144.68994
                    },
                    {
                        "Lat": -37.59222,
                        "Lng": 144.69058
                    },
                    {
                        "Lat": -37.59345,
                        "Lng": 144.69149
                    },
                    {
                        "Lat": -37.59542,
                        "Lng": 144.693
                    },
                    {
                        "Lat": -37.60382,
                        "Lng": 144.69984
                    },
                    {
                        "Lat": -37.60492,
                        "Lng": 144.7008
                    },
                    {
                        "Lat": -37.60492,
                        "Lng": 144.7008
                    },
                    {
                        "Lat": -37.60562,
                        "Lng": 144.70144
                    },
                    {
                        "Lat": -37.60562,
                        "Lng": 144.70144
                    },
                    {
                        "Lat": -37.60637,
                        "Lng": 144.70215
                    },
                    {
                        "Lat": -37.60667,
                        "Lng": 144.70245
                    },
                    {
                        "Lat": -37.60667,
                        "Lng": 144.70245
                    },
                    {
                        "Lat": -37.60749,
                        "Lng": 144.70325
                    },
                    {
                        "Lat": -37.60749,
                        "Lng": 144.70325
                    },
                    {
                        "Lat": -37.60836,
                        "Lng": 144.70412
                    },
                    {
                        "Lat": -37.60836,
                        "Lng": 144.70412
                    },
                    {
                        "Lat": -37.61077,
                        "Lng": 144.70648
                    },
                    {
                        "Lat": -37.61077,
                        "Lng": 144.70648
                    },
                    {
                        "Lat": -37.61121,
                        "Lng": 144.70692
                    },
                    {
                        "Lat": -37.61121,
                        "Lng": 144.70692
                    },
                    {
                        "Lat": -37.6122,
                        "Lng": 144.70789
                    },
                    {
                        "Lat": -37.6122,
                        "Lng": 144.70789
                    },
                    {
                        "Lat": -37.61302,
                        "Lng": 144.7087
                    },
                    {
                        "Lat": -37.61302,
                        "Lng": 144.7087
                    },
                    {
                        "Lat": -37.61316,
                        "Lng": 144.70884
                    },
                    {
                        "Lat": -37.61316,
                        "Lng": 144.70884
                    },
                    {
                        "Lat": -37.61325,
                        "Lng": 144.70892
                    },
                    {
                        "Lat": -37.61325,
                        "Lng": 144.70892
                    },
                    {
                        "Lat": -37.6138,
                        "Lng": 144.70946
                    },
                    {
                        "Lat": -37.61426,
                        "Lng": 144.70988
                    },
                    {
                        "Lat": -37.61426,
                        "Lng": 144.70988
                    },
                    {
                        "Lat": -37.61471,
                        "Lng": 144.71029
                    },
                    {
                        "Lat": -37.61471,
                        "Lng": 144.71029
                    },
                    {
                        "Lat": -37.61537,
                        "Lng": 144.71088
                    },
                    {
                        "Lat": -37.61537,
                        "Lng": 144.71088
                    },
                    {
                        "Lat": -37.61601,
                        "Lng": 144.71142
                    },
                    {
                        "Lat": -37.61601,
                        "Lng": 144.71142
                    },
                    {
                        "Lat": -37.61631,
                        "Lng": 144.71167
                    },
                    {
                        "Lat": -37.61631,
                        "Lng": 144.71167
                    },
                    {
                        "Lat": -37.61688,
                        "Lng": 144.71214
                    },
                    {
                        "Lat": -37.61688,
                        "Lng": 144.71214
                    },
                    {
                        "Lat": -37.618,
                        "Lng": 144.71305
                    },
                    {
                        "Lat": -37.618,
                        "Lng": 144.71305
                    },
                    {
                        "Lat": -37.61968,
                        "Lng": 144.71441
                    },
                    {
                        "Lat": -37.61968,
                        "Lng": 144.71441
                    },
                    {
                        "Lat": -37.62009,
                        "Lng": 144.71478
                    },
                    {
                        "Lat": -37.62044,
                        "Lng": 144.71513
                    },
                    {
                        "Lat": -37.62044,
                        "Lng": 144.71513
                    },
                    {
                        "Lat": -37.6206,
                        "Lng": 144.71529
                    },
                    {
                        "Lat": -37.62099,
                        "Lng": 144.71575
                    },
                    {
                        "Lat": -37.62099,
                        "Lng": 144.71575
                    },
                    {
                        "Lat": -37.62152,
                        "Lng": 144.71647
                    },
                    {
                        "Lat": -37.62186,
                        "Lng": 144.71701
                    },
                    {
                        "Lat": -37.62215,
                        "Lng": 144.71756
                    },
                    {
                        "Lat": -37.62247,
                        "Lng": 144.71821
                    },
                    {
                        "Lat": -37.6227,
                        "Lng": 144.71874
                    },
                    {
                        "Lat": -37.6227,
                        "Lng": 144.71874
                    },
                    {
                        "Lat": -37.62279,
                        "Lng": 144.71893
                    },
                    {
                        "Lat": -37.62279,
                        "Lng": 144.71893
                    },
                    {
                        "Lat": -37.6229,
                        "Lng": 144.71916
                    },
                    {
                        "Lat": -37.6229,
                        "Lng": 144.71916
                    },
                    {
                        "Lat": -37.62308,
                        "Lng": 144.71957
                    },
                    {
                        "Lat": -37.62349,
                        "Lng": 144.72043
                    },
                    {
                        "Lat": -37.62388,
                        "Lng": 144.72119
                    },
                    {
                        "Lat": -37.62417,
                        "Lng": 144.7217
                    },
                    {
                        "Lat": -37.62455,
                        "Lng": 144.72228
                    },
                    {
                        "Lat": -37.6249,
                        "Lng": 144.72276
                    },
                    {
                        "Lat": -37.62525,
                        "Lng": 144.7232
                    },
                    {
                        "Lat": -37.62565,
                        "Lng": 144.72365
                    },
                    {
                        "Lat": -37.62605,
                        "Lng": 144.72406
                    },
                    {
                        "Lat": -37.62644,
                        "Lng": 144.72442
                    },
                    {
                        "Lat": -37.62685,
                        "Lng": 144.72476
                    },
                    {
                        "Lat": -37.62781,
                        "Lng": 144.72542
                    },
                    {
                        "Lat": -37.62837,
                        "Lng": 144.72577
                    },
                    {
                        "Lat": -37.62837,
                        "Lng": 144.72577
                    },
                    {
                        "Lat": -37.62964,
                        "Lng": 144.72655
                    },
                    {
                        "Lat": -37.62964,
                        "Lng": 144.72655
                    },
                    {
                        "Lat": -37.63064,
                        "Lng": 144.72716
                    },
                    {
                        "Lat": -37.63064,
                        "Lng": 144.72716
                    },
                    {
                        "Lat": -37.63089,
                        "Lng": 144.72731
                    },
                    {
                        "Lat": -37.63089,
                        "Lng": 144.72731
                    },
                    {
                        "Lat": -37.63176,
                        "Lng": 144.72784
                    },
                    {
                        "Lat": -37.63176,
                        "Lng": 144.72784
                    },
                    {
                        "Lat": -37.63215,
                        "Lng": 144.72808
                    },
                    {
                        "Lat": -37.63215,
                        "Lng": 144.72808
                    },
                    {
                        "Lat": -37.63275,
                        "Lng": 144.72845
                    },
                    {
                        "Lat": -37.63275,
                        "Lng": 144.72845
                    },
                    {
                        "Lat": -37.63779,
                        "Lng": 144.73154
                    },
                    {
                        "Lat": -37.63779,
                        "Lng": 144.73154
                    },
                    {
                        "Lat": -37.6385,
                        "Lng": 144.73199
                    },
                    {
                        "Lat": -37.6385,
                        "Lng": 144.73199
                    },
                    {
                        "Lat": -37.63899,
                        "Lng": 144.73229
                    },
                    {
                        "Lat": -37.63899,
                        "Lng": 144.73229
                    },
                    {
                        "Lat": -37.63916,
                        "Lng": 144.73239
                    },
                    {
                        "Lat": -37.63916,
                        "Lng": 144.73239
                    },
                    {
                        "Lat": -37.63954,
                        "Lng": 144.73262
                    },
                    {
                        "Lat": -37.63954,
                        "Lng": 144.73262
                    },
                    {
                        "Lat": -37.63982,
                        "Lng": 144.73279
                    },
                    {
                        "Lat": -37.63982,
                        "Lng": 144.73279
                    },
                    {
                        "Lat": -37.6407,
                        "Lng": 144.73332
                    },
                    {
                        "Lat": -37.6407,
                        "Lng": 144.73332
                    },
                    {
                        "Lat": -37.64256,
                        "Lng": 144.73438
                    },
                    {
                        "Lat": -37.64455,
                        "Lng": 144.7354
                    },
                    {
                        "Lat": -37.64455,
                        "Lng": 144.7354
                    },
                    {
                        "Lat": -37.64877,
                        "Lng": 144.73747
                    },
                    {
                        "Lat": -37.64877,
                        "Lng": 144.73747
                    },
                    {
                        "Lat": -37.65454,
                        "Lng": 144.7403
                    },
                    {
                        "Lat": -37.65454,
                        "Lng": 144.7403
                    },
                    {
                        "Lat": -37.65616,
                        "Lng": 144.7411
                    },
                    {
                        "Lat": -37.65616,
                        "Lng": 144.7411
                    },
                    {
                        "Lat": -37.65682,
                        "Lng": 144.74143
                    },
                    {
                        "Lat": -37.65682,
                        "Lng": 144.74143
                    },
                    {
                        "Lat": -37.65711,
                        "Lng": 144.74158
                    },
                    {
                        "Lat": -37.65711,
                        "Lng": 144.74158
                    },
                    {
                        "Lat": -37.65784,
                        "Lng": 144.74197
                    },
                    {
                        "Lat": -37.65868,
                        "Lng": 144.7425
                    },
                    {
                        "Lat": -37.6591,
                        "Lng": 144.7428
                    },
                    {
                        "Lat": -37.65948,
                        "Lng": 144.7431
                    },
                    {
                        "Lat": -37.65989,
                        "Lng": 144.74345
                    },
                    {
                        "Lat": -37.66063,
                        "Lng": 144.74415
                    },
                    {
                        "Lat": -37.66063,
                        "Lng": 144.74415
                    },
                    {
                        "Lat": -37.66111,
                        "Lng": 144.74464
                    },
                    {
                        "Lat": -37.66162,
                        "Lng": 144.74524
                    },
                    {
                        "Lat": -37.66205,
                        "Lng": 144.7458
                    },
                    {
                        "Lat": -37.66238,
                        "Lng": 144.74626
                    },
                    {
                        "Lat": -37.66238,
                        "Lng": 144.74626
                    },
                    {
                        "Lat": -37.66263,
                        "Lng": 144.74664
                    },
                    {
                        "Lat": -37.66323,
                        "Lng": 144.74764
                    },
                    {
                        "Lat": -37.66323,
                        "Lng": 144.74764
                    },
                    {
                        "Lat": -37.66359,
                        "Lng": 144.74832
                    },
                    {
                        "Lat": -37.66359,
                        "Lng": 144.74832
                    },
                    {
                        "Lat": -37.66386,
                        "Lng": 144.74885
                    },
                    {
                        "Lat": -37.66432,
                        "Lng": 144.74981
                    },
                    {
                        "Lat": -37.66432,
                        "Lng": 144.74981
                    },
                    {
                        "Lat": -37.66442,
                        "Lng": 144.75002
                    },
                    {
                        "Lat": -37.66442,
                        "Lng": 144.75002
                    },
                    {
                        "Lat": -37.66547,
                        "Lng": 144.75221
                    },
                    {
                        "Lat": -37.66547,
                        "Lng": 144.75221
                    },
                    {
                        "Lat": -37.6665,
                        "Lng": 144.75437
                    },
                    {
                        "Lat": -37.6665,
                        "Lng": 144.75437
                    },
                    {
                        "Lat": -37.6667,
                        "Lng": 144.75479
                    },
                    {
                        "Lat": -37.6667,
                        "Lng": 144.75479
                    },
                    {
                        "Lat": -37.66699,
                        "Lng": 144.75541
                    },
                    {
                        "Lat": -37.66699,
                        "Lng": 144.75541
                    },
                    {
                        "Lat": -37.66716,
                        "Lng": 144.75576
                    },
                    {
                        "Lat": -37.66716,
                        "Lng": 144.75576
                    },
                    {
                        "Lat": -37.6673,
                        "Lng": 144.75605
                    },
                    {
                        "Lat": -37.6673,
                        "Lng": 144.75605
                    },
                    {
                        "Lat": -37.66757,
                        "Lng": 144.75663
                    },
                    {
                        "Lat": -37.66757,
                        "Lng": 144.75663
                    },
                    {
                        "Lat": -37.66774,
                        "Lng": 144.75698
                    },
                    {
                        "Lat": -37.66774,
                        "Lng": 144.75698
                    },
                    {
                        "Lat": -37.66813,
                        "Lng": 144.7578
                    },
                    {
                        "Lat": -37.66813,
                        "Lng": 144.7578
                    },
                    {
                        "Lat": -37.6685,
                        "Lng": 144.75858
                    },
                    {
                        "Lat": -37.6685,
                        "Lng": 144.75858
                    },
                    {
                        "Lat": -37.66892,
                        "Lng": 144.75946
                    },
                    {
                        "Lat": -37.66892,
                        "Lng": 144.75946
                    },
                    {
                        "Lat": -37.66912,
                        "Lng": 144.75988
                    },
                    {
                        "Lat": -37.66912,
                        "Lng": 144.75988
                    },
                    {
                        "Lat": -37.66992,
                        "Lng": 144.76152
                    },
                    {
                        "Lat": -37.66992,
                        "Lng": 144.76152
                    },
                    {
                        "Lat": -37.67067,
                        "Lng": 144.76289
                    },
                    {
                        "Lat": -37.67067,
                        "Lng": 144.76289
                    },
                    {
                        "Lat": -37.67096,
                        "Lng": 144.7634
                    },
                    {
                        "Lat": -37.67096,
                        "Lng": 144.7634
                    },
                    {
                        "Lat": -37.67124,
                        "Lng": 144.76383
                    },
                    {
                        "Lat": -37.67124,
                        "Lng": 144.76383
                    },
                    {
                        "Lat": -37.67139,
                        "Lng": 144.76402
                    },
                    {
                        "Lat": -37.67139,
                        "Lng": 144.76402
                    },
                    {
                        "Lat": -37.67151,
                        "Lng": 144.76421
                    },
                    {
                        "Lat": -37.67151,
                        "Lng": 144.76421
                    },
                    {
                        "Lat": -37.67188,
                        "Lng": 144.76476
                    },
                    {
                        "Lat": -37.67188,
                        "Lng": 144.76476
                    },
                    {
                        "Lat": -37.67236,
                        "Lng": 144.76548
                    },
                    {
                        "Lat": -37.67236,
                        "Lng": 144.76548
                    },
                    {
                        "Lat": -37.67288,
                        "Lng": 144.76625
                    },
                    {
                        "Lat": -37.67288,
                        "Lng": 144.76625
                    },
                    {
                        "Lat": -37.67317,
                        "Lng": 144.76668
                    },
                    {
                        "Lat": -37.67317,
                        "Lng": 144.76668
                    },
                    {
                        "Lat": -37.67331,
                        "Lng": 144.76689
                    },
                    {
                        "Lat": -37.67331,
                        "Lng": 144.76689
                    },
                    {
                        "Lat": -37.67434,
                        "Lng": 144.76843
                    },
                    {
                        "Lat": -37.67434,
                        "Lng": 144.76843
                    },
                    {
                        "Lat": -37.67505,
                        "Lng": 144.76949
                    },
                    {
                        "Lat": -37.67505,
                        "Lng": 144.76949
                    },
                    {
                        "Lat": -37.67527,
                        "Lng": 144.76981
                    },
                    {
                        "Lat": -37.67527,
                        "Lng": 144.76981
                    },
                    {
                        "Lat": -37.67569,
                        "Lng": 144.77044
                    },
                    {
                        "Lat": -37.67569,
                        "Lng": 144.77044
                    },
                    {
                        "Lat": -37.67584,
                        "Lng": 144.77066
                    },
                    {
                        "Lat": -37.67584,
                        "Lng": 144.77066
                    },
                    {
                        "Lat": -37.67607,
                        "Lng": 144.771
                    },
                    {
                        "Lat": -37.67607,
                        "Lng": 144.771
                    },
                    {
                        "Lat": -37.67735,
                        "Lng": 144.77292
                    },
                    {
                        "Lat": -37.67735,
                        "Lng": 144.77292
                    },
                    {
                        "Lat": -37.67786,
                        "Lng": 144.77371
                    },
                    {
                        "Lat": -37.67786,
                        "Lng": 144.77371
                    },
                    {
                        "Lat": -37.67808,
                        "Lng": 144.77406
                    },
                    {
                        "Lat": -37.67808,
                        "Lng": 144.77406
                    },
                    {
                        "Lat": -37.6787,
                        "Lng": 144.77507
                    },
                    {
                        "Lat": -37.6787,
                        "Lng": 144.77507
                    },
                    {
                        "Lat": -37.68044,
                        "Lng": 144.77788
                    },
                    {
                        "Lat": -37.68044,
                        "Lng": 144.77788
                    },
                    {
                        "Lat": -37.68162,
                        "Lng": 144.7798
                    },
                    {
                        "Lat": -37.68162,
                        "Lng": 144.7798
                    },
                    {
                        "Lat": -37.68196,
                        "Lng": 144.78036
                    },
                    {
                        "Lat": -37.68196,
                        "Lng": 144.78036
                    },
                    {
                        "Lat": -37.68279,
                        "Lng": 144.78171
                    },
                    {
                        "Lat": -37.68279,
                        "Lng": 144.78171
                    },
                    {
                        "Lat": -37.68307,
                        "Lng": 144.78217
                    },
                    {
                        "Lat": -37.68307,
                        "Lng": 144.78217
                    },
                    {
                        "Lat": -37.68336,
                        "Lng": 144.78263
                    },
                    {
                        "Lat": -37.68336,
                        "Lng": 144.78263
                    },
                    {
                        "Lat": -37.68358,
                        "Lng": 144.78297
                    },
                    {
                        "Lat": -37.68358,
                        "Lng": 144.78297
                    },
                    {
                        "Lat": -37.68623,
                        "Lng": 144.78727
                    },
                    {
                        "Lat": -37.68623,
                        "Lng": 144.78727
                    },
                    {
                        "Lat": -37.68647,
                        "Lng": 144.78765
                    },
                    {
                        "Lat": -37.68647,
                        "Lng": 144.78765
                    },
                    {
                        "Lat": -37.68666,
                        "Lng": 144.78794
                    },
                    {
                        "Lat": -37.68666,
                        "Lng": 144.78794
                    },
                    {
                        "Lat": -37.68761,
                        "Lng": 144.78948
                    },
                    {
                        "Lat": -37.68761,
                        "Lng": 144.78948
                    },
                    {
                        "Lat": -37.68796,
                        "Lng": 144.79007
                    },
                    {
                        "Lat": -37.68796,
                        "Lng": 144.79007
                    },
                    {
                        "Lat": -37.68823,
                        "Lng": 144.79052
                    },
                    {
                        "Lat": -37.68823,
                        "Lng": 144.79052
                    },
                    {
                        "Lat": -37.68852,
                        "Lng": 144.79098
                    },
                    {
                        "Lat": -37.68852,
                        "Lng": 144.79098
                    },
                    {
                        "Lat": -37.68869,
                        "Lng": 144.79125
                    },
                    {
                        "Lat": -37.68869,
                        "Lng": 144.79125
                    },
                    {
                        "Lat": -37.68907,
                        "Lng": 144.79185
                    },
                    {
                        "Lat": -37.68907,
                        "Lng": 144.79185
                    },
                    {
                        "Lat": -37.68946,
                        "Lng": 144.79248
                    },
                    {
                        "Lat": -37.68946,
                        "Lng": 144.79248
                    },
                    {
                        "Lat": -37.69114,
                        "Lng": 144.79522
                    },
                    {
                        "Lat": -37.69114,
                        "Lng": 144.79522
                    },
                    {
                        "Lat": -37.69401,
                        "Lng": 144.79985
                    },
                    {
                        "Lat": -37.69401,
                        "Lng": 144.79985
                    },
                    {
                        "Lat": -37.69446,
                        "Lng": 144.80058
                    },
                    {
                        "Lat": -37.69446,
                        "Lng": 144.80058
                    },
                    {
                        "Lat": -37.69506,
                        "Lng": 144.80155
                    },
                    {
                        "Lat": -37.69506,
                        "Lng": 144.80155
                    },
                    {
                        "Lat": -37.6953,
                        "Lng": 144.80192
                    },
                    {
                        "Lat": -37.6953,
                        "Lng": 144.80192
                    },
                    {
                        "Lat": -37.69535,
                        "Lng": 144.802
                    },
                    {
                        "Lat": -37.69535,
                        "Lng": 144.802
                    },
                    {
                        "Lat": -37.69624,
                        "Lng": 144.80343
                    },
                    {
                        "Lat": -37.69624,
                        "Lng": 144.80343
                    },
                    {
                        "Lat": -37.69693,
                        "Lng": 144.80452
                    },
                    {
                        "Lat": -37.69693,
                        "Lng": 144.80452
                    },
                    {
                        "Lat": -37.69807,
                        "Lng": 144.80635
                    },
                    {
                        "Lat": -37.69807,
                        "Lng": 144.80635
                    },
                    {
                        "Lat": -37.69834,
                        "Lng": 144.80676
                    },
                    {
                        "Lat": -37.69834,
                        "Lng": 144.80676
                    },
                    {
                        "Lat": -37.6985,
                        "Lng": 144.80701
                    },
                    {
                        "Lat": -37.6985,
                        "Lng": 144.80701
                    },
                    {
                        "Lat": -37.69898,
                        "Lng": 144.80769
                    },
                    {
                        "Lat": -37.69898,
                        "Lng": 144.80769
                    },
                    {
                        "Lat": -37.69902,
                        "Lng": 144.80775
                    },
                    {
                        "Lat": -37.69902,
                        "Lng": 144.80775
                    },
                    {
                        "Lat": -37.69929,
                        "Lng": 144.80813
                    },
                    {
                        "Lat": -37.70003,
                        "Lng": 144.80912
                    },
                    {
                        "Lat": -37.70003,
                        "Lng": 144.80912
                    },
                    {
                        "Lat": -37.70104,
                        "Lng": 144.8105
                    },
                    {
                        "Lat": -37.70104,
                        "Lng": 144.8105
                    },
                    {
                        "Lat": -37.70224,
                        "Lng": 144.81212
                    },
                    {
                        "Lat": -37.70224,
                        "Lng": 144.81212
                    },
                    {
                        "Lat": -37.7036,
                        "Lng": 144.81395
                    },
                    {
                        "Lat": -37.7036,
                        "Lng": 144.81395
                    },
                    {
                        "Lat": -37.7058,
                        "Lng": 144.81693
                    },
                    {
                        "Lat": -37.7058,
                        "Lng": 144.81693
                    },
                    {
                        "Lat": -37.70605,
                        "Lng": 144.81727
                    },
                    {
                        "Lat": -37.70678,
                        "Lng": 144.81834
                    },
                    {
                        "Lat": -37.70678,
                        "Lng": 144.81834
                    },
                    {
                        "Lat": -37.70718,
                        "Lng": 144.81902
                    },
                    {
                        "Lat": -37.70718,
                        "Lng": 144.81902
                    },
                    {
                        "Lat": -37.7073,
                        "Lng": 144.81925
                    },
                    {
                        "Lat": -37.7073,
                        "Lng": 144.81925
                    },
                    {
                        "Lat": -37.70742,
                        "Lng": 144.81946
                    },
                    {
                        "Lat": -37.70742,
                        "Lng": 144.81946
                    },
                    {
                        "Lat": -37.7079,
                        "Lng": 144.82043
                    },
                    {
                        "Lat": -37.7079,
                        "Lng": 144.82043
                    },
                    {
                        "Lat": -37.70829,
                        "Lng": 144.82131
                    },
                    {
                        "Lat": -37.70829,
                        "Lng": 144.82131
                    },
                    {
                        "Lat": -37.70842,
                        "Lng": 144.82164
                    },
                    {
                        "Lat": -37.70842,
                        "Lng": 144.82164
                    },
                    {
                        "Lat": -37.70859,
                        "Lng": 144.82207
                    },
                    {
                        "Lat": -37.70859,
                        "Lng": 144.82207
                    },
                    {
                        "Lat": -37.70916,
                        "Lng": 144.82355
                    },
                    {
                        "Lat": -37.70954,
                        "Lng": 144.82458
                    },
                    {
                        "Lat": -37.70954,
                        "Lng": 144.82458
                    },
                    {
                        "Lat": -37.70998,
                        "Lng": 144.82575
                    },
                    {
                        "Lat": -37.70998,
                        "Lng": 144.82575
                    },
                    {
                        "Lat": -37.71005,
                        "Lng": 144.82593
                    },
                    {
                        "Lat": -37.71005,
                        "Lng": 144.82593
                    },
                    {
                        "Lat": -37.7102,
                        "Lng": 144.82634
                    },
                    {
                        "Lat": -37.7102,
                        "Lng": 144.82634
                    },
                    {
                        "Lat": -37.71054,
                        "Lng": 144.8272
                    },
                    {
                        "Lat": -37.71054,
                        "Lng": 144.8272
                    },
                    {
                        "Lat": -37.71085,
                        "Lng": 144.82801
                    },
                    {
                        "Lat": -37.71085,
                        "Lng": 144.82801
                    },
                    {
                        "Lat": -37.71122,
                        "Lng": 144.829
                    },
                    {
                        "Lat": -37.71154,
                        "Lng": 144.8298
                    },
                    {
                        "Lat": -37.71154,
                        "Lng": 144.8298
                    },
                    {
                        "Lat": -37.71163,
                        "Lng": 144.83001
                    },
                    {
                        "Lat": -37.71163,
                        "Lng": 144.83001
                    },
                    {
                        "Lat": -37.71182,
                        "Lng": 144.83043
                    },
                    {
                        "Lat": -37.71182,
                        "Lng": 144.83043
                    },
                    {
                        "Lat": -37.71201,
                        "Lng": 144.83083
                    },
                    {
                        "Lat": -37.71201,
                        "Lng": 144.83083
                    },
                    {
                        "Lat": -37.71213,
                        "Lng": 144.83108
                    },
                    {
                        "Lat": -37.71213,
                        "Lng": 144.83108
                    },
                    {
                        "Lat": -37.71219,
                        "Lng": 144.8312
                    },
                    {
                        "Lat": -37.71219,
                        "Lng": 144.8312
                    },
                    {
                        "Lat": -37.71228,
                        "Lng": 144.83138
                    },
                    {
                        "Lat": -37.71228,
                        "Lng": 144.83138
                    },
                    {
                        "Lat": -37.71269,
                        "Lng": 144.83218
                    },
                    {
                        "Lat": -37.71269,
                        "Lng": 144.83218
                    },
                    {
                        "Lat": -37.71304,
                        "Lng": 144.83278
                    },
                    {
                        "Lat": -37.71375,
                        "Lng": 144.83387
                    },
                    {
                        "Lat": -37.71375,
                        "Lng": 144.83387
                    },
                    {
                        "Lat": -37.71431,
                        "Lng": 144.83463
                    },
                    {
                        "Lat": -37.71431,
                        "Lng": 144.83463
                    },
                    {
                        "Lat": -37.71549,
                        "Lng": 144.83602
                    },
                    {
                        "Lat": -37.71549,
                        "Lng": 144.83602
                    },
                    {
                        "Lat": -37.71612,
                        "Lng": 144.83664
                    },
                    {
                        "Lat": -37.71612,
                        "Lng": 144.83664
                    },
                    {
                        "Lat": -37.71693,
                        "Lng": 144.83742
                    },
                    {
                        "Lat": -37.71693,
                        "Lng": 144.83742
                    },
                    {
                        "Lat": -37.71723,
                        "Lng": 144.83768
                    },
                    {
                        "Lat": -37.71723,
                        "Lng": 144.83768
                    },
                    {
                        "Lat": -37.71747,
                        "Lng": 144.83791
                    },
                    {
                        "Lat": -37.71747,
                        "Lng": 144.83791
                    },
                    {
                        "Lat": -37.71837,
                        "Lng": 144.83874
                    },
                    {
                        "Lat": -37.71837,
                        "Lng": 144.83874
                    },
                    {
                        "Lat": -37.71871,
                        "Lng": 144.83905
                    },
                    {
                        "Lat": -37.71871,
                        "Lng": 144.83905
                    },
                    {
                        "Lat": -37.71982,
                        "Lng": 144.84008
                    },
                    {
                        "Lat": -37.71982,
                        "Lng": 144.84008
                    },
                    {
                        "Lat": -37.72,
                        "Lng": 144.84024
                    },
                    {
                        "Lat": -37.72,
                        "Lng": 144.84024
                    },
                    {
                        "Lat": -37.72035,
                        "Lng": 144.84057
                    },
                    {
                        "Lat": -37.72035,
                        "Lng": 144.84057
                    },
                    {
                        "Lat": -37.72055,
                        "Lng": 144.84074
                    },
                    {
                        "Lat": -37.72055,
                        "Lng": 144.84074
                    },
                    {
                        "Lat": -37.72085,
                        "Lng": 144.84102
                    },
                    {
                        "Lat": -37.72085,
                        "Lng": 144.84102
                    },
                    {
                        "Lat": -37.72153,
                        "Lng": 144.84166
                    },
                    {
                        "Lat": -37.72153,
                        "Lng": 144.84166
                    },
                    {
                        "Lat": -37.72174,
                        "Lng": 144.84188
                    }
                ]
            },
            {
                "Key": "inc-mel-04",
                "Priority": 4,
                "Headline": "Toorak Road — roadworks",
                "Detail": "Between Toorak Road and Warrigal Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 5806,
                "Latitude": -37.84796,
                "Longitude": 145.0429,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.84796,
                        "Lng": 145.0429
                    },
                    {
                        "Lat": -37.84817,
                        "Lng": 145.04351
                    },
                    {
                        "Lat": -37.84831,
                        "Lng": 145.04408
                    },
                    {
                        "Lat": -37.84831,
                        "Lng": 145.04408
                    },
                    {
                        "Lat": -37.8484,
                        "Lng": 145.04448
                    },
                    {
                        "Lat": -37.8484,
                        "Lng": 145.04448
                    },
                    {
                        "Lat": -37.8486,
                        "Lng": 145.04544
                    },
                    {
                        "Lat": -37.8486,
                        "Lng": 145.04544
                    },
                    {
                        "Lat": -37.84872,
                        "Lng": 145.04597
                    },
                    {
                        "Lat": -37.84872,
                        "Lng": 145.04597
                    },
                    {
                        "Lat": -37.84898,
                        "Lng": 145.04693
                    },
                    {
                        "Lat": -37.84898,
                        "Lng": 145.04693
                    },
                    {
                        "Lat": -37.84921,
                        "Lng": 145.04759
                    },
                    {
                        "Lat": -37.84921,
                        "Lng": 145.04759
                    },
                    {
                        "Lat": -37.84944,
                        "Lng": 145.04812
                    },
                    {
                        "Lat": -37.84944,
                        "Lng": 145.04812
                    },
                    {
                        "Lat": -37.84963,
                        "Lng": 145.0485
                    },
                    {
                        "Lat": -37.84983,
                        "Lng": 145.04886
                    },
                    {
                        "Lat": -37.84996,
                        "Lng": 145.04908
                    },
                    {
                        "Lat": -37.85035,
                        "Lng": 145.04967
                    },
                    {
                        "Lat": -37.85035,
                        "Lng": 145.04967
                    },
                    {
                        "Lat": -37.85116,
                        "Lng": 145.05086
                    },
                    {
                        "Lat": -37.85116,
                        "Lng": 145.05086
                    },
                    {
                        "Lat": -37.85141,
                        "Lng": 145.0513
                    },
                    {
                        "Lat": -37.85161,
                        "Lng": 145.05169
                    },
                    {
                        "Lat": -37.85189,
                        "Lng": 145.05234
                    },
                    {
                        "Lat": -37.85189,
                        "Lng": 145.05234
                    },
                    {
                        "Lat": -37.85198,
                        "Lng": 145.05258
                    },
                    {
                        "Lat": -37.85198,
                        "Lng": 145.05258
                    },
                    {
                        "Lat": -37.85244,
                        "Lng": 145.05393
                    },
                    {
                        "Lat": -37.85271,
                        "Lng": 145.05462
                    },
                    {
                        "Lat": -37.85271,
                        "Lng": 145.05462
                    },
                    {
                        "Lat": -37.85283,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.85283,
                        "Lng": 145.0549
                    },
                    {
                        "Lat": -37.85312,
                        "Lng": 145.05546
                    },
                    {
                        "Lat": -37.85343,
                        "Lng": 145.05595
                    },
                    {
                        "Lat": -37.85343,
                        "Lng": 145.05595
                    },
                    {
                        "Lat": -37.85354,
                        "Lng": 145.0561
                    },
                    {
                        "Lat": -37.85354,
                        "Lng": 145.0561
                    },
                    {
                        "Lat": -37.85364,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.85364,
                        "Lng": 145.05623
                    },
                    {
                        "Lat": -37.85373,
                        "Lng": 145.05634
                    },
                    {
                        "Lat": -37.85373,
                        "Lng": 145.05634
                    },
                    {
                        "Lat": -37.85385,
                        "Lng": 145.05648
                    },
                    {
                        "Lat": -37.85385,
                        "Lng": 145.05648
                    },
                    {
                        "Lat": -37.85403,
                        "Lng": 145.05668
                    },
                    {
                        "Lat": -37.85403,
                        "Lng": 145.05668
                    },
                    {
                        "Lat": -37.85449,
                        "Lng": 145.05713
                    },
                    {
                        "Lat": -37.85449,
                        "Lng": 145.05713
                    },
                    {
                        "Lat": -37.85481,
                        "Lng": 145.0574
                    },
                    {
                        "Lat": -37.85481,
                        "Lng": 145.0574
                    },
                    {
                        "Lat": -37.855,
                        "Lng": 145.05754
                    },
                    {
                        "Lat": -37.855,
                        "Lng": 145.05754
                    },
                    {
                        "Lat": -37.85529,
                        "Lng": 145.05775
                    },
                    {
                        "Lat": -37.85579,
                        "Lng": 145.05806
                    },
                    {
                        "Lat": -37.85579,
                        "Lng": 145.05806
                    },
                    {
                        "Lat": -37.85624,
                        "Lng": 145.05829
                    },
                    {
                        "Lat": -37.85663,
                        "Lng": 145.05846
                    },
                    {
                        "Lat": -37.85663,
                        "Lng": 145.05846
                    },
                    {
                        "Lat": -37.85698,
                        "Lng": 145.05858
                    },
                    {
                        "Lat": -37.85916,
                        "Lng": 145.05922
                    },
                    {
                        "Lat": -37.85916,
                        "Lng": 145.05922
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05947
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05947
                    },
                    {
                        "Lat": -37.86096,
                        "Lng": 145.05974
                    },
                    {
                        "Lat": -37.86096,
                        "Lng": 145.05974
                    },
                    {
                        "Lat": -37.86173,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.86173,
                        "Lng": 145.05996
                    },
                    {
                        "Lat": -37.86363,
                        "Lng": 145.06051
                    },
                    {
                        "Lat": -37.86363,
                        "Lng": 145.06051
                    },
                    {
                        "Lat": -37.86514,
                        "Lng": 145.06095
                    },
                    {
                        "Lat": -37.86554,
                        "Lng": 145.06108
                    },
                    {
                        "Lat": -37.86583,
                        "Lng": 145.06119
                    },
                    {
                        "Lat": -37.86583,
                        "Lng": 145.06119
                    },
                    {
                        "Lat": -37.86615,
                        "Lng": 145.06133
                    },
                    {
                        "Lat": -37.86652,
                        "Lng": 145.06153
                    },
                    {
                        "Lat": -37.86671,
                        "Lng": 145.06165
                    },
                    {
                        "Lat": -37.86704,
                        "Lng": 145.06189
                    },
                    {
                        "Lat": -37.86741,
                        "Lng": 145.0622
                    },
                    {
                        "Lat": -37.86741,
                        "Lng": 145.0622
                    },
                    {
                        "Lat": -37.86879,
                        "Lng": 145.06346
                    },
                    {
                        "Lat": -37.86879,
                        "Lng": 145.06346
                    },
                    {
                        "Lat": -37.86912,
                        "Lng": 145.06375
                    },
                    {
                        "Lat": -37.86943,
                        "Lng": 145.064
                    },
                    {
                        "Lat": -37.86966,
                        "Lng": 145.06417
                    },
                    {
                        "Lat": -37.87016,
                        "Lng": 145.0645
                    },
                    {
                        "Lat": -37.8706,
                        "Lng": 145.06475
                    },
                    {
                        "Lat": -37.87091,
                        "Lng": 145.06491
                    },
                    {
                        "Lat": -37.87123,
                        "Lng": 145.06506
                    },
                    {
                        "Lat": -37.87159,
                        "Lng": 145.06521
                    },
                    {
                        "Lat": -37.87159,
                        "Lng": 145.06521
                    },
                    {
                        "Lat": -37.87296,
                        "Lng": 145.0658
                    },
                    {
                        "Lat": -37.87342,
                        "Lng": 145.06601
                    },
                    {
                        "Lat": -37.87342,
                        "Lng": 145.06601
                    },
                    {
                        "Lat": -37.87379,
                        "Lng": 145.06619
                    },
                    {
                        "Lat": -37.87403,
                        "Lng": 145.06633
                    },
                    {
                        "Lat": -37.87429,
                        "Lng": 145.0665
                    },
                    {
                        "Lat": -37.87472,
                        "Lng": 145.06684
                    },
                    {
                        "Lat": -37.87506,
                        "Lng": 145.06716
                    },
                    {
                        "Lat": -37.87506,
                        "Lng": 145.06716
                    },
                    {
                        "Lat": -37.87553,
                        "Lng": 145.06769
                    },
                    {
                        "Lat": -37.87583,
                        "Lng": 145.06811
                    },
                    {
                        "Lat": -37.87611,
                        "Lng": 145.06857
                    },
                    {
                        "Lat": -37.87635,
                        "Lng": 145.06905
                    },
                    {
                        "Lat": -37.87647,
                        "Lng": 145.06932
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.06985
                    },
                    {
                        "Lat": -37.87677,
                        "Lng": 145.07022
                    },
                    {
                        "Lat": -37.87677,
                        "Lng": 145.07022
                    },
                    {
                        "Lat": -37.87691,
                        "Lng": 145.07083
                    },
                    {
                        "Lat": -37.87699,
                        "Lng": 145.07133
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07166
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.07226
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.07254
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07312
                    },
                    {
                        "Lat": -37.87688,
                        "Lng": 145.07487
                    },
                    {
                        "Lat": -37.87688,
                        "Lng": 145.07487
                    },
                    {
                        "Lat": -37.87669,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87669,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.07729
                    },
                    {
                        "Lat": -37.87666,
                        "Lng": 145.07729
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.07767
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.0779
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.0779
                    },
                    {
                        "Lat": -37.87664,
                        "Lng": 145.07811
                    },
                    {
                        "Lat": -37.87667,
                        "Lng": 145.07852
                    },
                    {
                        "Lat": -37.87676,
                        "Lng": 145.07915
                    },
                    {
                        "Lat": -37.87676,
                        "Lng": 145.07915
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07946
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07946
                    },
                    {
                        "Lat": -37.87691,
                        "Lng": 145.07982
                    },
                    {
                        "Lat": -37.87706,
                        "Lng": 145.08027
                    },
                    {
                        "Lat": -37.87715,
                        "Lng": 145.0805
                    },
                    {
                        "Lat": -37.87727,
                        "Lng": 145.08079
                    },
                    {
                        "Lat": -37.87769,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87769,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87794,
                        "Lng": 145.08217
                    },
                    {
                        "Lat": -37.87794,
                        "Lng": 145.08217
                    },
                    {
                        "Lat": -37.87871,
                        "Lng": 145.08366
                    },
                    {
                        "Lat": -37.87871,
                        "Lng": 145.08366
                    },
                    {
                        "Lat": -37.87914,
                        "Lng": 145.08446
                    },
                    {
                        "Lat": -37.87952,
                        "Lng": 145.08512
                    },
                    {
                        "Lat": -37.87952,
                        "Lng": 145.08512
                    },
                    {
                        "Lat": -37.87995,
                        "Lng": 145.08588
                    },
                    {
                        "Lat": -37.88015,
                        "Lng": 145.0862
                    },
                    {
                        "Lat": -37.88015,
                        "Lng": 145.0862
                    },
                    {
                        "Lat": -37.88035,
                        "Lng": 145.08654
                    }
                ]
            },
            {
                "Key": "inc-mel-05",
                "Priority": 4,
                "Headline": "Huntingdale Road — roadworks",
                "Detail": "Between Huntingdale Road and Burke Road - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 4764,
                "Latitude": -37.88206,
                "Longitude": 145.08889,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -37.88206,
                        "Lng": 145.08889
                    },
                    {
                        "Lat": -37.88144,
                        "Lng": 145.08799
                    },
                    {
                        "Lat": -37.88095,
                        "Lng": 145.08724
                    },
                    {
                        "Lat": -37.88048,
                        "Lng": 145.08649
                    },
                    {
                        "Lat": -37.88048,
                        "Lng": 145.08649
                    },
                    {
                        "Lat": -37.88012,
                        "Lng": 145.08582
                    },
                    {
                        "Lat": -37.88012,
                        "Lng": 145.08582
                    },
                    {
                        "Lat": -37.87983,
                        "Lng": 145.08535
                    },
                    {
                        "Lat": -37.87954,
                        "Lng": 145.08485
                    },
                    {
                        "Lat": -37.87887,
                        "Lng": 145.08363
                    },
                    {
                        "Lat": -37.87887,
                        "Lng": 145.08363
                    },
                    {
                        "Lat": -37.87805,
                        "Lng": 145.08203
                    },
                    {
                        "Lat": -37.87805,
                        "Lng": 145.08203
                    },
                    {
                        "Lat": -37.87786,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87786,
                        "Lng": 145.08166
                    },
                    {
                        "Lat": -37.87776,
                        "Lng": 145.08145
                    },
                    {
                        "Lat": -37.87776,
                        "Lng": 145.08145
                    },
                    {
                        "Lat": -37.87741,
                        "Lng": 145.08069
                    },
                    {
                        "Lat": -37.87721,
                        "Lng": 145.08018
                    },
                    {
                        "Lat": -37.87701,
                        "Lng": 145.0795
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07906
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07906
                    },
                    {
                        "Lat": -37.87687,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.87687,
                        "Lng": 145.07874
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07821
                    },
                    {
                        "Lat": -37.87681,
                        "Lng": 145.07792
                    },
                    {
                        "Lat": -37.87681,
                        "Lng": 145.07792
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07734
                    },
                    {
                        "Lat": -37.87682,
                        "Lng": 145.07734
                    },
                    {
                        "Lat": -37.87685,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87685,
                        "Lng": 145.07693
                    },
                    {
                        "Lat": -37.87704,
                        "Lng": 145.07472
                    },
                    {
                        "Lat": -37.8772,
                        "Lng": 145.07302
                    },
                    {
                        "Lat": -37.87722,
                        "Lng": 145.07261
                    },
                    {
                        "Lat": -37.87721,
                        "Lng": 145.07198
                    },
                    {
                        "Lat": -37.87715,
                        "Lng": 145.07128
                    },
                    {
                        "Lat": -37.87708,
                        "Lng": 145.0708
                    },
                    {
                        "Lat": -37.87703,
                        "Lng": 145.07055
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07013
                    },
                    {
                        "Lat": -37.87692,
                        "Lng": 145.07013
                    },
                    {
                        "Lat": -37.87675,
                        "Lng": 145.06957
                    },
                    {
                        "Lat": -37.87663,
                        "Lng": 145.06926
                    },
                    {
                        "Lat": -37.8764,
                        "Lng": 145.06874
                    },
                    {
                        "Lat": -37.87613,
                        "Lng": 145.06825
                    },
                    {
                        "Lat": -37.87597,
                        "Lng": 145.068
                    },
                    {
                        "Lat": -37.8757,
                        "Lng": 145.06762
                    },
                    {
                        "Lat": -37.87533,
                        "Lng": 145.06718
                    },
                    {
                        "Lat": -37.87512,
                        "Lng": 145.06695
                    },
                    {
                        "Lat": -37.87487,
                        "Lng": 145.06671
                    },
                    {
                        "Lat": -37.87469,
                        "Lng": 145.06656
                    },
                    {
                        "Lat": -37.87431,
                        "Lng": 145.06629
                    },
                    {
                        "Lat": -37.87401,
                        "Lng": 145.0661
                    },
                    {
                        "Lat": -37.87351,
                        "Lng": 145.06583
                    },
                    {
                        "Lat": -37.87351,
                        "Lng": 145.06583
                    },
                    {
                        "Lat": -37.87316,
                        "Lng": 145.06567
                    },
                    {
                        "Lat": -37.87261,
                        "Lng": 145.06545
                    },
                    {
                        "Lat": -37.87261,
                        "Lng": 145.06545
                    },
                    {
                        "Lat": -37.87118,
                        "Lng": 145.06483
                    },
                    {
                        "Lat": -37.87082,
                        "Lng": 145.06466
                    },
                    {
                        "Lat": -37.87056,
                        "Lng": 145.06452
                    },
                    {
                        "Lat": -37.87013,
                        "Lng": 145.06427
                    },
                    {
                        "Lat": -37.8697,
                        "Lng": 145.06398
                    },
                    {
                        "Lat": -37.86928,
                        "Lng": 145.06366
                    },
                    {
                        "Lat": -37.86898,
                        "Lng": 145.0634
                    },
                    {
                        "Lat": -37.86898,
                        "Lng": 145.0634
                    },
                    {
                        "Lat": -37.86775,
                        "Lng": 145.06226
                    },
                    {
                        "Lat": -37.8674,
                        "Lng": 145.06196
                    },
                    {
                        "Lat": -37.8674,
                        "Lng": 145.06196
                    },
                    {
                        "Lat": -37.86719,
                        "Lng": 145.06178
                    },
                    {
                        "Lat": -37.86674,
                        "Lng": 145.06144
                    },
                    {
                        "Lat": -37.86655,
                        "Lng": 145.06131
                    },
                    {
                        "Lat": -37.86619,
                        "Lng": 145.06111
                    },
                    {
                        "Lat": -37.86599,
                        "Lng": 145.06102
                    },
                    {
                        "Lat": -37.86572,
                        "Lng": 145.06092
                    },
                    {
                        "Lat": -37.86523,
                        "Lng": 145.06077
                    },
                    {
                        "Lat": -37.86458,
                        "Lng": 145.06059
                    },
                    {
                        "Lat": -37.86458,
                        "Lng": 145.06059
                    },
                    {
                        "Lat": -37.86097,
                        "Lng": 145.05954
                    },
                    {
                        "Lat": -37.86097,
                        "Lng": 145.05954
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05927
                    },
                    {
                        "Lat": -37.86004,
                        "Lng": 145.05927
                    },
                    {
                        "Lat": -37.85921,
                        "Lng": 145.05903
                    },
                    {
                        "Lat": -37.85921,
                        "Lng": 145.05903
                    },
                    {
                        "Lat": -37.85804,
                        "Lng": 145.05869
                    },
                    {
                        "Lat": -37.85804,
                        "Lng": 145.05869
                    },
                    {
                        "Lat": -37.85783,
                        "Lng": 145.05863
                    },
                    {
                        "Lat": -37.85783,
                        "Lng": 145.05863
                    },
                    {
                        "Lat": -37.85723,
                        "Lng": 145.05844
                    },
                    {
                        "Lat": -37.85666,
                        "Lng": 145.05823
                    },
                    {
                        "Lat": -37.85666,
                        "Lng": 145.05823
                    },
                    {
                        "Lat": -37.85612,
                        "Lng": 145.05799
                    },
                    {
                        "Lat": -37.85589,
                        "Lng": 145.05787
                    },
                    {
                        "Lat": -37.85589,
                        "Lng": 145.05787
                    },
                    {
                        "Lat": -37.8555,
                        "Lng": 145.05763
                    },
                    {
                        "Lat": -37.8551,
                        "Lng": 145.05735
                    },
                    {
                        "Lat": -37.8551,
                        "Lng": 145.05735
                    },
                    {
                        "Lat": -37.85486,
                        "Lng": 145.05716
                    },
                    {
                        "Lat": -37.85461,
                        "Lng": 145.05694
                    },
                    {
                        "Lat": -37.85461,
                        "Lng": 145.05694
                    },
                    {
                        "Lat": -37.85417,
                        "Lng": 145.0565
                    },
                    {
                        "Lat": -37.85417,
                        "Lng": 145.0565
                    },
                    {
                        "Lat": -37.85396,
                        "Lng": 145.05626
                    },
                    {
                        "Lat": -37.85396,
                        "Lng": 145.05626
                    },
                    {
                        "Lat": -37.8538,
                        "Lng": 145.05607
                    },
                    {
                        "Lat": -37.8538,
                        "Lng": 145.05607
                    },
                    {
                        "Lat": -37.8537,
                        "Lng": 145.05591
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on Donnybrook Road have lanes closed over 48 kilometres.",
                "CorridorKeys": [
                    "Donnybrook Road"
                ]
            },
            {
                "Text": "Expect delays of around 14 minutes.",
                "CorridorKeys": [
                    "Donnybrook Road"
                ]
            },
            {
                "Text": "Roadworks on Sunshine Avenue have lanes closed over 23 kilometres.",
                "CorridorKeys": [
                    "Sunshine Avenue"
                ]
            },
            {
                "Text": "Roadworks on Toorak Road have lanes closed over 6 kilometres.",
                "CorridorKeys": [
                    "Toorak Road"
                ]
            },
            {
                "Text": "Roadworks on Huntingdale Road have lanes closed over 5 kilometres.",
                "CorridorKeys": [
                    "Huntingdale Road"
                ]
            }
        ],
        "ContradictionPlanted": true
    },
    "brisbane": {
        "FetchedAtIso": "2026-06-25T04:10:12.563Z",
        "Bbox": [
            152.8,
            -27.7,
            153.3,
            -27.3
        ],
        "Incidents": [
            {
                "Key": "inc-bne-01",
                "Priority": 4,
                "Headline": "Bruce Highway — roadworks",
                "Detail": "Between Bruce Highway and Barfoot Street - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 5483,
                "Latitude": -27.29673,
                "Longitude": 153.01778,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.29673,
                        "Lng": 153.01778
                    },
                    {
                        "Lat": -27.29697,
                        "Lng": 153.01774
                    },
                    {
                        "Lat": -27.29737,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.29737,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.29854,
                        "Lng": 153.01729
                    },
                    {
                        "Lat": -27.29899,
                        "Lng": 153.0172
                    },
                    {
                        "Lat": -27.29936,
                        "Lng": 153.01716
                    },
                    {
                        "Lat": -27.29965,
                        "Lng": 153.01716
                    },
                    {
                        "Lat": -27.29993,
                        "Lng": 153.01718
                    },
                    {
                        "Lat": -27.29993,
                        "Lng": 153.01718
                    },
                    {
                        "Lat": -27.30037,
                        "Lng": 153.01725
                    },
                    {
                        "Lat": -27.30066,
                        "Lng": 153.01733
                    },
                    {
                        "Lat": -27.301,
                        "Lng": 153.01746
                    },
                    {
                        "Lat": -27.301,
                        "Lng": 153.01746
                    },
                    {
                        "Lat": -27.30134,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.30172,
                        "Lng": 153.01788
                    },
                    {
                        "Lat": -27.30172,
                        "Lng": 153.01788
                    },
                    {
                        "Lat": -27.30208,
                        "Lng": 153.01817
                    },
                    {
                        "Lat": -27.30232,
                        "Lng": 153.01841
                    },
                    {
                        "Lat": -27.30254,
                        "Lng": 153.01866
                    },
                    {
                        "Lat": -27.30274,
                        "Lng": 153.01893
                    },
                    {
                        "Lat": -27.30291,
                        "Lng": 153.0192
                    },
                    {
                        "Lat": -27.3031,
                        "Lng": 153.01956
                    },
                    {
                        "Lat": -27.30317,
                        "Lng": 153.01972
                    },
                    {
                        "Lat": -27.30317,
                        "Lng": 153.01972
                    },
                    {
                        "Lat": -27.30328,
                        "Lng": 153.02001
                    },
                    {
                        "Lat": -27.30353,
                        "Lng": 153.02076
                    },
                    {
                        "Lat": -27.30353,
                        "Lng": 153.02076
                    },
                    {
                        "Lat": -27.3038,
                        "Lng": 153.02151
                    },
                    {
                        "Lat": -27.30396,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30396,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30412,
                        "Lng": 153.02231
                    },
                    {
                        "Lat": -27.30481,
                        "Lng": 153.02388
                    },
                    {
                        "Lat": -27.30542,
                        "Lng": 153.0252
                    },
                    {
                        "Lat": -27.30577,
                        "Lng": 153.0259
                    },
                    {
                        "Lat": -27.30577,
                        "Lng": 153.0259
                    },
                    {
                        "Lat": -27.30603,
                        "Lng": 153.02636
                    },
                    {
                        "Lat": -27.3063,
                        "Lng": 153.02676
                    },
                    {
                        "Lat": -27.3063,
                        "Lng": 153.02676
                    },
                    {
                        "Lat": -27.30708,
                        "Lng": 153.02784
                    },
                    {
                        "Lat": -27.30708,
                        "Lng": 153.02784
                    },
                    {
                        "Lat": -27.30727,
                        "Lng": 153.0281
                    },
                    {
                        "Lat": -27.30727,
                        "Lng": 153.0281
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02823
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02823
                    },
                    {
                        "Lat": -27.30763,
                        "Lng": 153.02858
                    },
                    {
                        "Lat": -27.30763,
                        "Lng": 153.02858
                    },
                    {
                        "Lat": -27.30846,
                        "Lng": 153.02973
                    },
                    {
                        "Lat": -27.3086,
                        "Lng": 153.02994
                    },
                    {
                        "Lat": -27.30885,
                        "Lng": 153.03036
                    },
                    {
                        "Lat": -27.309,
                        "Lng": 153.03066
                    },
                    {
                        "Lat": -27.30917,
                        "Lng": 153.03105
                    },
                    {
                        "Lat": -27.30931,
                        "Lng": 153.03143
                    },
                    {
                        "Lat": -27.30941,
                        "Lng": 153.03177
                    },
                    {
                        "Lat": -27.30996,
                        "Lng": 153.03415
                    },
                    {
                        "Lat": -27.30996,
                        "Lng": 153.03415
                    },
                    {
                        "Lat": -27.31013,
                        "Lng": 153.03489
                    },
                    {
                        "Lat": -27.31013,
                        "Lng": 153.03489
                    },
                    {
                        "Lat": -27.31019,
                        "Lng": 153.03515
                    },
                    {
                        "Lat": -27.31033,
                        "Lng": 153.03562
                    },
                    {
                        "Lat": -27.31033,
                        "Lng": 153.03562
                    },
                    {
                        "Lat": -27.3105,
                        "Lng": 153.03605
                    },
                    {
                        "Lat": -27.31069,
                        "Lng": 153.03646
                    },
                    {
                        "Lat": -27.31114,
                        "Lng": 153.03729
                    },
                    {
                        "Lat": -27.31114,
                        "Lng": 153.03729
                    },
                    {
                        "Lat": -27.31131,
                        "Lng": 153.03761
                    },
                    {
                        "Lat": -27.31131,
                        "Lng": 153.03761
                    },
                    {
                        "Lat": -27.31181,
                        "Lng": 153.03851
                    },
                    {
                        "Lat": -27.31181,
                        "Lng": 153.03851
                    },
                    {
                        "Lat": -27.31198,
                        "Lng": 153.03881
                    },
                    {
                        "Lat": -27.31198,
                        "Lng": 153.03881
                    },
                    {
                        "Lat": -27.31233,
                        "Lng": 153.03948
                    },
                    {
                        "Lat": -27.31253,
                        "Lng": 153.03994
                    },
                    {
                        "Lat": -27.31253,
                        "Lng": 153.03994
                    },
                    {
                        "Lat": -27.31262,
                        "Lng": 153.04017
                    },
                    {
                        "Lat": -27.31262,
                        "Lng": 153.04017
                    },
                    {
                        "Lat": -27.31274,
                        "Lng": 153.04058
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.04087
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.04087
                    },
                    {
                        "Lat": -27.31285,
                        "Lng": 153.04109
                    },
                    {
                        "Lat": -27.31289,
                        "Lng": 153.04136
                    },
                    {
                        "Lat": -27.31289,
                        "Lng": 153.04136
                    },
                    {
                        "Lat": -27.31293,
                        "Lng": 153.04178
                    },
                    {
                        "Lat": -27.31293,
                        "Lng": 153.04178
                    },
                    {
                        "Lat": -27.31295,
                        "Lng": 153.04225
                    },
                    {
                        "Lat": -27.3129,
                        "Lng": 153.04289
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04343
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04343
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04448
                    },
                    {
                        "Lat": -27.31269,
                        "Lng": 153.04497
                    },
                    {
                        "Lat": -27.31269,
                        "Lng": 153.04497
                    },
                    {
                        "Lat": -27.31271,
                        "Lng": 153.04537
                    },
                    {
                        "Lat": -27.31271,
                        "Lng": 153.04537
                    },
                    {
                        "Lat": -27.31276,
                        "Lng": 153.04566
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04597
                    },
                    {
                        "Lat": -27.31292,
                        "Lng": 153.04619
                    },
                    {
                        "Lat": -27.31303,
                        "Lng": 153.04644
                    },
                    {
                        "Lat": -27.31318,
                        "Lng": 153.0467
                    },
                    {
                        "Lat": -27.31318,
                        "Lng": 153.0467
                    },
                    {
                        "Lat": -27.31337,
                        "Lng": 153.04695
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04707
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04707
                    },
                    {
                        "Lat": -27.31372,
                        "Lng": 153.0473
                    },
                    {
                        "Lat": -27.31383,
                        "Lng": 153.04739
                    },
                    {
                        "Lat": -27.31383,
                        "Lng": 153.04739
                    },
                    {
                        "Lat": -27.31409,
                        "Lng": 153.04756
                    },
                    {
                        "Lat": -27.31409,
                        "Lng": 153.04756
                    },
                    {
                        "Lat": -27.31427,
                        "Lng": 153.04765
                    },
                    {
                        "Lat": -27.31427,
                        "Lng": 153.04765
                    },
                    {
                        "Lat": -27.31446,
                        "Lng": 153.04774
                    },
                    {
                        "Lat": -27.31514,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31514,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.316,
                        "Lng": 153.04838
                    },
                    {
                        "Lat": -27.316,
                        "Lng": 153.04838
                    },
                    {
                        "Lat": -27.31646,
                        "Lng": 153.04857
                    },
                    {
                        "Lat": -27.31646,
                        "Lng": 153.04857
                    },
                    {
                        "Lat": -27.31831,
                        "Lng": 153.04936
                    },
                    {
                        "Lat": -27.31831,
                        "Lng": 153.04936
                    },
                    {
                        "Lat": -27.31863,
                        "Lng": 153.0495
                    },
                    {
                        "Lat": -27.31863,
                        "Lng": 153.0495
                    },
                    {
                        "Lat": -27.31931,
                        "Lng": 153.04978
                    },
                    {
                        "Lat": -27.31931,
                        "Lng": 153.04978
                    },
                    {
                        "Lat": -27.31972,
                        "Lng": 153.04994
                    },
                    {
                        "Lat": -27.31972,
                        "Lng": 153.04994
                    },
                    {
                        "Lat": -27.32043,
                        "Lng": 153.0502
                    },
                    {
                        "Lat": -27.32043,
                        "Lng": 153.0502
                    },
                    {
                        "Lat": -27.3207,
                        "Lng": 153.05029
                    },
                    {
                        "Lat": -27.3207,
                        "Lng": 153.05029
                    },
                    {
                        "Lat": -27.32097,
                        "Lng": 153.05037
                    },
                    {
                        "Lat": -27.32097,
                        "Lng": 153.05037
                    },
                    {
                        "Lat": -27.32163,
                        "Lng": 153.05057
                    },
                    {
                        "Lat": -27.32163,
                        "Lng": 153.05057
                    },
                    {
                        "Lat": -27.32245,
                        "Lng": 153.05078
                    },
                    {
                        "Lat": -27.32245,
                        "Lng": 153.05078
                    },
                    {
                        "Lat": -27.32299,
                        "Lng": 153.0509
                    },
                    {
                        "Lat": -27.32299,
                        "Lng": 153.0509
                    },
                    {
                        "Lat": -27.32394,
                        "Lng": 153.05111
                    },
                    {
                        "Lat": -27.32394,
                        "Lng": 153.05111
                    },
                    {
                        "Lat": -27.3246,
                        "Lng": 153.05125
                    },
                    {
                        "Lat": -27.3246,
                        "Lng": 153.05125
                    },
                    {
                        "Lat": -27.32501,
                        "Lng": 153.05133
                    },
                    {
                        "Lat": -27.32501,
                        "Lng": 153.05133
                    },
                    {
                        "Lat": -27.32529,
                        "Lng": 153.05137
                    },
                    {
                        "Lat": -27.32529,
                        "Lng": 153.05137
                    },
                    {
                        "Lat": -27.32552,
                        "Lng": 153.0514
                    },
                    {
                        "Lat": -27.32552,
                        "Lng": 153.0514
                    },
                    {
                        "Lat": -27.32628,
                        "Lng": 153.05146
                    },
                    {
                        "Lat": -27.32628,
                        "Lng": 153.05146
                    },
                    {
                        "Lat": -27.32718,
                        "Lng": 153.05145
                    },
                    {
                        "Lat": -27.3278,
                        "Lng": 153.05139
                    }
                ]
            },
            {
                "Key": "inc-bne-02",
                "Priority": 4,
                "Headline": "Sandgate Road — roadworks",
                "Detail": "Between Sandgate Road and Bruce Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 4810,
                "Latitude": -27.32929,
                "Longitude": 153.05104,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.32929,
                        "Lng": 153.05104
                    },
                    {
                        "Lat": -27.32804,
                        "Lng": 153.05123
                    },
                    {
                        "Lat": -27.32778,
                        "Lng": 153.05126
                    },
                    {
                        "Lat": -27.32778,
                        "Lng": 153.05126
                    },
                    {
                        "Lat": -27.32703,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32703,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32649,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32649,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32603,
                        "Lng": 153.0513
                    },
                    {
                        "Lat": -27.32603,
                        "Lng": 153.0513
                    },
                    {
                        "Lat": -27.32576,
                        "Lng": 153.05127
                    },
                    {
                        "Lat": -27.32576,
                        "Lng": 153.05127
                    },
                    {
                        "Lat": -27.32551,
                        "Lng": 153.05124
                    },
                    {
                        "Lat": -27.32551,
                        "Lng": 153.05124
                    },
                    {
                        "Lat": -27.32521,
                        "Lng": 153.0512
                    },
                    {
                        "Lat": -27.32521,
                        "Lng": 153.0512
                    },
                    {
                        "Lat": -27.3249,
                        "Lng": 153.05114
                    },
                    {
                        "Lat": -27.3249,
                        "Lng": 153.05114
                    },
                    {
                        "Lat": -27.32438,
                        "Lng": 153.05102
                    },
                    {
                        "Lat": -27.32438,
                        "Lng": 153.05102
                    },
                    {
                        "Lat": -27.32399,
                        "Lng": 153.05093
                    },
                    {
                        "Lat": -27.32399,
                        "Lng": 153.05093
                    },
                    {
                        "Lat": -27.32159,
                        "Lng": 153.05036
                    },
                    {
                        "Lat": -27.32159,
                        "Lng": 153.05036
                    },
                    {
                        "Lat": -27.32146,
                        "Lng": 153.05033
                    },
                    {
                        "Lat": -27.32146,
                        "Lng": 153.05033
                    },
                    {
                        "Lat": -27.32125,
                        "Lng": 153.05027
                    },
                    {
                        "Lat": -27.32125,
                        "Lng": 153.05027
                    },
                    {
                        "Lat": -27.32089,
                        "Lng": 153.05017
                    },
                    {
                        "Lat": -27.32089,
                        "Lng": 153.05017
                    },
                    {
                        "Lat": -27.32069,
                        "Lng": 153.05011
                    },
                    {
                        "Lat": -27.32069,
                        "Lng": 153.05011
                    },
                    {
                        "Lat": -27.32048,
                        "Lng": 153.05005
                    },
                    {
                        "Lat": -27.32048,
                        "Lng": 153.05005
                    },
                    {
                        "Lat": -27.32021,
                        "Lng": 153.04997
                    },
                    {
                        "Lat": -27.31935,
                        "Lng": 153.04966
                    },
                    {
                        "Lat": -27.31865,
                        "Lng": 153.04937
                    },
                    {
                        "Lat": -27.31865,
                        "Lng": 153.04937
                    },
                    {
                        "Lat": -27.3183,
                        "Lng": 153.04922
                    },
                    {
                        "Lat": -27.3183,
                        "Lng": 153.04922
                    },
                    {
                        "Lat": -27.31805,
                        "Lng": 153.04911
                    },
                    {
                        "Lat": -27.31805,
                        "Lng": 153.04911
                    },
                    {
                        "Lat": -27.31761,
                        "Lng": 153.04891
                    },
                    {
                        "Lat": -27.31761,
                        "Lng": 153.04891
                    },
                    {
                        "Lat": -27.31707,
                        "Lng": 153.04867
                    },
                    {
                        "Lat": -27.31707,
                        "Lng": 153.04867
                    },
                    {
                        "Lat": -27.31674,
                        "Lng": 153.04852
                    },
                    {
                        "Lat": -27.31674,
                        "Lng": 153.04852
                    },
                    {
                        "Lat": -27.31648,
                        "Lng": 153.04841
                    },
                    {
                        "Lat": -27.31648,
                        "Lng": 153.04841
                    },
                    {
                        "Lat": -27.31559,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31559,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31517,
                        "Lng": 153.04784
                    },
                    {
                        "Lat": -27.31517,
                        "Lng": 153.04784
                    },
                    {
                        "Lat": -27.31467,
                        "Lng": 153.04762
                    },
                    {
                        "Lat": -27.31467,
                        "Lng": 153.04762
                    },
                    {
                        "Lat": -27.31431,
                        "Lng": 153.04744
                    },
                    {
                        "Lat": -27.31431,
                        "Lng": 153.04744
                    },
                    {
                        "Lat": -27.31418,
                        "Lng": 153.04737
                    },
                    {
                        "Lat": -27.31396,
                        "Lng": 153.04722
                    },
                    {
                        "Lat": -27.31382,
                        "Lng": 153.04711
                    },
                    {
                        "Lat": -27.31361,
                        "Lng": 153.04691
                    },
                    {
                        "Lat": -27.31361,
                        "Lng": 153.04691
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04676
                    },
                    {
                        "Lat": -27.31328,
                        "Lng": 153.04649
                    },
                    {
                        "Lat": -27.31313,
                        "Lng": 153.04623
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04594
                    },
                    {
                        "Lat": -27.3129,
                        "Lng": 153.04557
                    },
                    {
                        "Lat": -27.31285,
                        "Lng": 153.04526
                    },
                    {
                        "Lat": -27.31283,
                        "Lng": 153.045
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04465
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04465
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04348
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04348
                    },
                    {
                        "Lat": -27.31307,
                        "Lng": 153.04243
                    },
                    {
                        "Lat": -27.31307,
                        "Lng": 153.04243
                    },
                    {
                        "Lat": -27.31308,
                        "Lng": 153.04193
                    },
                    {
                        "Lat": -27.31308,
                        "Lng": 153.04193
                    },
                    {
                        "Lat": -27.31306,
                        "Lng": 153.04159
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04113
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04113
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04086
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04086
                    },
                    {
                        "Lat": -27.31287,
                        "Lng": 153.04052
                    },
                    {
                        "Lat": -27.31287,
                        "Lng": 153.04052
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.0403
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.0403
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04003
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04003
                    },
                    {
                        "Lat": -27.31265,
                        "Lng": 153.03984
                    },
                    {
                        "Lat": -27.31265,
                        "Lng": 153.03984
                    },
                    {
                        "Lat": -27.31236,
                        "Lng": 153.03923
                    },
                    {
                        "Lat": -27.31236,
                        "Lng": 153.03923
                    },
                    {
                        "Lat": -27.312,
                        "Lng": 153.03857
                    },
                    {
                        "Lat": -27.312,
                        "Lng": 153.03857
                    },
                    {
                        "Lat": -27.31141,
                        "Lng": 153.03748
                    },
                    {
                        "Lat": -27.31141,
                        "Lng": 153.03748
                    },
                    {
                        "Lat": -27.31079,
                        "Lng": 153.03631
                    },
                    {
                        "Lat": -27.31079,
                        "Lng": 153.03631
                    },
                    {
                        "Lat": -27.31063,
                        "Lng": 153.03597
                    },
                    {
                        "Lat": -27.31046,
                        "Lng": 153.03553
                    },
                    {
                        "Lat": -27.31046,
                        "Lng": 153.03553
                    },
                    {
                        "Lat": -27.31037,
                        "Lng": 153.03523
                    },
                    {
                        "Lat": -27.31037,
                        "Lng": 153.03523
                    },
                    {
                        "Lat": -27.31032,
                        "Lng": 153.03506
                    },
                    {
                        "Lat": -27.31032,
                        "Lng": 153.03506
                    },
                    {
                        "Lat": -27.31026,
                        "Lng": 153.03481
                    },
                    {
                        "Lat": -27.31026,
                        "Lng": 153.03481
                    },
                    {
                        "Lat": -27.31021,
                        "Lng": 153.03458
                    },
                    {
                        "Lat": -27.31021,
                        "Lng": 153.03458
                    },
                    {
                        "Lat": -27.3101,
                        "Lng": 153.03411
                    },
                    {
                        "Lat": -27.3101,
                        "Lng": 153.03411
                    },
                    {
                        "Lat": -27.30956,
                        "Lng": 153.03175
                    },
                    {
                        "Lat": -27.30939,
                        "Lng": 153.0312
                    },
                    {
                        "Lat": -27.30918,
                        "Lng": 153.03067
                    },
                    {
                        "Lat": -27.30899,
                        "Lng": 153.03029
                    },
                    {
                        "Lat": -27.30871,
                        "Lng": 153.02982
                    },
                    {
                        "Lat": -27.30773,
                        "Lng": 153.02845
                    },
                    {
                        "Lat": -27.30773,
                        "Lng": 153.02845
                    },
                    {
                        "Lat": -27.30749,
                        "Lng": 153.02812
                    },
                    {
                        "Lat": -27.30749,
                        "Lng": 153.02812
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02796
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02796
                    },
                    {
                        "Lat": -27.30723,
                        "Lng": 153.02776
                    },
                    {
                        "Lat": -27.30723,
                        "Lng": 153.02776
                    },
                    {
                        "Lat": -27.30682,
                        "Lng": 153.0272
                    },
                    {
                        "Lat": -27.30682,
                        "Lng": 153.0272
                    },
                    {
                        "Lat": -27.30634,
                        "Lng": 153.02653
                    },
                    {
                        "Lat": -27.30634,
                        "Lng": 153.02653
                    },
                    {
                        "Lat": -27.30613,
                        "Lng": 153.02622
                    },
                    {
                        "Lat": -27.30598,
                        "Lng": 153.02596
                    },
                    {
                        "Lat": -27.30598,
                        "Lng": 153.02596
                    },
                    {
                        "Lat": -27.30557,
                        "Lng": 153.02514
                    },
                    {
                        "Lat": -27.30421,
                        "Lng": 153.0222
                    },
                    {
                        "Lat": -27.30421,
                        "Lng": 153.0222
                    },
                    {
                        "Lat": -27.30409,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30409,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30389,
                        "Lng": 153.02142
                    },
                    {
                        "Lat": -27.30389,
                        "Lng": 153.02142
                    },
                    {
                        "Lat": -27.30356,
                        "Lng": 153.02044
                    },
                    {
                        "Lat": -27.30356,
                        "Lng": 153.02044
                    },
                    {
                        "Lat": -27.30331,
                        "Lng": 153.01968
                    }
                ]
            },
            {
                "Key": "inc-bne-03",
                "Priority": 4,
                "Headline": "Airport Drive — roadworks",
                "Detail": "Between Gateway Motorway South and Airport Drive - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 3784,
                "Latitude": -27.43593,
                "Longitude": 153.09539,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.43593,
                        "Lng": 153.09539
                    },
                    {
                        "Lat": -27.43577,
                        "Lng": 153.0952
                    },
                    {
                        "Lat": -27.43554,
                        "Lng": 153.09487
                    },
                    {
                        "Lat": -27.43554,
                        "Lng": 153.09487
                    },
                    {
                        "Lat": -27.43536,
                        "Lng": 153.09455
                    },
                    {
                        "Lat": -27.43536,
                        "Lng": 153.09455
                    },
                    {
                        "Lat": -27.43517,
                        "Lng": 153.09415
                    },
                    {
                        "Lat": -27.43517,
                        "Lng": 153.09415
                    },
                    {
                        "Lat": -27.43504,
                        "Lng": 153.09376
                    },
                    {
                        "Lat": -27.43499,
                        "Lng": 153.09357
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.0931
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.09269
                    },
                    {
                        "Lat": -27.4349,
                        "Lng": 153.09202
                    },
                    {
                        "Lat": -27.4349,
                        "Lng": 153.09202
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.09181
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.09181
                    },
                    {
                        "Lat": -27.43492,
                        "Lng": 153.09157
                    },
                    {
                        "Lat": -27.43492,
                        "Lng": 153.09157
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.09121
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.09121
                    },
                    {
                        "Lat": -27.43505,
                        "Lng": 153.08952
                    },
                    {
                        "Lat": -27.43505,
                        "Lng": 153.08952
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08926
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08926
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08901
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08901
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.0886
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.0886
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.08752
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.08705
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.08705
                    },
                    {
                        "Lat": -27.43477,
                        "Lng": 153.08649
                    },
                    {
                        "Lat": -27.43477,
                        "Lng": 153.08649
                    },
                    {
                        "Lat": -27.43468,
                        "Lng": 153.08612
                    },
                    {
                        "Lat": -27.43468,
                        "Lng": 153.08612
                    },
                    {
                        "Lat": -27.43455,
                        "Lng": 153.08565
                    },
                    {
                        "Lat": -27.43441,
                        "Lng": 153.08521
                    },
                    {
                        "Lat": -27.43441,
                        "Lng": 153.08521
                    },
                    {
                        "Lat": -27.43421,
                        "Lng": 153.08466
                    },
                    {
                        "Lat": -27.43398,
                        "Lng": 153.08419
                    },
                    {
                        "Lat": -27.43369,
                        "Lng": 153.08374
                    },
                    {
                        "Lat": -27.43335,
                        "Lng": 153.08333
                    },
                    {
                        "Lat": -27.43335,
                        "Lng": 153.08333
                    },
                    {
                        "Lat": -27.43323,
                        "Lng": 153.0832
                    },
                    {
                        "Lat": -27.43323,
                        "Lng": 153.0832
                    },
                    {
                        "Lat": -27.43296,
                        "Lng": 153.08296
                    },
                    {
                        "Lat": -27.43296,
                        "Lng": 153.08296
                    },
                    {
                        "Lat": -27.43271,
                        "Lng": 153.08276
                    },
                    {
                        "Lat": -27.43234,
                        "Lng": 153.08252
                    },
                    {
                        "Lat": -27.43126,
                        "Lng": 153.08196
                    },
                    {
                        "Lat": -27.431,
                        "Lng": 153.0818
                    },
                    {
                        "Lat": -27.431,
                        "Lng": 153.0818
                    },
                    {
                        "Lat": -27.4308,
                        "Lng": 153.08168
                    },
                    {
                        "Lat": -27.4308,
                        "Lng": 153.08168
                    },
                    {
                        "Lat": -27.43063,
                        "Lng": 153.08157
                    },
                    {
                        "Lat": -27.43063,
                        "Lng": 153.08157
                    },
                    {
                        "Lat": -27.43052,
                        "Lng": 153.08149
                    },
                    {
                        "Lat": -27.43052,
                        "Lng": 153.08149
                    },
                    {
                        "Lat": -27.43043,
                        "Lng": 153.08142
                    },
                    {
                        "Lat": -27.43043,
                        "Lng": 153.08142
                    },
                    {
                        "Lat": -27.43008,
                        "Lng": 153.08115
                    },
                    {
                        "Lat": -27.43008,
                        "Lng": 153.08115
                    },
                    {
                        "Lat": -27.42972,
                        "Lng": 153.08085
                    },
                    {
                        "Lat": -27.42972,
                        "Lng": 153.08085
                    },
                    {
                        "Lat": -27.42957,
                        "Lng": 153.08071
                    },
                    {
                        "Lat": -27.42957,
                        "Lng": 153.08071
                    },
                    {
                        "Lat": -27.42926,
                        "Lng": 153.08046
                    },
                    {
                        "Lat": -27.42906,
                        "Lng": 153.08032
                    },
                    {
                        "Lat": -27.42882,
                        "Lng": 153.08017
                    },
                    {
                        "Lat": -27.4286,
                        "Lng": 153.08005
                    },
                    {
                        "Lat": -27.4282,
                        "Lng": 153.07987
                    },
                    {
                        "Lat": -27.42799,
                        "Lng": 153.0798
                    },
                    {
                        "Lat": -27.42757,
                        "Lng": 153.07969
                    },
                    {
                        "Lat": -27.42757,
                        "Lng": 153.07969
                    },
                    {
                        "Lat": -27.42712,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42712,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42679,
                        "Lng": 153.07961
                    },
                    {
                        "Lat": -27.42679,
                        "Lng": 153.07961
                    },
                    {
                        "Lat": -27.42638,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42599,
                        "Lng": 153.07967
                    },
                    {
                        "Lat": -27.42561,
                        "Lng": 153.07975
                    },
                    {
                        "Lat": -27.42454,
                        "Lng": 153.08001
                    },
                    {
                        "Lat": -27.42454,
                        "Lng": 153.08001
                    },
                    {
                        "Lat": -27.42403,
                        "Lng": 153.08013
                    },
                    {
                        "Lat": -27.42403,
                        "Lng": 153.08013
                    },
                    {
                        "Lat": -27.42323,
                        "Lng": 153.08031
                    },
                    {
                        "Lat": -27.42274,
                        "Lng": 153.08035
                    },
                    {
                        "Lat": -27.42237,
                        "Lng": 153.08034
                    },
                    {
                        "Lat": -27.42237,
                        "Lng": 153.08034
                    },
                    {
                        "Lat": -27.42221,
                        "Lng": 153.08032
                    },
                    {
                        "Lat": -27.4218,
                        "Lng": 153.08023
                    },
                    {
                        "Lat": -27.42158,
                        "Lng": 153.08017
                    },
                    {
                        "Lat": -27.42131,
                        "Lng": 153.08007
                    },
                    {
                        "Lat": -27.42093,
                        "Lng": 153.07989
                    },
                    {
                        "Lat": -27.42067,
                        "Lng": 153.07973
                    },
                    {
                        "Lat": -27.42031,
                        "Lng": 153.07947
                    },
                    {
                        "Lat": -27.42031,
                        "Lng": 153.07947
                    },
                    {
                        "Lat": -27.42009,
                        "Lng": 153.07928
                    },
                    {
                        "Lat": -27.41994,
                        "Lng": 153.07913
                    },
                    {
                        "Lat": -27.41958,
                        "Lng": 153.07874
                    },
                    {
                        "Lat": -27.41958,
                        "Lng": 153.07874
                    },
                    {
                        "Lat": -27.41889,
                        "Lng": 153.07801
                    },
                    {
                        "Lat": -27.41889,
                        "Lng": 153.07801
                    },
                    {
                        "Lat": -27.41869,
                        "Lng": 153.0778
                    },
                    {
                        "Lat": -27.4185,
                        "Lng": 153.07763
                    },
                    {
                        "Lat": -27.4185,
                        "Lng": 153.07763
                    },
                    {
                        "Lat": -27.41818,
                        "Lng": 153.07737
                    },
                    {
                        "Lat": -27.41788,
                        "Lng": 153.07717
                    },
                    {
                        "Lat": -27.41769,
                        "Lng": 153.07706
                    },
                    {
                        "Lat": -27.41769,
                        "Lng": 153.07706
                    },
                    {
                        "Lat": -27.4175,
                        "Lng": 153.07696
                    },
                    {
                        "Lat": -27.4175,
                        "Lng": 153.07696
                    },
                    {
                        "Lat": -27.41721,
                        "Lng": 153.07683
                    },
                    {
                        "Lat": -27.41698,
                        "Lng": 153.07675
                    },
                    {
                        "Lat": -27.41698,
                        "Lng": 153.07675
                    },
                    {
                        "Lat": -27.41675,
                        "Lng": 153.07669
                    },
                    {
                        "Lat": -27.41675,
                        "Lng": 153.07669
                    },
                    {
                        "Lat": -27.41632,
                        "Lng": 153.0766
                    },
                    {
                        "Lat": -27.41584,
                        "Lng": 153.07655
                    },
                    {
                        "Lat": -27.41584,
                        "Lng": 153.07655
                    },
                    {
                        "Lat": -27.41543,
                        "Lng": 153.07656
                    },
                    {
                        "Lat": -27.4151,
                        "Lng": 153.07659
                    },
                    {
                        "Lat": -27.4151,
                        "Lng": 153.07659
                    },
                    {
                        "Lat": -27.41309,
                        "Lng": 153.0768
                    },
                    {
                        "Lat": -27.41309,
                        "Lng": 153.0768
                    },
                    {
                        "Lat": -27.41256,
                        "Lng": 153.07686
                    }
                ]
            },
            {
                "Key": "inc-bne-04",
                "Priority": 4,
                "Headline": "River Road — roadworks",
                "Detail": "Between River Road and Mt Crosby Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 3759,
                "Latitude": -27.58851,
                "Longitude": 152.82298,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.58851,
                        "Lng": 152.82298
                    },
                    {
                        "Lat": -27.5883,
                        "Lng": 152.82273
                    },
                    {
                        "Lat": -27.5883,
                        "Lng": 152.82273
                    },
                    {
                        "Lat": -27.58814,
                        "Lng": 152.82253
                    },
                    {
                        "Lat": -27.58814,
                        "Lng": 152.82253
                    },
                    {
                        "Lat": -27.58793,
                        "Lng": 152.82227
                    },
                    {
                        "Lat": -27.58793,
                        "Lng": 152.82227
                    },
                    {
                        "Lat": -27.58765,
                        "Lng": 152.82192
                    },
                    {
                        "Lat": -27.58765,
                        "Lng": 152.82192
                    },
                    {
                        "Lat": -27.5874,
                        "Lng": 152.82161
                    },
                    {
                        "Lat": -27.5874,
                        "Lng": 152.82161
                    },
                    {
                        "Lat": -27.58699,
                        "Lng": 152.82109
                    },
                    {
                        "Lat": -27.58699,
                        "Lng": 152.82109
                    },
                    {
                        "Lat": -27.58483,
                        "Lng": 152.81839
                    },
                    {
                        "Lat": -27.58483,
                        "Lng": 152.81839
                    },
                    {
                        "Lat": -27.58454,
                        "Lng": 152.81803
                    },
                    {
                        "Lat": -27.58454,
                        "Lng": 152.81803
                    },
                    {
                        "Lat": -27.58413,
                        "Lng": 152.8175
                    },
                    {
                        "Lat": -27.58389,
                        "Lng": 152.81715
                    },
                    {
                        "Lat": -27.58389,
                        "Lng": 152.81715
                    },
                    {
                        "Lat": -27.58361,
                        "Lng": 152.81667
                    },
                    {
                        "Lat": -27.58347,
                        "Lng": 152.81638
                    },
                    {
                        "Lat": -27.5833,
                        "Lng": 152.81594
                    },
                    {
                        "Lat": -27.58317,
                        "Lng": 152.81555
                    },
                    {
                        "Lat": -27.58317,
                        "Lng": 152.81555
                    },
                    {
                        "Lat": -27.58305,
                        "Lng": 152.81512
                    },
                    {
                        "Lat": -27.58305,
                        "Lng": 152.81512
                    },
                    {
                        "Lat": -27.58293,
                        "Lng": 152.8146
                    },
                    {
                        "Lat": -27.58293,
                        "Lng": 152.8146
                    },
                    {
                        "Lat": -27.58278,
                        "Lng": 152.81396
                    },
                    {
                        "Lat": -27.58278,
                        "Lng": 152.81396
                    },
                    {
                        "Lat": -27.5826,
                        "Lng": 152.81321
                    },
                    {
                        "Lat": -27.5826,
                        "Lng": 152.81321
                    },
                    {
                        "Lat": -27.5825,
                        "Lng": 152.81282
                    },
                    {
                        "Lat": -27.5825,
                        "Lng": 152.81282
                    },
                    {
                        "Lat": -27.5824,
                        "Lng": 152.81242
                    },
                    {
                        "Lat": -27.58225,
                        "Lng": 152.81196
                    },
                    {
                        "Lat": -27.58225,
                        "Lng": 152.81196
                    },
                    {
                        "Lat": -27.58212,
                        "Lng": 152.81165
                    },
                    {
                        "Lat": -27.58212,
                        "Lng": 152.81165
                    },
                    {
                        "Lat": -27.58187,
                        "Lng": 152.81115
                    },
                    {
                        "Lat": -27.58162,
                        "Lng": 152.81073
                    },
                    {
                        "Lat": -27.58001,
                        "Lng": 152.80833
                    },
                    {
                        "Lat": -27.58001,
                        "Lng": 152.80833
                    },
                    {
                        "Lat": -27.57698,
                        "Lng": 152.80382
                    },
                    {
                        "Lat": -27.57655,
                        "Lng": 152.80312
                    },
                    {
                        "Lat": -27.57655,
                        "Lng": 152.80312
                    },
                    {
                        "Lat": -27.57645,
                        "Lng": 152.80294
                    },
                    {
                        "Lat": -27.57645,
                        "Lng": 152.80294
                    },
                    {
                        "Lat": -27.57633,
                        "Lng": 152.80269
                    },
                    {
                        "Lat": -27.57633,
                        "Lng": 152.80269
                    },
                    {
                        "Lat": -27.57605,
                        "Lng": 152.80197
                    },
                    {
                        "Lat": -27.57497,
                        "Lng": 152.79876
                    },
                    {
                        "Lat": -27.57497,
                        "Lng": 152.79876
                    },
                    {
                        "Lat": -27.57428,
                        "Lng": 152.7967
                    },
                    {
                        "Lat": -27.57416,
                        "Lng": 152.79631
                    },
                    {
                        "Lat": -27.57416,
                        "Lng": 152.79631
                    },
                    {
                        "Lat": -27.57404,
                        "Lng": 152.79586
                    },
                    {
                        "Lat": -27.57395,
                        "Lng": 152.79539
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.7949
                    },
                    {
                        "Lat": -27.57385,
                        "Lng": 152.79425
                    },
                    {
                        "Lat": -27.57385,
                        "Lng": 152.79425
                    },
                    {
                        "Lat": -27.57386,
                        "Lng": 152.79372
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.79333
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.79333
                    },
                    {
                        "Lat": -27.57394,
                        "Lng": 152.79294
                    },
                    {
                        "Lat": -27.57402,
                        "Lng": 152.79256
                    },
                    {
                        "Lat": -27.57402,
                        "Lng": 152.79256
                    },
                    {
                        "Lat": -27.57407,
                        "Lng": 152.79234
                    },
                    {
                        "Lat": -27.57407,
                        "Lng": 152.79234
                    },
                    {
                        "Lat": -27.57445,
                        "Lng": 152.79066
                    },
                    {
                        "Lat": -27.57445,
                        "Lng": 152.79066
                    },
                    {
                        "Lat": -27.57463,
                        "Lng": 152.78993
                    },
                    {
                        "Lat": -27.57463,
                        "Lng": 152.78993
                    },
                    {
                        "Lat": -27.57467,
                        "Lng": 152.78975
                    }
                ]
            },
            {
                "Key": "inc-bne-05",
                "Priority": 4,
                "Headline": "Rocks Road — roadworks",
                "Detail": "Between Gateway Motorway and Dohles Rocks Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 1419,
                "Latitude": -27.30331,
                "Longitude": 153.01968,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.30331,
                        "Lng": 153.01968
                    },
                    {
                        "Lat": -27.3032,
                        "Lng": 153.01932
                    },
                    {
                        "Lat": -27.3032,
                        "Lng": 153.01932
                    },
                    {
                        "Lat": -27.30284,
                        "Lng": 153.01822
                    },
                    {
                        "Lat": -27.30276,
                        "Lng": 153.01801
                    },
                    {
                        "Lat": -27.30266,
                        "Lng": 153.0178
                    },
                    {
                        "Lat": -27.30249,
                        "Lng": 153.0175
                    },
                    {
                        "Lat": -27.30237,
                        "Lng": 153.01733
                    },
                    {
                        "Lat": -27.30225,
                        "Lng": 153.01718
                    },
                    {
                        "Lat": -27.30206,
                        "Lng": 153.01698
                    },
                    {
                        "Lat": -27.30206,
                        "Lng": 153.01698
                    },
                    {
                        "Lat": -27.30197,
                        "Lng": 153.0169
                    },
                    {
                        "Lat": -27.30167,
                        "Lng": 153.01668
                    },
                    {
                        "Lat": -27.30129,
                        "Lng": 153.01646
                    },
                    {
                        "Lat": -27.3012,
                        "Lng": 153.01642
                    },
                    {
                        "Lat": -27.30091,
                        "Lng": 153.01633
                    },
                    {
                        "Lat": -27.30056,
                        "Lng": 153.01626
                    },
                    {
                        "Lat": -27.30042,
                        "Lng": 153.01625
                    },
                    {
                        "Lat": -27.30005,
                        "Lng": 153.01626
                    },
                    {
                        "Lat": -27.29971,
                        "Lng": 153.01631
                    },
                    {
                        "Lat": -27.29942,
                        "Lng": 153.0164
                    },
                    {
                        "Lat": -27.29901,
                        "Lng": 153.01657
                    },
                    {
                        "Lat": -27.29901,
                        "Lng": 153.01657
                    },
                    {
                        "Lat": -27.29868,
                        "Lng": 153.01672
                    },
                    {
                        "Lat": -27.29868,
                        "Lng": 153.01672
                    },
                    {
                        "Lat": -27.2985,
                        "Lng": 153.0168
                    },
                    {
                        "Lat": -27.2985,
                        "Lng": 153.0168
                    },
                    {
                        "Lat": -27.29812,
                        "Lng": 153.01697
                    },
                    {
                        "Lat": -27.29812,
                        "Lng": 153.01697
                    },
                    {
                        "Lat": -27.29764,
                        "Lng": 153.01716
                    },
                    {
                        "Lat": -27.29736,
                        "Lng": 153.01725
                    },
                    {
                        "Lat": -27.29736,
                        "Lng": 153.01725
                    },
                    {
                        "Lat": -27.2961,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.29571,
                        "Lng": 153.01779
                    },
                    {
                        "Lat": -27.29571,
                        "Lng": 153.01779
                    },
                    {
                        "Lat": -27.29459,
                        "Lng": 153.01809
                    },
                    {
                        "Lat": -27.29459,
                        "Lng": 153.01809
                    },
                    {
                        "Lat": -27.29402,
                        "Lng": 153.01824
                    },
                    {
                        "Lat": -27.29402,
                        "Lng": 153.01824
                    },
                    {
                        "Lat": -27.29248,
                        "Lng": 153.01867
                    }
                ]
            },
            {
                "Key": "inc-bne-06",
                "Priority": 4,
                "Headline": "At Logan Motorway",
                "Detail": "At Logan Motorway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 1145,
                "Latitude": -27.68126,
                "Longitude": 153.18734,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.68126,
                        "Lng": 153.18734
                    },
                    {
                        "Lat": -27.68227,
                        "Lng": 153.18787
                    },
                    {
                        "Lat": -27.68227,
                        "Lng": 153.18787
                    },
                    {
                        "Lat": -27.68449,
                        "Lng": 153.18895
                    },
                    {
                        "Lat": -27.68449,
                        "Lng": 153.18895
                    },
                    {
                        "Lat": -27.68542,
                        "Lng": 153.18946
                    },
                    {
                        "Lat": -27.68542,
                        "Lng": 153.18946
                    },
                    {
                        "Lat": -27.68886,
                        "Lng": 153.1913
                    },
                    {
                        "Lat": -27.68886,
                        "Lng": 153.1913
                    },
                    {
                        "Lat": -27.68945,
                        "Lng": 153.19161
                    },
                    {
                        "Lat": -27.68945,
                        "Lng": 153.19161
                    },
                    {
                        "Lat": -27.69061,
                        "Lng": 153.19224
                    }
                ]
            },
            {
                "Key": "inc-bne-07",
                "Priority": 4,
                "Headline": "Drews Road — breakdown",
                "Detail": "At Drews Road - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 431,
                "Latitude": -27.68024,
                "Longitude": 153.17028,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.68024,
                        "Lng": 153.17028
                    },
                    {
                        "Lat": -27.68162,
                        "Lng": 153.17148
                    },
                    {
                        "Lat": -27.68221,
                        "Lng": 153.17202
                    },
                    {
                        "Lat": -27.68255,
                        "Lng": 153.1724
                    },
                    {
                        "Lat": -27.68278,
                        "Lng": 153.17269
                    },
                    {
                        "Lat": -27.68312,
                        "Lng": 153.1732
                    }
                ]
            },
            {
                "Key": "inc-bne-08",
                "Priority": 4,
                "Headline": "At Deagon Deviation",
                "Detail": "At Deagon Deviation - Traffic congestion",
                "FunctionalClass": 1,
                "LengthMeters": 241,
                "Latitude": -27.33376,
                "Longitude": 153.05104,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.33376,
                        "Lng": 153.05104
                    },
                    {
                        "Lat": -27.33354,
                        "Lng": 153.05096
                    },
                    {
                        "Lat": -27.33354,
                        "Lng": 153.05096
                    },
                    {
                        "Lat": -27.33293,
                        "Lng": 153.0508
                    },
                    {
                        "Lat": -27.33293,
                        "Lng": 153.0508
                    },
                    {
                        "Lat": -27.33259,
                        "Lng": 153.05075
                    },
                    {
                        "Lat": -27.33259,
                        "Lng": 153.05075
                    },
                    {
                        "Lat": -27.33232,
                        "Lng": 153.05073
                    },
                    {
                        "Lat": -27.33232,
                        "Lng": 153.05073
                    },
                    {
                        "Lat": -27.33181,
                        "Lng": 153.05072
                    },
                    {
                        "Lat": -27.33163,
                        "Lng": 153.05073
                    }
                ]
            },
            {
                "Key": "inc-bne-09",
                "Priority": 4,
                "Headline": "Bay Road — roadworks",
                "Detail": "At Beenleigh - Redland Bay Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 130,
                "Latitude": -27.68238,
                "Longitude": 153.18773,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.68238,
                        "Lng": 153.18773
                    },
                    {
                        "Lat": -27.68132,
                        "Lng": 153.18717
                    }
                ]
            },
            {
                "Key": "inc-bne-10",
                "Priority": 4,
                "Headline": "Baudesert Road — breakdown",
                "Detail": "At Baudesert Road - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 0,
                "Latitude": -27.64583,
                "Longitude": 153.03848,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.64583,
                        "Lng": 153.03848
                    },
                    {
                        "Lat": -27.64583,
                        "Lng": 153.03848
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "Bruce Highway",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 5.5,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 4,
                "DelayMinutes": null,
                "Latitude": -27.29673,
                "Longitude": 153.01778,
                "IncidentKeys": [
                    "inc-bne-01"
                ]
            },
            {
                "RoadName": "Sandgate Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 4.8,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 6,
                "DelayMinutes": 5,
                "Latitude": -27.32929,
                "Longitude": 153.05104,
                "IncidentKeys": [
                    "inc-bne-02"
                ]
            },
            {
                "RoadName": "Airport Drive",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 3.8,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 3,
                "DelayMinutes": null,
                "Latitude": -27.43593,
                "Longitude": 153.09539,
                "IncidentKeys": [
                    "inc-bne-03"
                ]
            },
            {
                "RoadName": "River Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 3.8,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 3,
                "DelayMinutes": null,
                "Latitude": -27.58851,
                "Longitude": 152.82298,
                "IncidentKeys": [
                    "inc-bne-04"
                ]
            },
            {
                "RoadName": "Rocks Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 1.4,
                "Rank": 5,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 1,
                "DelayMinutes": 1,
                "Latitude": -27.30331,
                "Longitude": 153.01968,
                "IncidentKeys": [
                    "inc-bne-05"
                ]
            },
            {
                "RoadName": "Drews Road",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "disabledVehicle",
                "Description": "breakdown",
                "IncidentCount": 1,
                "TotalLengthKm": 0.4,
                "Rank": 6,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -27.68024,
                "Longitude": 153.17028,
                "IncidentKeys": [
                    "inc-bne-07"
                ]
            },
            {
                "RoadName": "Bay Road",
                "Severity": 1,
                "ImpactScore": 201.1,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 7,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -27.68238,
                "Longitude": 153.18773,
                "IncidentKeys": [
                    "inc-bne-09"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-bne-01",
                "Priority": 4,
                "Headline": "Bruce Highway — roadworks",
                "Detail": "Between Bruce Highway and Barfoot Street - Lane blocked",
                "FunctionalClass": 1,
                "LengthMeters": 5483,
                "Latitude": -27.29673,
                "Longitude": 153.01778,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.29673,
                        "Lng": 153.01778
                    },
                    {
                        "Lat": -27.29697,
                        "Lng": 153.01774
                    },
                    {
                        "Lat": -27.29737,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.29737,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.29854,
                        "Lng": 153.01729
                    },
                    {
                        "Lat": -27.29899,
                        "Lng": 153.0172
                    },
                    {
                        "Lat": -27.29936,
                        "Lng": 153.01716
                    },
                    {
                        "Lat": -27.29965,
                        "Lng": 153.01716
                    },
                    {
                        "Lat": -27.29993,
                        "Lng": 153.01718
                    },
                    {
                        "Lat": -27.29993,
                        "Lng": 153.01718
                    },
                    {
                        "Lat": -27.30037,
                        "Lng": 153.01725
                    },
                    {
                        "Lat": -27.30066,
                        "Lng": 153.01733
                    },
                    {
                        "Lat": -27.301,
                        "Lng": 153.01746
                    },
                    {
                        "Lat": -27.301,
                        "Lng": 153.01746
                    },
                    {
                        "Lat": -27.30134,
                        "Lng": 153.01763
                    },
                    {
                        "Lat": -27.30172,
                        "Lng": 153.01788
                    },
                    {
                        "Lat": -27.30172,
                        "Lng": 153.01788
                    },
                    {
                        "Lat": -27.30208,
                        "Lng": 153.01817
                    },
                    {
                        "Lat": -27.30232,
                        "Lng": 153.01841
                    },
                    {
                        "Lat": -27.30254,
                        "Lng": 153.01866
                    },
                    {
                        "Lat": -27.30274,
                        "Lng": 153.01893
                    },
                    {
                        "Lat": -27.30291,
                        "Lng": 153.0192
                    },
                    {
                        "Lat": -27.3031,
                        "Lng": 153.01956
                    },
                    {
                        "Lat": -27.30317,
                        "Lng": 153.01972
                    },
                    {
                        "Lat": -27.30317,
                        "Lng": 153.01972
                    },
                    {
                        "Lat": -27.30328,
                        "Lng": 153.02001
                    },
                    {
                        "Lat": -27.30353,
                        "Lng": 153.02076
                    },
                    {
                        "Lat": -27.30353,
                        "Lng": 153.02076
                    },
                    {
                        "Lat": -27.3038,
                        "Lng": 153.02151
                    },
                    {
                        "Lat": -27.30396,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30396,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30412,
                        "Lng": 153.02231
                    },
                    {
                        "Lat": -27.30481,
                        "Lng": 153.02388
                    },
                    {
                        "Lat": -27.30542,
                        "Lng": 153.0252
                    },
                    {
                        "Lat": -27.30577,
                        "Lng": 153.0259
                    },
                    {
                        "Lat": -27.30577,
                        "Lng": 153.0259
                    },
                    {
                        "Lat": -27.30603,
                        "Lng": 153.02636
                    },
                    {
                        "Lat": -27.3063,
                        "Lng": 153.02676
                    },
                    {
                        "Lat": -27.3063,
                        "Lng": 153.02676
                    },
                    {
                        "Lat": -27.30708,
                        "Lng": 153.02784
                    },
                    {
                        "Lat": -27.30708,
                        "Lng": 153.02784
                    },
                    {
                        "Lat": -27.30727,
                        "Lng": 153.0281
                    },
                    {
                        "Lat": -27.30727,
                        "Lng": 153.0281
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02823
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02823
                    },
                    {
                        "Lat": -27.30763,
                        "Lng": 153.02858
                    },
                    {
                        "Lat": -27.30763,
                        "Lng": 153.02858
                    },
                    {
                        "Lat": -27.30846,
                        "Lng": 153.02973
                    },
                    {
                        "Lat": -27.3086,
                        "Lng": 153.02994
                    },
                    {
                        "Lat": -27.30885,
                        "Lng": 153.03036
                    },
                    {
                        "Lat": -27.309,
                        "Lng": 153.03066
                    },
                    {
                        "Lat": -27.30917,
                        "Lng": 153.03105
                    },
                    {
                        "Lat": -27.30931,
                        "Lng": 153.03143
                    },
                    {
                        "Lat": -27.30941,
                        "Lng": 153.03177
                    },
                    {
                        "Lat": -27.30996,
                        "Lng": 153.03415
                    },
                    {
                        "Lat": -27.30996,
                        "Lng": 153.03415
                    },
                    {
                        "Lat": -27.31013,
                        "Lng": 153.03489
                    },
                    {
                        "Lat": -27.31013,
                        "Lng": 153.03489
                    },
                    {
                        "Lat": -27.31019,
                        "Lng": 153.03515
                    },
                    {
                        "Lat": -27.31033,
                        "Lng": 153.03562
                    },
                    {
                        "Lat": -27.31033,
                        "Lng": 153.03562
                    },
                    {
                        "Lat": -27.3105,
                        "Lng": 153.03605
                    },
                    {
                        "Lat": -27.31069,
                        "Lng": 153.03646
                    },
                    {
                        "Lat": -27.31114,
                        "Lng": 153.03729
                    },
                    {
                        "Lat": -27.31114,
                        "Lng": 153.03729
                    },
                    {
                        "Lat": -27.31131,
                        "Lng": 153.03761
                    },
                    {
                        "Lat": -27.31131,
                        "Lng": 153.03761
                    },
                    {
                        "Lat": -27.31181,
                        "Lng": 153.03851
                    },
                    {
                        "Lat": -27.31181,
                        "Lng": 153.03851
                    },
                    {
                        "Lat": -27.31198,
                        "Lng": 153.03881
                    },
                    {
                        "Lat": -27.31198,
                        "Lng": 153.03881
                    },
                    {
                        "Lat": -27.31233,
                        "Lng": 153.03948
                    },
                    {
                        "Lat": -27.31253,
                        "Lng": 153.03994
                    },
                    {
                        "Lat": -27.31253,
                        "Lng": 153.03994
                    },
                    {
                        "Lat": -27.31262,
                        "Lng": 153.04017
                    },
                    {
                        "Lat": -27.31262,
                        "Lng": 153.04017
                    },
                    {
                        "Lat": -27.31274,
                        "Lng": 153.04058
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.04087
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.04087
                    },
                    {
                        "Lat": -27.31285,
                        "Lng": 153.04109
                    },
                    {
                        "Lat": -27.31289,
                        "Lng": 153.04136
                    },
                    {
                        "Lat": -27.31289,
                        "Lng": 153.04136
                    },
                    {
                        "Lat": -27.31293,
                        "Lng": 153.04178
                    },
                    {
                        "Lat": -27.31293,
                        "Lng": 153.04178
                    },
                    {
                        "Lat": -27.31295,
                        "Lng": 153.04225
                    },
                    {
                        "Lat": -27.3129,
                        "Lng": 153.04289
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04343
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04343
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04448
                    },
                    {
                        "Lat": -27.31269,
                        "Lng": 153.04497
                    },
                    {
                        "Lat": -27.31269,
                        "Lng": 153.04497
                    },
                    {
                        "Lat": -27.31271,
                        "Lng": 153.04537
                    },
                    {
                        "Lat": -27.31271,
                        "Lng": 153.04537
                    },
                    {
                        "Lat": -27.31276,
                        "Lng": 153.04566
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04597
                    },
                    {
                        "Lat": -27.31292,
                        "Lng": 153.04619
                    },
                    {
                        "Lat": -27.31303,
                        "Lng": 153.04644
                    },
                    {
                        "Lat": -27.31318,
                        "Lng": 153.0467
                    },
                    {
                        "Lat": -27.31318,
                        "Lng": 153.0467
                    },
                    {
                        "Lat": -27.31337,
                        "Lng": 153.04695
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04707
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04707
                    },
                    {
                        "Lat": -27.31372,
                        "Lng": 153.0473
                    },
                    {
                        "Lat": -27.31383,
                        "Lng": 153.04739
                    },
                    {
                        "Lat": -27.31383,
                        "Lng": 153.04739
                    },
                    {
                        "Lat": -27.31409,
                        "Lng": 153.04756
                    },
                    {
                        "Lat": -27.31409,
                        "Lng": 153.04756
                    },
                    {
                        "Lat": -27.31427,
                        "Lng": 153.04765
                    },
                    {
                        "Lat": -27.31427,
                        "Lng": 153.04765
                    },
                    {
                        "Lat": -27.31446,
                        "Lng": 153.04774
                    },
                    {
                        "Lat": -27.31514,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31514,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.316,
                        "Lng": 153.04838
                    },
                    {
                        "Lat": -27.316,
                        "Lng": 153.04838
                    },
                    {
                        "Lat": -27.31646,
                        "Lng": 153.04857
                    },
                    {
                        "Lat": -27.31646,
                        "Lng": 153.04857
                    },
                    {
                        "Lat": -27.31831,
                        "Lng": 153.04936
                    },
                    {
                        "Lat": -27.31831,
                        "Lng": 153.04936
                    },
                    {
                        "Lat": -27.31863,
                        "Lng": 153.0495
                    },
                    {
                        "Lat": -27.31863,
                        "Lng": 153.0495
                    },
                    {
                        "Lat": -27.31931,
                        "Lng": 153.04978
                    },
                    {
                        "Lat": -27.31931,
                        "Lng": 153.04978
                    },
                    {
                        "Lat": -27.31972,
                        "Lng": 153.04994
                    },
                    {
                        "Lat": -27.31972,
                        "Lng": 153.04994
                    },
                    {
                        "Lat": -27.32043,
                        "Lng": 153.0502
                    },
                    {
                        "Lat": -27.32043,
                        "Lng": 153.0502
                    },
                    {
                        "Lat": -27.3207,
                        "Lng": 153.05029
                    },
                    {
                        "Lat": -27.3207,
                        "Lng": 153.05029
                    },
                    {
                        "Lat": -27.32097,
                        "Lng": 153.05037
                    },
                    {
                        "Lat": -27.32097,
                        "Lng": 153.05037
                    },
                    {
                        "Lat": -27.32163,
                        "Lng": 153.05057
                    },
                    {
                        "Lat": -27.32163,
                        "Lng": 153.05057
                    },
                    {
                        "Lat": -27.32245,
                        "Lng": 153.05078
                    },
                    {
                        "Lat": -27.32245,
                        "Lng": 153.05078
                    },
                    {
                        "Lat": -27.32299,
                        "Lng": 153.0509
                    },
                    {
                        "Lat": -27.32299,
                        "Lng": 153.0509
                    },
                    {
                        "Lat": -27.32394,
                        "Lng": 153.05111
                    },
                    {
                        "Lat": -27.32394,
                        "Lng": 153.05111
                    },
                    {
                        "Lat": -27.3246,
                        "Lng": 153.05125
                    },
                    {
                        "Lat": -27.3246,
                        "Lng": 153.05125
                    },
                    {
                        "Lat": -27.32501,
                        "Lng": 153.05133
                    },
                    {
                        "Lat": -27.32501,
                        "Lng": 153.05133
                    },
                    {
                        "Lat": -27.32529,
                        "Lng": 153.05137
                    },
                    {
                        "Lat": -27.32529,
                        "Lng": 153.05137
                    },
                    {
                        "Lat": -27.32552,
                        "Lng": 153.0514
                    },
                    {
                        "Lat": -27.32552,
                        "Lng": 153.0514
                    },
                    {
                        "Lat": -27.32628,
                        "Lng": 153.05146
                    },
                    {
                        "Lat": -27.32628,
                        "Lng": 153.05146
                    },
                    {
                        "Lat": -27.32718,
                        "Lng": 153.05145
                    },
                    {
                        "Lat": -27.3278,
                        "Lng": 153.05139
                    }
                ]
            },
            {
                "Key": "inc-bne-02",
                "Priority": 4,
                "Headline": "Sandgate Road — roadworks",
                "Detail": "Between Sandgate Road and Bruce Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 4810,
                "Latitude": -27.32929,
                "Longitude": 153.05104,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.32929,
                        "Lng": 153.05104
                    },
                    {
                        "Lat": -27.32804,
                        "Lng": 153.05123
                    },
                    {
                        "Lat": -27.32778,
                        "Lng": 153.05126
                    },
                    {
                        "Lat": -27.32778,
                        "Lng": 153.05126
                    },
                    {
                        "Lat": -27.32703,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32703,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32649,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32649,
                        "Lng": 153.05132
                    },
                    {
                        "Lat": -27.32603,
                        "Lng": 153.0513
                    },
                    {
                        "Lat": -27.32603,
                        "Lng": 153.0513
                    },
                    {
                        "Lat": -27.32576,
                        "Lng": 153.05127
                    },
                    {
                        "Lat": -27.32576,
                        "Lng": 153.05127
                    },
                    {
                        "Lat": -27.32551,
                        "Lng": 153.05124
                    },
                    {
                        "Lat": -27.32551,
                        "Lng": 153.05124
                    },
                    {
                        "Lat": -27.32521,
                        "Lng": 153.0512
                    },
                    {
                        "Lat": -27.32521,
                        "Lng": 153.0512
                    },
                    {
                        "Lat": -27.3249,
                        "Lng": 153.05114
                    },
                    {
                        "Lat": -27.3249,
                        "Lng": 153.05114
                    },
                    {
                        "Lat": -27.32438,
                        "Lng": 153.05102
                    },
                    {
                        "Lat": -27.32438,
                        "Lng": 153.05102
                    },
                    {
                        "Lat": -27.32399,
                        "Lng": 153.05093
                    },
                    {
                        "Lat": -27.32399,
                        "Lng": 153.05093
                    },
                    {
                        "Lat": -27.32159,
                        "Lng": 153.05036
                    },
                    {
                        "Lat": -27.32159,
                        "Lng": 153.05036
                    },
                    {
                        "Lat": -27.32146,
                        "Lng": 153.05033
                    },
                    {
                        "Lat": -27.32146,
                        "Lng": 153.05033
                    },
                    {
                        "Lat": -27.32125,
                        "Lng": 153.05027
                    },
                    {
                        "Lat": -27.32125,
                        "Lng": 153.05027
                    },
                    {
                        "Lat": -27.32089,
                        "Lng": 153.05017
                    },
                    {
                        "Lat": -27.32089,
                        "Lng": 153.05017
                    },
                    {
                        "Lat": -27.32069,
                        "Lng": 153.05011
                    },
                    {
                        "Lat": -27.32069,
                        "Lng": 153.05011
                    },
                    {
                        "Lat": -27.32048,
                        "Lng": 153.05005
                    },
                    {
                        "Lat": -27.32048,
                        "Lng": 153.05005
                    },
                    {
                        "Lat": -27.32021,
                        "Lng": 153.04997
                    },
                    {
                        "Lat": -27.31935,
                        "Lng": 153.04966
                    },
                    {
                        "Lat": -27.31865,
                        "Lng": 153.04937
                    },
                    {
                        "Lat": -27.31865,
                        "Lng": 153.04937
                    },
                    {
                        "Lat": -27.3183,
                        "Lng": 153.04922
                    },
                    {
                        "Lat": -27.3183,
                        "Lng": 153.04922
                    },
                    {
                        "Lat": -27.31805,
                        "Lng": 153.04911
                    },
                    {
                        "Lat": -27.31805,
                        "Lng": 153.04911
                    },
                    {
                        "Lat": -27.31761,
                        "Lng": 153.04891
                    },
                    {
                        "Lat": -27.31761,
                        "Lng": 153.04891
                    },
                    {
                        "Lat": -27.31707,
                        "Lng": 153.04867
                    },
                    {
                        "Lat": -27.31707,
                        "Lng": 153.04867
                    },
                    {
                        "Lat": -27.31674,
                        "Lng": 153.04852
                    },
                    {
                        "Lat": -27.31674,
                        "Lng": 153.04852
                    },
                    {
                        "Lat": -27.31648,
                        "Lng": 153.04841
                    },
                    {
                        "Lat": -27.31648,
                        "Lng": 153.04841
                    },
                    {
                        "Lat": -27.31559,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31559,
                        "Lng": 153.04802
                    },
                    {
                        "Lat": -27.31517,
                        "Lng": 153.04784
                    },
                    {
                        "Lat": -27.31517,
                        "Lng": 153.04784
                    },
                    {
                        "Lat": -27.31467,
                        "Lng": 153.04762
                    },
                    {
                        "Lat": -27.31467,
                        "Lng": 153.04762
                    },
                    {
                        "Lat": -27.31431,
                        "Lng": 153.04744
                    },
                    {
                        "Lat": -27.31431,
                        "Lng": 153.04744
                    },
                    {
                        "Lat": -27.31418,
                        "Lng": 153.04737
                    },
                    {
                        "Lat": -27.31396,
                        "Lng": 153.04722
                    },
                    {
                        "Lat": -27.31382,
                        "Lng": 153.04711
                    },
                    {
                        "Lat": -27.31361,
                        "Lng": 153.04691
                    },
                    {
                        "Lat": -27.31361,
                        "Lng": 153.04691
                    },
                    {
                        "Lat": -27.31348,
                        "Lng": 153.04676
                    },
                    {
                        "Lat": -27.31328,
                        "Lng": 153.04649
                    },
                    {
                        "Lat": -27.31313,
                        "Lng": 153.04623
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04594
                    },
                    {
                        "Lat": -27.3129,
                        "Lng": 153.04557
                    },
                    {
                        "Lat": -27.31285,
                        "Lng": 153.04526
                    },
                    {
                        "Lat": -27.31283,
                        "Lng": 153.045
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04465
                    },
                    {
                        "Lat": -27.31284,
                        "Lng": 153.04465
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04348
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04348
                    },
                    {
                        "Lat": -27.31307,
                        "Lng": 153.04243
                    },
                    {
                        "Lat": -27.31307,
                        "Lng": 153.04243
                    },
                    {
                        "Lat": -27.31308,
                        "Lng": 153.04193
                    },
                    {
                        "Lat": -27.31308,
                        "Lng": 153.04193
                    },
                    {
                        "Lat": -27.31306,
                        "Lng": 153.04159
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04113
                    },
                    {
                        "Lat": -27.31301,
                        "Lng": 153.04113
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04086
                    },
                    {
                        "Lat": -27.31296,
                        "Lng": 153.04086
                    },
                    {
                        "Lat": -27.31287,
                        "Lng": 153.04052
                    },
                    {
                        "Lat": -27.31287,
                        "Lng": 153.04052
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.0403
                    },
                    {
                        "Lat": -27.3128,
                        "Lng": 153.0403
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04003
                    },
                    {
                        "Lat": -27.31272,
                        "Lng": 153.04003
                    },
                    {
                        "Lat": -27.31265,
                        "Lng": 153.03984
                    },
                    {
                        "Lat": -27.31265,
                        "Lng": 153.03984
                    },
                    {
                        "Lat": -27.31236,
                        "Lng": 153.03923
                    },
                    {
                        "Lat": -27.31236,
                        "Lng": 153.03923
                    },
                    {
                        "Lat": -27.312,
                        "Lng": 153.03857
                    },
                    {
                        "Lat": -27.312,
                        "Lng": 153.03857
                    },
                    {
                        "Lat": -27.31141,
                        "Lng": 153.03748
                    },
                    {
                        "Lat": -27.31141,
                        "Lng": 153.03748
                    },
                    {
                        "Lat": -27.31079,
                        "Lng": 153.03631
                    },
                    {
                        "Lat": -27.31079,
                        "Lng": 153.03631
                    },
                    {
                        "Lat": -27.31063,
                        "Lng": 153.03597
                    },
                    {
                        "Lat": -27.31046,
                        "Lng": 153.03553
                    },
                    {
                        "Lat": -27.31046,
                        "Lng": 153.03553
                    },
                    {
                        "Lat": -27.31037,
                        "Lng": 153.03523
                    },
                    {
                        "Lat": -27.31037,
                        "Lng": 153.03523
                    },
                    {
                        "Lat": -27.31032,
                        "Lng": 153.03506
                    },
                    {
                        "Lat": -27.31032,
                        "Lng": 153.03506
                    },
                    {
                        "Lat": -27.31026,
                        "Lng": 153.03481
                    },
                    {
                        "Lat": -27.31026,
                        "Lng": 153.03481
                    },
                    {
                        "Lat": -27.31021,
                        "Lng": 153.03458
                    },
                    {
                        "Lat": -27.31021,
                        "Lng": 153.03458
                    },
                    {
                        "Lat": -27.3101,
                        "Lng": 153.03411
                    },
                    {
                        "Lat": -27.3101,
                        "Lng": 153.03411
                    },
                    {
                        "Lat": -27.30956,
                        "Lng": 153.03175
                    },
                    {
                        "Lat": -27.30939,
                        "Lng": 153.0312
                    },
                    {
                        "Lat": -27.30918,
                        "Lng": 153.03067
                    },
                    {
                        "Lat": -27.30899,
                        "Lng": 153.03029
                    },
                    {
                        "Lat": -27.30871,
                        "Lng": 153.02982
                    },
                    {
                        "Lat": -27.30773,
                        "Lng": 153.02845
                    },
                    {
                        "Lat": -27.30773,
                        "Lng": 153.02845
                    },
                    {
                        "Lat": -27.30749,
                        "Lng": 153.02812
                    },
                    {
                        "Lat": -27.30749,
                        "Lng": 153.02812
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02796
                    },
                    {
                        "Lat": -27.30737,
                        "Lng": 153.02796
                    },
                    {
                        "Lat": -27.30723,
                        "Lng": 153.02776
                    },
                    {
                        "Lat": -27.30723,
                        "Lng": 153.02776
                    },
                    {
                        "Lat": -27.30682,
                        "Lng": 153.0272
                    },
                    {
                        "Lat": -27.30682,
                        "Lng": 153.0272
                    },
                    {
                        "Lat": -27.30634,
                        "Lng": 153.02653
                    },
                    {
                        "Lat": -27.30634,
                        "Lng": 153.02653
                    },
                    {
                        "Lat": -27.30613,
                        "Lng": 153.02622
                    },
                    {
                        "Lat": -27.30598,
                        "Lng": 153.02596
                    },
                    {
                        "Lat": -27.30598,
                        "Lng": 153.02596
                    },
                    {
                        "Lat": -27.30557,
                        "Lng": 153.02514
                    },
                    {
                        "Lat": -27.30421,
                        "Lng": 153.0222
                    },
                    {
                        "Lat": -27.30421,
                        "Lng": 153.0222
                    },
                    {
                        "Lat": -27.30409,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30409,
                        "Lng": 153.02192
                    },
                    {
                        "Lat": -27.30389,
                        "Lng": 153.02142
                    },
                    {
                        "Lat": -27.30389,
                        "Lng": 153.02142
                    },
                    {
                        "Lat": -27.30356,
                        "Lng": 153.02044
                    },
                    {
                        "Lat": -27.30356,
                        "Lng": 153.02044
                    },
                    {
                        "Lat": -27.30331,
                        "Lng": 153.01968
                    }
                ]
            },
            {
                "Key": "inc-bne-03",
                "Priority": 4,
                "Headline": "Airport Drive — roadworks",
                "Detail": "Between Gateway Motorway South and Airport Drive - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 3784,
                "Latitude": -27.43593,
                "Longitude": 153.09539,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.43593,
                        "Lng": 153.09539
                    },
                    {
                        "Lat": -27.43577,
                        "Lng": 153.0952
                    },
                    {
                        "Lat": -27.43554,
                        "Lng": 153.09487
                    },
                    {
                        "Lat": -27.43554,
                        "Lng": 153.09487
                    },
                    {
                        "Lat": -27.43536,
                        "Lng": 153.09455
                    },
                    {
                        "Lat": -27.43536,
                        "Lng": 153.09455
                    },
                    {
                        "Lat": -27.43517,
                        "Lng": 153.09415
                    },
                    {
                        "Lat": -27.43517,
                        "Lng": 153.09415
                    },
                    {
                        "Lat": -27.43504,
                        "Lng": 153.09376
                    },
                    {
                        "Lat": -27.43499,
                        "Lng": 153.09357
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.0931
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.09269
                    },
                    {
                        "Lat": -27.4349,
                        "Lng": 153.09202
                    },
                    {
                        "Lat": -27.4349,
                        "Lng": 153.09202
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.09181
                    },
                    {
                        "Lat": -27.43491,
                        "Lng": 153.09181
                    },
                    {
                        "Lat": -27.43492,
                        "Lng": 153.09157
                    },
                    {
                        "Lat": -27.43492,
                        "Lng": 153.09157
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.09121
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.09121
                    },
                    {
                        "Lat": -27.43505,
                        "Lng": 153.08952
                    },
                    {
                        "Lat": -27.43505,
                        "Lng": 153.08952
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08926
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08926
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08901
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.08901
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.0886
                    },
                    {
                        "Lat": -27.43506,
                        "Lng": 153.0886
                    },
                    {
                        "Lat": -27.43495,
                        "Lng": 153.08752
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.08705
                    },
                    {
                        "Lat": -27.43488,
                        "Lng": 153.08705
                    },
                    {
                        "Lat": -27.43477,
                        "Lng": 153.08649
                    },
                    {
                        "Lat": -27.43477,
                        "Lng": 153.08649
                    },
                    {
                        "Lat": -27.43468,
                        "Lng": 153.08612
                    },
                    {
                        "Lat": -27.43468,
                        "Lng": 153.08612
                    },
                    {
                        "Lat": -27.43455,
                        "Lng": 153.08565
                    },
                    {
                        "Lat": -27.43441,
                        "Lng": 153.08521
                    },
                    {
                        "Lat": -27.43441,
                        "Lng": 153.08521
                    },
                    {
                        "Lat": -27.43421,
                        "Lng": 153.08466
                    },
                    {
                        "Lat": -27.43398,
                        "Lng": 153.08419
                    },
                    {
                        "Lat": -27.43369,
                        "Lng": 153.08374
                    },
                    {
                        "Lat": -27.43335,
                        "Lng": 153.08333
                    },
                    {
                        "Lat": -27.43335,
                        "Lng": 153.08333
                    },
                    {
                        "Lat": -27.43323,
                        "Lng": 153.0832
                    },
                    {
                        "Lat": -27.43323,
                        "Lng": 153.0832
                    },
                    {
                        "Lat": -27.43296,
                        "Lng": 153.08296
                    },
                    {
                        "Lat": -27.43296,
                        "Lng": 153.08296
                    },
                    {
                        "Lat": -27.43271,
                        "Lng": 153.08276
                    },
                    {
                        "Lat": -27.43234,
                        "Lng": 153.08252
                    },
                    {
                        "Lat": -27.43126,
                        "Lng": 153.08196
                    },
                    {
                        "Lat": -27.431,
                        "Lng": 153.0818
                    },
                    {
                        "Lat": -27.431,
                        "Lng": 153.0818
                    },
                    {
                        "Lat": -27.4308,
                        "Lng": 153.08168
                    },
                    {
                        "Lat": -27.4308,
                        "Lng": 153.08168
                    },
                    {
                        "Lat": -27.43063,
                        "Lng": 153.08157
                    },
                    {
                        "Lat": -27.43063,
                        "Lng": 153.08157
                    },
                    {
                        "Lat": -27.43052,
                        "Lng": 153.08149
                    },
                    {
                        "Lat": -27.43052,
                        "Lng": 153.08149
                    },
                    {
                        "Lat": -27.43043,
                        "Lng": 153.08142
                    },
                    {
                        "Lat": -27.43043,
                        "Lng": 153.08142
                    },
                    {
                        "Lat": -27.43008,
                        "Lng": 153.08115
                    },
                    {
                        "Lat": -27.43008,
                        "Lng": 153.08115
                    },
                    {
                        "Lat": -27.42972,
                        "Lng": 153.08085
                    },
                    {
                        "Lat": -27.42972,
                        "Lng": 153.08085
                    },
                    {
                        "Lat": -27.42957,
                        "Lng": 153.08071
                    },
                    {
                        "Lat": -27.42957,
                        "Lng": 153.08071
                    },
                    {
                        "Lat": -27.42926,
                        "Lng": 153.08046
                    },
                    {
                        "Lat": -27.42906,
                        "Lng": 153.08032
                    },
                    {
                        "Lat": -27.42882,
                        "Lng": 153.08017
                    },
                    {
                        "Lat": -27.4286,
                        "Lng": 153.08005
                    },
                    {
                        "Lat": -27.4282,
                        "Lng": 153.07987
                    },
                    {
                        "Lat": -27.42799,
                        "Lng": 153.0798
                    },
                    {
                        "Lat": -27.42757,
                        "Lng": 153.07969
                    },
                    {
                        "Lat": -27.42757,
                        "Lng": 153.07969
                    },
                    {
                        "Lat": -27.42712,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42712,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42679,
                        "Lng": 153.07961
                    },
                    {
                        "Lat": -27.42679,
                        "Lng": 153.07961
                    },
                    {
                        "Lat": -27.42638,
                        "Lng": 153.07962
                    },
                    {
                        "Lat": -27.42599,
                        "Lng": 153.07967
                    },
                    {
                        "Lat": -27.42561,
                        "Lng": 153.07975
                    },
                    {
                        "Lat": -27.42454,
                        "Lng": 153.08001
                    },
                    {
                        "Lat": -27.42454,
                        "Lng": 153.08001
                    },
                    {
                        "Lat": -27.42403,
                        "Lng": 153.08013
                    },
                    {
                        "Lat": -27.42403,
                        "Lng": 153.08013
                    },
                    {
                        "Lat": -27.42323,
                        "Lng": 153.08031
                    },
                    {
                        "Lat": -27.42274,
                        "Lng": 153.08035
                    },
                    {
                        "Lat": -27.42237,
                        "Lng": 153.08034
                    },
                    {
                        "Lat": -27.42237,
                        "Lng": 153.08034
                    },
                    {
                        "Lat": -27.42221,
                        "Lng": 153.08032
                    },
                    {
                        "Lat": -27.4218,
                        "Lng": 153.08023
                    },
                    {
                        "Lat": -27.42158,
                        "Lng": 153.08017
                    },
                    {
                        "Lat": -27.42131,
                        "Lng": 153.08007
                    },
                    {
                        "Lat": -27.42093,
                        "Lng": 153.07989
                    },
                    {
                        "Lat": -27.42067,
                        "Lng": 153.07973
                    },
                    {
                        "Lat": -27.42031,
                        "Lng": 153.07947
                    },
                    {
                        "Lat": -27.42031,
                        "Lng": 153.07947
                    },
                    {
                        "Lat": -27.42009,
                        "Lng": 153.07928
                    },
                    {
                        "Lat": -27.41994,
                        "Lng": 153.07913
                    },
                    {
                        "Lat": -27.41958,
                        "Lng": 153.07874
                    },
                    {
                        "Lat": -27.41958,
                        "Lng": 153.07874
                    },
                    {
                        "Lat": -27.41889,
                        "Lng": 153.07801
                    },
                    {
                        "Lat": -27.41889,
                        "Lng": 153.07801
                    },
                    {
                        "Lat": -27.41869,
                        "Lng": 153.0778
                    },
                    {
                        "Lat": -27.4185,
                        "Lng": 153.07763
                    },
                    {
                        "Lat": -27.4185,
                        "Lng": 153.07763
                    },
                    {
                        "Lat": -27.41818,
                        "Lng": 153.07737
                    },
                    {
                        "Lat": -27.41788,
                        "Lng": 153.07717
                    },
                    {
                        "Lat": -27.41769,
                        "Lng": 153.07706
                    },
                    {
                        "Lat": -27.41769,
                        "Lng": 153.07706
                    },
                    {
                        "Lat": -27.4175,
                        "Lng": 153.07696
                    },
                    {
                        "Lat": -27.4175,
                        "Lng": 153.07696
                    },
                    {
                        "Lat": -27.41721,
                        "Lng": 153.07683
                    },
                    {
                        "Lat": -27.41698,
                        "Lng": 153.07675
                    },
                    {
                        "Lat": -27.41698,
                        "Lng": 153.07675
                    },
                    {
                        "Lat": -27.41675,
                        "Lng": 153.07669
                    },
                    {
                        "Lat": -27.41675,
                        "Lng": 153.07669
                    },
                    {
                        "Lat": -27.41632,
                        "Lng": 153.0766
                    },
                    {
                        "Lat": -27.41584,
                        "Lng": 153.07655
                    },
                    {
                        "Lat": -27.41584,
                        "Lng": 153.07655
                    },
                    {
                        "Lat": -27.41543,
                        "Lng": 153.07656
                    },
                    {
                        "Lat": -27.4151,
                        "Lng": 153.07659
                    },
                    {
                        "Lat": -27.4151,
                        "Lng": 153.07659
                    },
                    {
                        "Lat": -27.41309,
                        "Lng": 153.0768
                    },
                    {
                        "Lat": -27.41309,
                        "Lng": 153.0768
                    },
                    {
                        "Lat": -27.41256,
                        "Lng": 153.07686
                    }
                ]
            },
            {
                "Key": "inc-bne-04",
                "Priority": 4,
                "Headline": "River Road — roadworks",
                "Detail": "Between River Road and Mt Crosby Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 3759,
                "Latitude": -27.58851,
                "Longitude": 152.82298,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.58851,
                        "Lng": 152.82298
                    },
                    {
                        "Lat": -27.5883,
                        "Lng": 152.82273
                    },
                    {
                        "Lat": -27.5883,
                        "Lng": 152.82273
                    },
                    {
                        "Lat": -27.58814,
                        "Lng": 152.82253
                    },
                    {
                        "Lat": -27.58814,
                        "Lng": 152.82253
                    },
                    {
                        "Lat": -27.58793,
                        "Lng": 152.82227
                    },
                    {
                        "Lat": -27.58793,
                        "Lng": 152.82227
                    },
                    {
                        "Lat": -27.58765,
                        "Lng": 152.82192
                    },
                    {
                        "Lat": -27.58765,
                        "Lng": 152.82192
                    },
                    {
                        "Lat": -27.5874,
                        "Lng": 152.82161
                    },
                    {
                        "Lat": -27.5874,
                        "Lng": 152.82161
                    },
                    {
                        "Lat": -27.58699,
                        "Lng": 152.82109
                    },
                    {
                        "Lat": -27.58699,
                        "Lng": 152.82109
                    },
                    {
                        "Lat": -27.58483,
                        "Lng": 152.81839
                    },
                    {
                        "Lat": -27.58483,
                        "Lng": 152.81839
                    },
                    {
                        "Lat": -27.58454,
                        "Lng": 152.81803
                    },
                    {
                        "Lat": -27.58454,
                        "Lng": 152.81803
                    },
                    {
                        "Lat": -27.58413,
                        "Lng": 152.8175
                    },
                    {
                        "Lat": -27.58389,
                        "Lng": 152.81715
                    },
                    {
                        "Lat": -27.58389,
                        "Lng": 152.81715
                    },
                    {
                        "Lat": -27.58361,
                        "Lng": 152.81667
                    },
                    {
                        "Lat": -27.58347,
                        "Lng": 152.81638
                    },
                    {
                        "Lat": -27.5833,
                        "Lng": 152.81594
                    },
                    {
                        "Lat": -27.58317,
                        "Lng": 152.81555
                    },
                    {
                        "Lat": -27.58317,
                        "Lng": 152.81555
                    },
                    {
                        "Lat": -27.58305,
                        "Lng": 152.81512
                    },
                    {
                        "Lat": -27.58305,
                        "Lng": 152.81512
                    },
                    {
                        "Lat": -27.58293,
                        "Lng": 152.8146
                    },
                    {
                        "Lat": -27.58293,
                        "Lng": 152.8146
                    },
                    {
                        "Lat": -27.58278,
                        "Lng": 152.81396
                    },
                    {
                        "Lat": -27.58278,
                        "Lng": 152.81396
                    },
                    {
                        "Lat": -27.5826,
                        "Lng": 152.81321
                    },
                    {
                        "Lat": -27.5826,
                        "Lng": 152.81321
                    },
                    {
                        "Lat": -27.5825,
                        "Lng": 152.81282
                    },
                    {
                        "Lat": -27.5825,
                        "Lng": 152.81282
                    },
                    {
                        "Lat": -27.5824,
                        "Lng": 152.81242
                    },
                    {
                        "Lat": -27.58225,
                        "Lng": 152.81196
                    },
                    {
                        "Lat": -27.58225,
                        "Lng": 152.81196
                    },
                    {
                        "Lat": -27.58212,
                        "Lng": 152.81165
                    },
                    {
                        "Lat": -27.58212,
                        "Lng": 152.81165
                    },
                    {
                        "Lat": -27.58187,
                        "Lng": 152.81115
                    },
                    {
                        "Lat": -27.58162,
                        "Lng": 152.81073
                    },
                    {
                        "Lat": -27.58001,
                        "Lng": 152.80833
                    },
                    {
                        "Lat": -27.58001,
                        "Lng": 152.80833
                    },
                    {
                        "Lat": -27.57698,
                        "Lng": 152.80382
                    },
                    {
                        "Lat": -27.57655,
                        "Lng": 152.80312
                    },
                    {
                        "Lat": -27.57655,
                        "Lng": 152.80312
                    },
                    {
                        "Lat": -27.57645,
                        "Lng": 152.80294
                    },
                    {
                        "Lat": -27.57645,
                        "Lng": 152.80294
                    },
                    {
                        "Lat": -27.57633,
                        "Lng": 152.80269
                    },
                    {
                        "Lat": -27.57633,
                        "Lng": 152.80269
                    },
                    {
                        "Lat": -27.57605,
                        "Lng": 152.80197
                    },
                    {
                        "Lat": -27.57497,
                        "Lng": 152.79876
                    },
                    {
                        "Lat": -27.57497,
                        "Lng": 152.79876
                    },
                    {
                        "Lat": -27.57428,
                        "Lng": 152.7967
                    },
                    {
                        "Lat": -27.57416,
                        "Lng": 152.79631
                    },
                    {
                        "Lat": -27.57416,
                        "Lng": 152.79631
                    },
                    {
                        "Lat": -27.57404,
                        "Lng": 152.79586
                    },
                    {
                        "Lat": -27.57395,
                        "Lng": 152.79539
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.7949
                    },
                    {
                        "Lat": -27.57385,
                        "Lng": 152.79425
                    },
                    {
                        "Lat": -27.57385,
                        "Lng": 152.79425
                    },
                    {
                        "Lat": -27.57386,
                        "Lng": 152.79372
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.79333
                    },
                    {
                        "Lat": -27.57389,
                        "Lng": 152.79333
                    },
                    {
                        "Lat": -27.57394,
                        "Lng": 152.79294
                    },
                    {
                        "Lat": -27.57402,
                        "Lng": 152.79256
                    },
                    {
                        "Lat": -27.57402,
                        "Lng": 152.79256
                    },
                    {
                        "Lat": -27.57407,
                        "Lng": 152.79234
                    },
                    {
                        "Lat": -27.57407,
                        "Lng": 152.79234
                    },
                    {
                        "Lat": -27.57445,
                        "Lng": 152.79066
                    },
                    {
                        "Lat": -27.57445,
                        "Lng": 152.79066
                    },
                    {
                        "Lat": -27.57463,
                        "Lng": 152.78993
                    },
                    {
                        "Lat": -27.57463,
                        "Lng": 152.78993
                    },
                    {
                        "Lat": -27.57467,
                        "Lng": 152.78975
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on the Bruce Highway have lanes closed over 6 kilometres.",
                "CorridorKeys": [
                    "Bruce Highway"
                ]
            },
            {
                "Text": "Roadworks on Sandgate Road have lanes closed over 5 kilometres.",
                "CorridorKeys": [
                    "Sandgate Road"
                ]
            },
            {
                "Text": "Expect delays of around 5 minutes.",
                "CorridorKeys": [
                    "Sandgate Road"
                ]
            },
            {
                "Text": "Roadworks on Airport Drive have lanes closed over 4 kilometres.",
                "CorridorKeys": [
                    "Airport Drive"
                ]
            },
            {
                "Text": "Roadworks on River Road have lanes closed over 4 kilometres.",
                "CorridorKeys": [
                    "River Road"
                ]
            }
        ],
        "ContradictionPlanted": false
    },
    "goldcoast": {
        "FetchedAtIso": "2026-06-25T04:10:13.376Z",
        "Bbox": [
            153.3,
            -28.2,
            153.6,
            -27.8
        ],
        "Incidents": [
            {
                "Key": "inc-gc-01",
                "Priority": 4,
                "Headline": "Smith Street — roadworks",
                "Detail": "Between Smith Street Motorway and Pacific Motorway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 616,
                "Latitude": -27.95436,
                "Longitude": 153.34847,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95436,
                        "Lng": 153.34847
                    },
                    {
                        "Lat": -27.95375,
                        "Lng": 153.34804
                    },
                    {
                        "Lat": -27.95375,
                        "Lng": 153.34804
                    },
                    {
                        "Lat": -27.95303,
                        "Lng": 153.34754
                    },
                    {
                        "Lat": -27.95303,
                        "Lng": 153.34754
                    },
                    {
                        "Lat": -27.95254,
                        "Lng": 153.34721
                    },
                    {
                        "Lat": -27.95254,
                        "Lng": 153.34721
                    },
                    {
                        "Lat": -27.95222,
                        "Lng": 153.34698
                    },
                    {
                        "Lat": -27.95222,
                        "Lng": 153.34698
                    },
                    {
                        "Lat": -27.95207,
                        "Lng": 153.34685
                    },
                    {
                        "Lat": -27.95207,
                        "Lng": 153.34685
                    },
                    {
                        "Lat": -27.95189,
                        "Lng": 153.34667
                    },
                    {
                        "Lat": -27.95173,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.95173,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.95165,
                        "Lng": 153.34637
                    },
                    {
                        "Lat": -27.95165,
                        "Lng": 153.34637
                    },
                    {
                        "Lat": -27.95147,
                        "Lng": 153.34609
                    },
                    {
                        "Lat": -27.9513,
                        "Lng": 153.34574
                    },
                    {
                        "Lat": -27.9513,
                        "Lng": 153.34574
                    },
                    {
                        "Lat": -27.95117,
                        "Lng": 153.34539
                    },
                    {
                        "Lat": -27.95117,
                        "Lng": 153.34539
                    },
                    {
                        "Lat": -27.95113,
                        "Lng": 153.34527
                    },
                    {
                        "Lat": -27.95113,
                        "Lng": 153.34527
                    },
                    {
                        "Lat": -27.95102,
                        "Lng": 153.34487
                    },
                    {
                        "Lat": -27.95102,
                        "Lng": 153.34487
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.3444
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.3444
                    },
                    {
                        "Lat": -27.9509,
                        "Lng": 153.344
                    }
                ]
            },
            {
                "Key": "inc-gc-02",
                "Priority": 4,
                "Headline": "Coast Highway — breakdown",
                "Detail": "At Gold Coast Highway - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 356,
                "Latitude": -27.94541,
                "Longitude": 153.3394,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.94541,
                        "Lng": 153.3394
                    },
                    {
                        "Lat": -27.94381,
                        "Lng": 153.33865
                    },
                    {
                        "Lat": -27.94244,
                        "Lng": 153.33804
                    }
                ]
            },
            {
                "Key": "inc-gc-03",
                "Priority": 4,
                "Headline": "Island Road — hazard",
                "Detail": "At Hope Island Road - Spilled loads on roadway",
                "FunctionalClass": 1,
                "LengthMeters": 217,
                "Latitude": -27.90186,
                "Longitude": 153.31543,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.90186,
                        "Lng": 153.31543
                    },
                    {
                        "Lat": -27.90104,
                        "Lng": 153.31518
                    },
                    {
                        "Lat": -27.90017,
                        "Lng": 153.31496
                    },
                    {
                        "Lat": -27.90017,
                        "Lng": 153.31496
                    },
                    {
                        "Lat": -27.89996,
                        "Lng": 153.31491
                    }
                ]
            },
            {
                "Key": "inc-gc-04",
                "Priority": 3,
                "Headline": "Smith Street — roadworks",
                "Detail": "Ramp to Smith Street (Northbound) - Closed due to road construction",
                "FunctionalClass": 3,
                "LengthMeters": 500,
                "Latitude": -27.95946,
                "Longitude": 153.34714,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95946,
                        "Lng": 153.34714
                    },
                    {
                        "Lat": -27.95975,
                        "Lng": 153.34769
                    },
                    {
                        "Lat": -27.95996,
                        "Lng": 153.34812
                    },
                    {
                        "Lat": -27.96015,
                        "Lng": 153.34857
                    },
                    {
                        "Lat": -27.96026,
                        "Lng": 153.34888
                    },
                    {
                        "Lat": -27.96026,
                        "Lng": 153.34888
                    },
                    {
                        "Lat": -27.96033,
                        "Lng": 153.3491
                    },
                    {
                        "Lat": -27.96037,
                        "Lng": 153.34927
                    },
                    {
                        "Lat": -27.9604,
                        "Lng": 153.34951
                    },
                    {
                        "Lat": -27.9604,
                        "Lng": 153.34963
                    },
                    {
                        "Lat": -27.96039,
                        "Lng": 153.34975
                    },
                    {
                        "Lat": -27.96035,
                        "Lng": 153.34995
                    },
                    {
                        "Lat": -27.96027,
                        "Lng": 153.35017
                    },
                    {
                        "Lat": -27.96018,
                        "Lng": 153.35034
                    },
                    {
                        "Lat": -27.96007,
                        "Lng": 153.35049
                    },
                    {
                        "Lat": -27.95996,
                        "Lng": 153.35061
                    },
                    {
                        "Lat": -27.95986,
                        "Lng": 153.35069
                    },
                    {
                        "Lat": -27.9597,
                        "Lng": 153.35079
                    },
                    {
                        "Lat": -27.95951,
                        "Lng": 153.35086
                    },
                    {
                        "Lat": -27.95931,
                        "Lng": 153.3509
                    },
                    {
                        "Lat": -27.95914,
                        "Lng": 153.35091
                    },
                    {
                        "Lat": -27.95892,
                        "Lng": 153.35089
                    }
                ]
            },
            {
                "Key": "inc-gc-05",
                "Priority": 2,
                "Headline": "Smith Street — roadworks",
                "Detail": "At Smith Street - Road construction",
                "FunctionalClass": 4,
                "LengthMeters": 378,
                "Latitude": -27.94869,
                "Longitude": 153.34699,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.94869,
                        "Lng": 153.34699
                    },
                    {
                        "Lat": -27.94878,
                        "Lng": 153.34677
                    },
                    {
                        "Lat": -27.9489,
                        "Lng": 153.34657
                    },
                    {
                        "Lat": -27.949,
                        "Lng": 153.34644
                    },
                    {
                        "Lat": -27.94914,
                        "Lng": 153.34631
                    },
                    {
                        "Lat": -27.94914,
                        "Lng": 153.34631
                    },
                    {
                        "Lat": -27.94934,
                        "Lng": 153.3462
                    },
                    {
                        "Lat": -27.94934,
                        "Lng": 153.3462
                    },
                    {
                        "Lat": -27.94953,
                        "Lng": 153.34615
                    },
                    {
                        "Lat": -27.94966,
                        "Lng": 153.34615
                    },
                    {
                        "Lat": -27.94987,
                        "Lng": 153.3462
                    },
                    {
                        "Lat": -27.95008,
                        "Lng": 153.3463
                    },
                    {
                        "Lat": -27.95008,
                        "Lng": 153.3463
                    },
                    {
                        "Lat": -27.95032,
                        "Lng": 153.34644
                    },
                    {
                        "Lat": -27.95032,
                        "Lng": 153.34644
                    },
                    {
                        "Lat": -27.95067,
                        "Lng": 153.34666
                    },
                    {
                        "Lat": -27.95067,
                        "Lng": 153.34666
                    },
                    {
                        "Lat": -27.95082,
                        "Lng": 153.34673
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.34676
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.34676
                    },
                    {
                        "Lat": -27.95101,
                        "Lng": 153.34677
                    },
                    {
                        "Lat": -27.95116,
                        "Lng": 153.34676
                    },
                    {
                        "Lat": -27.95116,
                        "Lng": 153.34676
                    },
                    {
                        "Lat": -27.9513,
                        "Lng": 153.34673
                    },
                    {
                        "Lat": -27.95141,
                        "Lng": 153.34669
                    },
                    {
                        "Lat": -27.95141,
                        "Lng": 153.34669
                    },
                    {
                        "Lat": -27.9516,
                        "Lng": 153.34657
                    }
                ]
            },
            {
                "Key": "inc-gc-06",
                "Priority": 2,
                "Headline": "Smith Street — roadworks",
                "Detail": "Between Smith Street and Greenacre Drive - Road construction",
                "FunctionalClass": 4,
                "LengthMeters": 350,
                "Latitude": -27.95134,
                "Longitude": 153.34657,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95134,
                        "Lng": 153.34657
                    },
                    {
                        "Lat": -27.95121,
                        "Lng": 153.34661
                    },
                    {
                        "Lat": -27.9511,
                        "Lng": 153.34662
                    },
                    {
                        "Lat": -27.9511,
                        "Lng": 153.34662
                    },
                    {
                        "Lat": -27.95097,
                        "Lng": 153.34661
                    },
                    {
                        "Lat": -27.95097,
                        "Lng": 153.34661
                    },
                    {
                        "Lat": -27.95078,
                        "Lng": 153.34656
                    },
                    {
                        "Lat": -27.9506,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.9506,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.95025,
                        "Lng": 153.3463
                    },
                    {
                        "Lat": -27.95025,
                        "Lng": 153.3463
                    },
                    {
                        "Lat": -27.95,
                        "Lng": 153.34617
                    },
                    {
                        "Lat": -27.94977,
                        "Lng": 153.34609
                    },
                    {
                        "Lat": -27.9496,
                        "Lng": 153.34607
                    },
                    {
                        "Lat": -27.94936,
                        "Lng": 153.34612
                    },
                    {
                        "Lat": -27.94936,
                        "Lng": 153.34612
                    },
                    {
                        "Lat": -27.94919,
                        "Lng": 153.34619
                    },
                    {
                        "Lat": -27.94903,
                        "Lng": 153.34631
                    },
                    {
                        "Lat": -27.94903,
                        "Lng": 153.34631
                    },
                    {
                        "Lat": -27.94888,
                        "Lng": 153.34649
                    },
                    {
                        "Lat": -27.94876,
                        "Lng": 153.34669
                    },
                    {
                        "Lat": -27.94876,
                        "Lng": 153.34669
                    },
                    {
                        "Lat": -27.94865,
                        "Lng": 153.34687
                    },
                    {
                        "Lat": -27.94859,
                        "Lng": 153.34694
                    }
                ]
            },
            {
                "Key": "inc-gc-07",
                "Priority": 2,
                "Headline": "Road construction",
                "Detail": "Road construction",
                "FunctionalClass": 4,
                "LengthMeters": 46,
                "Latitude": -28.1139,
                "Longitude": 153.318,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -28.1139,
                        "Lng": 153.318
                    },
                    {
                        "Lat": -28.11364,
                        "Lng": 153.31802
                    },
                    {
                        "Lat": -28.11364,
                        "Lng": 153.31802
                    },
                    {
                        "Lat": -28.11349,
                        "Lng": 153.31805
                    }
                ]
            },
            {
                "Key": "inc-gc-08",
                "Priority": 1,
                "Headline": "Closed",
                "Detail": "Closed",
                "FunctionalClass": 5,
                "LengthMeters": 193,
                "Latitude": -27.95334,
                "Longitude": 153.34614,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95334,
                        "Lng": 153.34614
                    },
                    {
                        "Lat": -27.95318,
                        "Lng": 153.34612
                    },
                    {
                        "Lat": -27.95304,
                        "Lng": 153.34613
                    },
                    {
                        "Lat": -27.95304,
                        "Lng": 153.34613
                    },
                    {
                        "Lat": -27.95285,
                        "Lng": 153.34609
                    },
                    {
                        "Lat": -27.95285,
                        "Lng": 153.34609
                    },
                    {
                        "Lat": -27.95245,
                        "Lng": 153.34608
                    },
                    {
                        "Lat": -27.95245,
                        "Lng": 153.34608
                    },
                    {
                        "Lat": -27.9523,
                        "Lng": 153.34607
                    },
                    {
                        "Lat": -27.952,
                        "Lng": 153.34619
                    },
                    {
                        "Lat": -27.952,
                        "Lng": 153.34619
                    },
                    {
                        "Lat": -27.95165,
                        "Lng": 153.34637
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "Smith Street",
                "Severity": 1,
                "ImpactScore": 228.8,
                "PrimaryType": "construction",
                "Description": "3 incidents including roadworks",
                "IncidentCount": 3,
                "TotalLengthKm": 1.3,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 2,
                "DelayMinutes": null,
                "Latitude": -27.95436,
                "Longitude": 153.34847,
                "IncidentKeys": [
                    "inc-gc-01",
                    "inc-gc-05",
                    "inc-gc-06"
                ]
            },
            {
                "RoadName": "Coast Highway",
                "Severity": 1,
                "ImpactScore": 205,
                "PrimaryType": "disabledVehicle",
                "Description": "breakdown",
                "IncidentCount": 1,
                "TotalLengthKm": 0.4,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -27.94541,
                "Longitude": 153.3394,
                "IncidentKeys": [
                    "inc-gc-02"
                ]
            },
            {
                "RoadName": "Island Road",
                "Severity": 1,
                "ImpactScore": 203.4,
                "PrimaryType": "roadHazard",
                "Description": "hazard",
                "IncidentCount": 1,
                "TotalLengthKm": 0.2,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -27.90186,
                "Longitude": 153.31543,
                "IncidentKeys": [
                    "inc-gc-03"
                ]
            },
            {
                "RoadName": "Smith Street",
                "Severity": 2,
                "ImpactScore": 145,
                "PrimaryType": "construction",
                "Description": "roadworks northbound",
                "IncidentCount": 1,
                "TotalLengthKm": 0.5,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 3,
                "Direction": "northbound",
                "Suburb": null,
                "NormalDriveTimeMinutes": 8,
                "DelayMinutes": null,
                "Latitude": -27.95946,
                "Longitude": 153.34714,
                "IncidentKeys": [
                    "inc-gc-04"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-gc-01",
                "Priority": 4,
                "Headline": "Smith Street — roadworks",
                "Detail": "Between Smith Street Motorway and Pacific Motorway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 616,
                "Latitude": -27.95436,
                "Longitude": 153.34847,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95436,
                        "Lng": 153.34847
                    },
                    {
                        "Lat": -27.95375,
                        "Lng": 153.34804
                    },
                    {
                        "Lat": -27.95375,
                        "Lng": 153.34804
                    },
                    {
                        "Lat": -27.95303,
                        "Lng": 153.34754
                    },
                    {
                        "Lat": -27.95303,
                        "Lng": 153.34754
                    },
                    {
                        "Lat": -27.95254,
                        "Lng": 153.34721
                    },
                    {
                        "Lat": -27.95254,
                        "Lng": 153.34721
                    },
                    {
                        "Lat": -27.95222,
                        "Lng": 153.34698
                    },
                    {
                        "Lat": -27.95222,
                        "Lng": 153.34698
                    },
                    {
                        "Lat": -27.95207,
                        "Lng": 153.34685
                    },
                    {
                        "Lat": -27.95207,
                        "Lng": 153.34685
                    },
                    {
                        "Lat": -27.95189,
                        "Lng": 153.34667
                    },
                    {
                        "Lat": -27.95173,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.95173,
                        "Lng": 153.34648
                    },
                    {
                        "Lat": -27.95165,
                        "Lng": 153.34637
                    },
                    {
                        "Lat": -27.95165,
                        "Lng": 153.34637
                    },
                    {
                        "Lat": -27.95147,
                        "Lng": 153.34609
                    },
                    {
                        "Lat": -27.9513,
                        "Lng": 153.34574
                    },
                    {
                        "Lat": -27.9513,
                        "Lng": 153.34574
                    },
                    {
                        "Lat": -27.95117,
                        "Lng": 153.34539
                    },
                    {
                        "Lat": -27.95117,
                        "Lng": 153.34539
                    },
                    {
                        "Lat": -27.95113,
                        "Lng": 153.34527
                    },
                    {
                        "Lat": -27.95113,
                        "Lng": 153.34527
                    },
                    {
                        "Lat": -27.95102,
                        "Lng": 153.34487
                    },
                    {
                        "Lat": -27.95102,
                        "Lng": 153.34487
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.3444
                    },
                    {
                        "Lat": -27.95094,
                        "Lng": 153.3444
                    },
                    {
                        "Lat": -27.9509,
                        "Lng": 153.344
                    }
                ]
            },
            {
                "Key": "inc-gc-02",
                "Priority": 4,
                "Headline": "Coast Highway — breakdown",
                "Detail": "At Gold Coast Highway - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 356,
                "Latitude": -27.94541,
                "Longitude": 153.3394,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.94541,
                        "Lng": 153.3394
                    },
                    {
                        "Lat": -27.94381,
                        "Lng": 153.33865
                    },
                    {
                        "Lat": -27.94244,
                        "Lng": 153.33804
                    }
                ]
            },
            {
                "Key": "inc-gc-03",
                "Priority": 4,
                "Headline": "Island Road — hazard",
                "Detail": "At Hope Island Road - Spilled loads on roadway",
                "FunctionalClass": 1,
                "LengthMeters": 217,
                "Latitude": -27.90186,
                "Longitude": 153.31543,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.90186,
                        "Lng": 153.31543
                    },
                    {
                        "Lat": -27.90104,
                        "Lng": 153.31518
                    },
                    {
                        "Lat": -27.90017,
                        "Lng": 153.31496
                    },
                    {
                        "Lat": -27.90017,
                        "Lng": 153.31496
                    },
                    {
                        "Lat": -27.89996,
                        "Lng": 153.31491
                    }
                ]
            },
            {
                "Key": "inc-gc-04",
                "Priority": 3,
                "Headline": "Smith Street — roadworks",
                "Detail": "Ramp to Smith Street (Northbound) - Closed due to road construction",
                "FunctionalClass": 3,
                "LengthMeters": 500,
                "Latitude": -27.95946,
                "Longitude": 153.34714,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -27.95946,
                        "Lng": 153.34714
                    },
                    {
                        "Lat": -27.95975,
                        "Lng": 153.34769
                    },
                    {
                        "Lat": -27.95996,
                        "Lng": 153.34812
                    },
                    {
                        "Lat": -27.96015,
                        "Lng": 153.34857
                    },
                    {
                        "Lat": -27.96026,
                        "Lng": 153.34888
                    },
                    {
                        "Lat": -27.96026,
                        "Lng": 153.34888
                    },
                    {
                        "Lat": -27.96033,
                        "Lng": 153.3491
                    },
                    {
                        "Lat": -27.96037,
                        "Lng": 153.34927
                    },
                    {
                        "Lat": -27.9604,
                        "Lng": 153.34951
                    },
                    {
                        "Lat": -27.9604,
                        "Lng": 153.34963
                    },
                    {
                        "Lat": -27.96039,
                        "Lng": 153.34975
                    },
                    {
                        "Lat": -27.96035,
                        "Lng": 153.34995
                    },
                    {
                        "Lat": -27.96027,
                        "Lng": 153.35017
                    },
                    {
                        "Lat": -27.96018,
                        "Lng": 153.35034
                    },
                    {
                        "Lat": -27.96007,
                        "Lng": 153.35049
                    },
                    {
                        "Lat": -27.95996,
                        "Lng": 153.35061
                    },
                    {
                        "Lat": -27.95986,
                        "Lng": 153.35069
                    },
                    {
                        "Lat": -27.9597,
                        "Lng": 153.35079
                    },
                    {
                        "Lat": -27.95951,
                        "Lng": 153.35086
                    },
                    {
                        "Lat": -27.95931,
                        "Lng": 153.3509
                    },
                    {
                        "Lat": -27.95914,
                        "Lng": 153.35091
                    },
                    {
                        "Lat": -27.95892,
                        "Lng": 153.35089
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on Smith Street have lanes closed.",
                "CorridorKeys": [
                    "Smith Street"
                ]
            },
            {
                "Text": "A breakdown on the Coast Highway is blocking a lane.",
                "CorridorKeys": [
                    "Coast Highway"
                ]
            },
            {
                "Text": "A hazard on Island Road is slowing traffic.",
                "CorridorKeys": [
                    "Island Road"
                ]
            },
            {
                "Text": "Roadworks on Smith Street northbound have lanes closed.",
                "CorridorKeys": [
                    "Smith Street"
                ]
            }
        ],
        "ContradictionPlanted": false
    },
    "perth": {
        "FetchedAtIso": "2026-06-25T04:10:14.250Z",
        "Bbox": [
            115.6,
            -32.3,
            116.1,
            -31.6
        ],
        "Incidents": [
            {
                "Key": "inc-per-01",
                "Priority": 4,
                "Headline": "Altone Road — road closure",
                "Detail": "At Altone Road - Closed",
                "FunctionalClass": 1,
                "LengthMeters": 285,
                "Latitude": -31.86102,
                "Longitude": 115.94429,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86102,
                        "Lng": 115.94429
                    },
                    {
                        "Lat": -31.86097,
                        "Lng": 115.94482
                    },
                    {
                        "Lat": -31.86092,
                        "Lng": 115.94498
                    },
                    {
                        "Lat": -31.86085,
                        "Lng": 115.9451
                    },
                    {
                        "Lat": -31.86085,
                        "Lng": 115.9451
                    },
                    {
                        "Lat": -31.86074,
                        "Lng": 115.94515
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94528
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94528
                    },
                    {
                        "Lat": -31.86058,
                        "Lng": 115.94539
                    },
                    {
                        "Lat": -31.86057,
                        "Lng": 115.94548
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94564
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94564
                    },
                    {
                        "Lat": -31.86067,
                        "Lng": 115.94573
                    },
                    {
                        "Lat": -31.86073,
                        "Lng": 115.94577
                    },
                    {
                        "Lat": -31.86073,
                        "Lng": 115.94577
                    },
                    {
                        "Lat": -31.86083,
                        "Lng": 115.94596
                    },
                    {
                        "Lat": -31.86086,
                        "Lng": 115.94605
                    },
                    {
                        "Lat": -31.86087,
                        "Lng": 115.94613
                    },
                    {
                        "Lat": -31.86082,
                        "Lng": 115.94668
                    },
                    {
                        "Lat": -31.86084,
                        "Lng": 115.94702
                    }
                ]
            },
            {
                "Key": "inc-per-02",
                "Priority": 4,
                "Headline": "Tonkin Highway — roadworks",
                "Detail": "At Tonkin Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 223,
                "Latitude": -31.86065,
                "Longitude": 115.95505,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86065,
                        "Lng": 115.95505
                    },
                    {
                        "Lat": -31.86063,
                        "Lng": 115.95451
                    },
                    {
                        "Lat": -31.86064,
                        "Lng": 115.95393
                    },
                    {
                        "Lat": -31.8607,
                        "Lng": 115.95267
                    }
                ]
            },
            {
                "Key": "inc-per-03",
                "Priority": 4,
                "Headline": "Altone Road — roadworks",
                "Detail": "At Altone Road - Closed due to road construction",
                "FunctionalClass": 1,
                "LengthMeters": 202,
                "Latitude": -31.86092,
                "Longitude": 115.94748,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86092,
                        "Lng": 115.94748
                    },
                    {
                        "Lat": -31.86093,
                        "Lng": 115.94722
                    },
                    {
                        "Lat": -31.8609,
                        "Lng": 115.94672
                    },
                    {
                        "Lat": -31.86091,
                        "Lng": 115.94652
                    },
                    {
                        "Lat": -31.86097,
                        "Lng": 115.94602
                    },
                    {
                        "Lat": -31.86104,
                        "Lng": 115.94578
                    },
                    {
                        "Lat": -31.86104,
                        "Lng": 115.94578
                    },
                    {
                        "Lat": -31.86109,
                        "Lng": 115.94575
                    },
                    {
                        "Lat": -31.86109,
                        "Lng": 115.94575
                    },
                    {
                        "Lat": -31.86116,
                        "Lng": 115.94564
                    },
                    {
                        "Lat": -31.86118,
                        "Lng": 115.94554
                    },
                    {
                        "Lat": -31.86118,
                        "Lng": 115.94541
                    }
                ]
            },
            {
                "Key": "inc-per-04",
                "Priority": 4,
                "Headline": "Mitchell Freeway — roadworks",
                "Detail": "At Mitchell Freeway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 168,
                "Latitude": -31.94188,
                "Longitude": 115.84621,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.94188,
                        "Lng": 115.84621
                    },
                    {
                        "Lat": -31.94194,
                        "Lng": 115.84643
                    },
                    {
                        "Lat": -31.94194,
                        "Lng": 115.84643
                    },
                    {
                        "Lat": -31.94207,
                        "Lng": 115.84685
                    },
                    {
                        "Lat": -31.94226,
                        "Lng": 115.84757
                    },
                    {
                        "Lat": -31.94234,
                        "Lng": 115.84792
                    }
                ]
            },
            {
                "Key": "inc-per-05",
                "Priority": 4,
                "Headline": "Beach Road — breakdown",
                "Detail": "At Scarborough Beach Road - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 147,
                "Latitude": -31.91262,
                "Longitude": 115.82313,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.91262,
                        "Lng": 115.82313
                    },
                    {
                        "Lat": -31.91394,
                        "Lng": 115.82329
                    }
                ]
            },
            {
                "Key": "inc-per-06",
                "Priority": 4,
                "Headline": "Farmer Freeway — breakdown",
                "Detail": "At Graham Farmer Freeway - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 85,
                "Latitude": -31.94657,
                "Longitude": 115.85001,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.94657,
                        "Lng": 115.85001
                    },
                    {
                        "Lat": -31.94644,
                        "Lng": 115.84998
                    },
                    {
                        "Lat": -31.94644,
                        "Lng": 115.84998
                    },
                    {
                        "Lat": -31.94619,
                        "Lng": 115.84991
                    },
                    {
                        "Lat": -31.94583,
                        "Lng": 115.84978
                    }
                ]
            },
            {
                "Key": "inc-per-07",
                "Priority": 4,
                "Headline": "Lord Street — roadworks",
                "Detail": "At Lord Street - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 81,
                "Latitude": -31.86163,
                "Longitude": 115.96323,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86163,
                        "Lng": 115.96323
                    },
                    {
                        "Lat": -31.86189,
                        "Lng": 115.96404
                    }
                ]
            },
            {
                "Key": "inc-per-08",
                "Priority": 4,
                "Headline": "Stephenson Avenue — roadworks",
                "Detail": "At Stephenson Avenue - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 33,
                "Latitude": -31.89319,
                "Longitude": 115.80395,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.89319,
                        "Lng": 115.80395
                    },
                    {
                        "Lat": -31.89326,
                        "Lng": 115.80402
                    },
                    {
                        "Lat": -31.89326,
                        "Lng": 115.80402
                    },
                    {
                        "Lat": -31.89342,
                        "Lng": 115.80418
                    }
                ]
            },
            {
                "Key": "inc-per-09",
                "Priority": 4,
                "Headline": "Reid Highway — roadworks",
                "Detail": "At Reid Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 30,
                "Latitude": -31.86278,
                "Longitude": 115.96859,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86278,
                        "Lng": 115.96859
                    },
                    {
                        "Lat": -31.86284,
                        "Lng": 115.9689
                    }
                ]
            },
            {
                "Key": "inc-per-10",
                "Priority": 4,
                "Headline": "Northern Highway — roadworks",
                "Detail": "At Great Northern Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 16,
                "Latitude": -31.86425,
                "Longitude": 116.01415,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86425,
                        "Lng": 116.01415
                    },
                    {
                        "Lat": -31.86426,
                        "Lng": 116.01398
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "Mitchell Freeway",
                "Severity": 1,
                "ImpactScore": 262.9,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.2,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -31.94188,
                "Longitude": 115.84621,
                "PriorityBoosted": true,
                "IncidentKeys": [
                    "inc-per-04"
                ]
            },
            {
                "RoadName": "Altone Road",
                "Severity": 1,
                "ImpactScore": 220,
                "PrimaryType": "roadClosure",
                "Description": "2 incidents including road closure",
                "IncidentCount": 2,
                "TotalLengthKm": 0.5,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -31.86102,
                "Longitude": 115.94429,
                "IncidentKeys": [
                    "inc-per-01",
                    "inc-per-03"
                ]
            },
            {
                "RoadName": "Tonkin Highway",
                "Severity": 1,
                "ImpactScore": 203.5,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.2,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -31.86065,
                "Longitude": 115.95505,
                "IncidentKeys": [
                    "inc-per-02"
                ]
            },
            {
                "RoadName": "Beach Road",
                "Severity": 1,
                "ImpactScore": 201.7,
                "PrimaryType": "disabledVehicle",
                "Description": "breakdown",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -31.91262,
                "Longitude": 115.82313,
                "IncidentKeys": [
                    "inc-per-05"
                ]
            },
            {
                "RoadName": "Farmer Freeway",
                "Severity": 1,
                "ImpactScore": 199.3,
                "PrimaryType": "disabledVehicle",
                "Description": "breakdown",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 5,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -31.94657,
                "Longitude": 115.85001,
                "IncidentKeys": [
                    "inc-per-06"
                ]
            },
            {
                "RoadName": "Lord Street",
                "Severity": 1,
                "ImpactScore": 199.1,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 6,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -31.86163,
                "Longitude": 115.96323,
                "IncidentKeys": [
                    "inc-per-07"
                ]
            },
            {
                "RoadName": "Stephenson Avenue",
                "Severity": 1,
                "ImpactScore": 195.2,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0,
                "Rank": 7,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -31.89319,
                "Longitude": 115.80395,
                "IncidentKeys": [
                    "inc-per-08"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-per-04",
                "Priority": 4,
                "Headline": "Mitchell Freeway — roadworks",
                "Detail": "At Mitchell Freeway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 168,
                "Latitude": -31.94188,
                "Longitude": 115.84621,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.94188,
                        "Lng": 115.84621
                    },
                    {
                        "Lat": -31.94194,
                        "Lng": 115.84643
                    },
                    {
                        "Lat": -31.94194,
                        "Lng": 115.84643
                    },
                    {
                        "Lat": -31.94207,
                        "Lng": 115.84685
                    },
                    {
                        "Lat": -31.94226,
                        "Lng": 115.84757
                    },
                    {
                        "Lat": -31.94234,
                        "Lng": 115.84792
                    }
                ]
            },
            {
                "Key": "inc-per-01",
                "Priority": 4,
                "Headline": "Altone Road — road closure",
                "Detail": "At Altone Road - Closed",
                "FunctionalClass": 1,
                "LengthMeters": 285,
                "Latitude": -31.86102,
                "Longitude": 115.94429,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86102,
                        "Lng": 115.94429
                    },
                    {
                        "Lat": -31.86097,
                        "Lng": 115.94482
                    },
                    {
                        "Lat": -31.86092,
                        "Lng": 115.94498
                    },
                    {
                        "Lat": -31.86085,
                        "Lng": 115.9451
                    },
                    {
                        "Lat": -31.86085,
                        "Lng": 115.9451
                    },
                    {
                        "Lat": -31.86074,
                        "Lng": 115.94515
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94528
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94528
                    },
                    {
                        "Lat": -31.86058,
                        "Lng": 115.94539
                    },
                    {
                        "Lat": -31.86057,
                        "Lng": 115.94548
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94564
                    },
                    {
                        "Lat": -31.86061,
                        "Lng": 115.94564
                    },
                    {
                        "Lat": -31.86067,
                        "Lng": 115.94573
                    },
                    {
                        "Lat": -31.86073,
                        "Lng": 115.94577
                    },
                    {
                        "Lat": -31.86073,
                        "Lng": 115.94577
                    },
                    {
                        "Lat": -31.86083,
                        "Lng": 115.94596
                    },
                    {
                        "Lat": -31.86086,
                        "Lng": 115.94605
                    },
                    {
                        "Lat": -31.86087,
                        "Lng": 115.94613
                    },
                    {
                        "Lat": -31.86082,
                        "Lng": 115.94668
                    },
                    {
                        "Lat": -31.86084,
                        "Lng": 115.94702
                    }
                ]
            },
            {
                "Key": "inc-per-02",
                "Priority": 4,
                "Headline": "Tonkin Highway — roadworks",
                "Detail": "At Tonkin Highway - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 223,
                "Latitude": -31.86065,
                "Longitude": 115.95505,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.86065,
                        "Lng": 115.95505
                    },
                    {
                        "Lat": -31.86063,
                        "Lng": 115.95451
                    },
                    {
                        "Lat": -31.86064,
                        "Lng": 115.95393
                    },
                    {
                        "Lat": -31.8607,
                        "Lng": 115.95267
                    }
                ]
            },
            {
                "Key": "inc-per-05",
                "Priority": 4,
                "Headline": "Beach Road — breakdown",
                "Detail": "At Scarborough Beach Road - Broken-down vehicle",
                "FunctionalClass": 1,
                "LengthMeters": 147,
                "Latitude": -31.91262,
                "Longitude": 115.82313,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -31.91262,
                        "Lng": 115.82313
                    },
                    {
                        "Lat": -31.91394,
                        "Lng": 115.82329
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on the Mitchell Freeway have lanes closed.",
                "CorridorKeys": [
                    "Mitchell Freeway"
                ]
            },
            {
                "Text": "Altone Road is closed.",
                "CorridorKeys": [
                    "Altone Road"
                ]
            },
            {
                "Text": "Roadworks on the Tonkin Highway have lanes closed.",
                "CorridorKeys": [
                    "Tonkin Highway"
                ]
            },
            {
                "Text": "A breakdown on Beach Road is blocking a lane.",
                "CorridorKeys": [
                    "Beach Road"
                ]
            }
        ],
        "ContradictionPlanted": false
    },
    "adelaide": {
        "FetchedAtIso": "2026-06-25T04:10:15.113Z",
        "Bbox": [
            138.4,
            -35.2,
            138.8,
            -34.7
        ],
        "Incidents": [
            {
                "Key": "inc-adl-01",
                "Priority": 4,
                "Headline": "Corner Road — roadworks",
                "Detail": "At Waterloo Corner Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 136,
                "Latitude": -34.76367,
                "Longitude": 138.59294,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.76367,
                        "Lng": 138.59294
                    },
                    {
                        "Lat": -34.76271,
                        "Lng": 138.59261
                    },
                    {
                        "Lat": -34.76271,
                        "Lng": 138.59261
                    },
                    {
                        "Lat": -34.76249,
                        "Lng": 138.59254
                    }
                ]
            },
            {
                "Key": "inc-adl-02",
                "Priority": 4,
                "Headline": "Ashwin Parade — road closure",
                "Detail": "At Ashwin Parade - Closed",
                "FunctionalClass": 1,
                "LengthMeters": 134,
                "Latitude": -34.9154,
                "Longitude": 138.56624,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.9154,
                        "Lng": 138.56624
                    },
                    {
                        "Lat": -34.91513,
                        "Lng": 138.56606
                    },
                    {
                        "Lat": -34.91513,
                        "Lng": 138.56606
                    },
                    {
                        "Lat": -34.91485,
                        "Lng": 138.56587
                    },
                    {
                        "Lat": -34.91485,
                        "Lng": 138.56587
                    },
                    {
                        "Lat": -34.91472,
                        "Lng": 138.56579
                    },
                    {
                        "Lat": -34.91472,
                        "Lng": 138.56579
                    },
                    {
                        "Lat": -34.91463,
                        "Lng": 138.56572
                    },
                    {
                        "Lat": -34.91463,
                        "Lng": 138.56572
                    },
                    {
                        "Lat": -34.91432,
                        "Lng": 138.5656
                    }
                ]
            },
            {
                "Key": "inc-adl-03",
                "Priority": 4,
                "Headline": "Martins Road — roadworks",
                "Detail": "At Martins Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 129,
                "Latitude": -34.79072,
                "Longitude": 138.59662,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.79072,
                        "Lng": 138.59662
                    },
                    {
                        "Lat": -34.79188,
                        "Lng": 138.59674
                    }
                ]
            },
            {
                "Key": "inc-adl-04",
                "Priority": 4,
                "Headline": "Marian Road — roadworks",
                "Detail": "At Marian Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 87,
                "Latitude": -34.89876,
                "Longitude": 138.63939,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.89876,
                        "Lng": 138.63939
                    },
                    {
                        "Lat": -34.89954,
                        "Lng": 138.63945
                    }
                ]
            },
            {
                "Key": "inc-adl-05",
                "Priority": 4,
                "Headline": "Ryans Road — roadworks",
                "Detail": "At Ryans Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 60,
                "Latitude": -34.78067,
                "Longitude": 138.59563,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.78067,
                        "Lng": 138.59563
                    },
                    {
                        "Lat": -34.78121,
                        "Lng": 138.59569
                    }
                ]
            },
            {
                "Key": "inc-adl-06",
                "Priority": 4,
                "Headline": "Barwell Avenue — roadworks",
                "Detail": "At Barwell Avenue - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 50,
                "Latitude": -34.94809,
                "Longitude": 138.57149,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.94809,
                        "Lng": 138.57149
                    },
                    {
                        "Lat": -34.94764,
                        "Lng": 138.57146
                    }
                ]
            },
            {
                "Key": "inc-adl-07",
                "Priority": 4,
                "Headline": "Tennyson Street — roadworks",
                "Detail": "At Tennyson Street - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 46,
                "Latitude": -34.94928,
                "Longitude": 138.57155,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.94928,
                        "Lng": 138.57155
                    },
                    {
                        "Lat": -34.94887,
                        "Lng": 138.57154
                    }
                ]
            },
            {
                "Key": "inc-adl-08",
                "Priority": 4,
                "Headline": "Bolivar Road — roadworks",
                "Detail": "At Bolivar Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 19,
                "Latitude": -34.76366,
                "Longitude": 138.59315,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.76366,
                        "Lng": 138.59315
                    },
                    {
                        "Lat": -34.76382,
                        "Lng": 138.59321
                    }
                ]
            },
            {
                "Key": "inc-adl-09",
                "Priority": 4,
                "Headline": "Flaxmill Road — roadworks",
                "Detail": "At Flaxmill Road - Road construction",
                "FunctionalClass": 2,
                "LengthMeters": 308,
                "Latitude": -35.12163,
                "Longitude": 138.50782,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -35.12163,
                        "Lng": 138.50782
                    },
                    {
                        "Lat": -35.12427,
                        "Lng": 138.50679
                    }
                ]
            },
            {
                "Key": "inc-adl-10",
                "Priority": 4,
                "Headline": "At Southern Expressway South",
                "Detail": "At Southern Expressway South - Road construction",
                "FunctionalClass": 2,
                "LengthMeters": 135,
                "Latitude": -35.1671,
                "Longitude": 138.50691,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -35.1671,
                        "Lng": 138.50691
                    },
                    {
                        "Lat": -35.16623,
                        "Lng": 138.50796
                    }
                ]
            }
        ],
        "Corridors": [
            {
                "RoadName": "Corner Road",
                "Severity": 1,
                "ImpactScore": 201.3,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 1,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -34.76367,
                "Longitude": 138.59294,
                "IncidentKeys": [
                    "inc-adl-01"
                ]
            },
            {
                "RoadName": "Ashwin Parade",
                "Severity": 1,
                "ImpactScore": 201.3,
                "PrimaryType": "roadClosure",
                "Description": "road closure",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 2,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 3,
                "DelayMinutes": 2,
                "Latitude": -34.9154,
                "Longitude": 138.56624,
                "IncidentKeys": [
                    "inc-adl-02"
                ]
            },
            {
                "RoadName": "Martins Road",
                "Severity": 1,
                "ImpactScore": 201.1,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 3,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -34.79072,
                "Longitude": 138.59662,
                "IncidentKeys": [
                    "inc-adl-03"
                ]
            },
            {
                "RoadName": "Marian Road",
                "Severity": 1,
                "ImpactScore": 199.4,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 4,
                "IsSelected": true,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -34.89876,
                "Longitude": 138.63939,
                "IncidentKeys": [
                    "inc-adl-04"
                ]
            },
            {
                "RoadName": "Ryans Road",
                "Severity": 1,
                "ImpactScore": 197.8,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 5,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": 0,
                "DelayMinutes": null,
                "Latitude": -34.78067,
                "Longitude": 138.59563,
                "IncidentKeys": [
                    "inc-adl-05"
                ]
            },
            {
                "RoadName": "Barwell Avenue",
                "Severity": 1,
                "ImpactScore": 197,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0.1,
                "Rank": 6,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -34.94809,
                "Longitude": 138.57149,
                "IncidentKeys": [
                    "inc-adl-06"
                ]
            },
            {
                "RoadName": "Tennyson Street",
                "Severity": 1,
                "ImpactScore": 196.6,
                "PrimaryType": "construction",
                "Description": "roadworks",
                "IncidentCount": 1,
                "TotalLengthKm": 0,
                "Rank": 7,
                "IsSelected": false,
                "FunctionalClass": 1,
                "Direction": null,
                "Suburb": null,
                "NormalDriveTimeMinutes": null,
                "DelayMinutes": null,
                "Latitude": -34.94928,
                "Longitude": 138.57155,
                "IncidentKeys": [
                    "inc-adl-07"
                ]
            }
        ],
        "SelectedIncidents": [
            {
                "Key": "inc-adl-01",
                "Priority": 4,
                "Headline": "Corner Road — roadworks",
                "Detail": "At Waterloo Corner Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 136,
                "Latitude": -34.76367,
                "Longitude": 138.59294,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.76367,
                        "Lng": 138.59294
                    },
                    {
                        "Lat": -34.76271,
                        "Lng": 138.59261
                    },
                    {
                        "Lat": -34.76271,
                        "Lng": 138.59261
                    },
                    {
                        "Lat": -34.76249,
                        "Lng": 138.59254
                    }
                ]
            },
            {
                "Key": "inc-adl-02",
                "Priority": 4,
                "Headline": "Ashwin Parade — road closure",
                "Detail": "At Ashwin Parade - Closed",
                "FunctionalClass": 1,
                "LengthMeters": 134,
                "Latitude": -34.9154,
                "Longitude": 138.56624,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.9154,
                        "Lng": 138.56624
                    },
                    {
                        "Lat": -34.91513,
                        "Lng": 138.56606
                    },
                    {
                        "Lat": -34.91513,
                        "Lng": 138.56606
                    },
                    {
                        "Lat": -34.91485,
                        "Lng": 138.56587
                    },
                    {
                        "Lat": -34.91485,
                        "Lng": 138.56587
                    },
                    {
                        "Lat": -34.91472,
                        "Lng": 138.56579
                    },
                    {
                        "Lat": -34.91472,
                        "Lng": 138.56579
                    },
                    {
                        "Lat": -34.91463,
                        "Lng": 138.56572
                    },
                    {
                        "Lat": -34.91463,
                        "Lng": 138.56572
                    },
                    {
                        "Lat": -34.91432,
                        "Lng": 138.5656
                    }
                ]
            },
            {
                "Key": "inc-adl-03",
                "Priority": 4,
                "Headline": "Martins Road — roadworks",
                "Detail": "At Martins Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 129,
                "Latitude": -34.79072,
                "Longitude": 138.59662,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.79072,
                        "Lng": 138.59662
                    },
                    {
                        "Lat": -34.79188,
                        "Lng": 138.59674
                    }
                ]
            },
            {
                "Key": "inc-adl-04",
                "Priority": 4,
                "Headline": "Marian Road — roadworks",
                "Detail": "At Marian Road - Road construction",
                "FunctionalClass": 1,
                "LengthMeters": 87,
                "Latitude": -34.89876,
                "Longitude": 138.63939,
                "IsNew": false,
                "Shape": [
                    {
                        "Lat": -34.89876,
                        "Lng": 138.63939
                    },
                    {
                        "Lat": -34.89954,
                        "Lng": 138.63945
                    }
                ]
            }
        ],
        "TrafficSentences": [
            {
                "Text": "Roadworks on Corner Road have lanes closed.",
                "CorridorKeys": [
                    "Corner Road"
                ]
            },
            {
                "Text": "Ashwin Parade is closed.",
                "CorridorKeys": [
                    "Ashwin Parade"
                ]
            },
            {
                "Text": "Expect delays of around 2 minutes.",
                "CorridorKeys": [
                    "Ashwin Parade"
                ]
            },
            {
                "Text": "Roadworks on Martins Road have lanes closed.",
                "CorridorKeys": [
                    "Martins Road"
                ]
            },
            {
                "Text": "Roadworks on Marian Road have lanes closed.",
                "CorridorKeys": [
                    "Marian Road"
                ]
            }
        ],
        "ContradictionPlanted": false
    }
}
