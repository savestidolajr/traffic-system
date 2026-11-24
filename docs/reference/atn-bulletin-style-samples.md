---
title: "ATN Bulletin Style Samples (ARN-provided)"
description: "Transcripts of real ATN traffic bulletins: 12 metro reads from the PoC (Traffic_Examples.zip, 28 Jan 2026), 9 regional/rural reads (25 Jun 2026), 62 written network-roundup reads (TRAFFIC DRIBBLE.docx, 30 Jun 2026), and 97 ads-removed Sydney/Melbourne airchecks (3 Jul 2026), with style analysis and a gap assessment against our generated scripts"
tags:
  - "style"
  - "script-generation"
  - "atn"
  - "samples"
aliases:
  - "atn samples"
  - "bulletin transcripts"
  - "style reference"
  - "traffic examples"
purpose: "reference"
status: "current"
last_verified: 2026-07-03
review_after: 2026-09-30
volatility: "low"
source_of_truth: "Traffic_Examples.zip (ARN, 28 Jan 2026) — 12 metro MP3s under ATN_Examples/; 9 regional/rural MP3s (ARN, 25 Jun 2026): Gympie, Ipswich, Darwin, Nowra, Murray Bridge, Ballarat, Wollongong, Sunshine Coast, Townsville; TRAFFIC DRIBBLE.docx (ARN, 30 Jun 2026) — 62 written network-roundup reads across the 7 metro markets; KIIS 1011 [MELB-Ads removed].zip + Gold 1017 [SYD-Ads removed].zip (ARN, 3 Jul 2026) — 97 ads-removed full-day station airchecks with tracked manifest/transcripts under docs/reference/atn-audio-corpus/"
verified_by:
  - "whisper-base transcription, 2026-06-10 (metro)"
  - "whisper small.en + medium.en transcription + AU road/place hand-correction, 2026-06-25 (regional)"
  - "TRAFFIC DRIBBLE.docx ingest + market classification (sign-off + place gazetteer), 2026-06-30 (network roundup)"
  - "zip inventory + SHA-256 manifest + faster-whisper small.en transcription, 2026-07-03 (ads-removed expanded corpus)"
common_queries:
  - "what do real ARN bulletins sound like"
  - "atn style reference"
  - "how should generated scripts sound"
  - "are our scripts on-style"
---

# ATN Bulletin Style Samples

ARN supplied `Traffic_Examples.zip` (28 Jan 2026) during the PoC: 12 real
on-air bulletins from the Australian Traffic Network (ATN), two per
station across six station/market combinations. These are the first
ground-truth examples for what "sounds like ARN traffic" means.
The 21 metro and regional transcript sections below are the source of the
runtime ATN exemplar bank (`Workflow/Prompts/Exemplars`), but the bank holds
BODY COPY ONLY: sponsor and hotline labels, station idents, presenter openers
and sign-offs, branded weather prefixes and driver-advice lines are removed on
the way in, because the composer writes the body and the station's own bookends
are added around it. Keep the sections here verbatim — this file is the
provenance — and re-derive the bank when correcting or extending it.

A second batch of **9 regional and rural reads** was supplied on
25 Jun 2026 (Gympie, Ipswich, Darwin, Nowra, Murray Bridge, Ballarat,
Wollongong, Sunshine Coast, Townsville). These are ARN's own regional
network stations (ex-Grant Broadcasters) currently carrying ATN's
"Time Saver Traffic" reads — i.e. the exact markets Arn is built to
replace. See **Regional & rural corpus** below.

A third batch, `TRAFFIC DRIBBLE.docx` (30 Jun 2026), adds 62 written
network-roundup reads across the seven metro markets. A fourth batch,
supplied on 3 Jul 2026, adds **97 ads-removed station airchecks** for
KIIS 101.1 Melbourne and Gold/WSFM 101.7 Sydney; see **Expanded
ads-removed audio corpus** below.

**Transcription method:** local Whisper (base model), primed with
Australian place names. Obvious mishears of road names are corrected
in square-bracket-free form below (e.g. "Princess Freeway" → Princes
Freeway, "Lankov Road" → Lane Cove Road, "Bulty" → Bolte); embedded
sponsor reads are kept but *italicised* since they are inserted by ATN,
not part of the traffic copy. Original audio lives in the zip — re-listen
before trusting any disputed wording.

> **Not to be confused with voice demos.** Renders of *our own* generated
> bulletins voiced with candidate ElevenLabs voices are **not** ATN ground
> truth — they live in `docs/reference/arn-voice-candidate-demos.md`, kept
> separate so this ground-truth corpus stays uncontaminated.

## Metro corpus at a glance

| File | Station | Market | Aired | Length | Words | Pace |
|---|---|---|---|---|---|---|
| `gold1043_0700` | Gold 104.3 | Melbourne | 7:00 AM | 47s | ~146 | 3.1 w/s |
| `gold1043_0939` | Gold 104.3 | Melbourne | 9:39 AM | 47s | ~142 | 3.0 w/s |
| `kiis1011_0829` | KIIS 101.1 | Melbourne | 8:29 AM | 32s | ~113 | 3.5 w/s |
| `kiis1011_1158` | KIIS 101.1 | Melbourne | 11:58 AM | 35s | ~110 | 3.1 w/s |
| `96fm_0759` | 96FM | Perth | 7:59 AM | 37s | ~123 | 3.3 w/s |
| `96fm_0814` | 96FM | Perth | 8:14 AM | 37s | ~99 | 2.7 w/s |
| `edge961_0750` | CADA (Edge 96.1) | Sydney | 7:50 AM | 39s | ~59 | 1.5 w/s* |
| `edge961_0822` | CADA (Edge 96.1) | Sydney | 8:22 AM | 39s | ~117 | 3.0 w/s |
| `ws1017_0814` | WSFM 101.7 | Sydney | 8:14 AM | 47s | ~146 | 3.1 w/s |
| `ws1017_1058` | WSFM 101.7 | Sydney | 10:58 AM | 47s | ~129 | 2.7 w/s |
| `kiis1065_0739` | KIIS 106.5 | Sydney | 7:39 AM | 32s | ~112 | 3.5 w/s |
| `kiis1065_0749` | KIIS 106.5 | Sydney | 7:49 AM | 34s | ~112 | 3.3 w/s |

\* short read with long music bed; word count excludes bed.

Net traffic copy (excluding the embedded sponsor read of ~8–12s) runs
**roughly 25–35 seconds** — consistent with our 30-second target.

## Transcripts

### Gold 104.3 Melbourne — 7:00 AM

> *[Sponsor read: SUV hybrid drive-away offer, ~8s]*
> The West Gate Bridge chockers citybound with a crash with two centre
> lanes out of action and delays back out onto the Princes Freeway.
> There's also a breakdown in the West Gate Tunnel citybound before the
> Dynon Road exit. And a crash on the Deer Park Bypass citybound after
> Christies Road has traffic slow going back to Leakes Road — make sure
> you stick to Ballarat Road instead. And a power outage has lights out
> through Ringwood along the Ringwood Bypass, Maroondah Highway and
> Ringwood Street. Schools are back for 2026 and so are reduced speed
> limits — a reminder to slow down in school zones.

### Gold 104.3 Melbourne — 9:39 AM

> *[Sponsor read: Harvey Norman sale, ~7s]*
> Absolutely chockers on the West Gate citybound from the Princes
> Freeway over the bridge into the Burnley Tunnel — it's desperately
> trying to recover following peak and an earlier crash on the bridge.
> Delays also pushing onto the Bolte Bridge just for good measure. As
> well, it's patchy for the Monash citybound between EastLink and
> Warrigal Road, the usual hotspot. Works on the Tulla outbound at
> Mickleham Road, speed and lane restrictions set up — no dramas
> citybound till you get to the Bolte.
> *[Sponsor read: Momentum Energy, ~8s]*

### KIIS 101.1 Melbourne — 8:29 AM

> G'day there, it's Andy looking over Melbourne, and it is awful on the
> West Gate citybound — delays from the Princes Freeway to the Burnley
> Tunnel following an earlier crash. The Tulla hosting debris citybound
> after Racecourse Road, lanes closed, speeds down, delays back to the
> Bulla Road exit — take Mount Alexander Road.
> *[Sponsor read: Mable aged care, ~8s]*
> The KIIS-cast: a bit of morning smoke, partly cloudy afternoon, a top
> of 24 degrees on the menu. That's KIIS traffic and weather together.
> I'm with the Kyle and Jackie O Show.

### KIIS 101.1 Melbourne — 11:58 AM

> Works affecting your run outbound on the Tullamarine Freeway at
> Mickleham Road — speed and lane restrictions are in place with some
> minor delays. Also delays citybound on the West Gate Freeway: you'll
> find it slow from the West Gate Bridge through to the Burnley Tunnel.
> To give you an idea how long it'll take you to drive from Forsyth Road
> on the Princes to the Burnley — 22 minutes.
> *[Sponsor read: report online, police.vic.gov.au, ~8s]*
> The KIIS-cast: partly cloudy today with a top of 24. That's KIIS
> traffic and weather together.

### 96FM Perth — 7:59 AM

> The freeway is looking a bit more full now: so, Freeway southbound at
> Burns Beach Road, again Whitfords Avenue through to Reid Highway and
> Hutton Street through to the Powis. Kwinana Freeway northbound Thomas
> Road through to Rowley Road, then busy Farrington Road through to
> Cranford Avenue as well. Tonkin Highway slow southbound at Reid
> Highway and in both directions at Welshpool Road East. Also Roe
> Highway slow southbound at the Great Eastern Highway Bypass at Tonkin
> Highway, and northbound at Nicholson Road too.
> *[Sponsor read: King Living sale, ~9s]*
> It's Christie with the latest Perth traffic.

### 96FM Perth — 8:14 AM

> Mitchell Freeway southbound slow Burns Beach Road, Whitfords Avenue
> to Reid Highway, and Hutton Street to the Powis. Kwinana Freeway
> northbound Thomas Road to Rowley Road, Farrington Road to Cranford
> Avenue as well. And Roe Highway slow southbound at the Great Eastern
> Highway Bypass at Tonkin Highway, and northbound at Nicholson Road.
> *[Sponsor read: Petbarn members sale, ~9s]*
> *[Traffic hotline number]* It's Christie with the latest Perth
> traffic.

### CADA (Edge 96.1) Sydney — 7:50 AM

> Mays Hill: M4 medical emergency eastbound in the breakdown lane at
> Coleman Street. Traffic is fairly busy for the Ryde Bridge from Rhodes
> to Top Ryde citybound, slowing up to North Ryde, Lane Cove Road or
> Quarry Road. Please stay alert and follow the speed limit.
> *[Sponsor read: AAMI insurance, ~5s]*
> I'm Monique Bavino, that's the latest traffic on CADA.

### CADA (Edge 96.1) Sydney — 8:22 AM

> It's very busy for the Ryde Bridge from Rhodes to Top Ryde citybound,
> following a breakdown that's cleared off North Ryde at Lane Cove Road.
> The M2 chewing up citybound North Ryde to the Lane Cove Tunnel. Very
> heavy M4 motorway citybound at Eastern Creek — stacks of traffic at
> the M7/M2 merger, Bella Vista. We've got slow traffic as well at
> Oatlands, Pennant Hills Road, coming towards the school zones. Busy
> for the M5 citybound through Moorebank into Revesby.
> *[Sponsor read: NSW SharkSmart app, ~10s]*
> I'm Monique Bavino, that's the latest traffic on CADA.

### WSFM 101.7 Sydney — 8:14 AM

> *[Sponsor read: Aldi, ~7s]*
> At Ryde, a crash at Cressy Road and down on Lane Cove Road it's
> northbound in the right-hand lane approaching Hancock Street; also
> slow north- and southbound over the Ryde Bridge at the moment. On the
> M4 motorway at Mays Hill there's a medical emergency, cars in the
> breakdown lane citybound just before Coleman Street — emergency crews
> are on site and traffic is slow past the site as well. Built up now
> from Frenchs Forest as far as Warringah Road heading south from the
> underpass to Starkey Street, and slowing down eastbound into the Lane
> Cove Tunnel on the M2.
> *[Sponsor read: GIO insurance, ~8s]*

### WSFM 101.7 Sydney — 10:58 AM

> In Macquarie Park a car has broken down on Lane Cove Road northbound
> approaching Waterloo Road — the left lane is blocked off. At
> Haberfield a truck breakdown eastbound on City West Link approaching
> James Street. Burwood: an over-height truck stuck on Burwood Road at
> the railway bridge, both ways affected. And Centenary Drive under
> pace in Strathfield approaching Arthur Street. Drummoyne: slight
> congestion on Victoria Road coming up to Lyons Road, and General
> Holmes Drive near the airport approaching Foreshore Road also
> struggling. The M4 eastbound from Mulgoa Road to Church Street
> Parramatta, a 20-minute run.
> *[Sponsor read: Panadol, ~8s]*

### KIIS 106.5 Sydney — 7:39 AM

> It's Brittany Gora looking over Sydney, with the M7 building eastbound
> from Richmond Road to the M2 at Windsor Road, taking just under 20
> minutes. A crash in Cecil Hills, Cowpasture Road northbound
> approaching Elizabeth Drive. A breakdown in Rose Hill at the motorway,
> James Ruse Drive northbound past Grand Avenue.
> *[Sponsor read: TermPlus, ~10s]*
> KIIS-cast: sunshine, 29 in the city, 35 in the west. That's KIIS
> traffic and weather together. This is the Kyle and Jackie O Show.

### KIIS 106.5 Sydney — 7:49 AM

> It's Brittany Gora looking over Sydney, and we're seeing a 36-minute
> run westbound on the M4, Woodville Road to Church Street, caught up in
> this medical emergency with a reduced 40k speed — it's in the
> breakdown lane citybound approaching Coleman Street, Mays Hill. A
> crawl through the Rozelle Interchange on citybound approaches to the
> Anzac Bridge.
> *[Sponsor read: commercial radio advertising, ~10s]*
> KIIS-cast: a warm sunny day, 29 in the city, 35 in the west. That's
> KIIS traffic and weather together. This is the Kyle and Jackie O Show.

## Regional & rural corpus (ARN, 25 Jun 2026)

Nine regional/rural reads from ARN's regional network (ex-Grant
Broadcasters stations now carrying ATN "Time Saver Traffic"). Unlike the
metro set these are mostly **single-read slots dominated by local sponsor
copy**, with sparse, wide-area traffic. They are the ground truth for what
"sounds like ARN traffic" in the non-metro markets the pilot will actually
serve.

**Transcription method:** local Whisper `small.en` + `medium.en`
(2026-06-25), primed with an Australian regional road/place gazetteer,
then hand-corrected against known road and suburb names (e.g.
"Wilcox Street" → Woolcock Street, "Mossville Road" → Moss Vale Road,
"nickel-may" → Nicklin Way, "Cunner Park" → Kunda Park, "Manum Road" →
Mannum Road). Embedded sponsor reads are inserted by ATN, not part of the
traffic copy, so they are summarised as *italic bracketed labels* rather
than transcribed in full. Items still genuinely uncertain after
correction are flagged `(?)` — re-listen to the source MP3 before trusting
disputed wording.

| File | Station | Market | State | Length | Words | Pace |
|---|---|---|---|---|---|---|
| `Traffic Gympie 2506` | Zinc 96.1 | Gympie / Sunshine Coast | QLD | 53s | ~168 | 3.2 w/s |
| `Traffic Ipswich 2506` | River 94.9 | Ipswich | QLD | 51s | ~117 | 2.3 w/s |
| `Traffic Darwin 2506` | Mix 104.9 | Darwin | NT | 46s | ~127 | 2.9 w/s |
| `Traffic Nowra` | Power FM | Nowra / Shoalhaven | NSW | 45s | ~106 | 2.4 w/s |
| `Traffic Murray Bridge` | 5MU | Murray Bridge / Murraylands | SA | 44s | ~111 | 2.5 w/s |
| `Traffic Ballarat` | Power FM | Ballarat | VIC | 42s | ~137 | 3.3 w/s |
| `Traffic Wollongong` | Wave FM 96.5 | Wollongong / Illawarra | NSW | 39s | ~120 | 3.2 w/s |
| `Traffic Sunshine Coast` | 91.1 Hot FM | Sunshine Coast | QLD | 38s | ~123 | 3.3 w/s |
| `Traffic Townsville 2506` | 106.3 | Townsville | QLD | 32s | ~99 | 3.1 w/s |

Word counts include the sponsor read(s). **Net traffic copy runs only
~15–25s**; the rest of each 32–53s slot is one or two local sponsor reads
plus the station/presenter wrapper — the inverse of the metro ratio. The
same ATN reader and network ad-buys recur across stations and states
(Travis reads Gympie *and* the Sunshine Coast; the Bob Jane T-Mart EOFY
ad runs in Darwin *and* Ballarat), confirming these are syndicated ATN
regional reads, not local desks.

### Transcripts

#### Zinc 96.1 Gympie (QLD) — morning

> *[Sponsor read: local community/business spot, ~5s]*
> Zinc 96, Time Saver Traffic.
> *[Sponsor read: Pacific Smiles Dental, school-holiday hours, ~12s]*
> G'day, another check on your roads. Looking good between the Coast and
> Gympie. Got delays down through North Lakes — that's around 10 minutes
> on top of your trip — and getting heavier through Mountain Creek and the
> Sunshine Motorway. Beckmans Road (?). Noosaville State School (?) —
> school drop-offs underway, school-zone speed limits in place. Bit of
> traffic on Channon Street (?) around Louisa Street and around the KFC.
> Also Exhibition Road, building on the south side now around the school.
> *[Sponsor read: Subway $10 value meal, ~10s]*
> It's Travis — another check in 30 on Zinc 96.

#### River 94.9 Ipswich (QLD) — morning drive

> *[Sponsor read: the Industry School / trade pathways, ~8s]*
> River 94.9 traffic — every 15 minutes on your way to work.
> Traffic on the Centenary Highway is a heavy run northbound through
> Sumner, and it looks good for the Western Freeway — no delays through
> Indooroopilly, Fig Tree Pocket. The Ipswich Motorway is busy between the
> Centenary Highway and Oxley Road. East Street is also queuing up a little
> at times at the Brisbane and Limestone Street traffic lights.
> *[Sponsor read: Chemist Warehouse EOFY fragrance sale, ~10s]*
> It's Carole Park (?) — another check in 15 minutes on River 94.9 and the
> iHeart app.

#### Mix 104.9 Darwin (NT) — morning

> *[Sponsor read: used-car dealership, Berrimah, ~6s]* Mix 104.9 real-time
> traffic.
> No major delays across Darwin this morning, with the drive into the city
> looking pretty good — traffic moving well on McMinn Street, Cavanagh
> Street and along the Esplanade. Around the airport, Bagot Road and
> McMillans Road are also running smoothly. Watch for crews packing up
> night works on Berrimah Road at Export Drive, while roadworks continue on
> the Stuart Highway near Bilroy Road (?).
> *[Sponsor read: Bob Jane T-Mart EOFY price-buster sale, ~12s]*
> That's the latest traffic on Mix 104.9.

#### Power FM Nowra (NSW) — morning

> *[Sponsor read: Great Southern Motorcycles, Nowra, ~7s]*
> Power FM Traffic.
> G'day all (?) — you'll have a good run this morning on the Princes
> Highway, north- and southbound, going past Turpentine Road. Also Falls
> Creek, Tourist Bay Road (?) clear to the Princes Highway. Cambewarra
> Mountain. Steady-flowing traffic on Moss Vale Road going eastbound. And
> from the Southern Highlands, no delays — the Motorway going north- and
> southbound.
> *[Sponsor read: St Vincent de Paul Winter Appeal, ~9s]*
> That's the latest traffic — with Stolzy and Izzy for breakfast on Power
> FM.

#### 5MU Murray Bridge (SA) — morning

> *[Sponsor read: tyre business at Sturt Reserve, Murray Bridge, ~6s]*
> A smooth run across the regions this morning, with traffic flowing nicely
> throughout the Murraylands, South Coast and Hills areas — including
> Adelaide Road around Mannum Road and through Mount Barker. Just watch for
> roadworks on the South Eastern Freeway near Callington, and Inman Valley
> Road near Torrens Vale Road (?) at Bald Hills — that's where speed
> restrictions are in place around ongoing roadworks.
> *[Sponsor read: Hungry Jack's 50% off via Uber Eats, ~10s]*
> You're all clear on your local — 5MU.

#### Power FM Ballarat (VIC) — Thursday morning

> *[Sponsor read: Barry James Smash Repairs, Ballarat, ~7s]*
> Having a look around town, there's no delays or collisions — it's a nice
> run for your Thursday morning. Otherwise, have a look at your run into
> Melbourne: it's flying through on the Western Freeway, you get to Ferris
> Road and delays start up through to Hopkins Road. As for the Deer Park
> Bypass, a slow run from Robinsons Road on the Ring Road, where it's banked
> up all the way through to the West Gate.
> *[Sponsor read: Bob Jane T-Mart EOFY price-buster sale, ~12s]*
> That's the latest traffic on Power FM.

#### Wave FM 96.5 Wollongong (NSW) — morning

> *[Sponsor read: Pro-Hire equipment hire, ~5s]*
> Wave 96.5, first with the Illawarra's traffic.
> In Coniston, Spring Hill Road's seeing a bit of congestion in both
> directions just past Masters Road. The Princes Highway's moving well for
> both north- and southbounders — much the same for the M1, with a bit of
> activity at the on- and off-ramps to Five Islands Road. Cataract to
> Heathcote (?) via the Princes Highway northbound — a 22-minute run.
> *[Sponsor read: NRMA Roadside Assistance, ~10s]*
> That's the latest traffic on Wave 96.5 — more fun in the morning with
> Lyndal and Crammy.

#### 91.1 Hot FM Sunshine Coast (QLD) — morning

> *[Sponsor read: New Trend Homes, ~5s]* 91.1 Hot FM, Sunshine Coast
> Traffic.
> G'day — I've got a breakdown on the Bruce Highway northbound through
> Murrumba Downs, not causing any delays though. It is slow southbound
> through North Lakes, but easing — it's a couple of minutes. The Sunshine
> Motorway's also a couple of minutes through Mountain Creek. And a bit of
> traffic through Kunda Park, Maroochydore Road, the Nicklin Way through
> Bokarina, and Caloundra Road, Kawana Way, the Link Road roundabout,
> Meridan Plains.
> *[Sponsor read: income-protection insurance, ~10s]*
> It's Travis — another check in 30. 91.1 Hot FM.

#### 106.3 Townsville (QLD) — morning

> 106.3 Townsville, Time Saver Traffic.
> Well, traffic is settling down on Woolcock Street between Cosgrove and
> Mount Louisa. It's been heavier at times on Dalrymple Road through Mount
> Louisa, around Bamford Lane, and also around Nathan Street at Heatley.
> Steady on Sturt Street around the CBD, and also Ross River Road through
> Cranbrook.
> *[Sponsor read: Black & White Cabs, ~10s]*
> I'm Brad — that's the latest traffic on 106.3.

### Regional style — how it differs from metro

The metro fingerprint below still holds (anchored locations, directional
vocabulary, presenter/station wrapper). What the regional set adds:

1. **Sponsor reads dominate the slot.** Every read is mostly local-business
   ad copy — often two reads bracketing the traffic — with only ~15–25s of
   actual traffic. This is the inverse of the metro ratio (metro = ~25–35s
   traffic + one ~8–10s read). The traffic product is genuinely thin in
   these markets.
2. **"All clear" is the norm and is stated plainly.** "No delays or
   collisions", "a smooth run across the regions", "no major delays across
   Darwin", "running smoothly", "moving well". Regional bulletins report the
   *absence* of problems as the headline — the good-run framing is central,
   not occasional. (Directly validates `MarketSchedule.GoodRunRoads` and the
   all-clear / forecast-fill handling in `generalised-bulletin-spec.md`.)
3. **Wide-area sweep, low incident density.** One or two genuine items (a
   breakdown, roadworks, a school zone) across a whole region, padded with
   "moving well / steady" roll-calls of named roads — not the dense
   incident stack of a Sydney or Melbourne read.
4. **School-zone and roadworks reminders are prominent** (school holidays,
   drop-offs, speed limits, "speed restrictions in place around ongoing
   roadworks") — they fill an otherwise quiet bulletin.
5. **Drive times still appear but are rare** — Wollongong's "22-minute run",
   Gympie's "10 minutes on top of your trip" — usually one per bulletin,
   where the metro reads carry one or two as a matter of course.
6. **Direction language persists even on clear roads** ("north- and
   southbound", "eastbound") — the convention survives regardless of
   severity.
7. **Landmark/suburb anchoring over cross-streets** — "around the KFC",
   "Sturt Reserve", "near the airport", "between Cosgrove and Mount Louisa"
   — reflecting sparser named-junction data outside the metros.
8. **Network syndication is visible.** The "Time Saver Traffic" branding,
   the shared reader (Travis across QLD), and identical national ad-buys
   across unrelated markets show ATN runs these as a centralised regional
   feed — the same operating model Arn replaces per market.
9. **Cadence slows where copy is sparse** — Ipswich (2.3 w/s) and Nowra
   (2.4 w/s) fill with reassurance and connective tissue rather than
   information density.

**Implication for our pipeline:** the regional corpus confirms the
generalised, good-run-aware bulletin is the right target, not an
incident-only one. For these markets the highest-value content is often
"everything's clear, here's your run", drive-time headlines where data
exists, and school-zone/roadworks reminders — exactly the multi-source
shape in `generalised-bulletin-spec.md`. The thin-traffic reality also
reinforces the slot-fill / forecast-fill work (a 30s slot with 12s of real
traffic must still be filled honestly).

## "Traffic Dribble" network-roundup corpus (ARN, 30 Jun 2026)

A third corpus — ARN-supplied `TRAFFIC DRIBBLE.docx` (30 Jun 2026) — adds
**62 written traffic reads** spanning all seven metro markets (Sydney,
Melbourne, Brisbane, Gold Coast, Adelaide, Perth, Canberra). Unlike the two
sets above these are **pure scripted traffic copy** — the writer's own
words, with **no embedded sponsor reads** and no Whisper mishears to correct.
That makes this the cleanest apples-to-apples ground truth we have against
our **copy-only** generated output, and the most useful corpus for prompt
tuning.

> **The single most important takeaway is that this corpus contradicts the
> drive-time-headline instinct.** The metro PoC set leaned on drive times as
> the headline metric; this far larger set **barely uses minute counts at all**
> (see fingerprint below). It reframes "magnitude" as *spatial extent*, not
> *minutes* — a direct steer for the pipeline.

### At a glance

| Market | Reads | Words (min–max / mean) | Items/read |
|---|---|---|---|
| Sydney | 13 | 51–144 / 82 | 5.2 |
| Melbourne | 10 | 56–150 / 78 | 5.9 |
| Brisbane | 13 | 59–96 / 75 | 5.7 |
| Gold Coast | 8 | 63–107 / 84 | 5.9 |
| Adelaide | 6 | 49–99 / 69 | 5.2 |
| Perth | 7 | 52–83 / 69 | 5.4 |
| Canberra | 5 | 47–74 / 59 | 4.6 |

Net copy runs ~47–150 words (mean ~76) across **4–11 anchored items** per
read (mean ~5.5) — consistent with a tight 30s slot once the station
sign-off/weather tag is added. Markets differ as in the PoC set: Sydney and
Melbourne run longest and most incident-dense; Adelaide/Perth/Canberra are
shorter, more congestion-roll-call than incident-led.

### Style fingerprint (quantified, n=62)

| Feature | Hit rate |
|---|---|
| Directional term (citybound/inbound/outbound/N–S–E–W-bound/both ways) | 94% (58/62) |
| Cross-street/landmark anchor (approaching / near / just before / just past) | 87% (54/62) |
| Spatial-extent magnitude (from A to B / back to / all the way) | 56% (35/62) |
| Lane detail (left/right/centre lane, N of M lanes, breakdown lane) | 48% (30/62) |
| Drive-time minute count | 3% (2/62) |
| Crash / smash / accident | 53% (33/62) |
| Bingle / prang / nose-to-tail / rollover | 11% (7/62) |
| Breakdown / broken down | 48% (30/62) |
| Recovery / delta (earlier… / now cleared / picking up pace) | 8% (5/62) |
| Detour / diversion (being diverted via…) | 3% (2/62) |
| Station / network sign-off ("That's the latest X traffic" / "up to date on…") | 82% (51/62) |
| Weather tag (temp + conditions) | 37% (23/62) |

### The headline finding: extent over minutes

Drive-time minute counts appear in **only 3% of reads (2 of 62), and never as
the lead** — both are a single monitored-route item ("the M4 — Mulgoa Rd to
the Church St off ramp is taking 29 minutes") buried mid-bulletin. Instead,
magnitude is carried two ways:

1. **Spatial extent (56%)** — *where the queue reaches*: "queues all the way
   back to the Rozelle Interchange", "delays back towards Berowra", "from
   Casula to Revesby", "traffic's all the way back to Robina", "from Bilinga
   all the way back to Palm Beach". The named tail of the queue *is* the
   magnitude.
2. **Qualitative severity idiom** — "bumper to bumper", "a sea of brake
   lights", "crawling", "under speed", "stop-start", "on the brakes",
   "chockers", "a mess", "feeling the pinch". The slang carries the severity;
   it is not decoration.

Everything else holds the metro fingerprint, but harder: **directional terms
are near-universal (94%, ~2 per read)** even on clear roads, and
**cross-street/landmark anchoring is on ~87% of items** ("just past the
Wahroonga interchange", "approaching Mowbray Road", "near New Settlement Rd",
"at Pine Street"). Incident vocabulary is rich and varied — crash, smash,
accident (53%), breakdown (48%), plus bingle/prang/nose-to-tail/rollover
(11%) and car fire/truck/semi — and **lane detail is on ~48%** ("two of three
lanes shut", "the left lane is blocked", "centre lane", "in the breakdown
lane"). Reads are **crisp, mostly discrete anchored sentences** with light
connective tissue — not heavily woven — and **82% close on a station/network
sign-off** ("That's the latest Sydney traffic", "you're up to date on KIIS
1065"), 37% with a one-line weather tag. Recovery/delta framing ("picking up
pace after an earlier breakdown", "that's now out but traffic is still slow")
and detour/diversion advice ("being diverted via the Logan Rd exit") are
present but rare (8% / 3%), the latter reserved for full closures.

### Transcripts

Source: ARN-supplied `TRAFFIC DRIBBLE.docx` (30 Jun 2026), preserved
essentially verbatim — obvious source typos (e.g. "ouf"→out, "blocing"→
blocking, "Expresswat"→Expressway) are left as supplied; re-check the source
before trusting disputed wording. Grouped by market via station sign-off,
then an Australian place gazetteer for reads that close on a weather tag
(KIIS 97.3 = Brisbane, not the Gold Coast). Reads within a market are
separated by `---`.

#### Sydney (13 reads)

> IN Pyrmont there's a crash on the Western Distributor citybound just past the Anzac bridge has two of three lanes shut. We have queues all the way back to the Rozelle Interchange with delays also for the City West Link, Victoria Rd, The M4 and Iron Cove Link Tunnels
> At Denham Court - a three car crash on Camden Valley Way northbound at Denham Court Rd
> On the M4 - Mulgoa Rd to the Church St off ramp is taking 29 minutes
> A Bus and car crash at Homebush on Parramatta Rd at Station St - a westbound lane is out of action
> A van's broken down at Artarmon on the Gore Hill Freeway southbound towards Reserve Rd
> Picking up pace on the Hume Motorway southbound after an earlier breakdown at Campbelltown Rd
> Sydney weather - showers and 16 degrees and you're up to date on KIIS 1065
>
> ---

> We've got a two car accident on the M1 northbound just past the Wahroonga interchange. The left lane is blocked with delays back towards Berowra
> A truck broken down on Homebush Bay Drive at the M4 on-ramp. Two ouf of the three northbound lanes are blocked. Delays are back to Railway St
> The M5 East tunnel is under speed and we have queues from Kingsgrove to Bexley Rd
> The Warringah Freeway citybound is slow from Miller St and Military Rd is slow through Neutral Bay
> That's the latest Sydney traffic
>
> ---

> The M4 is running nicely from Penirth to Prospect but under speed through to Parramatta and again around Homebush
> There's a smash on the M2 at Macquarie Park citybound near the Lane Cove Rd exit.. delays back to Beecroft Rd
> The M5 is bumper to bumper from Casula to Revesby.
> The Eastern Distributor is very slow northbound from Dacey Avenue all the way to Wolloomollo
> A breakdown southbound on the Pacific Highway at Killara blocking one lane near Powell St
> Sydney's weather - sunny and 22 today and you're up to date on Gold 101.7
>
> ---

> Some emergency roadworks happening on Parramatta Rd at Leichhardt thanks to a burst water main. The left lane citybound is currently blocked near Norton St. with delays back to Canterbury Rd.
> The crash on the Princes Highway at Blakehurst approaching the Tom Ugly's Bridge has now cleared but northbound traffic is still slow.
> The M1 from Mulgoa Rd to Church is taking about 28 minutes at the moment
> That's the latest Sydney traffic
>
> ---

> Reports of a minor bingle in the M5 East Tunnel Citybound... delays back to Kingsgrove at this stage
> The Cumberland Highway is crawling through Merrylands and Smithfield
> Heavy traffic northbound on Pennant Hills Rd approaching the M2
> All clear on the Gore Hill and Warringah Freeways
> That's the latest Sydney traffic
>
> ---

> The Pacific Highway is crawling through Chatswood and Artarmon with heavy traffic in both directions.
> The Gore Hill Freeway is busy heading towards the city with delays building through the tunnel approaches.
> Military Road is well under speed through Neutral Bay and Cremorne.
> Pretty steady at the moment on Mona Vale Rd all the way to St Ives.
> Showers and 14 today... and you're up to date on KIIS 106-5
>
> ---

> Parramatta Rd is crawling at the moment from Burwood all the way to Camperdown
> Heavy traffic on Victoria Rd between Gladesville and Rozelle then it's a slow trip across the Anzac Bridge as well.
> Smash northbound on General Holmes Drive just before the airport tunnel with delays back to Brighton
> Breakdown on Southern Cross Drive southbound just near Link Rd blocking the left lane.
> That's the latest Sydney traffic
>
> ---

> The M7 is moving pretty well at the moment.... just a minor hold up approaching the M4 interchange.
> The M4 itself is under speed westbound from Granville to the Cumberland Highway
> There's a smash on the Great Western Highway at Prospect.... it's westbound near Reservoir Rd
> and the M2 is slow outbound around Baulkham Hills.
> Sunny and 19 today and you're up to date on KIIS 106-5
>
> ---

> We've got a three car crash at Rozelle on Victoria Road citybound approaching the Iron Cove Bridge.
> The right lane's blocked and traffic is backing up through Drummoyne and Five Dock.
> Also slow on the City West Link approaching The Crescent and the Anzac Bridge is pretty much stop-start into Pyrmont.
> The M4 is under speed from Homebush through to Parramatta and we've got a breakdown on Concord Road near Parramatta Road causing minor delays.
> That's the latest Sydney traffic.
>
> ---

> There's a truck breakdown on the M5 East citybound inside the tunnel just before the Bexley Road exit.
> One lane's gone and traffic is crawling from King Georges Road.
> The Princes Highway's also busy through Rockdale and Arncliffe while General Holmes Drive is slow towards the airport.
> Elsewhere..... reports of a crash on Pennant Hills Road southbound at Thornleigh near the M2
> and Military Road is bumper to bumper through Cremorne and Neutral Bay.
> That's the latest Sydney traffic
>
> ---

> We've got a breakdown on the Harbour Bridge southbound in the second lane from the left.
> Traffic's banking up through North Sydney... with the Warringah Freeway slow from Miller Street.
> A crash at Liverpool on the Hume Highway northbound near Cumberland Highway.... one lane's blocked and traffic is backing up through Casula.
> The M5 is also very slow from Moorebank through to Revesby.
> Heavy traffic on Woodville Road through Guildford.... and the Cumberland Highway is under speed through Smithfield.
> That's the latest Sydney traffic
>
> ---

> A smash on the Pacific Highway southbound at Chatswood approaching Mowbray Road... traffic is already queuing through Roseville and Lindfield.
> The Gore Hill Freeway is slow approaching Artarmon... and the Lane Cove Tunnel is heavy heading towards the city.
> Elsewhere, Mona Vale Road is under speed approaching St Ives.
> The M4's slowing from Church Street through to Homebush while the Western Distributor is stop-start approaching the CBD.
>
> ---

> Reports of a car fire on the Princes Highway southbound at Sylvania approaching Tom Uglys Bridge... fire crews are on scene... two lanes are blocked at the moment with delays back to Blakehurst
> Captain Cook Bridge is busy southbound while the M5 East is under speed from Arncliffe through to Kingsgrove.
> Three car  crash on Homebush Bay Drive northbound near the M4.... Two lanes are affected with traffic back past Parramatta Rd
> Southern Cross Drive is also slow through Tempe while the Eastern Distributor is crawling from Gardeners Road all the way through to Woolloomooloo.

#### Melbourne (10 reads)

> Inbound on the West Gate is slow from Williamstown Rd through to the tunnel
> The Monash is a sea of brake lights from Warrigal Rd to Toorak Rd
> The Tullamarine is crawling from Bell St all the way through to Flemington Rd..
> And if you're on the Eastern we've got heavy traffic from Doncaster Rd towards Hoddle St
> That's the latest Melbourne traffic
>
> ---

> There's a breakdown outbound on the CityLink... sitting in the centre lane approaching the Bulla Rd exit.
> Delays on the Monash now stretching back to Springvale Rd
> St Kilda Rd is busy both ways between Commercial Rd and Flinders St
> and give yourself extra time if you're travelling across the Bolte Bridge with traffic very slow over the river
> That's the latest Melbourne traffic
>
> ---

> We've got a minor smash inbound on the Princes Freeway near Millers St... Emergency services are on scene and traffic is already building up behind it
> Things are heavy through Richmond on Hoddle St approaching Swan St .
> Usual school drop off delays on Burke Rd through Camberwell in both directions.
> The Ring Road is moving well for a change... just a little under speed around the Plenty Rd interchange..
> Showers and 14 today... and you're up to date on KIIS 101-1
>
> ---

> The West Gate Bridge is busy inbound but still moving.. while delays are building on the Kings Way corridor.
> There's a breakdown on the Eastern Freeway citybound approaching Chandler Highway, blocking the left hand lane.
> Roadworks continue on Footscray Rd near Docklands....
> The Nepean Highway is slow between Moorabbin and Elsternwick
> The Calder is slow from Keilor Park towards the city while Sydney Rd is bumper to bumper through Brunswick
> That's the latest Melbourne traffic
>
> ---

> The Monash outbound is slow from the CityLink interchange through to Malvern.
> The Tullamarine is heavy heading towards the airport with delays from the Calder interchange
> really slow on Dandenong Rd approaching Chadstone
> and the West Gate is under speed but still moving
> Cloudy and 16 today... and you're up to date on KIIS 101-1
>
> ---

> Traffic is crawling southbound on the CityLink approaching the Burnely Tunnel after a multi-car crash.... Delays are back towards Flemington Rd.
> The Eastern Freeway is also feeling the pinch at the moment with slow traffic through Doncaster and Kew, especially around Hoddle St
> The Yarra crossings are moving ok and the Tullamarine is all clear all the way to the airport.
> That's the latest Melbourne traffic
>
> ---

> We've got a three car smash inbound on the Monash Freeway at Warrigal Road.... that's blocing the right lane... traffic already banking up through Wheelers Hill with delays stretching back towards Springvale Road.
> The Princes Highway is also slow through Oakleigh.
> Heavy traffic approaching the Burnley Tunnel...and Hoddle Street is crawling towards Swan Street.
> Reports of a crash on the Eastern Freeway citybound near Bulleen Road.. traffic back through Doncaster....
> The West Gate Bridge is under speed and we're also seeing delays on Geelong Road approaching the freeway.
> That's the latest Melbourne traffic
> +++ +
> Car fire on the CityLink outbound approaching the Bolte Bridge... Traffic is stop-start from Footscray Rd
> The Monash is also under speed through Malvern and the Tullamarine is slowing near Flemington Road.
> The West Gate is under speed heading west
> Sydney Road still slow through Brunswick and Bell Street is busy approaching Pascoe Vale Road.
>
> ---

> There's a two car accident on Dandenong Road inbound near Chadstone Shopping Centre.
> One lane's blocked and traffic is backing up towards Warrigal Road.
> The Monash remains heavy through Glen Iris.
> Burke Road's crawling through Camberwell while Toorak Road is slow approaching the city.
> Partly cloudy and 17 today.. and you're up to date on Gold 104.3
>
> ---

> We've got a breakdown northbound on the Tullamarine Freeway approaching English Street... traffic is slowing right back through Essendon Fields.
> The Calder Freeway is also busy approaching the city.
> Heavy traffic around Bell Street... and the Ring Road is under speed between Plenty Road and Greensborough Highway.
> The Frankston remains heavy through Seaford.
> Meanwhile CityLink is slow approaching the Burnley Tunnel and traffic's building on Kings Way heading into the city
> That's the latest Melbourne traffic
>
> ---

> Reports of a truck rollover on the Princes Freeway outbound near Werribee.
> Traffic is being directed around the scene with delays already back through Point Cook.
> The West Gate Freeway is feeling the pinch as well.
> Geelong Road is heavy through Brooklyn and the Monash is still busy from South Yarra through to Malvern.
> Roadworks on Footscray Rd in Docklands has two lanes closed... traffic is crawling from Dynon Rd.
> And there's a breakdown in the CBD on Spencer St near La trobe St....
> That's the latest Melbourne traffic

#### Brisbane (13 reads)

> Gateway Motorway at Eight Miles Plains - there's a crash with a semi involved.. one of the vehicles has rolled. All the northbound lanes are blocked with traffic being diverted via the Logan Rd exit.
> Car broken down on the Bruce Highway northbound near New Settlement Rd.
> Heavy traffic southbound through North Lakes as you're heading towards Anzac Avenue to the Bruce Highway
> The M1 southbound at Beenleigh there's a smash in the right hand lane just before Main St
> A shower or two today top of 21 and you're up to date on KIIS 97-3
>
> ---

> We've got a crash at Castelford... It's on Linkfield Rd eastbound just before Gympie Rd.
> Two car accident at Belmont on Belmont Rd near Marwood St
> really heavy on the Centenary Highway northbound from the Ipswich Motorway to the Jinderlee Bridge
> And the Bruce Highway's under speed through North Lakes.
> Sunny and 23 today and you're up to date on KIIS 97-3.
>
> ---

> There's a car broken down on M1 at Slacks Creek... it's on the left shoulder just before you hit Paradise Rd
> A crash at Waterford West... Kingston Rd near Evergreen Avenue.
> We had an earlier car fire on Kingsford Smith Drive at Eagle Farm... that's now out but traffic is still slow.
> Breakdown outbound in the Clem 7 at Wollongabba
> And really busy inbound on the Riverside Expresswat
> Cloudy and 22 today... and you're up to date on KIIS 97-3
>
> ---

> Traffic already building on the Pacific Motorway inbound from Springwood through to Eight Mile Plains.
> The Gateway Motorway is fairly steady northbound around the airport precinct, while Gympie Road's beginning to fill up through Chermside.
> If you're heading in from Ipswich, expect delays on the Centenary Motorway approaching Jindalee.
> Ipswich Road is also slowing through Moorooka
> That's the latest Brisbane traffic
>
> ---

> Logan Road is carrying plenty of traffic this morning with delays building around Garden City.
> Old Cleveland Road is busy through Carindale heading towards the city.
> The M1 is stop start from Loganholme to Slacks Creek.
> Breakdown northbound on the Riverside near Anne St
> Gympie Road is slow through Chermside and Kedron heading towards the city.
> Showers and 18 today... and you're up to date on KIIS 97-3
>
> ---

> Still heavy on the Bruce Highway southbound from North Lakes to Mango Hill
> Slow on Sandgate Rd through Clayfield and Albion at the moment
> Got a breakdown reported on the Inner City Bypass westbound approaching Bowen Hills.
> The Story Bridge is busy but moving.
> Coronation Drive is slow inbound from Toowong towards the CBD.
> The William Jolly Bridge approaches are heavy while Roma Street and Turbo Street have the usual bottlenecks.
> The Captain Cook Bridge busy in both directions
> While traffic is steady on the Riverside.
> That's the latest Brisbane Traffic
>
> ---

> The Pacific Motorway outbound is already slow through Woolloongabba and Greenslopes.
> Coronation Drive heading west is building up quickly heading out of the city.
> Heavy northbound across the Story Bridge at the moment while the Riverside Expressway is beginning to fill up in both directions.
> and the Gateway is busy both ways around the airport precinct.
> Sunny and 21 today and you're up to date on KIIS 97-3
>
> ---

> The Captain Cook Bridge is carrying heavy traffic through the CBD corridor
> Gympie Road remains slow through Chermside, Kedron and Lutwyche.
> The Bruce Highway southbound is carrying plenty of vehicles approaching the city.
> Stafford Road is busy through Everton Park and Stafford Heights, while the Webster Road corridor is slowing through several intersections.
> Logan Road is slow around Mount Gravatt and Greenslopes.
> and the M1 patchy southbound between Eight Mile Plains and Loganholme
> That's the latest Brisbane traffic
>
> ---

> We've got a three car crash on the Pacific Motorway inbound at Rochedale near Miles Platting Road.
> The centre lane's blocked and traffic is already backing up through Springwood.
> The Gateway Motorway is heavy northbound approaching the airport precinct.
> Logan Road's also under speed around Garden City while the Riverside Expressway is slow approaching the CBD.
> Sunny and 22 today... and you're up to date on KIIS 97-3
>
> ---

> a truck's broken down on the Gateway Motorway southbound just before the Gateway Bridge... One lane's blocked and traffic is backing up through Bracken Ridge.
> Gympie Road is bumper to bumper through Chermside and Kedron.
> The Bruce Highway is also slow southbound from North Lakes through to Mango Hill.
> The Centenary Motorway'S heavy approaching Jindalee Bridge.
> and the Inner City Bypass is under speed approaching Bowen Hills.
> That's the latest Brisbane traffic
>
> ---

> A crash on Ipswich Road northbound near Moorooka... Emergency services are on the scene... One lane is blocked with traffic queuing back through Rocklea.
> The Ipswich Motorway is stop-start approaching Oxley.
> The M1 is busy as usual from Loganholme to Slacks Creek
> Heavy across the Captain Cook Bridge and we're seeing delays both ways on the Riverside Expressway.
>
> ---

> Reports of a multi-vehicle smash on the Gateway Motorway northbound near Nudgee Road.... Emergency crews are blocking two lanes ... traffic back through the airport precinct,
> We've got a two vehicle crash on Logan Road southbound at Mount Gravatt near Klumpp Road... the left lane's blocked and traffic is slow right through the shopping precinct.
> The Pacific Motorway remains heavy from Eight Mile Plains to Greenslopes.
> Old Cleveland Road is busy through Carindale and the Gateway's slowing near Belmont.
> That's the latest Brisbane Traffic
>
> ---

> A breakdown in the Clem7 outbound near Woolloongabba is causing delays through the tunnel...and we're seeing flow-on delays onto connecting roads at the moment too
> The Pacific Motorway is slow through Greenslopes.
> The Story Bridge is heavy northbound and Kingsford Smith Drive is also under speed near Eagle Farm.
> The Ipswich Motorway remains heavy through Oxley and Richlands.
> Coronation Drive westbound is slow and the William Jolly Bridge approaches are also congested.
> Sunny and 23 tomorrow and you're up to date on KIIS 97-3

#### Gold Coast (8 reads)

> The M1 is crawling southbound from Helensvale to Molendinar after an earlier smash. There are also delays southbound from Bermuda St to Stewart Rd.
> The Gold Coast Highway's under speed through Miami then the usual delays through Surfers
> Smith St westbound is bumper to bumper from Olsen Ave.. looks like we could have a breakdown
> Slow going on Thomas Drive across Chevron Island
> and Brisbane Rd is heavy westbound through Arundel
> We'll check it again in 15 minutes.... Gold Coast weather sunny and 22... right now 18 degrees.
>
> ---

> We've got a three car smash on the M1 northbound at Highland Park.... traffic's all the way back to Robina.
> Delays also northbound through Coomera and if you're heading up to Brissy.. traffic is crawling as usual from Slacks Creek
> There's an accident on Bermuda St in Burleigh --- it's southbound just before Reedy Creek Rd.
> Hope Island Rd is very heavy both ways through the roadworks at Helensvale
> and the Gold Coast Highway is bumper to bumper through Broadbeach
> Gold Coast weather ... showers and 19 today.. right now it's 15 degrees
>
> ---

> Got reports of a smash at Nerang.... eastbound on Southport Nerang Rd at the intersection of Riverwood Drive
> Northbound on the M1 is a little slow through Coomera and Pimpama but elsewhere is all clear so far.
> No dramas at this stage on the Gold Coast Highway, Brisbane Rd or Hope Island Rd
> Just watch out for a breakdown northbound on Bundall Rd at Surfers just before Thomas Drive
> Gold Coast Weather ... sunny and 27 today, right now it's 23 degrees
>
> ---

> No major dramas so far on Gold Coast roads....
> The M1 is starting to build up northbound between Robina and Nerang..
> Southbound is pretty good... just a little under speed around Coomera and Helensvale.
> Smith St starting to get busy approaching Olsen Ave.
> And all clear on the Gold Coast Highway through Surfers and Broadbeach
> Fine and 25 today.. right now 17 degrees
>
> ---

> We've got a breakdown at Southport on Marine Parade... it's southbound right near Broadwater Parklands.... just minor delays at the moment.
> M1 is slow now from Varsity all the way to Nerang northbound.... Southbound is on the brakes from Coomera to Gaven.
> Bermuda St is heavy approaching Burleigh both ways
> Brisbane Rd eastbound is under speed from Arundel to Labrador
> That's the latest Gold Coast traffic
>
> ---

> Bermuda Street slow through Burleigh Waters, and Reedy Creek Road is building around the school zones.
> There's a breakdown on West Burleigh Road near Stockland --- that's eastbound in the left lane.
> M1 is slow southbound from Helensvale to Molendinar then again around Palm Beach.... while northbound is busy around Gaven
> Gold Coast Highway is crawling through Surfers at the moment... that's hurting Thomas Drive as well.
> Delays on Robina Town Centre Drive as well around the shops.
> Cloudy today, tops of 23.. right now 19 degrees
>
> ---

> A truck smash has closed the Tugun Tunnel in both directions on the M1...... southbound traffic is being diverted at Stewart Rd while northbound traffic is being diverted at the Gold Coast Highway.
> The Highway is a mess a result with delays from Bilinga all the way back to Palm Beach
> The rest of the M1 is all clear all the way to Brisbane at the moment.
> Smash on Sunshine Boulevard northbound near Pac Fair.
> and slow going Smith St eastbound through Southport
> That's the latest Gold Coast traffic.
>
> ---

> Bermuda Street is slow through Burleigh Waters..... Reedy Creek Road is busy around Varsity....  and Pappas Way is heavy near the school zones.
> We’ve got a breakdown on West Burleigh Road eastbound near Treetops Plaza.....It’s blocking part of the left lane and causing delays back towards the M1.
> Gold Coast Highway is getting busier through Miami and Mermaid...
> The M1 is still patchy southbound through Nerang and Robina... but northbound is fine at the moment
> Hope Island Road is heavy both ways near Santa Barbara Road
> Brisbane Road is still busy through Arundel and Labrador.
> Cloudy today, tops of 23.. right now 19 degrees

#### Adelaide (6 reads)

> Things are already building on the South Eastern Freeway inbound, with traffic slow towards the Tollgate
> Main North Road is busy through Prospect and Blair Athol, while Port Road is on the brakes heading into the city.
> South Rd is pretty congested at the moment.. especially around Torrens Rd.
> Cross Rd is slow both ways around Unley and Fullarton
> That's the latest Adelaide traffic
>
> ---

> The Northern Expressway is moving pretty well at the moment although things are starting to slow down around the Curtis Rd interchange
> The South Eastern is busy as usual heading into the city
> Greenhill Rd is bumper to bumper as well
> and there's a breakdown on King William St near North Terrace
> Cloudy and 16 today.. and you're up to date on KIIS 102-3
>
> ---

> Main North Rd is crawling around Prospect...
> Really slow on Port Wakefield Rd as well for those heading towards the city.
> Heavy on the Salisbury Highway around Waterloo Corner
> and give yourselves extra time on the Gawler with heavy traffic through the northern corridor.
> That's the latest Adelaide Traffic
>
> ---

> We've got a two car smash on Henley Beach Rd in Glenelg blocking one of the outbound lanes.
> The South Eastern Freeway outbound now much through the Tollgate.
> The CBD exits are busy around West Terrace and South Terrace.. .and Cross Rd is stop start in both directions
> Slow going on Port Rd around Woodville and Hindmarsh....
> That's the latest Adelaide traffic
>
> ---

> We've got a two car crash inbound on the South Eastern Freeway just before the Tollgate... that's blocking the left lane
> Cross Road is also slowing through Fullarton and Unley while Greenhill Road is bumper to bumper approaching the city.
> A truck's broken down on South Road northbound near Richmond Road... Traffic is getting through but delays are back towards Daws Rd.
> Port Road is heavy through Hindmarsh and Woodville... while Main North Road is under speed around Prospect.
> A breakdown on the South Eastern Freeway outbound near Stirling is causing a few delays
> That's the latest Adelaide traffic
>
> ---

> Emergency roadworks this morning on Cross Road near Goodwood Road after a burst water main... one lane is shut and traffic is crawling through there.
> The South Eastern is busy through the hills while South Road remains heavy northbound..
> There's a breakdown in the centre lane on Port Road citybound near South Road.
> and heavy traffic on West Terrace approaching North Terrace
> Partly cloudy and 18 today.. and you're up to date on KIIS 102-3

#### Perth (7 reads)

> Breakdown eastbound on the Graham Farmer Freeway approaching the tunnel.
> The Mitchell Freeway southbound's already slow from the Reid Highway through to Sutton St.
> The Kwinana is heavy outbound through Cockburn and Murdoch
> Pretty steady on the Great Eastern Highway at the moment through Belmont and Rivervale
> That's the latest Perth traffic
>
> ---

> Mitchell Freeway northbound is crawling through Osborne Park and Warwick.
> Stirling Highway is busy through Nedlands and Claremont with plenty of traffic heading towards the coast.
> West Coast Highway has moderate delays through City Beach and Scarborough
> The Mounts Bay Road approach remains slow heading out of the city
> That's the latest Perth traffic
>
> ---

> We've got a two car crash on the Mitchell Freeway southbound approaching Cedric Street.
> The right lane's blocked and traffic is backing up through Warwick and Greenwood.
> Hepburn Avenue is also under speed approaching the freeway ramps.
> The Graham Farmer Freeway remains busy heading into the city and Stirling Highway is slow through Nedlands.
> Sunny and 25 today... and that's the latest Perth traffic.
>
> ---

> A truck has broken down on the Kwinana Freeway northbound near Leach Highway... One lane's blocked and traffic is slowing back through Bull Creek and Murdoch.
> South Street is also very heavy through Kardinya.
> The Causeway approaches are busy while Great Eastern Highway is under speed through Rivervale.
> reports of a crash on Tonkin Highway southbound near Morley Drive.... traffic's already banking up through Bayswater...
> The airport approaches are slow and Great Eastern Highway is heavy through Belmont.
> That's the latest Perth traffic
>
> ---

> We've got a breakdown on Stirling Highway southbound near Claremont Quarter... it's partially blocking the left lane so traffic is having to slow down..
> West Coast Highway is also busy through Scarborough.
> The Mitchell remains heavy citybound approaching Vincent Street while Mounts Bay Road is slow heading into the CBD.
> Lord Street remains busy through the eastern corridor and Roe Highway is under speed near Midland Road
> That's the latest traffic on Gold 96FM.
>
> ---

> A three vehicle nose-to-tail is causing delays on Roe Highway westbound near Berkshire Road.
> One lane's blocked and traffic is backing up through Forrestfield.
> Tonkin Highway is also heavy approaching the airport precinct.
> Leach Highway remains under speed near Welshpool while Orrong Road is busy through Kewdale.
> A breakdown on the Mitchell Freeway northbound near Hutton Street is causing delays through Osborne Park.
> Traffic is slowing from Vincent Street through to Cedric Street.
> That's the latest Perth traffic
>
> ---

> Major delays on Roe Highway after a truck rollover eastbound approaching Great Northern Highway..... two lanes are out of action and traffic is queuing back through Hazelmere.
> Tonkin Highway is also under speed.
> The Great Eastern Highway is still busy through Midland while Reid Highway is slowing at the Alexander Drive interchange.
> a crash on Kwinana Freeway southbound near Russell Road.. traffic is backing up through Success and Cockburn Central.
> That's the latest Perth traffic

#### Canberra (5 reads)

> The Parkway northbound is slowing from Cotter Road through to the Glenloch Interchange
> Northbourne Avenue is getting busier through Dickson and Lyneham, while Adelaide Avenue inbound is starting to fill up
> A minor two-car nose-to-tail reported on Belconnen Way near the Coulter Drive intersection... one lane inbound is blocked at the moment
> Slow but moving on the Tuggeranong Parkway.
>
> ---

> Horse Park Drive is heavy heading towards Federal Highway while Flemington Road is slowing through Harrison and Mitchell
> Minor smash on Well Station Drive... no major delays though
> The Monaro Highway is getting busy approaching Hindmarsh Drive...
> Northbourne Ave is now bumper-to-bumper approaching Barry Drive.
>
> ---

> Northbourne Avenue outbound is filling quickly through Civic and Dickson.
> The Tuggeranong Parkway southbound is building up approaching Cotter Road.
> We've had reports of a minor prang on Adelaide Avenue near Yarra Glen... that's blocking the left lane at the moment
> Belconnen Way still busy after the earlier smash near Coulter Drive
>
> ---

> We've got a two-car crash on Parkes Way eastbound near Clunies Ross Street.
> The left lane's blocked and traffic is backing up through the Glenloch Interchange approaches.
> Northbourne Avenue is also heavy through Dickson and Lyneham while Adelaide Avenue remains busy heading into the city.
> The Tuggeranong Parkway is slow but moving southbound.
> That's the latest Canberra traffic.
>
> ---

> A broken-down bus is causing delays on Northbourne Avenue southbound near the intersection with MacArthur Avenue.
> Traffic is getting through but it's a slow run from Dickson.
> The Federal Highway approaches are also busy.
> Horse Park Drive remains under speed heading towards Gungahlin town centre.
> Emergency roadworks on Adelaide Avenue inbound near Yarra Glen... One lane remains closed and traffic is crawling from Cotter Road.
> You're up to date on Mix 106.3


## Expanded ads-removed audio corpus (ARN, 3 Jul 2026)

ARN supplied two expanded ATN aircheck archives with ads removed:

- `KIIS 1011 [MELB-Ads removed].zip` — KIIS 101.1 Melbourne, 52 MP3s across 26 clock-time groups.
- `Gold 1017 [SYD-Ads removed].zip` — Gold/WSFM 101.7 Sydney, 45 MP3s across 29 clock-time groups.

These are the first broad station-specific **audio** corpora rather than
small spot samples or written copy. They are useful for daypart cadence,
opening/close variety, repeated same-station wording, and the difference
between short tactical updates and longer congestion-led reads.

Tracked corpus artifacts:

- Manifest: `docs/reference/atn-audio-corpus/2026-07-03-ads-removed-manifest.csv`
- Machine transcripts: `docs/reference/atn-audio-corpus/2026-07-03-ads-removed-transcripts.md`

Raw audio is extracted locally under
`docs/reference/atn-audio-corpus/_local-raw/2026-07-03-ads-removed/` and
gitignored: large binary source material stays available on this workstation
without pushing ~47 MB of MP3s into normal repo history.
The manifest carries the local
path, duration, word count, pace, original filename, clock time, variant,
and SHA-256 for every MP3 so any transcript can be traced back to its audio
file.

### Expanded audio corpus at a glance

| Station | Market | Files | Clock groups | Duration range | Word-count range | Mean pace |
|---|---|---:|---:|---|---|---:|
| KIIS 101.1 | Melbourne | 52 | 26 | 19.0–32.0s | 66–117 | 3.68 w/s |
| Gold / WSFM 101.7 | Sydney | 45 | 29 | 19.2–29.1s | 69–104 | 3.33 w/s |

**Transcription method:** `faster-whisper small.en` (2026-07-03),
station/market-prompted, no manual road-name correction pass yet. Treat
these transcripts as a searchable corpus layer, not a final editorial
transcript; re-listen before trusting disputed place names.

## What the samples actually do (style fingerprint)

1. **Anchored locations, always.** Every incident is pinned to a road +
   direction + cross-street/landmark: "northbound in the right-hand
   lane approaching Hancock Street", "citybound just before Coleman
   Street". Never a bare road name.
2. **Lane-level specificity.** "Two centre lanes out of action", "left
   lane blocked off", "in the breakdown lane", "reduced 40k speed".
3. **Drive times as the headline metric — *in the metro PoC set only*.** "Forsyth Road on the Princes
   to the Burnley — 22 minutes", "Mulgoa Road to Church Street, a
   20-minute run", "36-minute run westbound on the M4". Often more than
   one per bulletin. **Caveat (30 Jun 2026):** this is corpus/daypart-specific —
   the larger "Traffic Dribble" set above uses a minute count in only 3% of
   reads and never as the lead, carrying magnitude through *spatial extent*
   ("back to the Rozelle Interchange", "from Casula to Revesby") instead. Treat
   a drive-time headline as one tool the data may earn, NOT the default.
4. **Detour advice.** "Stick to Ballarat Road instead", "take Mount
   Alexander Road".
5. **Directional vocabulary is constant**: citybound / outbound /
   eastbound / both ways. Sydney and Melbourne lean hard on
   "citybound".
6. **Aussie idiom, but concrete**: "chockers", "chewing up", "stacks of
   traffic", "a crawl", "under pace", "no dramas till you get to the
   Bolte", "the usual hotspot", "just for good measure". The slang
   carries information (severity), it is not decoration.
7. **Presenter and station wrapper.** Sign-on ("It's Brittany Gora
   looking over Sydney") and sign-off ("That's the latest traffic on
   CADA", "That's KIIS traffic and weather together — this is the Kyle
   and Jackie O Show"). KIIS bundles a one-line weather forecast.
8. **An embedded ~8–10s sponsor read** in every single bulletin —
   beginning, middle, or end. Traffic copy itself is ~25–35s.
9. **Recovery/delta framing**: "desperately trying to recover following
   peak", "following a breakdown that's cleared" — they narrate change
   since the last report, not just current state.
10. **Format differences by station**: Gold = longer, more incidents,
    most colourful; KIIS = tight + weather + show handoff; CADA =
    snappy; 96FM Perth = rapid-fire road list with minimal connective
    tissue.

## Gap assessment vs our generated scripts (2026-06-10)

Live sample from our pipeline (gpt-5-mini, Melbourne, 30s target):

> "Heads up — West Gate's got roadworks and congestion, it's crawling
> through the works and a bit of a mess with multiple incidents slowing
> things down. Meanwhile Bourke Street's got a planned event, slow but
> moving through town. Over on Nicholson Street, construction is
> keeping traffic sluggish. Not far from there Wellington Street's
> under a road closure with only minor hold-ups, and Princes Highway
> and Tivoli Road are also closed, minor impacts across those routes."

**On-style:** tone and idiom land ("heads up", "a bit of a mess",
"crawling"); transition connectors match ("Meanwhile…", "Over on…",
"Not far from there…"); length and pace are right; one-road-once and
no-fabrication rules hold.

**Off-style (the real gaps, in priority order):**

| # | Gap | ATN does | We do | Root cause |
|---|---|---|---|---|
| 1 | **No directional language** | citybound/outbound/eastbound on nearly every incident | none in the sample script | corridor `Direction` exists in the data but the prompt never asks for it and the narrative summarizer only sometimes surfaces it |
| 2 | **No cross-street anchors** | "approaching Coleman Street", "after Christies Road" | bare road names only | HERE incident `Location`/detail carries anchor text; the corridor summary collapses it |
| 3 | **Drive times nearly banned** | 1–2 drive times per bulletin, the single most informative stat | "Max ONE numeric measurement" rule in `SystemInstructions.hbs` | prompt rule contradicts the reference style; corridor `DriveTimeMinutes`/`DelayMinutes` data exists |
| 4 | **No detour advice** | "stick to Ballarat Road instead" | never | not in prompt; routing data would be needed for safe suggestions |
| 5 | **No lane-level detail** | "two centre lanes out", "left lane blocked" | "roadworks and congestion" | HERE detail often includes lane info; it's dropped during summarization |
| 6 | **No sign-on/sign-off/weather** | every bulletin has presenter + station wrapper | explicitly forbidden ("No intros, sign-offs") | deliberate PoC decision — branding fields (`stationTag`, `signOff`) exist but the bulletin is delivered as traffic-copy-only for ATN-style insertion downstream; **needs an explicit product decision with ARN** |
| 7 | **Vague filler instead of severity** | slang carries data ("chockers" = severe) | "slowing things down", "minor impacts across those routes" | review agent catches some filler; the closing summary phrase pattern persists |

**Bottom line:** we are ~60% of the way there. The voice is right; the
*information density* is not. Real ATN bulletins are dense, anchored,
and metric-forward; ours are correctly toned but geographically vague.
The three highest-leverage fixes are prompt-side and data exists for
all of them: (1) require direction per incident, (2) pass cross-street
anchors through the narrative summarizer, (3) relax the
one-numeric-measurement rule to allow 1–2 drive times. Items 6 (station
wrapper) requires a product decision with ARN, and 4 (detours) needs
routing support.

These transcripts are also the natural seed corpus for the dormant
`StyleExamplesService` RAG path (index `traffic-transcripts`) — see
`docs/reference/phase-0-code-reviews.md` for that feature's status and
the licensing question that must be answered first.

## Update 2026-06-10 (evening): gaps 1–3, 5, 7 shipped

The agent setup was rebuilt against this corpus using an offline eval
harness (Foundry Responses API, gpt-5-mini; deterministic scorers for
direction coverage / anchors / numeric budget / filler / fabrication +
an LLM judge primed with three ATN reference segments). Five prompt
iterations (V0→V4 + anchor-chaining fix) landed:

- **`SystemInstructions.hbs`** — full ATN persona: cardinal
  data-is-the-only-truth rule (anchors/directions only from data — the
  eval showed style pressure without this rule makes the model FABRICATE
  cross-streets), anchored-location style, ≤2 numbers with drive-time
  comparison preferred, spoken distances, severity-bearing idiom,
  complete-clause ending, no-pad-on-sparse, plus two ATN transcripts as
  embedded few-shot (Gold Melbourne + WSFM Sydney).
- **`Draft.hbs` / `Refine.hbs`** — aligned guidelines.
- **`Review.hbs`** — now receives the traffic data
  (`IScriptReviewer.ReviewScriptAsync` gained a `BroadcastWithTraffic?`
  param) and checks fabrication, numeric budget, road revisiting,
  dangling endings.
- **`TrafficNarrativeSummarizer`** — per-incident `Location:` anchor
  lines (the cross-street detail was previously collapsed) and
  always-numeric impacts (the data layer no longer rations numbers; the
  prompt budget decides what gets voiced).
- **`TrafficScriptStyleValidator`** — numeric cap raised for drive-time
  comparisons, dangling-conjunction and banned-closer checks added.

Judge scores (vs ATN references): Sydney-style data-complete scenario
went from "metrics muddled" to **7/10, would_air: true, anchoring 9,
metric_use 9**. Live Melbourne run now produces e.g. *"Big one on the
West Gate — construction between CityLink and Williamstown Road and at
Kings Way, drive time nudged to about 13 minutes…"* at exactly 30.0s
audio. **Remaining ceiling is data, not prompts**: HERE construction
incidents carry no direction, so Melbourne roadworks roundups can't
voice "citybound" honestly (gap 1 is fixed wherever the data has
direction). Gaps 4 (detours — needs routing) and 6 (station wrapper —
ARN product decision) remain open.

## Update 2026-06-10 (late): HERE data enrichment shipped — gap 1 closed

Research confirmed HERE carries most of the missing data; the unused
parts are now wired in:

- **Direction from geometry** (`CompassDirection`, Domain/Services):
  HERE incident shapes are directional polylines but the API never
  states a compass direction. The shape bearing now resolves to
  "citybound"/"outbound" (±60° relative to the coverage centre) or a
  compass quadrant ("eastbound"), for any incident with ≥150 m of
  shape — including construction, which carries no direction in text.
  `CorridorAggregator` falls back to it when description keywords are
  absent.
- **Flow API** (`flow` endpoint, same key, standard tier): corridors
  now get `JamTendency` (-2…+2 → "Trend: congestion building/easing"
  in the LLM data — the honest basis for ATN's recovery framing) and a
  flow-derived delay (Σ len/speed − len/freeFlow) for corridors without
  routed drive times. Flow failures degrade silently.
- **Incident `summary`** (lane detail like "Lane blocked" not already
  in the description) and **`junctionTraversability`** (side-street
  status along closures) are captured and surfaced as `Lanes:` /
  `Junctions:` lines.

Live result (quiet Tuesday evening, Melbourne): *"biggest hold-up is
the West Gate **citybound** at Power Street… about a 6-minute delay…
Wellington Street **eastbound** is closed, **congestion's building**…"*
— judge score moved 3 → 6 overall (directionality 3 → 6) on the
hardest market; data-complete scenarios already judge would_air.
Remaining honest limits: minor laneway closures whose HERE detail is
just "Closed" cannot be anchored, and drive-time routing only runs for
FC≤3 corridors.

## Update 2026-06-14: prompt regression caught + realigned (eval baseline)

Re-ran the eval harness (`scripts/eval.sh`, Sydney/Melbourne/Gold Coast) for a
fresh baseline and found the **06-10 ATN tuning had partially regressed in the
live `prompts/*.md`** — the runtime prompts are `prompts/system.md` etc. (the
`.hbs` names above are historical), and the gap-#3 fix did not survive the
`.hbs`→`.md` migration:

- **Gap #3 (drive-time numerics) had reverted.** `prompts/system.md` still read
  "Max ONE numeric distance or time measurement", even though
  `TrafficScriptStyleValidator` (fails only at >3 matches, i.e. ≤2 voiced
  numbers incl. a drive-time comparison) and `TrafficNarrativeSummarizer`
  ("the LLM's style rules pick at most two to voice") were both still tuned for
  two, drive-time-forward. The prompt was suppressing ATN's headline metric.
  Realigned the prompt to "at most two, lead with a drive-time comparison when
  the data has it; otherwise at most one distance, don't recite a queue length
  per incident".
- **New units defect.** A Sydney run voiced "a mile-and-a-half" — the model
  converted the km queue length (`TrafficNarrativeSummarizer` emits `~{km}km`)
  to imperial. `system.md` taught the metric *form* but never forbade imperial,
  and the validator's `NumericMetricRegex` only matches metric units so it slid
  through silently. Added an explicit "metric only, never miles/imperial" rule.
- **Dangling-conjunction endings.** `system.md`'s "End mid-flow on the last item"
  fought the validator's `DanglingEndingRegex` (bans ending on and/but/with/so).
  Tightened to "finish the sentence cleanly, never trail off on a conjunction or
  dash" — two FAILs → zero.

After the fixes all three markets PASS (fabrication/style/safety/pronunciation)
with zero imperial units and zero dangling endings; drive-time comparisons
resurface where the data carries them (e.g. Melbourne "a usual nine-minute run").

**Still open (data-side, separate slice):** "99 Road" voices deterministically
on the Gold Coast corpus and "an unnamed road" appears intermittently — both are
`CorridorAggregator` road-name extraction defects (`MajorRoadPatterns` /
`NormalizeRoadName` miss some named surface roads; an unresolved corridor with a
known location anchor still falls back to "an unnamed road" via
`ResolveDisplayRoadName`). The drive-time **data ceiling** (routed times only for
FC≤3 corridors) means construction-heavy markets still can't lead with a drive
time. Voice selection is now an ARN-taste task over two shortlisted real
Australian Voice Library voices (Daryl - AU Male Radio, Peter Michael - AU Male
VO; no cloning, no training). `scripts/voice-audition.py` tunes them on `eleven_v3`
across the 3 stability modes plus ffmpeg-`atempo` faster reads, and renders this
corpus's reference clips alongside for comparison → `output/voice-audition/index.html`.
(Synthetic Voice Design was tried and dropped — it drifted American; accent must
be a real AU voice or be prompted explicitly, not carried by reference audio.)

## Update 2026-06-17: self-referential anchors + decimal extents fixed (data-side)

Re-validated against **gpt-5.4-mini** (now the default model). The ATN-tuned
prompts hold — direction, drive-times where the data carries them, and the
severity-bearing idiom all land. Two `TrafficNarrativeSummarizer` defects were
fixed at source:

- **Self-referential suburb anchor.** The `" near {Suburb}"` clause is now
  suppressed when road-name extraction has fallen back to the suburb itself —
  previously emitted "Neranwood near Neranwood" / "Springbrook near Springbrook"
  (the `RoadName == Suburb` fallback case of the `ResolveDisplayRoadName` defect).
- **Decimal extents.** Corridor extent km is rounded to whole numbers
  (`{km:F1}` → `Math.Round(…, AwayFromZero):F0`) so the model can't voice a
  decimal — "roadworks stretching about 1.6 kays" → "about a kilometre" /
  "around seven kays". Ranking is unaffected (it uses a separate length score).

**Still open:** incident *descriptions* that name the road as their own
cross-street endpoint ("President Avenue between South Street and President
Avenue") — a HERE description artifact, distinct from the road-name extraction;
the FC≤3 drive-time routing ceiling; and Gold Coast length overshoot on
construction-heavy runs.

## Update 2026-06-25: "stop reciting, start weaving" — flow + good-run roll-call fixes

A live-HERE scenario matrix (12 condition permutations, all 30s, generated through the
shipped draft→review→refine→slot-fit pipeline against real HERE traffic/weather/fuel) was
critiqued against this corpus. Two divergences dominated the "sounds AI-generated" signal,
and both were fixed prompt-side (+ one constant):

1. **The good-run road-dump.** Bulletins ended with a flat roll-call of up to eight clear
   roads ("Boundary Road, Waterworks Road, Logan Motorway, Hay Road, Mount Glorious Road,
   Mount Mee, Tamborine Mountain Road and Winn Road are all running well"). ATN never
   enumerates a list — it sweeps an area ("a smooth run throughout the Murraylands, South
   Coast and Hills"; "moving well on McMinn, Cavanagh and the Esplanade"). Root cause:
   `prompts/draft.md` literally said *"Name as many of these roads as it takes to fill the
   slot"*, and `BroadcastOrchestrator.GoodRunScriptCap` fed up to 8 roads.
   **Fix:** draft.md fill + secondary good-run guidance rewritten to "name the two or three
   that matter, sweep the rest into one phrase, group by area" (a roll-call is a FAIL);
   `GoodRunScriptCap` 8 → 6; `system.md` STYLE DEFAULTS gained an explicit anti-roll-call
   rule; `review.md` rejects a road-name roll-call; `refine.md` fills by area-sweep + data
   detail, never extra road names.
2. **Reciting data instead of a flowing read.** Items came out as a stack of identical
   "Road X is [state], with Y minutes" clauses with three minute-counts in a row. **Fix:**
   `system.md`/`draft.md` now require weaving with varied item intros ("sometimes the road
   first, sometimes the hold-up or the spot"), forbid repeating that template, and cut the
   numeric rule to ONE headline figure (a drive-time comparison counts as the one) with
   every other item qualitative — never stacked numbers. `review.md`/`refine.md` aligned.
3. **Side fixes:** a new BOUNDARY forbids narrating absent data ("no weather details
   provided" — observed once on a sparse read); the monitored-route headline now speaks
   naturally ("the M4 from Parramatta to the CBD" not the verbatim route label).

**Before → after (same pipeline, live HERE, 30s).** Before: *"Seventeen Mile Rocks Road's
the big headache, at a standstill and delays are up around 19 minutes. Jutland Street's also
locked up, with about a 10-minute wait… Boundary Road, Waterworks Road and Logan Motorway are
all running well, along with the hill runs."* (3 stacked numbers; road list). After (Melbourne):
*"Burke Road northbound is shut at the Eastern Freeway, with minor delays, and Greensborough
Bypass southbound is closed at Grimshaw Street… Over on the Ring Road at Pascoe Vale Road, a
broken-down vehicle has traffic crawling, while the Eastern Freeway eastbound at Hoddle Street
has a breakdown causing a drag. Monash Freeway's running well, and EastLink and Plenty Road are
clear."* (anchored, woven, zero stacked numbers, a 2-road good-run + implicit sweep).

**Verification:** deterministic eval (replay corpus) — fabrication/style/safety PASS on all
three markets before and after; the sweep behaviour appears even with no flow data ("the rest
of the run's clear… the wider network's moving pretty freely"). Live matrix — all 12 scenarios
flow and none dumps a road list.

**Still open:** on genuinely sparse, anchor-poor, NO-flow traffic-only data (the replay
Sydney/Gold Coast worst case) the draft underfills the word estimate, because the padding
levers were deliberately removed; production has live flow/good-runs to fill honestly (the
live matrix filled at 65–85 words) and the audio slot-fill + same-day fallback guarantee slot
length, so this is a corpus artifact, not a production gap. Operator-curated corridor ORDER
and producer off-data CAUSE adherence are still intermittent (model variance; prompt paths
unchanged) and worth a dedicated hardening pass.

## Update 2026-06-30: "Traffic Dribble" live comparison + per-theme feasibility

Generated 3 scripts through the shipped pipeline against **live HERE** (`./scripts/eval.sh
--script-only --live-here --review`, Sydney/Melbourne/Gold Coast, gpt-5.4-mini) and compared
them to the new corpus above. **Caveat:** the live fetch was interpeak/light (minor
construction + 1–2 congestion corridors + one closure per market; no crashes, no lane-blocking,
no real drive-time figures), so *density* differences are partly daypart, not pipeline — but the
structural/stylistic gaps are consistent across all three and match the harness's own anchoring
score (**0.2 / 0.0 / 0.0**).

The differences consolidate into five themes. The actionable question is **which are even
physically replicable** — grounded in the HERE Traffic/Routing API surface and the current code.
The last column shows the **on-air effect**: *before* = today's live-generated wording, *after* =
the corpus target (real ATN phrasings where they exist):

| # | Theme | Replicable? | Where the gap lives | Touch-point | Before → after (script effect) |
|---|---|---|---|---|---|
| 1a | **Anchor each item to a cross-street/landmark** | **Mostly yes** — coverage-limited, not blocked | Anchors come from HERE incident `Description` + revgeocoded `Location`; today's closures anchored ("At Pine Street") but minor construction/flow-congestion corridors had no usable description → suppressed by the `len>12`/self-referential filters. Pure flow corridors have road+suburb, no cross-street. | `TrafficNarrativeSummarizer` Location lines; `HereTrafficProvider.EnrichWithGeocoding`; loosen suppression / add nearest-junction revgeocode | **Before:** "Over on the Warringah Freeway, it's only minor construction." **After:** "the Warringah Freeway citybound's got roadworks just before the Falcon Street off-ramp" — anchored, like the corpus's "just past the Wahroonga interchange". |
| 1b | **Magnitude as queue EXTENT ("back to {place}", "from A to B")** | **Yes — but UNWIRED** | `TrafficCorridor.Extent` is declared AND the summarizer emits it, but **nothing in `src/` ever assigns it** (verified). `StartPoint`/`EndPoint` coords + `ReverseGeocodeAsync` already exist. Closing it = revgeocode the corridor endpoints → set `Extent`. Honest for flow corridors (OLR span = the congested stretch); approximate for an incident footprint. | `CorridorAggregator` (populate `Extent`); **highest-ROI data change** | **Before:** "Citylink outbound… copping about a 10-minute delay." **After:** "Citylink outbound's chockers, banked right back to Williamstown Road" — extent, not a minute count (cf. corpus "queues all the way back to the Rozelle Interchange"). |
| 2 | **Invert numeric instinct: minutes → extent + qualitative** | **Yes — prompt only** | `FormatImpact` already emits numeric AND qualitative; the prompt decides what to voice. Corpus says minutes are a 3% exception. Tighten `draft.md`/`system.md` to ration minutes ≈0 and lead on extent+feel. Depends on 1b for the extent half to have content. | `prompts/draft.md`, `prompts/system.md` | **Before:** "Flemington Road's also at a standstill, adding around 8 minutes." **After:** "Flemington Road's at a standstill too" — the minute count drops; at most ONE figure survives the whole read. |
| 3 | **Stop narrating ("the worst of it", "On the brighter side", "Off that"); no road-name strings** | **Yes — prompt + review gate** | Pure style; the anti-roll-call rule already exists (model-variance compliance issue). Ban the meta-scaffolding phrases; have `review.md` reject them and bare-road-name strings. | `prompts/draft.md`, `prompts/review.md`, `prompts/refine.md` | **Before:** "Then it's smaller snags on Plenty Road northbound, Calder Freeway citybound and Bulleen Road outbound… On the brighter side, the Monash…" **After:** "just the usual minor roadworks dotted around the north, and the Monash into town's only a touch slow" — area sweep, no meta-scaffolding, no 3-road string. |
| 4a | **Lane COUNT ("two of three lanes shut")** | **NO — hard HERE limit** | HERE `summary` tops out at "Lane blocked"/"Closed"/"Contraflow"; no lane count. `advancedFeatures=lanes` is HOV/fork-split (billed), not "2 of 3". We can voice qualitative lane status only when the description carries it. | n/a (not closeable from HERE) | **Before (and stays):** "the left lane's blocked." **Unreachable from HERE:** "two of three lanes out of action" — the count would be invented. |
| 4b | **Incident sub-variety / vehicle count (bingle, prang, three-car smash)** | **NO if invented** (fabrication); register-synonyms = a prompt+safety call | HERE gives the type (`accident`/`disabledVehicle`/…) but never "three-car" or "nose-to-tail". Inventing a count or sub-type violates the no-fabrication boundary. Treating "smash"/"prang" as severity-neutral synonyms of "accident" is a deliberate style+safety decision, not free. | `prompts/system.md` synonym policy + `safety.md` | **Before:** "an accident on Logan Road." **Reachable (synonym policy):** "a smash on Logan Road." **Unreachable:** "a three-car nose-to-tail" — vehicle count / sub-type invented. |
| 4c | **Recovery/delta ("picking up pace after an earlier breakdown", "now cleared")** | **Yes via same-day delta** (partly wired); **NO via single fetch** | Cross-bulletin delta is wired (`IncidentStateService`, `IncidentChanges`, same-day continuity, Cleared section). Single-fetch `jamTendency` is unreliable (banked lesson: real HERE flow lacks it) — never voice "building/easing" off one fetch. | `IncidentStateService`; continuity prompt paths | **Before (stated fresh every read):** "A breakdown on the Hume Motorway southbound at Campbelltown Road." **After (same-day delta):** corpus-verbatim "Picking up pace on the Hume Motorway southbound after an earlier breakdown at Campbelltown Rd." |
| 5 | **Station/network sign-off + weather tag** | **Yes — product decision** | Technically trivial (branding fields + weather content source exist); deliberately OFF so ATN-style wrappers are inserted downstream. Needs an explicit ARN call on whether Arn emits the wrapper. | product/templating, not HERE | **Before:** "…the rest of the run's clear." (ends cold). **After:** "…the rest of the run's clear. Sydney weather — showers and 16, and you're up to date on KIIS 1065." |

**Bottom line on the user's worry ("geographic position might not even be possible"):** geographic
precision is *largely* possible. Cross-street **anchoring mostly works already** (the floor is
HERE description coverage, not a wall), and **queue-extent is fully feasible — the field and
endpoints exist, only the revgeocode-and-assign step is missing** (1b is the single highest-ROI
fix). The genuinely *impossible-from-HERE* items are narrow: exact **lane counts** (4a) and
invented **vehicle counts / colloquial sub-types** (4b). Themes 2, 3 and the prompt half of
everything are entirely in our control; theme 5 is an ARN product decision. Net: the corpus's
signature density is achievable, with two honest, bounded exceptions.

## Update 2026-06-30 (later): themes 1a/1b/2/3 SHIPPED + verified

The four addable themes landed and were verified against this corpus via the eval harness
(`./scripts/eval.sh --script-only --review` replay + `--live-here`, gpt-5.4-mini, Sydney/Melbourne/Gold Coast):

- **1b — queue extent (data).** `HereTrafficProvider.EnrichCorridorsWithExtentAsync` now reverse-geocodes
  the two ends of each ranked corridor's affected stretch (routed `StartPoint`/`EndPoint`, else the primary
  incident's shape ends when the stretch ≥ 800 m) and sets `TrafficCorridor.Extent` = "from A to B" when both
  ends resolve to distinct, speakable names that aren't the corridor's own road. Fail-closed: any failure
  (incl. a replay 404 for un-recorded coords) leaves `Extent` None — so replay is unchanged and live gets it.
  Pure logic (`ComposeExtent`/`ResolveCorridorEndpoints`/`CleanEndpointName`) is unit-tested (`ExtentEnrichmentTests`).
- **1a — anchoring (summarizer).** `TrafficNarrativeSummarizer` now emits ONE inline cross-street anchor on the
  condensed **minor** bullet too (shared `CorridorAnchors` helper; self-reference still suppressed) — minor-only
  reads previously carried no anchor at all.
- **2 + 3 — prompts.** `system.md`/`draft.md` gained: pin every item to its spot; voice the `Extent` ("from A
  to B") as magnitude rather than a minute count; and an anti-scaffolding rule banning "the worst of it / the
  worst of the rest / on the brighter side / off that / first up / moving on". `review.md` rejects that
  self-narration. (4b synonym register and 5 wrapper deliberately NOT shipped — they need an ATN/safety + product call.)

**Verified (no regressions).** Infrastructure non-integration suite **789 passed / 0 failed / 22 skipped**;
touched unit tests green. Eval fabrication/style/safety **PASS on all three markets**, both replay and live.
Overall target-shape **76% → 88% (replay) / 91% (live)**; the **anchors** dimension moved off the floor
(Melbourne 0.0 → 0.8, Gold Coast 0.0 → 0.4 replay; Sydney 100% live). Extent now voices live
("roadworks from Plenty Road to the M80", "between the Eastern Freeway and Bridge Street"); minute-counts gave
way to feel ("a good half-hour crawl", "backed up the best part of an hour"); the scaffolding tells are gone.

**Watch item:** richer drafts run long at the DRAFT stage (Melbourne ~41s, Gold Coast ~39s vs 30s) — the
shipped review → refine → slot-fit trims over-length drafts by dropping the weakest *items*, keeping the
anchored detail on those that remain, so this is by design, not a production regression.

**Still open (unchanged):** 4a lane counts (hard HERE limit), 4b vehicle-count/sub-type synonyms (fabrication
boundary — needs sign-off), 4c single-fetch recovery trend (use same-day delta only), 5 station wrapper (ARN
product decision).

### 10-variant cross-market confirmation (30 Jun 2026)

Generated 10 live variants (Sydney, Melbourne, Brisbane, Gold Coast, Adelaide, Perth, Canberra,
Newcastle, Wollongong, Hobart) and fingerprinted them against the corpus. The shipped fixes hold
across the full metro+regional spread — **overall target-shape 93% (n=10)**:

| Signature | Generated (10) | ATN corpus | Verdict |
|---|---|---|---|
| Cross-street anchor | **100%** (~3.6/script) | ~87% | matched/exceeded (was 0.0–0.2) |
| Extent ("from A to B"/"between X and Y") | 50% | 56% | matched (was ~0) |
| Meta-scaffolding tells | **0%** | 0% | eliminated |
| Qualitative severity idiom | 80% | ~90% | close |
| Directional term | 70% | 94% | slightly under |
| Lane detail | 20% | 48% | under — HERE-limited (4a) |
| **Minute count** | **70%** (often stacked) | **3%** | **the remaining gap** |

**The remaining divergence is minute-count discipline at the DRAFT stage.** ATN voices a minute
figure in ~3% of reads; our drafts quote one in 70%, sometimes stacked (Melbourne 36+13, Brisbane
20+13, Perth 23+5). Root cause: `TrafficNarrativeSummarizer.FormatImpact` emits a raw "~N min" for
every delayed corridor and leaves rationing to the prompt. **The shipped pipeline already trims this**
— verified live with `--full-pipeline`: Sydney shipped carried ZERO minutes (draft had "around 7
minutes"), Melbourne shipped carried ONE (the draft's stacked "13 minutes" was dropped by review →
refine, per `review.md` item 4), and the Melbourne draft's "Canberra Road" fabrication (a corruption
of the good-run "Canterbury Road") was removed by the fabrication gate. So the shipped bulletins are
materially closer to ATN than the drafts; the highest-leverage NEXT lever is summarizer-side — emit
the qualitative label without the raw minute for all but the single worst corridor, so the draft
can't over-quote in the first place.

### Update 2026-06-30 (latest): minute-discipline + order-narration shipped

Two of the three residual tells from the look-and-feel review were closed:

- **Minutes → ONE numbers channel (done, verified).** `TrafficNarrativeSummarizer.FormatImpact` no
  longer emits a per-corridor `~N min` (it returns the qualitative tier label only — "heavy delays",
  "at a standstill"), and the per-corridor `Drive time: N min` line + `FormatNormalDriveTime` were
  removed. A time is now spoken ONLY on a monitored point-to-point run (`AppendMonitoredRouteBullets`),
  voiced absolute ("the M4, Parramatta to the city, 22 minutes") — exactly the ATN pattern.
  `DelayMinutes`/`DriveTimeMinutes` stay on the model for ranking + the operator console. Prompts
  (`system.md`/`draft.md`/`review.md`/`refine.md`) aligned. **Verified live (draft + shipped):
  zero minute counts across Sydney/Melbourne/Gold Coast** — where Melbourne previously stacked
  "36-minute" + "13 minutes" and Gold Coast read "35-minute run", magnitude is now feel + extent.
  Unit suite 790 pass; replay + live fab/style/safety PASS.
- **Order-narration → principle, not a blocklist (improved, residual).** `system.md`/`draft.md` now
  state the rule — "lead with the worst item by PLACING it first, never by announcing it; don't label
  or rank items aloud … or anything like them; don't signpost the switch to clear roads" — and
  `review.md` rejects it. The egregious tells ("the big one", "the worst of it", "on the brighter
  side", "off that") are eliminated, but the model coins milder variants ("the one copping it", "on
  the flip side") that occasionally survive review (~1 in 3 shipped). A hard blocklist was rejected
  deliberately: it would over-reject legitimate slang ("Heathcote Road's copping it" is fine; "the ONE
  copping it" is the ranking tell) — distinguishing them needs the principle, not a regex. Residual is
  much-reduced, not zero.

**Not done (deferred, was theme 2 / "vary closes"):** the repeated "rest of the run's clear" sweep —
separate change. **Watch (unrelated, pre-existing):** intermittent model fabrication of elaborated
road names (shipped Melbourne "West Gate Bridge", earlier "Canberra Road") slips the fabrication gate
— a future hardening pass, not caused by these changes.

### Update 2026-06-30 (variety): Phase 1 anti-repetition shipped + verified

Goal: two sequential bulletins on identical traffic data must not read the same (listener fatigue).
First, a model experiment settled the mechanism — on this `gpt-5.4-mini` Responses deployment,
`temperature`/`top_p` ARE accepted and honored (low values collapse variety; `seed` is rejected,
`400 "Unknown parameter"`), BUT the **default already produces 5/5 distinct scripts on identical input
(0.19 pairwise similarity)**. So sampling is not the lever — the residual sameness is the model
returning to a few favourite phrasings ("rest of the run's clear", "the one copping it") and the
prompt **seeding** them as the sole canned example. Decision: keep sampling at default; fix it
input-side. (Banked: do NOT pin a low temperature — proven to collapse variety.)

**Phase 1 (shipped):**
- **Per-bulletin variety angle (input entropy)** — `LlmScriptGenerator.VarietyAngles` (8 ATN-valid,
  style-only nudges: opening approach / rhythm / "reach for fresher idiom") picked at random per
  draft via `Random.Shared` and injected as `draft.md`'s `<freshness>` block. Different input each
  call → different realization, independent of sampling. Draft only.
- **Previous-read anti-repetition** — the existing same-day `{{previousScript}}` hook's instruction
  was strengthened from "don't re-read word-for-word" to "listeners JUST heard it — change the
  opening, sentence shapes, connectors and wrap-up; never reuse its stock phrases".
- **De-seeded the canned examples** — `system.md`/`draft.md` now frame the connector set and the
  clear-run wrap-up as "options to rotate, not a default", with explicit "vary every read".

**Verified:** two replay runs on IDENTICAL input → same-market scripts differ at **0.47–0.59**
similarity (none identical; different openings/orderings/wrap-ups). The parroted **"rest of the run's
clear" → 0/6** scripts (was ~every script). Unit suite **790 pass / 0 fail**; eval fab/style/safety
**PASS all 3**, overall 91% — no quality regression.

**Phase 2 (proposed, not built):** an ARN-editable, per-station **PhrasePalette** library (on the
VoiceProfile/DeliveryStyle/PronunciationDictionary rail) — categorised + tier/condition-tagged
phrase banks (connectors, severity, incident-type, weather, clear-run sweeps), seeded from the ATN
corpus, injected as a ROTATED, context-gated, SUGGESTIVE subset per bulletin (never "use these").
Gives ARN editorial ownership of the lexicon + a deeper rotation pool. Awaiting go-ahead.

## Update 2026-06-30 (reviewer loop): 4-cycle refinement to ATN-grade

Ran the closed reviewer loop (independent `atn-reviewer` agent as judge, 5 diverse-density
markets per round — Sydney/Melbourne/Brisbane/Adelaide/Hobart, live HERE, full pipeline). Four fix cycles,
each followed by a fresh-batch re-review:

| Cycle | Fix | Layer |
|---|---|---|
| 1 | **Self-referential anchor cleaning** — `CleanAnchor` strips the corridor's own road from an anchor ("Between Bruce Hwy and Sandgate Rd" → "near Sandgate Rd"; drops "At Kessels Rd" on the Kessels Rd corridor). Replaced the weak subset-only `IsSelfReferentialAnchor`. Unit-tested. | summarizer |
| 2 | **Roadworks ≠ jam** (never escalate a minor/roadworks item to a congestion verb the data doesn't state — killed the "crawling" fabrication) + **anti-clone/filler** ("in the same boat", "much the same", "X's got works at Y" ×N). | draft |
| 3 | **Translate labels, don't recite** — say how it DRIVES, not "congestion/construction/moderate delays"; pick ONE descriptor, never stack type+impact+detail; banned corporate fillers ("the pinch point", "keeping things a bit busy"). | draft |
| 4 | **Direction on origin→dest runs** ("Parramatta to the city" = citybound — reading the data, not inferring) + **no invented ranking** between equal items + **sparse all-clear must fill the slot with colour, not collapse**. | system + draft |

**Verdict trajectory (independent judge):** Brisbane **send-back → borderline → would-air**; the self-ref
anchors, "crawling" fabrication, label-stacking, "in the same boat" filler and invented severity rankings
are all eliminated; direction now lands on derivable routes (Melbourne/Brisbane anchoring + direction
0.9–1.0). Melbourne and Brisbane finish **would-air**; Sydney/Adelaide oscillate would-air/borderline with
the live data.

**The one stubborn residual: underfill on light/sparse data** (a quiet interpeak Sydney, sparse Hobart →
18–26s reads). This is NOT force-fixable at the prompt: the data genuinely thin, and pushing "fill the
slot" harder makes the model PAD with ungrounded colour (the judge caught a soft "through the city centre"
fabrication when it tried). The honest resolution is the **production audio slot-fill (content-first grow →
pitch-preserving slow-to-fill → same-day/filler fallback)**, which the `--script-only` eval does not model —
so a sparse-market "send-back on dead air" is largely an eval artifact, not an on-air defect. Regional ATN
reads are themselves thin (~15–25s of traffic copy; see the regional corpus). Verified: build clean, unit
suite **792 pass / 0 fail** (incl. 2 new self-ref-anchor tests); eval fabrication/style/safety PASS every
market, every cycle.

**Net:** on real (non-trivial) traffic the bulletins now read as genuine ATN copy — anchored, directional,
severity-by-feel, varied, fabrication-free, clean-closing. Remaining polish (occasional flat "minor delays"
label, light-data underfill) is `refine`/slot-fill territory, not structural.

## Update 2026-06-30 (polish fixes): caps + closure shipped; roll-call/close-repeat + sparse-fill spec'd

Self-judged a fresh 10-market live batch (Sydney…Hobart) against this corpus. On dense data the reads are
ATN-grade (Perth, Newcastle, Melbourne **would-air**); the residual tells cluster on light/sparse data. Of
the actionable (non-data-bound) findings, two were deterministic bugs — fixed and verified — and two are
spec'd as the right *next* changes (deliberately NOT more prompt bans).

### Shipped + verified

**1. CAPS normalisation (`RoadNameFormatter.ToSpeakable`).** HERE sometimes emits SHOUTING road names
("SMITH STREET MOTORWAY") that leaked straight to air. New per-word title-caser in `Domain/Services`: only
all-caps words are touched; route codes (M4) and state acronyms (CBD) stay upper; anything already carrying
lower-case passes through untouched (so "M4 Parramatta to Sydney CBD" is never re-cased). Wired at every
road-name feed into the writer — `TrafficNarrativeSummarizer` (corridor name, monitored-route name, cleared
road, anchors, extent) and `ContentFormatters.FormatGoodRuns`. Case-only ⇒ transparent to the
(case-insensitive) `FabricationScorer`. **Live-verified:** Gold Coast now reads "Smith Street outbound" (was
"SMITH STREET MOTORWAY"); no shouting tokens across 3 markets.

**2. Closure leads its own clause (`IncidentTypeClassifier.IsClosure` + `FormatImpact`).** A closure with a
small routed delay hit the delay branch first and rendered "Impact: minor delays", so the writer wrote "X is
shut … so minor delays there" — burying the one thing a listener must hear. `FormatImpact` now short-circuits
for closures (the road being shut IS the magnitude; the residual through-lane delay is dropped), reinforced by
a `draft.md` rule ("a closure stands ALONE; never tack a delay onto the shut road itself"). **Live-verified:**
Melbourne "Greensborough Bypass southbound is shut at the M80 ramp"; Gold Coast "Coast Highway's shut between
the Pacific Motorway and Marble Arch Place" — both lead on the closure, no delay subordination.

Verification: build clean; touched unit tests green (summarizer +3, new `RoadNameFormatterTests`); full infra
suite **806 pass / 0 fail / 22 skip** (no regression); replay eval 95% (no fab/style/safety regression); live
eval fab/style/safety PASS all 3.

### Spec 3 (not built): deterministic roll-call + close-repeat REJECTORS

The lexical tells — "in the same boat" (survived the Phase-1 de-seed), a 3-road **roll-call** (Wollongong:
"Market Street and Crown Street … and Russell Street"), and the near-universal close "rest of the run's clear"
(8/10) — are all the model reaching for favourite phrases on thin data. Prompt bans demonstrably don't stick
(banlists are whack-a-mole; the temperature experiment showed the prompt is already over-prescriptive). The
durable fix is to move them from *prompt instructions the model can ignore* to *deterministic code checks that
fail the draft and force a refine*:
- **`BulletinStyleGuard` (Infrastructure), run on the draft.** (a) **Roll-call:** ≥3 road mentions (reuse
  `FabricationScorer`'s `NamedRoadRegex`/`RoadCodeRegex`) in a run joined only by connectors (and / , / too /
  also / as well) with no intervening condition verb (slow/shut/crawling/delays/roadworks/standstill/queue) or
  anchor preposition (at/near/between/approaching) ⇒ trip. (b) **Repeated close:** the final clause matches the
  previous same-station script's close (already available as `PreviousScript`) or a seeded attractor ⇒ trip.
- **Wire into `LlmScriptGenerator` draft→review→refine:** a tripped guard injects a targeted NEEDS_REVISION
  ("you listed N road names in a row — sweep them into one area phrase" / "vary the close; the last read ended
  the same way") rather than relying on the LLM reviewer to notice. Refine retries already capped.
- **Eval:** add `RollCallScorer` + `CloseVarietyScorer` dimensions so the harness surfaces them too.
- **Risk:** must scope the roll-call detector to INCIDENT items and ALLOW the grounded "two named + area
  sweep" clear-road form (the ATN-sanctioned escape) — over-tripping would punish the intended good-run sweep.
  Interacts with Spec 4 (which names MORE clear roads): grouped-by-area naming is not a roll-call.

### Spec 4 (not built): widen sparse-market fill — give the writer honest material, never pad

Sparse markets underfill (Wollongong 22s, Hobart 10s) because the data is thin AND `FormatGoodRuns` caps named
clear roads at **2** + "(plus N more)". ATN regional reads name **4–6 clear roads grouped by area** — that's
the honest fill. Pushing the prompt to "fill harder" is proven to fabricate (the reviewer caught "through the
city centre"), so the lever is *more honest material*, not padding:
- **Adaptive good-run cap:** `FormatGoodRuns` names more clear roads (≈4–6, grouped by area) when the market is
  sparse (low corridor count); busy markets keep the brief 2-name + sweep so a dense read never becomes a
  clear-road list. The roads are already in the flow pool — today they're collapsed into "(plus N more)".
- **Prompt:** the `goodRunsAreFill`/`isLowCorridorCount` guidance says "name a handful (up to ~5) grouped by
  area" for sparse reads, consistent with the higher cap.
- **Optional (higher risk):** raise the HERE `maxCorridors`/good-run fetch breadth for thin markets; widening
  radius risks pulling irrelevant far roads, so prefer surfacing the already-fetched pool first.
- **Honest limit:** this helps sparse-but-not-empty markets (Wollongong, Adelaide). A near-empty market
  (Hobart, 1 corridor) has a floor — there is genuinely little to say, and the production audio slot-fill +
  fallback chain is the real answer there, not the writer.
