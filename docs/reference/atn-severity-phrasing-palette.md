---
title: "ATN Severity-to-Phrasing Palette"
description: "Deterministic mapping from HERE data categories (jam factor bands, closure/crash/roadworks types) to the phrasing real ATN traffic copy uses, extracted from all 180 sanitised ATN samples; used as prompt-injection examples for the draft/polish agents"
tags: [reference, atn, style, phrasing, prompts]
purpose: "reference"
status: "current"
last_verified: 2026-07-16
review_after: 2026-09-16
volatility: "low"
source_of_truth: "docs/reference/atn-bulletin-samples-sanitised.md (180 sanitised ATN samples)"
verified_by:
  - "Extracted programmatically from all 180 sanitised ATN samples"
aliases:
  - "severity palette"
  - "phrasing palette"
common_queries:
  - "What words does ATN use for heavy congestion?"
  - "How should severity map to on-air phrasing?"
---

# ATN Severity-to-Phrasing Palette

Deterministic reference: what HERE data categories map to in real ATN traffic copy.
Extracted from all 180 sanitised ATN samples across all 4 corpora.
Use as prompt-injection examples: "For HEAVY/JAM conditions use any of: ..."

---

## 1. Flow / Congestion Severity

HERE provides congestion levels: `JAM` (stopped), `QUEUE`, `SLOW`, `FREE_FLOW`, and drive-time
comparisons (`typicalDuration` vs `baseDuration`).

### 1A. HEAVY / JAM (HERE: `JAM`, `heavy`)

> Conditions where traffic is stopped, crawling, or bumper-to-bumper for an extended stretch.

**Openers** (lead with these for the worst item):
- "Absolutely chockers on the [ROAD]"
- "It's awful on the [ROAD]"
- "It's a mess on the [ROAD]"
- "An absolute shocker for the [ROAD]"
- "It's a shambles on the [ROAD]"
- "Still a massive struggle for the [ROAD]"
- "It's a struggle on the [ROAD]"

**Adjectives (mid-item)**:
- "chockers"
- "crawling"
- "bumper to bumper" / "bumper and bumper"
- "stop-start" / "stop start"
- "pretty much stop-start"
- "a sea of brake lights"
- "banked up" / "banking up"
- "queues all the way back to"
- "queuing back through"
- "backing up through"
- "chock-a-block"
- "pretty chock-a-block"
- "very heavy"
- "really heavy"
- "pretty heavy traffic"
- "really slow"
- "extremely slow"
- "desperately trying to recover"
- "a bit of a hot spot"

**Sentence shapes** (complete clauses from real ATN copy):
- "[ROAD] is chockers [DIRECTION] with [CAUSE]"
- "Absolutely chockers on the [ROAD] [DIRECTION] from [A] [DIRECTION] into [B]"
- "[ROAD] is bumper to bumper through [SUBURB]"
- "[ROAD] is stop-start from [A] to [B]"
- "[ROAD] is a sea of brake lights from [A] to [B]"
- "Traffic is crawling [DIRECTION] on the [ROAD] from [A]"
- "[ROAD] is banking up through [SUBURB]"
- "Stacks of traffic at [LOCATION]"
- "Queues all the way back to [LOCATION]"
- "Heavy traffic on [ROAD] through [SUBURB]"

**Real examples:**
- "Absolutely chockers on the West Gate citybound from the Princes Freeway over the bridge into the Burnley Tunnel — it's desperately trying to recover following peak and an earlier crash on the bridge."
- "It's awful on the West Gate citybound — delays from the Princes Freeway to the Burnley Tunnel following an earlier crash."
- "The Monash is a sea of brake lights from Warrigal Rd to Toorak Rd."
- "An absolute shocker for the M5 right now."
- "the M4 motorway tunnel, it is still a massive struggle from Waddell Street heading all the way out the motorway towards the M7"
- "bumper to bumper for much of the M5 westbound"
- "the Anzac Bridge is pretty much stop-start into Pyrmont"
- "Military Road is bumper to bumper through Cremorne and Neutral Bay"

---

### 1B. MODERATE / QUEUE (HERE: `QUEUE`, `moderate`, `slow`)

> Build-ups, slowing down, under speed but not stopped.

**Phrases:**
- "slow" / "slowing" / "slow but moving"
- "busy" / "busy but moving"
- "building" / "building up" / "building quickly"
- "filling up" / "beginning to fill up"
- "thickening up"
- "patchy"
- "under speed" / "under pace"
- "a little under speed"
- "feeling the pinch"
- "carrying plenty of traffic" / "carrying plenty of vehicles"
- "starting to get busy" / "getting busier"
- "slowing right back"
- "heavier"
- "banking up a little"
- "queuing up a little"
- "a little slow through"
- "a bit of congestion"

**Sentence shapes:**
- "[ROAD] is [slow/busy/heavy] [DIRECTION] through [SUBURB]"
- "[ROAD] [DIRECTION] is [building/filling up]"
- "[ROAD] [DIRECTION] is under speed from [A] to [B]"
- "Delays on the [ROAD] now stretching back to [LOCATION]"
- "[ROAD] remains heavy [DIRECTION] through [SUBURB]"
- "[AREA] is also [slowing/filling up]"

**Real examples:**
- "The West Gate Bridge is busy inbound but still moving"
- "Patchy for the Monash citybound between EastLink and Warrigal Road, the usual hotspot."
- "The M4 is under speed from Homebush through to Parramatta"
- "The Calder is slow from Keilor Park towards the city"
- "Sydney Rd is bumper to bumper through Brunswick [note: this specific road warrants bumper-to-bumper even in a moderate bulletin]"
- "Coronation Drive is slow inbound from Toowong towards the CBD"
- "The Centenary Highway northbound's already slow from the Reid Highway through to Sutton St"
- "The Parkway northbound is slowing from Cotter Road through to the Glenloch Interchange"

---

### 1C. LIGHT / MINOR (HERE: `QUEUE` with low delay, `minor`)

> Slight delays, scattered, not significant.

**Phrases:**
- "minor delays"
- "just minor delays"
- "a couple of minutes"
- "a few brake lights"
- "just a little under speed"
- "a slight" / "slight"
- "no dramas"
- "just a minor hold up"
- "slight at the moment"
- "a slight at the moment"

**Sentence shapes:**
- "causing minor delays"
- "just a couple of minutes on top of your trip"
- "Just a few brake lights forming now on the [ROAD]"
- "no dramas [DIRECTION] till you get to [LOCATION]"

**Real examples:**
- "Works on the Tulla outbound at Mickleham Road, speed and lane restrictions set up — no dramas citybound till you get to the Bolte."
- "a breakdown on Concord Road near Parramatta Road causing minor delays."
- "The M7 is moving pretty well at the moment.... just a minor hold up approaching the M4 interchange."
- "Got delays down through North Lakes — that's around 10 minutes on top of your trip"
- "The Sunshine Motorway's also a couple of minutes through Mountain Creek"
- "Just a few brake lights forming now on the Western Freeway, city bound from Lakes Road to Hopkins Road"

---

### 1D. FREE_FLOW / CLEAR (HERE: `FREE_FLOW`, `freeFlow`, `clear`)

> Everything moving at normal speed. ATN handles this in 3 distinct sub-styles.

#### Style 1: Full clear — single verb sweep
**Phrases:**
- "all clear"
- "moving well"
- "moving nicely"
- "a smooth run"
- "running smoothly"
- "steady" / "pretty steady"
- "steady-flowing"
- "looking good"
- "no delays"
- "no major delays"
- "no major issues"
- "a good run"
- "flying through"
- "pretty good"

**Sentence shapes:**
- "[ROAD] is all clear [DIRECTION]"
- "[AREA] is a smooth run this morning"
- "No major delays across [MARKET] this morning"
- "[ROAD]s are [DIRECTION] running well"

**Real examples:**
- "All clear on the Gore Hill and Warringah Freeways"
- "Traffic moving well on McMinn Street, Cavanagh Street and along the Esplanade"
- "A smooth run across the regions this morning, with traffic flowing nicely throughout the Murraylands"
- "Elsewhere is all clear so far"
- "The rest of the M1 is all clear all the way to Brisbane at the moment"

#### Style 2: Area sweep (group nearby roads together)
**Phrases:** Name 2-3 specific roads, then "and AREA are all running well / moving well"

**Real examples:**
- "traffic moving well on McMinn Street, Cavanagh Street and along the Esplanade"
- "it's flying through on the Western Freeway, you get to Ferris Road and delays start up through to Hopkins Road"
- "the Tullamarine is all clear all the way to the airport"
- "The Yarra crossings are moving ok"

#### Style 3: Positive comparison / "better than usual"
- "moving well for a change"
- "The Ring Road is moving well for a change... just a little under speed around the Plenty Rd interchange"
- "it's a good run on our roads at the moment for the drive home, no major issues"
- "We are accident and breakdown free"
- "We're excellent and breakdown free"
- "a pretty good one on our roads at the moment with no major issues"

---

## 2. Incident Types

### 2A. CRASH / COLLISION

HERE incident type: `ACCIDENT`.
ATN has **7 distinct escalation levels** for describing crashes:

#### Level 1: Minor / incidental
> Minor fender-bender, not blocking much, delays minimal.
- "a minor smash"
- "a minor bingle"
- "minor prang"
- "a minor two-car nose-to-tail"
- "reports of a minor bingle in the [TUNNEL/ROAD]"
- "Minor smash on [ROAD]... no major delays though"

#### Level 2: Standard crash (one lane, localised)
- "a crash on [ROAD] [DIRECTION] at [LOCATION]"
- "a smash on [ROAD] [DIRECTION] [APPROACHING/NEAR] [CROSS STREET]"
- "a two-car crash" / "a two car accident"
- "a three car crash"
- "a two-car collision" / "three-car collision"
- "a collision"
- "there's a crash"
- "we've got a crash"
- "got a crash"
- "reports of a crash" / "word of a crash"

#### Level 3: Blocking lanes (specific about which lanes)
- "[CRASH] — [N] lane(s) blocked/closed/out of action"
- "[CRASH], the [ADJECTIVE] lane's blocked"
- "two of three lanes shut"
- "two right-hand lanes closed"
- "the right lane's blocked"
- "one lane's gone"
- "the left lane is blocked off"

#### Level 4: Multi-vehicle / serious
- "a multi-car crash"
- "multi-vehicle smash"
- "a three-car smash"
- "a three vehicle nose-to-tail"
- "a crash with a semi involved"
- "a truck smash"
- "truck rollover"
- "a car crash has brought down wires"
- "a truck and car crash"
- "a bus and car crash"
- "a bus and bicycle crash"
- "a car and pedestrian incident"

#### Level 5: Car fire
- "car fire on the [ROAD] [DIRECTION]"
- "Reports of a car fire on the [ROAD] [DIRECTION] at [LOCATION]"

#### Level 6: Closure-inducing (road closed)
- "[CRASH] has closed [ROAD] [DIRECTION]"
- "A truck smash has closed the [TUNNEL] in both directions"
- "All [DIRECTION] lanes are blocked with traffic being diverted"

#### Level 7: Post-incident (clearing / cleared)
- "an earlier crash has now cleared"
- "following an earlier crash"
- "after an earlier smash"
- "now cleared" / "has cleared"
- "picking up pace after an earlier crash"
- "recovering after an earlier crash"
- "the crash on [ROAD] has now cleared but [DIRECTION] traffic is still slow"
- "all lanes back open"

**Incident location patterns** (always anchored):
- "[DIRECTION] at [CROSS STREET]" — e.g. "citybound at Coleman Street"
- "[DIRECTION] approaching [CROSS STREET]" — e.g. "northbound approaching Hancock Street"
- "[DIRECTION] just past [CROSS STREET]" — e.g. "citybound just past the Anzac bridge"
- "[DIRECTION] before [CROSS STREET]" — e.g. "citybound before the Burnley Tunnel"
- "[DIRECTION] after [CROSS STREET]" — e.g. "outbound after Racecourse Road"
- "[DIRECTION] near [LANDMARK/CROSS STREET]" — e.g. "southbound near the M2"
- "at [SUBURB]" — e.g. "At Haberfield"
- "at [INTERSECTION]" — e.g. "at the intersection of Riverwood Drive"
- "just off [MAJOR ROAD]" — e.g. "just off the Abbott Road Prospect Highway intersection"

---

### 2B. BREAKDOWN

HERE incident type: `BREAKDOWN`.

**Openers / lead-ins:**
- "a breakdown [DIRECTION] on the [ROAD]"
- "a truck's broken down on the [ROAD]"
- "a car has broken down on [ROAD]"
- "a van's broken down on [ROAD]"
- "a broken-down truck on [ROAD]"
- "a broken-down bus on [ROAD]"
- "got a breakdown"
- "there's a breakdown"
- "we've got a breakdown"
- "look out for a breakdown"
- "watch out for a breakdown"

**Lane detail:**
- "[DIRECTION] in the [ADJECTIVE] lane"
- "it's partially blocking the left lane"
- "it's in the left-hand emergency lane"
- "it's sitting in the centre lane"
- "it's in the breakdown lane [DIRECTION]"
- "in the [ADJECTIVE] lane" — e.g. "in the left lane"
- "blocking one lane near [CROSS STREET]"

**Severity tones:**
- "causing minor delays" (least severe)
- "causing delays" (moderate)
- "causing nothing but grief for the [ROAD]" (most severe)
- "causing delays through [AREA]"
- "delays are extensive"

**Real examples:**
- "a breakdown outbound on the CityLink... sitting in the centre lane approaching the Bulla Rd exit"
- "a broken-down truck causing nothing but grief for the Monash City, bound at Toorak Road"
- "a van's broken down at Artarmon on the Gore Hill Freeway southbound towards Reserve Rd"
- "a truck breakdown on the M5 East citybound inside the tunnel just before the Bexley Road exit"
- "a broken-down B double truck on Foreshore Road westbound"
- "a broken-down bus at Mount Collar on the Pacific Highway at Excelsior Road"

**Post-breakdown recovery:**
- "all cleared off"
- "an earlier breakdown has been cleared from the [ROAD]"
- "truck breakdown has moved off"

---

### 2C. ROADWORKS (planned)

HERE incident type: `ROADWORKS`.

**Openers:**
- "Works on the [ROAD] [DIRECTION] at [LOCATION]"
- "Works affecting your run [DIRECTION] on the [ROAD] at [LOCATION]"
- "Roadworks continue on [ROAD] near [LOCATION]"
- "Roadworks on [ROAD] in [SUBURB]"
- "Emergency roadworks this morning on [ROAD] near [LOCATION] after [CAUSE]"
- "Watch for crews packing up night works on [ROAD] at [LOCATION]"
- "works currently underway on the [ROAD] at [LOCATION]"
- "Some emergency roadworks happening on [ROAD] at [SUBURB]"
- "works continue in [SUBURB], [ROAD] is down to one lane in both directions"
- "Level crossing works [SUBURB], [ROAD] closed to the rail line"
- "Emergency utility works in [SUBURB] along [ROAD], the left [DIRECTION] lane shut near [CROSS STREET]"

**Detail patterns:**
- "speed and lane restrictions are in place" / "speed and lane restrictions set up"
- "lane closures in place"
- "speeds at 40"
- "crews on scene"
- "that's where speed restrictions are in place around ongoing roadworks"
- "the left-hand lane closed, speeds down to 40"
- "with some minor delays"
- "no dramas [DIRECTION] till you get to [LOCATION]"

**Roadworks vs no-drama qualifier:**
- "[ROADWORK LOCATION] — no dramas [DIRECTION] till you get to [LOCATION]"
  (Very ATN style — acknowledges roadworks but reassures with a qualifier.)

**Burst water main (common sub-type):**
- "Emergency roadworks this morning on [ROAD] after a burst water main"
- "Flooding following a burst water main in [SUBURB]"

---

### 2D. ROAD CLOSURE

HERE: `ROAD_CLOSED`.

**Phrases:**
- "[ROAD] closed [DIRECTION] at [LOCATION]"
- "closed off [DIRECTION]"
- "both ways affected"
- "closed in both directions"
- "shut both ways"
- "blocked off both directions"
- "all [DIRECTION] lanes are blocked"
- "two lanes out of action"
- "southbound lane's out of action"
- "wires are down, affecting [N] lane(s) [DIRECTION]"
- "a lane's out of action"

**Real examples:**
- "All the northbound lanes are blocked with traffic being diverted via the Logan Rd exit"
- "a crash has brought down wires and closed off Seven Hills Road"
- "the Great Western Highway remains closed both ways"
- "Two of the three northbound lanes are closed and heavy delays along Mona Vale Road"

**Diversion language (always paired with closures):**
- "give it a miss and take [ALTERNATIVE]"
- "take [ROAD] instead"
- "use [ROAD] instead"
- "make sure you stick to [ROAD] instead"
- "maybe use [ROAD] instead"
- "traffic being diverted via [ROAD]"
- "the diversions via [ROAD] add at least [N] minutes to the trip"
- "avoid [ROAD]" (rare — ATN prefers positive direction: "take X instead")
- "take [ROAD]"

---

## 3. Drive Time / Travel Time Presentation

HERE provides: corridor-level `typicalDuration`, `baseDuration`, and `delay`.

ATN uses **4 distinct drive-time formats**:

### Format A: "X-minute run" (noun phrase)
> "The M4 eastbound from Mulgoa Road to Church Street Parramatta, a 20-minute run."
> "a 22-minute run"
> "a 36-minute run westbound on the M4, Woodville Road to Church Street"
> "a 14-minute trip"
> "your run time's been sitting at about one hour"

### Format B: "taking X minutes" (verb)
> "taking just under 20 minutes"
> "taking about 28 minutes at the moment"
> "taking 40 minutes to get to the M7"
> "taking you now 15 minutes"

### Format C: "Travel time is X minutes" (statement)
> "Travel time on the M4 motorway from Mulgoa Road to Church Street is about 20 minutes at the moment."
> "Travel time is 36 minutes from Preston's to Kingsgrove"
> "Travel time from Eastern Creek to Homebush Bay Drive is currently 21 minutes"
> "Travel time from the Hawkesbury River Bridge to Wahroonga, 13 minutes"
> "Travel time citybound along the M5 though, from Preston's to the main tunnel not too bad, currently 22 minutes."

### Format D: "X minutes on top of your trip" (delay-only)
> "that's around 10 minutes on top of your trip"
> "The Sunshine Motorway's also a couple of minutes through Mountain Creek"
> "That stretch is going to take you just under 40 minutes"

### Occurrence rule (from corpus):
ATN almost never gives more than **one** drive-time figure per bulletin.
When they do, it's at most **two** (e.g. "The M7 getting from Richmond Road to the M2 in 10 minutes and the M5 from Preston's to the M5 tunnel is a 14-minute trip").
The single figure is always the **headline** corridor.

### Drive time anchor patterns:
- Always anchored with start AND end landmarks
- Format: `[ROAD] from [A] to [B]`
- Examples: "from Mulgoa Road to Church Street Parramatta", "from Preston's to Kingsgrove", "from Forsyth Road on the Princes to the Burnley", "from the Hawkesbury River Bridge to Wahroonga"

---

## 4. Diversions / Alternative Routes

> ATN almost always suggests an alternative after a closure or lane-blocking incident.

**Positive direction (take X):**
- "make sure you stick to Ballarat Road instead"
- "take Mount Alexander Road"
- "give it a miss, take the Princes Highway instead"
- "take Whitehall Street"
- "take Millers Road or Melbourne Road"

**Suggestions (softer):**
- "maybe use Toorak Road or High Street instead"
- "use [ROAD] instead"

**Construction types:**
- "take [ROAD] instead"
- "stick to [ROAD] instead"
- "give it a miss and take [ROAD]"
- "give it a miss, take [ROAD] instead"
- "maybe use [ROAD] or [ROAD] instead"
- "use [ROAD] instead"
- "try [ROAD]"

**Negative (avoidance — rare, and always paired with a positive):**
- "give it a miss" (never "avoid [ROAD]" alone)
- "no access to Moreland Street, take Whitehall Street instead"

---

## 5. Bulletin Structure

### 5A. Openers

**Metro openers:**
- "G'day there, looking over [MARKET] on this [WEATHER] morning"
- "Good idea, looking over [MARKET] on this [WEATHER] [TIME]"
- "Hello there"
- "It's [NAME] looking over [MARKET]"
- "It's [NAME] looking over [MARKET], with the [TOP ITEM]"
- "Looking over [MARKET] this [WEATHER] afternoon"
- "G'day, another check on your roads"
- "Having a look around town"

**Regional openers:**
- "[STATION], Time Saver Traffic."
- "[STATION] real-time traffic."
- "[STATION] traffic — every [N] minutes on your way to work."
- "[STATION], first with the [REGION]'s traffic."

**Cold open (no greeting, straight into traffic):**
- "Mays Hill: M4 medical emergency eastbound..."
- "In Pyrmont there's a crash on..."
- "At Ryde, a crash at Cressy Road..."

### 5B. Transitions between items

**Soft transitions:**
- "As well,"
- "Elsewhere..."
- "Also" / "Also slow / busy"
- "Meanwhile"
- "Plus"
- "And"

**Clear separation (after a crash/closure):**
- "Elsewhere,"
- "Elsewhere....."
- "Apart from that,"
- "And"

**Area shift:**
- "Looking at [AREA],"
- "Into [SUBURB],"
- "Around [AREA],"

**"just for good measure"** — ATN-specific humorous connector:
> "Delays also pushing onto the Bolte Bridge just for good measure."

### 5C. Weather integration

**Station-specific weather formulas:**
- **KIIS Melbourne:** "The KIIS cast — [WEATHER]. It's [NAME] first with KIIS traffic and weather together."
- **KIIS Sydney:** "KIIS-cast: [WEATHER]. That's KIIS traffic and weather together. This is the [SHOW]."
- **Gold:** "[WEATHER SUMMARY]... and you're up to date on Gold [FREQUENCY]."
- **WSFM:** "[WEATHER SUMMARY]... and you're up to date on Gold 101.7."

**Weather embedded in opener (not a separate section):**
> "G'day there, looking over Melbourne on this cloudy, wet morning."
> "Looking over Melbourne this cloudy afternoon, a shower or two expected. 16 the top, right now it's 14."

**Weather as closer:**
> "Showers and 16 today... and you're up to date on KIIS 1065"
> "Cloudy and 16 today.. and you're up to date on KIIS 101-1"
> "Sunny and 23 today and you're up to date on KIIS 97-3"
> "Partly cloudy and 17 today.. and you're up to date on Gold 104.3"

### 5D. Closures

**Standard sign-off (by station):**
- "That's the latest Sydney traffic" / "That's the latest Melbourne traffic"
- "That's Sydney's latest traffic"
- "You're up to date on [STATION]"
- "I'm [NAME], that's the latest traffic on [STATION]"
- "It's [NAME] with the latest [MARKET] traffic"
- "We'll check it again in [N] minutes"

**Sign-off + show handoff:**
- "It's [NAME] first with KIIS traffic and weather together. Will and Woody driving you home next."
- "I'm [NAME], that's the latest traffic on CADA."
- "It's Christie with the latest Perth traffic."
- "It's Travis — another check in 30 on Zinc 96."

---

## 6. Special Sentence Patterns (ATN-specific)

### Consequence chain (the classic ATN sentence shape)

> **[INCIDENT] at [LOCATION]. [N] lane(s) [affected]. Delays [EXTENT]. [DIVERSION].**

Real examples:
> "A crash on the Deer Park Bypass citybound after Christies Road has traffic slow going back to Leakes Road — make sure you stick to Ballarat Road instead."
> "There's a truck breakdown on the M5 East citybound inside the tunnel just before the Bexley Road exit. One lane's gone and traffic is crawling from King Georges Road."
> "We've got a two car crash on the Mitchell Freeway southbound approaching Cedric Street. The right lane's blocked and traffic is backing up through Warwick and Greenwood."
> "A three vehicle nose-to-tail is causing delays on Roe Highway westbound near Berkshire Road. One lane's blocked and traffic is backing up through Forrestfield."

### "Recovery" phrasing (post-incident, still heavy)

> This is extremely distinctive to ATN — acknowledging an incident is cleared but traffic hasn't recovered.

- "desperately trying to recover following peak and an earlier crash on the bridge"
- "recovering after an earlier crash"
- "in recovery mode"
- "the damage done" (after lanes reopened)
- "picking up pace after an earlier crash"
- "all lanes just reopened, but the damage done"
- "an earlier three-car smash has now cleared with travel time 36 minutes"

### The "usual" marker (routine congestion acknowledged)

ATN often flags known congestion without pretending it's newsworthy:
- "the usual hotspot"
- "Usual school drop off delays on Burke Rd through Camberwell in both directions."
- "the usual delays through Surfers"
- "as you'd expect at this time of the day"
- "busy as usual from Loganholme to Slacks Creek"
- "The South Eastern is busy as usual heading into the city"

### The "paired extremes" pattern

When everything is clear EXCEPT one hot spot:
> "Works on the Tulla outbound at Mickleham Road, speed and lane restrictions set up — **no dramas citybound till you get to the Bolte.**"
> "Northbound on the M1 is a little slow through Coomera and Pimpama but **elsewhere is all clear so far.**"

---

## 7. Summary: When HERE says X, ATN says...

| HERE Data | ATN Palette |
|---|---|
| `JAM` / heavy congestion | chockers, crawling, bumper to bumper, stop-start, a sea of brake lights, it's awful, it's a mess, an absolute shocker, a massive struggle |
| `QUEUE` / moderate | slow, busy, building, filling up, under speed, patchy, thickening up, a little under speed, feeling the pinch |
| `QUEUE` / minor | minor delays, a couple of minutes, a few brake lights, just a little under speed, no dramas |
| `FREE_FLOW` / clear | all clear, moving well, steady, a smooth run, flying through, good run, no delays, no major issues |
| `ACCIDENT` (crash) | smash, bingle, prang, nose-to-tail, collision, multi-car crash, truck rollover, car fire |
| `BREAKDOWN` | breakdown, broken-down [truck/car/van/bus], [vehicle] has broken down |
| `ROADWORKS` | works on [ROAD], roadworks continue, emergency roadworks, crews packing up night works, speed and lane restrictions |
| `ROAD_CLOSED` | closed, blocked, shut, out of action, wires down, both ways affected |
| `HEAVY` + still clearing | desperately trying to recover, recovering after, in recovery mode, the damage done |
| Normal / routine congestion | the usual hotspot, as you'd expect at this time of day, busy as usual |
| Drive time (one per bulletin) | "a [N]-minute run", "taking [N] minutes", "travel time is [N] minutes", "[N] minutes on top of your trip" |

---

*Source: All 180 sanitised ATN samples across metro audio corpus (12), regional/rural corpus (9), Traffic Dribble network-roundup corpus (62), and expanded ads-removed audio corpus (97). Compiled from docs/reference/atn-bulletin-samples-sanitised.md.*
