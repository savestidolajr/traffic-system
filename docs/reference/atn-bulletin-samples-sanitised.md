---
title: "Sanitised ATN Traffic Bulletin Samples"
description: "All four ARN-supplied ATN sample corpora with sponsor reads, station wrappers, and PII stripped; traffic copy only, the ground-truth style reference for prompt tuning and bulletin evals"
tags: [reference, atn, style, corpus, ground-truth]
purpose: "reference"
status: "current"
last_verified: 2026-07-16
review_after: 2026-10-16
volatility: "low"
source_of_truth: "ARN-supplied ATN sample corpora (sanitised)"
verified_by:
  - "Compiled from the four ARN-supplied corpora; sanitisation reviewed at import"
aliases:
  - "atn samples"
  - "atn corpus"
common_queries:
  - "What do real ATN bulletins sound like?"
  - "Where is the ATN style ground truth?"
---

# Sanitised ATN Traffic Bulletin Samples

All sponsor reads, station wrappers, and PII stripped. Traffic copy only,
compiled from all four ARN-supplied ATN sample corpora for use as style
reference and prompt tuning ground truth.

Sources:
- Metro audio corpus: Traffic_Examples.zip (ARN, 28 Jan 2026) — 12 reads across 6 station/market pairs
- Regional/rural audio corpus (ARN, 25 Jun 2026) — 9 reads across regional markets
- "Traffic Dribble" network-roundup corpus: TRAFFIC DRIBBLE.docx (ARN, 30 Jun 2026) — 62 written reads
- Expanded ads-removed audio corpus (ARN, 3 Jul 2026) — 97 reads (52 KIIS 101.1 Melb + 45 WSFM 101.7 Syd)

Total: 180 sanitised ATN traffic bulletin samples

---

## Metro Audio Corpus (12 reads)

Source: Traffic_Examples.zip (ARN, 28 Jan 2026)
Transcription: Whisper base, AU place-name primed, hand-corrected

### Gold 104.3 Melbourne — 7:00 AM

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

> Absolutely chockers on the West Gate citybound from the Princes
> Freeway over the bridge into the Burnley Tunnel — it's desperately
> trying to recover following peak and an earlier crash on the bridge.
> Delays also pushing onto the Bolte Bridge just for good measure. As
> well, it's patchy for the Monash citybound between EastLink and
> Warrigal Road, the usual hotspot. Works on the Tulla outbound at
> Mickleham Road, speed and lane restrictions set up — no dramas
> citybound till you get to the Bolte.

### KIIS 101.1 Melbourne — 8:29 AM

> G'day there, it's Andy looking over Melbourne, and it is awful on the
> West Gate citybound — delays from the Princes Freeway to the Burnley
> Tunnel following an earlier crash. The Tulla hosting debris citybound
> after Racecourse Road, lanes closed, speeds down, delays back to the
> Bulla Road exit — take Mount Alexander Road.
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
> It's Christie with the latest Perth traffic.

### 96FM Perth — 8:14 AM

> Mitchell Freeway southbound slow Burns Beach Road, Whitfords Avenue
> to Reid Highway, and Hutton Street to the Powis. Kwinana Freeway
> northbound Thomas Road to Rowley Road, Farrington Road to Cranford
> Avenue as well. And Roe Highway slow southbound at the Great Eastern
> Highway Bypass at Tonkin Highway, and northbound at Nicholson Road.
> It's Christie with the latest Perth traffic.

### CADA (Edge 96.1) Sydney — 7:50 AM

> Mays Hill: M4 medical emergency eastbound in the breakdown lane at
> Coleman Street. Traffic is fairly busy for the Ryde Bridge from Rhodes
> to Top Ryde citybound, slowing up to North Ryde, Lane Cove Road or
> Quarry Road. Please stay alert and follow the speed limit.
> I'm Monique Bavino, that's the latest traffic on CADA.

### CADA (Edge 96.1) Sydney — 8:22 AM

> It's very busy for the Ryde Bridge from Rhodes to Top Ryde citybound,
> following a breakdown that's cleared off North Ryde at Lane Cove Road.
> The M2 chewing up citybound North Ryde to the Lane Cove Tunnel. Very
> heavy M4 motorway citybound at Eastern Creek — stacks of traffic at
> the M7/M2 merger, Bella Vista. We've got slow traffic as well at
> Oatlands, Pennant Hills Road, coming towards the school zones. Busy
> for the M5 citybound through Moorebank into Revesby.
> I'm Monique Bavino, that's the latest traffic on CADA.

### WSFM 101.7 Sydney — 8:14 AM

> At Ryde, a crash at Cressy Road and down on Lane Cove Road it's
> northbound in the right-hand lane approaching Hancock Street; also
> slow north- and southbound over the Ryde Bridge at the moment. On the
> M4 motorway at Mays Hill there's a medical emergency, cars in the
> breakdown lane citybound just before Coleman Street — emergency crews
> are on site and traffic is slow past the site as well. Built up now
> from Frenchs Forest as far as Warringah Road heading south from the
> underpass to Starkey Street, and slowing down eastbound into the Lane
> Cove Tunnel on the M2.

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

### KIIS 106.5 Sydney — 7:39 AM

> It's Brittany Gora looking over Sydney, with the M7 building eastbound
> from Richmond Road to the M2 at Windsor Road, taking just under 20
> minutes. A crash in Cecil Hills, Cowpasture Road northbound
> approaching Elizabeth Drive. A breakdown in Rose Hill at the motorway,
> James Ruse Drive northbound past Grand Avenue.
> KIIS-cast: sunshine, 29 in the city, 35 in the west. That's KIIS
> traffic and weather together. This is the Kyle and Jackie O Show.

### KIIS 106.5 Sydney — 7:49 AM

> It's Brittany Gora looking over Sydney, and we're seeing a 36-minute
> run westbound on the M4, Woodville Road to Church Street, caught up in
> this medical emergency with a reduced 40k speed — it's in the
> breakdown lane citybound approaching Coleman Street, Mays Hill. A
> crawl through the Rozelle Interchange on citybound approaches to the
> Anzac Bridge.
> KIIS-cast: a warm sunny day, 29 in the city, 35 in the west. That's
> KIIS traffic and weather together. This is the Kyle and Jackie O Show.

---
## Regional & Rural Audio Corpus (9 reads)

Source: ARN-supplied MP3s (25 Jun 2026)
Transcription: Whisper small.en + medium.en, AU road/place gazetteer, hand-corrected
Sponsor reads removed; station wrappers kept for context.

### Zinc 96.1 Gympie (QLD) — morning

> Zinc 96, Time Saver Traffic.
> G'day, another check on your roads. Looking good between the Coast and
> Gympie. Got delays down through North Lakes — that's around 10 minutes
> on top of your trip — and getting heavier through Mountain Creek and the
> Sunshine Motorway. Beckmans Road (?). Noosaville State School (?) —
> school drop-offs underway, school-zone speed limits in place. Bit of
> traffic on Channon Street (?) around Louisa Street and around the KFC.
> Also Exhibition Road, building on the south side now around the school.
> It's Travis — another check in 30 on Zinc 96.

### River 94.9 Ipswich (QLD) — morning drive

> River 94.9 traffic — every 15 minutes on your way to work.
> Traffic on the Centenary Highway is a heavy run northbound through
> Sumner, and it looks good for the Western Freeway — no delays through
> Indooroopilly, Fig Tree Pocket. The Ipswich Motorway is busy between the
> Centenary Highway and Oxley Road. East Street is also queuing up a little
> at times at the Brisbane and Limestone Street traffic lights.
> It's Carole Park (?) — another check in 15 minutes on River 94.9 and the
> iHeart app.

### Mix 104.9 Darwin (NT) — morning

> Mix 104.9 real-time traffic.
> No major delays across Darwin this morning, with the drive into the city
> looking pretty good — traffic moving well on McMinn Street, Cavanagh
> Street and along the Esplanade. Around the airport, Bagot Road and
> McMillans Road are also running smoothly. Watch for crews packing up
> night works on Berrimah Road at Export Drive, while roadworks continue on
> the Stuart Highway near Bilroy Road (?).
> That's the latest traffic on Mix 104.9.

### Power FM Nowra (NSW) — morning

> Power FM Traffic.
> G'day all (?) — you'll have a good run this morning on the Princes
> Highway, north- and southbound, going past Turpentine Road. Also Falls
> Creek, Tourist Bay Road (?) clear to the Princes Highway. Cambewarra
> Mountain. Steady-flowing traffic on Moss Vale Road going eastbound. And
> from the Southern Highlands, no delays — the Motorway going north- and
> southbound.
> That's the latest traffic — with Stolzy and Izzy for breakfast on Power
> FM.

### 5MU Murray Bridge (SA) — morning

> A smooth run across the regions this morning, with traffic flowing nicely
> throughout the Murraylands, South Coast and Hills areas — including
> Adelaide Road around Mannum Road and through Mount Barker. Just watch for
> roadworks on the South Eastern Freeway near Callington, and Inman Valley
> Road near Torrens Vale Road (?) at Bald Hills — that's where speed
> restrictions are in place around ongoing roadworks.
> You're all clear on your local — 5MU.

### Power FM Ballarat (VIC) — Thursday morning

> Having a look around town, there's no delays or collisions — it's a nice
> run for your Thursday morning. Otherwise, have a look at your run into
> Melbourne: it's flying through on the Western Freeway, you get to Ferris
> Road and delays start up through to Hopkins Road. As for the Deer Park
> Bypass, a slow run from Robinsons Road on the Ring Road, where it's banked
> up all the way through to the West Gate.
> That's the latest traffic on Power FM.

### Wave FM 96.5 Wollongong (NSW) — morning

> Wave 96.5, first with the Illawarra's traffic.
> In Coniston, Spring Hill Road's seeing a bit of congestion in both
> directions just past Masters Road. The Princes Highway's moving well for
> both north- and southbounders — much the same for the M1, with a bit of
> activity at the on- and off-ramps to Five Islands Road. Cataract to
> Heathcote (?) via the Princes Highway northbound — a 22-minute run.
> That's the latest traffic on Wave 96.5 — more fun in the morning with
> Lyndal and Crammy.

### 91.1 Hot FM Sunshine Coast (QLD) — morning

> 91.1 Hot FM, Sunshine Coast Traffic.
> G'day — I've got a breakdown on the Bruce Highway northbound through
> Murrumba Downs, not causing any delays though. It is slow southbound
> through North Lakes, but easing — it's a couple of minutes. The Sunshine
> Motorway's also a couple of minutes through Mountain Creek. And a bit of
> traffic through Kunda Park, Maroochydore Road, the Nicklin Way through
> Bokarina, and Caloundra Road, Kawana Way, the Link Road roundabout,
> Meridan Plains.
> It's Travis — another check in 30. 91.1 Hot FM.

### 106.3 Townsville (QLD) — morning

> 106.3 Townsville, Time Saver Traffic.
> Well, traffic is settling down on Woolcock Street between Cosgrove and
> Mount Louisa. It's been heavier at times on Dalrymple Road through Mount
> Louisa, around Bamford Lane, and also around Nathan Street at Heatley.
> Steady on Sturt Street around the CBD, and also Ross River Road through
> Cranbrook.
> I'm Brad — that's the latest traffic on 106.3.

---
## "Traffic Dribble" Network-Roundup Corpus (62 reads)

Source: TRAFFIC DRIBBLE.docx (ARN, 30 Jun 2026)
Pure scripted traffic copy, no embedded sponsor reads. Verbatim from source.

### Sydney (13 reads)

1.
> IN Pyrmont there's a crash on the Western Distributor citybound just past the Anzac bridge has two of three lanes shut. We have queues all the way back to the Rozelle Interchange with delays also for the City West Link, Victoria Rd, The M4 and Iron Cove Link Tunnels
> At Denham Court - a three car crash on Camden Valley Way northbound at Denham Court Rd
> On the M4 - Mulgoa Rd to the Church St off ramp is taking 29 minutes
> A Bus and car crash at Homebush on Parramatta Rd at Station St - a westbound lane is out of action
> A van's broken down at Artarmon on the Gore Hill Freeway southbound towards Reserve Rd
> Picking up pace on the Hume Motorway southbound after an earlier breakdown at Campbelltown Rd
> Sydney weather - showers and 16 degrees and you're up to date on KIIS 1065

2.
> We've got a two car accident on the M1 northbound just past the Wahroonga interchange. The left lane is blocked with delays back towards Berowra
> A truck broken down on Homebush Bay Drive at the M4 on-ramp. Two ouf of the three northbound lanes are blocked. Delays are back to Railway St
> The M5 East tunnel is under speed and we have queues from Kingsgrove to Bexley Rd
> The Warringah Freeway citybound is slow from Miller St and Military Rd is slow through Neutral Bay
> That's the latest Sydney traffic

3.
> The M4 is running nicely from Penirth to Prospect but under speed through to Parramatta and again around Homebush
> There's a smash on the M2 at Macquarie Park citybound near the Lane Cove Rd exit.. delays back to Beecroft Rd
> The M5 is bumper to bumper from Casula to Revesby.
> The Eastern Distributor is very slow northbound from Dacey Avenue all the way to Wolloomollo
> A breakdown southbound on the Pacific Highway at Killara blocking one lane near Powell St
> Sydney's weather - sunny and 22 today and you're up to date on Gold 101.7

4.
> Some emergency roadworks happening on Parramatta Rd at Leichhardt thanks to a burst water main. The left lane citybound is currently blocked near Norton St. with delays back to Canterbury Rd.
> The crash on the Princes Highway at Blakehurst approaching the Tom Ugly's Bridge has now cleared but northbound traffic is still slow.
> The M1 from Mulgoa Rd to Church is taking about 28 minutes at the moment
> That's the latest Sydney traffic

5.
> Reports of a minor bingle in the M5 East Tunnel Citybound... delays back to Kingsgrove at this stage
> The Cumberland Highway is crawling through Merrylands and Smithfield
> Heavy traffic northbound on Pennant Hills Rd approaching the M2
> All clear on the Gore Hill and Warringah Freeways
> That's the latest Sydney traffic

6.
> The Pacific Highway is crawling through Chatswood and Artarmon with heavy traffic in both directions.
> The Gore Hill Freeway is busy heading towards the city with delays building through the tunnel approaches.
> Military Road is well under speed through Neutral Bay and Cremorne.
> Pretty steady at the moment on Mona Vale Rd all the way to St Ives.
> Showers and 14 today... and you're up to date on KIIS 106-5

7.
> Parramatta Rd is crawling at the moment from Burwood all the way to Camperdown
> Heavy traffic on Victoria Rd between Gladesville and Rozelle then it's a slow trip across the Anzac Bridge as well.
> Smash northbound on General Holmes Drive just before the airport tunnel with delays back to Brighton
> Breakdown on Southern Cross Drive southbound just near Link Rd blocking the left lane.
> That's the latest Sydney traffic

8.
> The M7 is moving pretty well at the moment.... just a minor hold up approaching the M4 interchange.
> The M4 itself is under speed westbound from Granville to the Cumberland Highway
> There's a smash on the Great Western Highway at Prospect.... it's westbound near Reservoir Rd
> and the M2 is slow outbound around Baulkham Hills.
> Sunny and 19 today and you're up to date on KIIS 106-5

9.
> We've got a three car crash at Rozelle on Victoria Road citybound approaching the Iron Cove Bridge.
> The right lane's blocked and traffic is backing up through Drummoyne and Five Dock.
> Also slow on the City West Link approaching The Crescent and the Anzac Bridge is pretty much stop-start into Pyrmont.
> The M4 is under speed from Homebush through to Parramatta and we've got a breakdown on Concord Road near Parramatta Road causing minor delays.
> That's the latest Sydney traffic.

10.
> There's a truck breakdown on the M5 East citybound inside the tunnel just before the Bexley Road exit.
> One lane's gone and traffic is crawling from King Georges Road.
> The Princes Highway's also busy through Rockdale and Arncliffe while General Holmes Drive is slow towards the airport.
> Elsewhere..... reports of a crash on Pennant Hills Road southbound at Thornleigh near the M2
> and Military Road is bumper to bumper through Cremorne and Neutral Bay.
> That's the latest Sydney traffic

11.
> We've got a breakdown on the Harbour Bridge southbound in the second lane from the left.
> Traffic's banking up through North Sydney... with the Warringah Freeway slow from Miller Street.
> A crash at Liverpool on the Hume Highway northbound near Cumberland Highway.... one lane's blocked and traffic is backing up through Casula.
> The M5 is also very slow from Moorebank through to Revesby.
> Heavy traffic on Woodville Road through Guildford.... and the Cumberland Highway is under speed through Smithfield.
> That's the latest Sydney traffic

12.
> A smash on the Pacific Highway southbound at Chatswood approaching Mowbray Road... traffic is already queuing through Roseville and Lindfield.
> The Gore Hill Freeway is slow approaching Artarmon... and the Lane Cove Tunnel is heavy heading towards the city.
> Elsewhere, Mona Vale Road is under speed approaching St Ives.
> The M4's slowing from Church Street through to Homebush while the Western Distributor is stop-start approaching the CBD.

13.
> Reports of a car fire on the Princes Highway southbound at Sylvania approaching Tom Uglys Bridge... fire crews are on scene... two lanes are blocked at the moment with delays back to Blakehurst
> Captain Cook Bridge is busy southbound while the M5 East is under speed from Arncliffe through to Kingsgrove.
> Three car crash on Homebush Bay Drive northbound near the M4.... Two lanes are affected with traffic back past Parramatta Rd
> Southern Cross Drive is also slow through Tempe while the Eastern Distributor is crawling from Gardeners Road all the way through to Woolloomooloo.

#### Melbourne (10 reads)

1.
> Inbound on the West Gate is slow from Williamstown Rd through to the tunnel
> The Monash is a sea of brake lights from Warrigal Rd to Toorak Rd
> The Tullamarine is crawling from Bell St all the way through to Flemington Rd..
> And if you're on the Eastern we've got heavy traffic from Doncaster Rd towards Hoddle St
> That's the latest Melbourne traffic

2.
> There's a breakdown outbound on the CityLink... sitting in the centre lane approaching the Bulla Rd exit.
> Delays on the Monash now stretching back to Springvale Rd
> St Kilda Rd is busy both ways between Commercial Rd and Flinders St
> and give yourself extra time if you're travelling across the Bolte Bridge with traffic very slow over the river
> That's the latest Melbourne traffic

3.
> We've got a minor smash inbound on the Princes Freeway near Millers St... Emergency services are on scene and traffic is already building up behind it
> Things are heavy through Richmond on Hoddle St approaching Swan St .
> Usual school drop off delays on Burke Rd through Camberwell in both directions.
> The Ring Road is moving well for a change... just a little under speed around the Plenty Rd interchange..
> Showers and 14 today... and you're up to date on KIIS 101-1

4.
> The West Gate Bridge is busy inbound but still moving.. while delays are building on the Kings Way corridor.
> There's a breakdown on the Eastern Freeway citybound approaching Chandler Highway, blocking the left hand lane.
> Roadworks continue on Footscray Rd near Docklands....
> The Nepean Highway is slow between Moorabbin and Elsternwick
> The Calder is slow from Keilor Park towards the city while Sydney Rd is bumper to bumper through Brunswick
> That's the latest Melbourne traffic

5.
> The Monash outbound is slow from the CityLink interchange through to Malvern.
> The Tullamarine is heavy heading towards the airport with delays from the Calder interchange
> really slow on Dandenong Rd approaching Chadstone
> and the West Gate is under speed but still moving
> Cloudy and 16 today... and you're up to date on KIIS 101-1

6.
> Traffic is crawling southbound on the CityLink approaching the Burnely Tunnel after a multi-car crash.... Delays are back towards Flemington Rd.
> The Eastern Freeway is also feeling the pinch at the moment with slow traffic through Doncaster and Kew, especially around Hoddle St
> The Yarra crossings are moving ok and the Tullamarine is all clear all the way to the airport.
> That's the latest Melbourne traffic

7.
> We've got a three car smash inbound on the Monash Freeway at Warrigal Road.... that's blocing the right lane... traffic already banking up through Wheelers Hill with delays stretching back towards Springvale Road.
> The Princes Highway is also slow through Oakleigh.
> Heavy traffic approaching the Burnley Tunnel...and Hoddle Street is crawling towards Swan Street.
> Reports of a crash on the Eastern Freeway citybound near Bulleen Road.. traffic back through Doncaster....
> The West Gate Bridge is under speed and we're also seeing delays on Geelong Road approaching the freeway.
> That's the latest Melbourne traffic

   +++ +
> Car fire on the CityLink outbound approaching the Bolte Bridge... Traffic is stop-start from Footscray Rd
> The Monash is also under speed through Malvern and the Tullamarine is slowing near Flemington Road.
> The West Gate is under speed heading west
> Sydney Road still slow through Brunswick and Bell Street is busy approaching Pascoe Vale Road.

8.
> There's a two car accident on Dandenong Road inbound near Chadstone Shopping Centre.
> One lane's blocked and traffic is backing up towards Warrigal Road.
> The Monash remains heavy through Glen Iris.
> Burke Road's crawling through Camberwell while Toorak Road is slow approaching the city.
> Partly cloudy and 17 today.. and you're up to date on Gold 104.3

9.
> We've got a breakdown northbound on the Tullamarine Freeway approaching English Street... traffic is slowing right back through Essendon Fields.
> The Calder Freeway is also busy approaching the city.
> Heavy traffic around Bell Street... and the Ring Road is under speed between Plenty Road and Greensborough Highway.
> The Frankston remains heavy through Seaford.
> Meanwhile CityLink is slow approaching the Burnley Tunnel and traffic's building on Kings Way heading into the city
> That's the latest Melbourne traffic

10.
> Reports of a truck rollover on the Princes Freeway outbound near Werribee.
> Traffic is being directed around the scene with delays already back through Point Cook.
> The West Gate Freeway is feeling the pinch as well.
> Geelong Road is heavy through Brooklyn and the Monash is still busy from South Yarra through to Malvern.
> Roadworks on Footscray Rd in Docklands has two lanes closed... traffic is crawling from Dynon Rd.
> And there's a breakdown in the CBD on Spencer St near La trobe St....
> That's the latest Melbourne traffic

#### Brisbane (13 reads)

1.
> Gateway Motorway at Eight Miles Plains - there's a crash with a semi involved.. one of the vehicles has rolled. All the northbound lanes are blocked with traffic being diverted via the Logan Rd exit.
> Car broken down on the Bruce Highway northbound near New Settlement Rd.
> Heavy traffic southbound through North Lakes as you're heading towards Anzac Avenue to the Bruce Highway
> The M1 southbound at Beenleigh there's a smash in the right hand lane just before Main St
> A shower or two today top of 21 and you're up to date on KIIS 97-3

2.
> We've got a crash at Castelford... It's on Linkfield Rd eastbound just before Gympie Rd.
> Two car accident at Belmont on Belmont Rd near Marwood St
> really heavy on the Centenary Highway northbound from the Ipswich Motorway to the Jinderlee Bridge
> And the Bruce Highway's under speed through North Lakes.
> Sunny and 23 today and you're up to date on KIIS 97-3.

3.
> There's a car broken down on M1 at Slacks Creek... it's on the left shoulder just before you hit Paradise Rd
> A crash at Waterford West... Kingston Rd near Evergreen Avenue.
> We had an earlier car fire on Kingsford Smith Drive at Eagle Farm... that's now out but traffic is still slow.
> Breakdown outbound in the Clem 7 at Wollongabba
> And really busy inbound on the Riverside Expresswat
> Cloudy and 22 today... and you're up to date on KIIS 97-3

4.
> Traffic already building on the Pacific Motorway inbound from Springwood through to Eight Mile Plains.
> The Gateway Motorway is fairly steady northbound around the airport precinct, while Gympie Road's beginning to fill up through Chermside.
> If you're heading in from Ipswich, expect delays on the Centenary Motorway approaching Jindalee.
> Ipswich Road is also slowing through Moorooka
> That's the latest Brisbane traffic

5.
> Logan Road is carrying plenty of traffic this morning with delays building around Garden City.
> Old Cleveland Road is busy through Carindale heading towards the city.
> The M1 is stop start from Loganholme to Slacks Creek.
> Breakdown northbound on the Riverside near Anne St
> Gympie Road is slow through Chermside and Kedron heading towards the city.
> Showers and 18 today... and you're up to date on KIIS 97-3

6.
> Still heavy on the Bruce Highway southbound from North Lakes to Mango Hill
> Slow on Sandgate Rd through Clayfield and Albion at the moment
> Got a breakdown reported on the Inner City Bypass westbound approaching Bowen Hills.
> The Story Bridge is busy but moving.
> Coronation Drive is slow inbound from Toowong towards the CBD.
> The William Jolly Bridge approaches are heavy while Roma Street and Turbo Street have the usual bottlenecks.
> The Captain Cook Bridge busy in both directions
> While traffic is steady on the Riverside.
> That's the latest Brisbane Traffic

7.
> The Pacific Motorway outbound is already slow through Woolloongabba and Greenslopes.
> Coronation Drive heading west is building up quickly heading out of the city.
> Heavy northbound across the Story Bridge at the moment while the Riverside Expressway is beginning to fill up in both directions.
> and the Gateway is busy both ways around the airport precinct.
> Sunny and 21 today and you're up to date on KIIS 97-3

8.
> The Captain Cook Bridge is carrying heavy traffic through the CBD corridor
> Gympie Road remains slow through Chermside, Kedron and Lutwyche.
> The Bruce Highway southbound is carrying plenty of vehicles approaching the city.
> Stafford Road is busy through Everton Park and Stafford Heights, while the Webster Road corridor is slowing through several intersections.
> Logan Road is slow around Mount Gravatt and Greenslopes.
> and the M1 patchy southbound between Eight Mile Plains and Loganholme
> That's the latest Brisbane traffic

9.
> We've got a three car crash on the Pacific Motorway inbound at Rochedale near Miles Platting Road.
> The centre lane's blocked and traffic is already backing up through Springwood.
> The Gateway Motorway is heavy northbound approaching the airport precinct.
> Logan Road's also under speed around Garden City while the Riverside Expressway is slow approaching the CBD.
> Sunny and 22 today... and you're up to date on KIIS 97-3

10.
> a truck's broken down on the Gateway Motorway southbound just before the Gateway Bridge... One lane's blocked and traffic is backing up through Bracken Ridge.
> Gympie Road is bumper to bumper through Chermside and Kedron.
> The Bruce Highway is also slow southbound from North Lakes through to Mango Hill.
> The Centenary Motorway'S heavy approaching Jindalee Bridge.
> and the Inner City Bypass is under speed approaching Bowen Hills.
> That's the latest Brisbane traffic

11.
> A crash on Ipswich Road northbound near Moorooka... Emergency services are on the scene... One lane is blocked with traffic queuing back through Rocklea.
> The Ipswich Motorway is stop-start approaching Oxley.
> The M1 is busy as usual from Loganholme to Slacks Creek
> Heavy across the Captain Cook Bridge and we're seeing delays both ways on the Riverside Expressway.

12.
> Reports of a multi-vehicle smash on the Gateway Motorway northbound near Nudgee Road.... Emergency crews are blocking two lanes ... traffic back through the airport precinct,
> We've got a two vehicle crash on Logan Road southbound at Mount Gravatt near Klumpp Road... the left lane's blocked and traffic is slow right through the shopping precinct.
> The Pacific Motorway remains heavy from Eight Mile Plains to Greenslopes.
> Old Cleveland Road is busy through Carindale and the Gateway's slowing near Belmont.
> That's the latest Brisbane Traffic

13.
> A breakdown in the Clem7 outbound near Woolloongabba is causing delays through the tunnel...and we're seeing flow-on delays onto connecting roads at the moment too
> The Pacific Motorway is slow through Greenslopes.
> The Story Bridge is heavy northbound and Kingsford Smith Drive is also under speed near Eagle Farm.
> The Ipswich Motorway remains heavy through Oxley and Richlands.
> Coronation Drive westbound is slow and the William Jolly Bridge approaches are also congested.
> Sunny and 23 tomorrow and you're up to date on KIIS 97-3

#### Gold Coast (8 reads)

1.
> The M1 is crawling southbound from Helensvale to Molendinar after an earlier smash. There are also delays southbound from Bermuda St to Stewart Rd.
> The Gold Coast Highway's under speed through Miami then the usual delays through Surfers
> Smith St westbound is bumper to bumper from Olsen Ave.. looks like we could have a breakdown
> Slow going on Thomas Drive across Chevron Island
> and Brisbane Rd is heavy westbound through Arundel
> We'll check it again in 15 minutes.... Gold Coast weather sunny and 22... right now 18 degrees.

2.
> We've got a three car smash on the M1 northbound at Highland Park.... traffic's all the way back to Robina.
> Delays also northbound through Coomera and if you're heading up to Brissy.. traffic is crawling as usual from Slacks Creek
> There's an accident on Bermuda St in Burleigh --- it's southbound just before Reedy Creek Rd.
> Hope Island Rd is very heavy both ways through the roadworks at Helensvale
> and the Gold Coast Highway is bumper to bumper through Broadbeach
> Gold Coast weather ... showers and 19 today.. right now it's 15 degrees

3.
> Got reports of a smash at Nerang.... eastbound on Southport Nerang Rd at the intersection of Riverwood Drive
> Northbound on the M1 is a little slow through Coomera and Pimpama but elsewhere is all clear so far.
> No dramas at this stage on the Gold Coast Highway, Brisbane Rd or Hope Island Rd
> Just watch out for a breakdown northbound on Bundall Rd at Surfers just before Thomas Drive
> Gold Coast Weather ... sunny and 27 today, right now it's 23 degrees

4.
> No major dramas so far on Gold Coast roads....
> The M1 is starting to build up northbound between Robina and Nerang..
> Southbound is pretty good... just a little under speed around Coomera and Helensvale.
> Smith St starting to get busy approaching Olsen Ave.
> And all clear on the Gold Coast Highway through Surfers and Broadbeach
> Fine and 25 today.. right now 17 degrees

5.
> We've got a breakdown at Southport on Marine Parade... it's southbound right near Broadwater Parklands.... just minor delays at the moment.
> M1 is slow now from Varsity all the way to Nerang northbound.... Southbound is on the brakes from Coomera to Gaven.
> Bermuda St is heavy approaching Burleigh both ways
> Brisbane Rd eastbound is under speed from Arundel to Labrador
> That's the latest Gold Coast traffic

6.
> Bermuda Street slow through Burleigh Waters, and Reedy Creek Road is building around the school zones.
> There's a breakdown on West Burleigh Road near Stockland --- that's eastbound in the left lane.
> M1 is slow southbound from Helensvale to Molendinar then again around Palm Beach.... while northbound is busy around Gaven
> Gold Coast Highway is crawling through Surfers at the moment... that's hurting Thomas Drive as well.
> Delays on Robina Town Centre Drive as well around the shops.
> Cloudy today, tops of 23.. right now 19 degrees

7.
> A truck smash has closed the Tugun Tunnel in both directions on the M1...... southbound traffic is being diverted at Stewart Rd while northbound traffic is being diverted at the Gold Coast Highway.
> The Highway is a mess as a result with delays from Bilinga all the way back to Palm Beach
> The rest of the M1 is all clear all the way to Brisbane at the moment.
> Smash on Sunshine Boulevard northbound near Pac Fair.
> and slow going Smith St eastbound through Southport
> That's the latest Gold Coast traffic.

8.
> Bermuda Street is slow through Burleigh Waters..... Reedy Creek Road is busy around Varsity....  and Pappas Way is heavy near the school zones.
> We've got a breakdown on West Burleigh Road eastbound near Treetops Plaza.....It's blocking part of the left lane and causing delays back towards the M1.
> Gold Coast Highway is getting busier through Miami and Mermaid...
> The M1 is still patchy southbound through Nerang and Robina... but northbound is fine at the moment
> Hope Island Road is heavy both ways near Santa Barbara Road
> Brisbane Road is still busy through Arundel and Labrador.
> Cloudy today, tops of 23.. right now 19 degrees

#### Adelaide (6 reads)

1.
> Things are already building on the South Eastern Freeway inbound, with traffic slow towards the Tollgate
> Main North Road is busy through Prospect and Blair Athol, while Port Road is on the brakes heading into the city.
> South Rd is pretty congested at the moment.. especially around Torrens Rd.
> Cross Rd is slow both ways around Unley and Fullarton
> That's the latest Adelaide traffic

2.
> The Northern Expressway is moving pretty well at the moment although things are starting to slow down around the Curtis Rd interchange
> The South Eastern is busy as usual heading into the city
> Greenhill Rd is bumper to bumper as well
> and there's a breakdown on King William St near North Terrace
> Cloudy and 16 today.. and you're up to date on KIIS 102-3

3.
> Main North Rd is crawling around Prospect...
> Really slow on Port Wakefield Rd as well for those heading towards the city.
> Heavy on the Salisbury Highway around Waterloo Corner
> and give yourselves extra time on the Gawler with heavy traffic through the northern corridor.
> That's the latest Adelaide Traffic

4.
> We've got a two car smash on Henley Beach Rd in Glenelg blocking one of the outbound lanes.
> The South Eastern Freeway outbound now much through the Tollgate.
> The CBD exits are busy around West Terrace and South Terrace.. .and Cross Rd is stop start in both directions
> Slow going on Port Rd around Woodville and Hindmarsh....
> That's the latest Adelaide traffic

5.
> We've got a two car crash inbound on the South Eastern Freeway just before the Tollgate... that's blocking the left lane
> Cross Road is also slowing through Fullarton and Unley while Greenhill Road is bumper to bumper approaching the city.
> A truck's broken down on South Road northbound near Richmond Road... Traffic is getting through but delays are back towards Daws Rd.
> Port Road is heavy through Hindmarsh and Woodville... while Main North Road is under speed around Prospect.
> A breakdown on the South Eastern Freeway outbound near Stirling is causing a few delays
> That's the latest Adelaide traffic

6.
> Emergency roadworks this morning on Cross Road near Goodwood Road after a burst water main... one lane is shut and traffic is crawling through there.
> The South Eastern is busy through the hills while South Road remains heavy northbound..
> There's a breakdown in the centre lane on Port Road citybound near South Road.
> and heavy traffic on West Terrace approaching North Terrace
> Partly cloudy and 18 today.. and you're up to date on KIIS 102-3

#### Perth (7 reads)

1.
> Breakdown eastbound on the Graham Farmer Freeway approaching the tunnel.
> The Mitchell Freeway southbound's already slow from the Reid Highway through to Sutton St.
> The Kwinana is heavy outbound through Cockburn and Murdoch
> Pretty steady on the Great Eastern Highway at the moment through Belmont and Rivervale
> That's the latest Perth traffic

2.
> Mitchell Freeway northbound is crawling through Osborne Park and Warwick.
> Stirling Highway is busy through Nedlands and Claremont with plenty of traffic heading towards the coast.
> West Coast Highway has moderate delays through City Beach and Scarborough
> The Mounts Bay Road approach remains slow heading out of the city
> That's the latest Perth traffic

3.
> We've got a two car crash on the Mitchell Freeway southbound approaching Cedric Street.
> The right lane's blocked and traffic is backing up through Warwick and Greenwood.
> Hepburn Avenue is also under speed approaching the freeway ramps.
> The Graham Farmer Freeway remains busy heading into the city and Stirling Highway is slow through Nedlands.
> Sunny and 25 today... and that's the latest Perth traffic.

4.
> A truck has broken down on the Kwinana Freeway northbound near Leach Highway... One lane's blocked and traffic is slowing back through Bull Creek and Murdoch.
> South Street is also very heavy through Kardinya.
> The Causeway approaches are busy while Great Eastern Highway is under speed through Rivervale.
> reports of a crash on Tonkin Highway southbound near Morley Drive.... traffic's already banking up through Bayswater...
> The airport approaches are slow and Great Eastern Highway is heavy through Belmont.
> That's the latest Perth traffic

5.
> We've got a breakdown on Stirling Highway southbound near Claremont Quarter... it's partially blocking the left lane so traffic is having to slow down..
> West Coast Highway is also busy through Scarborough.
> The Mitchell remains heavy citybound approaching Vincent Street while Mounts Bay Road is slow heading into the CBD.
> Lord Street remains busy through the eastern corridor and Roe Highway is under speed near Midland Road
> That's the latest traffic on Gold 96FM.

6.
> A three vehicle nose-to-tail is causing delays on Roe Highway westbound near Berkshire Road.
> One lane's blocked and traffic is backing up through Forrestfield.
> Tonkin Highway is also heavy approaching the airport precinct.
> Leach Highway remains under speed near Welshpool while Orrong Road is busy through Kewdale.
> A breakdown on the Mitchell Freeway northbound near Hutton Street is causing delays through Osborne Park.
> Traffic is slowing from Vincent Street through to Cedric Street.
> That's the latest Perth traffic

7.
> Major delays on Roe Highway after a truck rollover eastbound approaching Great Northern Highway..... two lanes are out of action and traffic is queuing back through Hazelmere.
> Tonkin Highway is also under speed.
> The Great Eastern Highway is still busy through Midland while Reid Highway is slowing at the Alexander Drive interchange.
> a crash on Kwinana Freeway southbound near Russell Road.. traffic is backing up through Success and Cockburn Central.
> That's the latest Perth traffic

#### Canberra (5 reads)

1.
> The Parkway northbound is slowing from Cotter Road through to the Glenloch Interchange
> Northbourne Avenue is getting busier through Dickson and Lyneham, while Adelaide Avenue inbound is starting to fill up
> A minor two-car nose-to-tail reported on Belconnen Way near the Coulter Drive intersection... one lane inbound is blocked at the moment
> Slow but moving on the Tuggeranong Parkway.

2.
> Horse Park Drive is heavy heading towards Federal Highway while Flemington Road is slowing through Harrison and Mitchell
> Minor smash on Well Station Drive... no major delays though
> The Monaro Highway is getting busy approaching Hindmarsh Drive...
> Northbourne Ave is now bumper-to-bumper approaching Barry Drive.

3.
> Northbourne Avenue outbound is filling quickly through Civic and Dickson.
> The Tuggeranong Parkway southbound is building up approaching Cotter Road.
> We've had reports of a minor prang on Adelaide Avenue near Yarra Glen... that's blocking the left lane at the moment
> Belconnen Way still busy after the earlier smash near Coulter Drive

4.
> We've got a two-car crash on Parkes Way eastbound near Clunies Ross Street.
> The left lane's blocked and traffic is backing up through the Glenloch Interchange approaches.
> Northbourne Avenue is also heavy through Dickson and Lyneham while Adelaide Avenue remains busy heading into the city.
> The Tuggeranong Parkway is slow but moving southbound.
> That's the latest Canberra traffic.

5.
> A broken-down bus is causing delays on Northbourne Avenue southbound near the intersection with MacArthur Avenue.
> Traffic is getting through but it's a slow run from Dickson.
> The Federal Highway approaches are also busy.
> Horse Park Drive remains under speed heading towards Gungahlin town centre.
> Emergency roadworks on Adelaide Avenue inbound near Yarra Glen... One lane remains closed and traffic is crawling from Cotter Road.
> You're up to date on Mix 106.3

---
## Expanded Ads-Removed Audio Corpus (97 reads)

Sources: KIIS 1011 [MELB-Ads removed].zip + Gold 1017 [SYD-Ads removed].zip (ARN, 3 Jul 2026)
Transcription: faster-whisper small.en, machine-generated (some road names may be misheard)
These are the rawest transcripts — re-listen before trusting disputed place names.


### KIIS 101.1 — Melbourne

**kiis1011-melb-0559-v1 — 05:59 (v1):**
> G'day there, looking over Melbourne on this cloudy, rainy morning. Heading for a top of 17 degrees today, crews are out attending a building fire in Mentone. Lower Dandenong Road closed between the Napeen Highway and Boundary Road, giving a missed head north takes scent to Dandenong Road instead, brake lights forming on the Western Freeway City bound from Leakes Road to Hopkins Road. It's handy first, with KIIS traffic and weather together.

**kiis1011-melb-0559-base — 05:59 (base):**
> G'day there, looking over Melbourne on this cloudy morning, heading for a top of 16 degrees today. Just a few brake lights forming now on the Western Freeway, city bound from Lakes Road to Hopkins Road, level crossing works Newport, Maddox Road close to the rail line, give it a miss and take Millers Road or Melbourne Road, clear about the CBD. It's Andy first, with KIIS traffic and weather together.

**kiis1011-melb-0625-v1 — 06:25 (v1):**
> Good idea, looking over Melbourne on this cloudy, wet morning. Cruiser in, Mentone attending a building fire. Lower Dandenong Road closed between the Dapin Highway and Boundary Road. Giving a miss head north takes scent to Dandenong Road instead. An incident for the Princess Freeway Citybound near Palmers Road, sickening up on the Depark bypass approaching Robinson Road Citybound. The KIIS cast cloudy and rain atop of 17 degrees. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0625-base — 06:25 (base):**
> G'day there, looking over Melbourne on this cloudy morning, heading for a top of 16 degrees today, a burst water main in Footscray. Footscray Road outbound at Moreland Street. The left-hand lane closed, speeds down to 40, with a crash in Deremette, Mount Dermot Road, just near the Deer Park Bypass and Lindbrooke, the South Gipps Lane Highway, near the South Gipps Lane Freeway. The KIIS cars cloudier shower to a top of 16. It's Andy first, with KIIS traffic and weather together.

**kiis1011-melb-0659-v1 — 06:59 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. A crash for the ring ride out toner bound just after the quarter. The right-hand lane closed. Speeds down, of course, and delays back past Pasco Vow Road. That's where a broken-down truck for the Monash City Bannock 2 Act Road. The left-hand lane closed. Speeds are 40. A truck fired Coburg, Bell Street, East Bannock Nicholson Street. Search West Pack F post to get started. Tees and C supply. The KIIS cars cloudy rain atop of 17. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0659-base — 06:59 (base):**
> G'day there, looking over Melbourne on this partly cloudy morning. Flooding, following a burst water main in Footscray. Footscray road outbound at Moreland Street, the left- hand lane closed, and underwater speeds at 40 in the remaining lane. Elsewhere, word of a crash in Lindbrook, the South Gippsland Highway, Citybound before the South Gippsland Freeway, thickening up on the Prince's Citybound from Forsyth Road to the Point Cook Bend. The KIIS cars cloudy, a shower at 2, a top of 16 degrees. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0724-v1 — 07:24 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. Delays and lots of them for the ring-ride Outona banner crash after the Court of Freeway, the right-hand lane closed and delays back to Sydney Road, just for good measure. Where did the crash, Greens, rebound before Keilor Park Drive? That's where it broke into our truck for the Monash City banner 2 rack road in the left lane. The KIIS cast at Cloudy 1 rained about, heading for a top of 17 degrees. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0724-base — 07:24 (base):**
> G'day there looking over Melbourne on this partly cloudy morning. Debris for the Ring Road Greens rebound just after the Deer Park bypass, a lane closed. Speeds at 40, wind of a crash in Bundoora, plenty roads southbound just after McKimmes Road, Waterworks, Doncaster, Doncaster Road, Citybound after Rose Street, the left-hand lane closed. The KIIS cars cloudy, a shower too developing, a top of 16 degrees. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0739-v1 — 07:39 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. It's a shambles on the ring road. A crash green's profound on the EJ Witten Bridge. Two right-hand lanes close. Speeds at 40, somewhat redundant of course. Delays back past Ballarat Road, recovering our tone about approaching the Calder Freeway following an earlier crash and a crash for the Hume Freeway City Ban after Donnybrook Road. The KIIS cars cloudy, rain atop of 17 degrees. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0739-base — 07:39 (base):**
> Today they're looking over Melbourne on this cloudy morning. A crashing Epping. O'Hearns right at Edgar's Road, crews attending multiple lanes closed at the intersection elsewhere flooding following a burst water main in Footscray. Footscrow road outbound at Mall and Street, the left-hand lane closed, speeds down at 40, thickening out for the Princes, city bound from Forsyth Road to the Point Cook Bend. The KIIS cast cloudy a shower to a top of 16. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0749-v1 — 07:49 (v1):**
> Good idea looking over Melbourne. On this cloudy and wet morning, a crash of the Ring Road Greens rebound on the EJ Witten Bridge causing grief. Two right-hand lanes closed, speeds down of course, somewhat redundant traffic crawling well back to the Deer Park Bypass, elsewhere a broken-down truck for the Monash City-Bannock 2 Act Road. Left-hand lane closed, delays back to East Link take the Princes Highway. The KIIS cars cloudy, rain atop of 17. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0749-base — 07:49 (base):**
> G'day there, looking over Melbourne on this cloudy morning. Crews are attending a crash in Epping, O'Hern's right at Edgar's Road, multiple lanes closed at the intersection. Another collision in Narry Warren, Narry Warren-Crammon Road, northbound before Footlight Road, Waterworks Doncaster, Doncaster Road, City Pan after Rose Street, the left-hand lane closed. The KIIS cast cloudy with a shower to developing a top of 16 degrees. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0816-v1 — 08:16 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. A broken-down truck causing nothing but grief for the Monash City, bound at Toorak Road, a lane closed and delays back to East Link, giving a mistake to Princes Highway. Elsewhere a collision for the Ring Road Greens, profound on the EJ Witten Bridge, two lanes closed and delays back to the Deer Park Bypass. It's Andy first, with KIIS traffic and weather together.

**kiis1011-melb-0816-base — 08:16 (base):**
> Good day then. Looking over Melbourne on this cloudy morning. Crews are attending a crash in Narry Warren. Narry Warren Cranburn Road northbound before Footard Road delays back to Golf, Lenx Road, a collision Epping, O'Hearn's Road, Edger's Road, multiple lanes closed at the intersection. The Ring Road recovering Greensborough Bount approaching Ballarat Road following a crash. The KIIS cast cloudy, a shower to developing a top of 16. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0829-base — 08:29 (base):**
> G'day there, looking over Melbourne on this cloudy morning. A breakdown being attended to when the ring rode out toned about at Pasco Vow Road. The left-hand lane closed. Speech down, of course. Delays back to the Hume Freeway. Wait a bit of accident. St Kilda East. Dantherong Road, Citybound before Hotham Street. Waterworks, Doncaster. Doncaster Road, Citybound after Rose Street. The KIIS cast cloudy, a shower to developing a top of 16 degrees. It's Andy first, with KIIS traffic and weather together.

**kiis1011-melb-0850-v1 — 08:50 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. It's a mess on the Monash, a broken-down truck, citybound at Two Rack Road, the left-hand lane closed and traffic crawling back to East Link. Give it a miss, take the Princes Highway instead. The Ring Road recovering Greensborobound, approaching the EJ Witten Bridge following a crash, still full back to the Deer Park bypass. The KIIS cast cloudy rain atop of 17 degrees. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0850-base — 08:50 (base):**
> G'day there, looking over Melbourne on this cloudy morning. Break lights for the ring- right out-toner bound. It's recovering, approaching Pasco Valley Road, following an earlier breakdown. Airswear breakdown for the Monash outbound at Jackson's Road in the emergency lane. A collision, Narry Warren. Narry Warren-Crammon Road northbound before Fulart Road. Patchy for the western city bound around Hopkins Road. The KIIS cars cloudy, a shower or two atop of 16. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0906-v1 — 09:06 (v1):**
> G'day there. Looking over Melbourne on this cloudy, wet morning. The Monash in Recovery Mode City bound from East Link to Two Rack Road following an earlier broken- down truck. All lanes just reopened, but the damage done. Take the Princes Highway. Elsewhere a collision. Berwick Clyde Road southbound at the Princes Highway and a broken-down truck Clayton, the Princes Highway outbound at Clayton Road. The KIIS cars cloudy and rain today atop of 17. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-0906-base — 09:06 (base):**
> G'day there. Looking over Melbourne on this cloudy morning. Flooding. Following a burst water main. Footscray. Footscray right outbound at Moreland Street. The left- hand lane closed. Speed's down of course and just for good measure, no access to Moreland Street either. Take Whitehall Street. Waterworks Doncaster. Doncaster roads City Ban after Rose Street. The KIIS cast cloudy with a shower to developing. A top of 16 degrees on the way. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0934-v1 — 09:34 (v1):**
> G'day there, looking over Melbourne on this cloudy, wet morning. Braked lights remain for the Monash City bound from Wellington Road to Turak Road following an early broken-down truck patchy for the Westgate passing Todd Road into the Burnley, a collision barrack Clyde Road southbound after the Princess Highway, and a broken-down truck Clayton, the Princess Highway outbound at Clayton Road. The KIIS cars cloudy and rain are top of 17 degrees. It's Andy first with KIIS traffic and weather together.

**kiis1011-melb-0934-base — 09:34 (base):**
> G'day there, looking over Melbourne on this cloudy morning, word of a crash in Northcote, St George's Road City bound just after Arthurton Road, elsewhere, flooding, following a burst water main, Footscray, Footscray Road out, down at Moreland Street, the left-hand lane close speeds at 40 with crews on the scene. No access to Moreland Street, take Whitehall Street instead. The KIIS cast cloudy, a shower too, developing a top of 16 degrees. It's Andy Furst with KIIS traffic and weather together.

**kiis1011-melb-1158-v1 — 11:58 (v1):**
> Looking over Melbourne this rainy afternoon, it should be easing 17 the top, right now 14 degrees. In Glen Waverly, a crash on Fern Tree Gully Road westbound at Springvale Road in the middle lane. In Wonturna, a crash on Barania Road eastbound at Wonturna Road. Three lanes are closed, traffic using the right-hand turning lane to get past the northbound left-hand lane on Wonturna Road also closed. It's KIIS first, with KIIS, traffic and weather together.

**kiis1011-melb-1158-base — 11:58 (base):**
> Looking over Melbourne this cloudy afternoon, a shower or two expected. 16 the top, right now it's 14. It's a broken down truck on the Monash outbound at Burke Road. It's in the left-hand emergency lane with speed back to 40. Then further along works on the Monash outbound just after Warriggle Road. This also works on the Tullar Freeway Citybound past the airport in the right-hand lane. It's KIIS first, with KIIS, traffic and weather together.

**kiis1011-melb-1345-v1 — 13:45 (v1):**
> Looking over Melbourne this cloudy afternoon, rain will be easing 17 that's up right now 14 degrees. A crash on the Ring Road altitude about before the West Gate Princes Interchange. Two right-hand lanes are blocked, delays are heavy. Backpass Fitzgerald Road and onto the Deepak Bypass. And in Ringwood, a crash on the Ringwood Bypass at Warrandyte Road. Multiple lanes close through the intersection. It's KIIS first with KIIS, traffic and weather together.

**kiis1011-melb-1345-base — 13:45 (base):**
> Looking over Melbourne, this partly cloudy afternoon, a shower or two expected. 16 the top, right now it's 14. In Cranmer, North, a crash to watch out for on Narry Warren Road, north of Thompson's Road, there are delays in both directions. Works continue in Berwick, Clyde Road is down to one lane in both directions at the Monash Freeway. It's KIIS first, with KIIS, traffic and weather together.

**kiis1011-melb-1440-v1 — 14:40 (v1):**
> Looking over Melbourne this cloudy afternoon, rain is easing, 17 that's up right now, 15 degrees. On the Calder Freeway, a crash in Mount Behoek, Kilo Park Drive in the left-hand lane with speed back to 40 delays back past Green Galley Road. Also, a crash to watch out for in Cranburn on the Princess Highway City ballot in Harry Warren Cranburn Road in the right-hand lane. It's Krizz first, with KIIS, traffic and weather together.

**kiis1011-melb-1440-base — 14:40 (base):**
> Looking over Melbourne this cloudy afternoon, a shower or two expected. 16 that's up right now. 15 degrees. There's works currently underway on the Monash app at Sirac Road. They're in the left-hand lane with speed back to 40. Also works in Berrycon Clyde Road at the Monash Freeway. It's down to one lane in both directions. It's KIIS first with KIIS, traffic and weather together.

**kiis1011-melb-1459-v1 — 14:59 (v1):**
> Hello there, a couple of crashes to watch out for at the moment. One in Parram, Melbourne roads closed. As a result, both ways east of Williams Road. There may be used Turok Road or High Street. A crash in South Yarra, Alexandra Avenue, West Bamber 4 Park Road in the left-hand lane. A crash to a Nariwarren North, Hetherton Road eastbound at Beallgrave, Hallam Road. The KIIS cars, rain easing today, rain increasing tomorrow in 17, right now 15. First with KIIS traffic and weather together.

**kiis1011-melb-1459-base — 14:59 (base):**
> Hello there, it's a pretty good one on our roads at the moment with no major issues as we speak. We're excellent and breakdown free. Slow at the moment on the Hymn though, outbound off the Ring Road and the Ring Road itself is slow. Greensboro bound past, the Deer Park bypass, also slow at the moment on the Monash Freeway outbound from Turek Road to Warrigal Road. The KISS cars to shower to today, showers tomorrow in 17, right now 16, first with KISS traffic and weather together.

**kiis1011-melb-1515-v1 — 15:15 (v1):**
> Looking over Melbourne, rain easing today, but rain increasing tomorrow at the top of 17 expected. Right now it's 15. A couple crashes to watch out for. One in Peran, Malvern Road's closed as a result. Both ways east of Williams Road there. Maybe use Turek Road or High Street instead. And a crash in south Yarra, Alexandra Avenue Westbound before Punt Road in the left-hand lane. It's Steve, first with KIIS traffic and weather together on The Smallsy Show.

**kiis1011-melb-1515-base — 15:15 (base):**
> Looking over Melbourne, a shower or two today. Then more showers tomorrow in a top of 17. Right now it's 16. Gotta break down to watch out for the moment on the Ring Road Greensboro bound ramp to the Deer Park bypass. Left-hand lanes closed as a result. It speeds it down to 40 kms. And keep in mind that speed camera on the King's Way, the intersection of Kingsway and Park Street in South Melbourne. It's Steve Fes with KIIS traffic and weather together on The Sm Aussie Show.

**kiis1011-melb-1540-v1 — 15:40 (v1):**
> We can go for Melbourne. Rain easing today, but rain increasing tomorrow and up to 17 for a max. Right now it's 15. Got a breakdown to watch out for at the moment in Preston, Bell Street, eastbound at St George's Road in the left-hand lane there. And in Roeville, reports of a crash to watch out for on Stud Road, southbound before Wellington Road. It's Steve, first with KIIS traffic and weather together on The Small Z Show.

**kiis1011-melb-1540-base — 15:40 (base):**
> Looking over Melbourne, a shower or two about the place this afternoon, and more showers tomorrow, 17 for your Tuesday. Right now it's 15. No major issues on the roads at the moment. It's slow though on the Hume outbound off the Ring Road, then before Donnybrook Road, an inbound past Cooper Street. And slowing at the moment too, citybound on the Westgate Freeway from the vaulty through the Boonee Tunnel there. It's Steve, first with KIIS traffic and weather together on The Smallsy Show.

**kiis1011-melb-1559-v1 — 15:59 (v1):**
> Looking over Melbourne. Rain easing today, then rain increasing tomorrow and 17 for your Wednesday. Right now it's 14. Got a crash to watch out for on Croydon. Maroondah Highway outbound at Clindar Road. Heavy back to the Ringwood bypass. Keep that in mind. Reports for crash two in Roeville on Stud Road. South Bama for Wellington Road. Got a breakdown to look out for in Preston Bell Street. Eastbound at St George's Road. It's Steve first with KIIS traffic and weather together. Will and Woody driving you home next.

**kiis1011-melb-1559-base — 15:59 (base):**
> Looking over Melbourne, a shower or two for today. Then more showers tomorrow in 17. Right now it's 15. Reports for a couple of crashes at the moment. One in Broadmeadows, Pascovar Road northbound before Barrie Road and also one on Pascovar Road in Glenroy. This one's at Glenroy Road. Slight at the moment too on the Hume outbound off the Ring Road then before Donnybrook Road and inbound past Cooper Street. It's Steve first with KIIS traffic and weather together. Will and Woody driving you home next.

**kiis1011-melb-1624-v1 — 16:24 (v1):**
> Looking over Melbourne. Rain easing today but rain increasing tomorrow in 17. Right now it's 14. A crash in Croydon causing some issues. Maroondah Highway outbound at Clinde Road. It's in the left-hand lane there. It speeds down to 40 kms. Traffic is heavy back to the Ringwood bypass. And a breakdown in Preston, Bell Street, eastbound at St George's Road. It's Steve. First with KIIS traffic and weather together. Will and Woody driving you home.

**kiis1011-melb-1624-base — 16:24 (base):**
> Looking over Melbourne, a shower or two today. Then more showers tomorrow in 17. Right now it's 15. No major issues and no roads at the moment. We are accident and breakdown-free. This is like the moment they're on the Hume, outbound off the Ring Road, past O'Hearns Road, then from Craggyburn Road to Donnybrook Road. Also, it's like the moment on the West Gate Citybound from the Bolty through the Burnley Tunnel. It's the final week to enjoy the Lounge Lovers end of financial year sale. 20-50% off store-wide ends Tuesday. Visit a Lounge Lovers showroom or shop online. Terms apply. It stays first with KIIS traffic and weather together. Will and Woody, driving you home.

**kiis1011-melb-1639-v1 — 16:39 (v1):**
> Looking over Melbourne and rain easing today, but rain increasing tomorrow and 17 expected max. Right now it's 14. A crash in Baronia to look out for the moment. Dorset Road southbound after the mountain highway. One lane is closed as a result. Look out for a breakdown in Preston, Bell Street, eastbound at St George's Road, affecting the left-hand lane there. It's Steve, first with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1639-base — 16:39 (base):**
> Looking over Melbourne, a shower or two for today, then showers easing tomorrow in 17. Right now it's 15 and it's a good run on our roads at the moment for the drive home, no major issues. Slight at the moment though, on the Hume Freeway outbound off the Ring Road past O'Hearns Road, then approaching Donnybrook Road and also slight at the moment on the West Gate City bound from the Bolte through the Burling Tunnel. It's Steve, first with KIIS traffic and weather together, Will and Woody, driving you home.

**kiis1011-melb-1654-v1 — 16:54 (v1):**
> Looking over Melbourne. Rain easing today, but rain increasing tomorrow in 17. Right now it's 14. Some crashes to watch out for at the moment. In Coburg, Moreland Road at Fifth Avenue. On the Ring Road, our tone abound before the quarter freeway. Two left- hand lanes affected there. And in Baronia, Dorset Road southbound after the mountain highway, one lanes closed. It's Steve. First with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1654-base — 16:54 (base):**
> Looking over Melbourne, a shower or two today, then showers easing in 17 tomorrow. Right now it's 15 degrees. Reports for crash at the moment in Hallam, Hallam Road at Centre Road. Delays in both directions there as a result. Apart from that, no other major issues at the moment. It is slow at the moment on the West Gate Freeway, slowing anyway, city bound from the Balti through the Boonley Tunnel there. It's Steve first with KIIS traffic and weather together. Will and Woody, driving you home.

**kiis1011-melb-1724-v1 — 17:24 (v1):**
> Looking over Melbourne, a shower or two today, then more showers tomorrow easing though to top of 17 expected after a low tonight of 12. Right now 15. No major issues on those roads at the moment, which is good news. We are accident and breakdown free. It is slightly over though on the Hume outbound off the Ring Road past O'Hoon's Road, then from Craggyroon Road to Donnybrook Road. It's tea, first with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1724-v2 — 17:24 (v2):**
> Looking over Melbourne, rain easing. Tomorrow, rain increasing and 17 right now, 14. Couple of crashes to watch out for in Coburg, Moreland Road at Fifth Avenue in Narry Warren, Princess Highway, Citybound before Narry Warren Road and in Cranbin West, Thompson's Road, eastbound at Evans Road. Reports of a crash on the Ring Road are toned about after Keilor Park Drive delays their back to the Calder Freeway. It's Steve, first with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1724-base — 17:24 (base):**
> Looking over Melbourne, a shower or two today, then more showers tomorrow, easing though, and a top of 17 expected after a low tonight of 12, right now 15. No major issues on those roads at the moment, which is good news, we are accident and breakdown free. This is like the moment though on the Hume outbound off the Ring Road past O'Hoon's Road, then from Craggyroon Road to Donnybrook Road. It's Steve, first with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1739-v1 — 17:39 (v1):**
> Looking over Melbourne. Rain easing today, but rain increasing tomorrow and 17 for a max. Right now, it's 14. Some crashes to watch out for at the moment. In Hallam, Princess Highway, outbound after Belgrave Hallam Road. In Nariwarran, Princess Highway, city bound before Nariwarran Road. In Lindhurst, Evans Road, southbound after Thompson's Road. And in Brunswick, Glendline Road, west of Ligon Street. It's Steve, first with KIIS traffic and weather together. Will and Woody, driving you home.

**kiis1011-melb-1739-base — 17:39 (base):**
> Looking over Melbourne, a shower or two today, then showers easing tomorrow in 17. Right now it's 15. Got a crash to watch out for at the moment in one turn, a mountain highway westbound before Baronea Road there. It's like the moment on the Hume, as you'd expect at this time of the day, outbound off the Ring Road past O'Hearns Road, then from Craggyburn Road to Donnybrook Road. It's Steve, first with KIIS traffic and weather together, Will and Woody driving you home.

**kiis1011-melb-1753-v1 — 17:53 (v1):**
> Looking over Melbourne, rain easing today and tonight, but rain increasing tomorrow and up to 17. Right now, it's 14. Plenty of crashes to watch out for at the moment. In Hallam on the Princess Highway, outbound after Belgrave Hallam Road. In Nariwaran, Princess Highway, city bound before Nariwaran, North Road. In Lindhurst, Evans Road, southbound after Thompsons Road. And reports for crash on the Call to Freeway outbound after Keilor Park Drive. It's Steve, first with KIIS traffic and weather together.

**kiis1011-melb-1753-base — 17:53 (base):**
> Looking over Melbourne, a shower or two today, then showers easing in 17 tomorrow. Right now, it's 15. Gonna crash to watch out for the moment in one turn at Mountain Highway, westbound before Baronia Road, and reports for crashing Coburg-Belle Street eastbound just before Sydney Road there. Slide the moment to on the Hume, outbound off the Ring Road past O'Hoon's Road, then approaching Donnybrook Road. It's Steve, first with KIIS traffic and weather together.

**kiis1011-melb-1824-v1 — 18:24 (v1):**
> Looking over Melbourne this partly cloudy evening, going down to 11 overnight. Right now it's 14 degrees. A crash in Hellam on the Princess Highway outbound after Belgrave-Hellam Road. It's in the left-hand lane with speed back to 40. Also a crash in Narry Warren on the Princess Highway citybound approaching Narry Warren North Road to left-hand lanes out of action there. It's KIIS first with KIIS, traffic and weather together.

**kiis1011-melb-1824-base — 18:24 (base):**
> Looking over Melbourne this cloudy evening, going down to 12 overnight. Tomorrow showers easing in 17, that's up right now it's 14 degrees. There's a breakdown on the Monash outbound before Burke Road, it's in the left-hand lane. A multi-car collision in 1 turn hour has the mountain highway closed westbound before Baronia Road, you start road in Baronia Road instead. It's KIIS first, with KIIS, traffic and weather together.

**kiis1011-melb-1839-v1 — 18:39 (v1):**
> Looking over Melbourne this partly cloudy evening, going down to 11 overnight. Right now it's 14 degrees. A crash in Hellam on the Princess Highway outbound after Belgrave Hellam Road, the left-hand lane blocked with speed back to 40. Also watch out for a breakdown in the city outbound over the Swan Street Bridge, the left-hand lane out of action there. Get 10% of your purchase back in an AMART gift card when you spend 999 or more. This offer must end today. Stores open until 9pm, so hurry into AMART. Tease and seize apply. It's Christmas with KIIS traffic and weather together.

**kiis1011-melb-1839-base — 18:39 (base):**
> Looking over Melbourne this cloudy evening, going down to 12 overnight, right now it's 15 degrees. Breakdown to watch out for on the Monash, outbound before Burke Road, it's in the left-hand lane with speed set to 40. A multi-car collision in one turn hour has the mountain highway closed westbound approaching Baronia Road, Eustard Road and Baronia Road instead. It's KIIS first, with KIIS, traffic and weather together.


### Gold / WSFM 101.7 — Sydney

**ws1017-syd-0539-v1 — 05:39 (v1):**
> Reevesby, two cars have collided on the River Road above the M5 motorway. At Pimball, there's a broken-down truck on Mona Vale Road. It's northbound, just past Telegraph Road in the left lane. Travel time on the M4 motorway from Margauxa Road to Church Street is about 20 minutes at the moment. The M7 getting from Richmond Road to the M2 in 10 minutes and the M5 from Preston's to the M5 tunnel is a 14-minute trip. That's Sydney's latest traffic.

**ws1017-syd-0539-base — 05:39 (base):**
> This morning we've got a truck and car crash into Ramesby on Milcara Road, that's Eastbound near Mont Street, closing lane 1. Erskine Park now with two cars, Smarsh, Erskine Park Road, corner of Swallow Drive, with delays building all directions. To the M5 at Milcara, watch out for the Citybound breakdown approaching Henry Lawson Drive, the Sydney Harbour Tunnel and Bridge getting a good run in both directions. Dettie on the M4 is building Citybound from Easton Creek towards the Prospect Highway. Trouble time on Lower Road to Church Street will take you now 15 minutes. That's Sydney's latest traffic.

**ws1017-syd-0559-base — 05:59 (base):**
> In reef speed, traffic light repairs underway on the river road above the M5. It follows an earlier crash, but the lights themselves are working. At Pimball, a truck has broken down on Monavale Road, northbound just past Telegraph Road in the left lane. Ludnam break down the northern road southbound just after the M12, and Lightfog in Sydney's south-western suburbs at Picton, Appen and over the Hugh motorway at Douglas Park. That's Sydney's latest traffic.

**ws1017-syd-0614-v1 — 06:14 (v1):**
> Fantastic furniture's end-of-financial-year sale just got eat. Pimple, a broken-down truck on Monavale Road northbound, just past Telegraph Road, over to Rees V. Traffic light repairs underway on the River Road just above the M5 motorway following an earlier crash. Windsor Downs, a reported crash on Richmond Road southbound after St Mary's Road and went out for light fog in Sydney's south-western suburbs at Picton, Appen and over the Hugh Motorway at Douglas Park. The M4 Malgoa road to Church Street taking 27 minutes. That's Sydney's latest traffic.

**ws1017-syd-0614-base — 06:14 (base):**
> Inside the M11 motorway tunnel at Thornley, there's a broken-down truck northbound in the left lane that speeds down to 40kms in the tunnel. Mass got a breakdown on Southern Cross Drive, a southbound approaching Botany Road in the left lane. The M5 motorway at Liverpool has a breakdown westbound at Moorbank Avenue. Travel time citybound along the M5 though, from Preston's to the main tunnel not too bad, currently 22 minutes. Sydney Harbour Bridge Harbour Tunnel also clear both ways, and the M4 building quickly eastbound through the M7 interchange. That's Sydney's latest traffic.

**ws1017-syd-0628-base — 06:28 (base):**
> Botany. There's a broken-down B double truck on Foreshore Road westbound. It's taking up the left-hand lane at Hales Street, building city-bound through the M5 motorway tunnel as well. Fairfield reports for crash on Vine Street near Lawson Street in the Sports Centre, building southbound from North Sydney along the Warringah Freeway toward the Harbour Bridge. The M1 Pacific motorway steady-run central coast to Sydney though. Travel time from the Hawke Spree River Bridge to Warrunga, 13 minutes. That's Sydney's latest traffic.

**ws1017-syd-0728-v1 — 07:28 (v1):**
> On the M1 Pacific Moto at Bar Point, a truck has broken down northbound approaching Jolles Bridge. On the M5 in Riverwood, an earlier three-car smash has cleared Citybound before Belmore Road. Travel time is 36 minutes from Preston's to Kingsgrove, the M4 slowing Citybound from the M7 to Prospect Highway. Travel time from Eastern Creek to Homewish Bay Drive, 28 minutes, and in Windsor Downs, watch out for roadworks on Richmond Road just south of St. Mary's Road, a bit of heavy traffic southbound. That's Sydney's latest traffic.

**ws1017-syd-0728-base — 07:28 (base):**
> A Carlingford, a car's broken down on Carlingford Road, eastbound at Hepburn Avenue in the left lane. Campbelltown, a two-car crash. Campbelltown Road, northbound at Queen Street. A Botany, broken down truck on Foreshore Road is westbound in the left lane at Hales Street. Rosebury, an earlier incident on Gardiners Road, eastbound near Botany Road, still has delays back along Gardiners Road toward Kent Road and the exit from the M8 motorway tunnel. That's Sydney's latest traffic.

**ws1017-syd-0744-v1 — 07:44 (v1):**
> On the M1 Pacific motorway in Barre Point, a truck has broken down northbound approaching Jolls Bridge. On the M5 in Riverwood, an earlier three-car crash has now cleared Citybound before Bellmoor Road with travel time 36 minutes from Preston's into Kingsgrove and the M4 motorway slowing Citybound from the M7 over to Prospect Highway. Travel time Eastern Creek to Homewish Bay Drive is roughly 28 minutes. That's Sydney's latest traffic.

**ws1017-syd-0744-base — 07:44 (base):**
> Botany. A truck's broken down westbound on Forshaw Road. It's in the left-hand lane at Hales Street, picking up pace now through Rosebury on Gardiners Road eastbound after an earlier incident near Botany Road. There are queues at Masse, got a long airport drive at the International Airport Terminal entry. At Rosell, still a slow run onto the Anzac Bridge from City West Link at the M4 Tunnel and the M4 Travel Time Eastern Creek to Homewish Bay Drive now 26 minutes at Sydney's latest traffic.

**ws1017-syd-0814-base — 08:14 (base):**
> At Ermington, there's been a two-car crash on Silverwater Road southbound before Holkiss Street. Heavy delays now back past Victoria Road. Botany are broken down truckers on Foreshore Road westbound in the left lane at Hales Street. Padstow Heights reports of a crash on Davies Road northbound at Prosser Avenue. The M4 motorway is slowing now eastbound from the M7 to the Prospect Highway and again at Silverwater Road. Travel time from Eastern Creek to Homebush Bay Drive is currently 21 minutes. That's Sydney's latest traffic.

**ws1017-syd-0828-v1 — 08:28 (v1):**
> The M5 motorway now easing citybound after an earlier crash was cleared just before Belmore Road. Travel time is 28 minutes from Preston's into Kingsgrove. It's also quite slow on the M5 alternative of Newbridge Road Citybound through Milpera. M2 congested eastbound from Deli Road to the Lane Cove Tunnel. Sydney Harbour Bridge looking quite steady in both directions. And the M4 slowing citybound from the M7 to Prospect Highway. Travel time is 24 minutes from Eastern Creek to Homebush Bay Drive. That's Sydney's latest traffic.

**ws1017-syd-0828-base — 08:28 (base):**
> At Ermington, a two-car crash on Silverwater Road southbound before Holkers Street, slow back toward Victoria Road. Inside the M11 motorway north connects Tunnel at Pennett Hills, broken down track northbound in the left-hand lane with speed reduced through the tunnel. Botany broken down track is on Foreshore Road westbound in the left lane at Hales Street. The M5 motorway still slows Citybound Casula to Moorbank and the M4 slowing Citybound past the Prospect Highway. Travel time for Homebush Bay Drive is currently 21 minutes. That's Sydney's latest traffic.

**ws1017-syd-0844-v1 — 08:44 (v1):**
> On the Yemon Pacific motorway in Asquith, police operation is closing two of three southbound lanes just past Keringai Chase Road. Anzac Bridge slowing city-bound and the western distributor congested past Darling Harbour and toward the King Street exit after an earlier breakdown in the CBD. The M5 easing city-bound after an earlier crash cleared before Belmore Road. Travel time 28 minutes from Preston's into Kingsgrove. That's Sydney's latest traffic.

**ws1017-syd-0844-base — 08:44 (base):**
> Atson Ives. There's been a truck and car crash on Motivale Road at Woodbury Road. Two of the three northbound lanes are closed and heavy delays along Motivale Road. The M2 motorway at Bulcombe Hills. There's a broken-down bus. It's westbound at Windsor Road but it's blocking the right-hand lane and the M2 queuing back now toward Pienaet Hills Road. Plus, at Ermington a two-car crash still holding up Silver Water Road southbound before Holkers Street. Some heavy delays back to Victoria Road. That's Sydney's latest traffic.

**ws1017-syd-0858-base — 08:58 (base):**
> At St Ives, a truck and car crash on Motobale Road, northbound at Woodbury Road, left lane remains closed and heavy delays. On the M2 motorway, a broken-down bus is westbound at Windsor Road, the two right-hand lanes are blocked and heavy delays back toward Penard Hills Road. Inside the M11 north connects tunnel at Penard Hills, a broken-down truck northbound with speed reduced in the tunnel, and Lucas Heights, a broken-down truck on Heathcote Road, eastbound approaching New Illawarra Road. That's Sydney's latest traffic.

**ws1017-syd-0958-v1 — 09:58 (v1):**
> In Midgenbury, a crash on Wargrove Road, Lane 1 northbounders block just past the M4 on-ramps. Leftover delays at Asquith after an earlier police operation on the M1 Pacific motorway, but all lanes are open now near Coringay Chase Road. There's a broken-down bus at Mount Collar on the Pacific Highway at Excelsior Road, and it banks down the trucks broken down on Stacey Street. It's blocking a northbound lane at Myrtle Road. That's Sydney's latest traffic.

**ws1017-syd-0958-base — 09:58 (base):**
> A truck's broken down on the M4 motorway at Grey Stains. It's westbound at Eta Long Road. In Liverpool, emergency roadwork underway. Elizabeth's drive at McLean Street that's affecting eastbounders. We've got delays coming through Jamoine and Gladesville after a crash at Huntley's Point, westbound on Victoria Road at Huntley's Point Road. In Cowan, an earlier breakdown has been cleared from the M1 Pacific motorway. Northbound traffic getting back on track past the Pacific Highway overpass. That's Sydney's latest traffic.

**ws1017-syd-1058-base — 10:58 (base):**
> It's been a crash at Blacktown involving a motorbike on Richmond Road, eastbound at Kent Street. In Waterloo, a cyclist and a car have collided on South Dowlings Street, northbound at O'Day Avenue. There's a breakdown on the Sydney Harbour Bridge, that's northbound, and at North Taramura, crews are dealing with a fallen tree. Bobbinhead Road affected at Lure Crescent both ways. Emergency roadwork at Liverpool, Elizabeth Drive at McLean Street, that's eastbound. That's Sydney's latest traffic.

**ws1017-syd-1158-base — 11:58 (base):**
> A two-car crash at Borkham Hills, Windsor Road, northbound at Marinda Road. Emergency crews have taken out the right northbound lane. In Mentionbury, we had an oil spill on the M4 that's been tidied up now, and eastbound is getting back on track now past Roper Road. In the Blue Mountains at Mount Victoria, the Great Western Highway remains closed both ways. The diversions via Bell through Shiffley Road and Darling Causeway add at least 30 minutes to the trip. That's Sydney's latest traffic.

**ws1017-syd-1358-base — 13:58 (base):**
> Mount Georgia Crash, Woodstock Avenue, Carlisle Avenue. Northmeader breakdown for Southbound. Windsor Road approaching James, Roost Drive and Hursville. Crash, Croydon Road at Queen's Road. Summer Hill wires are down, affecting Lane 3 Westbound. Pameter Road between Sloane Street and Liverpool Road. Castle Hill, lights are out. Old Northern Road, Coors Road. Roselands are broken down semi-Southbound. Lane 1, King George's Road approaching Canary's Road. That's Sydney's latest traffic.

**ws1017-syd-1459-v1 — 14:59 (v1):**
> M5 motorway through Hammondville. A multi-car crash where two or three westbound lanes are blocked off on the approach to Heathcote Road. Chatswood, a van's broken down westbound Fullitts Road just passing Pacific Highway. M2 motorway, Balkham Hills, a truck crash eastbound that's in the median strip just as you pass Windsor Road. To the Hume Highway, Kessula multi-car crash northbound as you pass Old Carajong Road. Trucks broken down in the Sydney Harbour Tunnel northbound near the exit into Milsons Point with a left-hand lane shut. That's Sydney's latest traffic.

**ws1017-syd-1459-base — 14:59 (base):**
> Banks Meadow. There's a truck and car collision on Foreshore Road. Blocking an eastbound lane approaching Botany Road. Delays southbound down General Holmes Drive through Brighton Lissands after a truck breakdown is cleared just from before Bay Street. Liverpool Newbridge Road passing Speed Street 3. Car crash with an eastbound lane shut. In the North Connex Tunnel through Warrunga at the Offramp to Pennant Hills Road northbound a truck breakdown. Traffic light repairs in Castle Hill. Old Northern Road occurs Road. A northbound lane being blocked off here. And the umphil motorway is filling quickly westbound at Church Street. That's Sydney's latest traffic.

**ws1017-syd-1524-base — 15:24 (base):**
> The M5 motorway crawling westbound from Fairford Road towards Heathcote Road. There's been a multi-car crash in Hammondville. Couple of lanes are blocked off around the area and a city-bound lane as well. Another multi-car crash just clearing from Kasulah-Hume Highway passing Karajong Road northbound. Into Balkam Hills, a sideline truck crash eastbound for the M2 motorway past Windsor Road. Delays north in the Sydney Harbour Tunnel towards the exit of Milsons Point. A truck breakdown has moved off. A van's broken down Chatswood fullers Road westbound past Pacific Highway. That's city's latest traffic.

**ws1017-syd-1540-base — 15:40 (base):**
> Heavy conditions outbound for the M4 motorway from Church Street towards a Prospect Highway after a multi-car crash has cleared from Hunting Wood. We've eased up to eastbound, entering the Lane Cove tunnel following an earlier incident. Expect some very heavy delays outbound for General Holmes Drive. The airport tunneling mascot right into Brighton-less Sands and earlier truck breakdowns moved off. Looking to Iran in Castle Hill, traffic light repairs Old Northern Road at Kurz Road. Crews are on scene closing off the left at Northbound Lane. That's Sydney's latest traffic.

**ws1017-syd-1559-v1 — 15:59 (v1):**
> Homebush West, a multi-car crash. Centenary Drive, southbound off-ramp towards Austin Avenue. Roselle, the M4 motorway tunnel-a-bus breakdown. It's westbound in the breakdown lane just as you come off the Anzac Bridge, a 40k speed drop. An absolute shocker for the M5 right now. A multi-car crash westbound through Hammondville towards Heathcote Road. Delays are extensive back-passing King George's Road and city-bound pressure coming up from the Hugh motorway with a lane also blocked off. Weatherill Park, the Horsley Drive, a breakdown near Ferris Road. That's Sydney's latest traffic.

**ws1017-syd-1559-base — 15:59 (base):**
> Heavy outbound pressure for General Holmes Drive heading towards the M5 tunnel in Mascot. We've had a car that's broken down and still expect delays continuing south along General Holmes Drive to Brightless Sands onto the Grand Parade after an earlier truck breakdown here. Blacktown Bungaraby Road closed off eastbound following a car and pedestrian incident at Patrick Street. Crews are on scene. North Connect's tunnel northbound off ramp to Pennant Hills Road are trucks broken down and traffic lights are being repaired in Castle Hill. Old northern road at Currs Road, the left northbound lane shut. That's Sydney's latest traffic.

**ws1017-syd-1615-base — 16:15 (base):**
> All westbound lanes have reopened on the M5 motorway through Hammondville approaching Heathcote Road following a multi-car crash. Still expect extensive delays back past King George's Road. Your run time's been sitting at about one hour exiting the main tunnel through Kingsgrove into Preston's and there's plenty of citybound pressure to approaching Homebush West, a multi-car crash southbound Centenary Drive off-ramp to Austin Avenue. Delays in Weatherill Park after a breakdown is now cleared from the Halsey Drive westbound approaching Ferris Road. That's Sydney's latest traffic.

**ws1017-syd-1624-base — 16:24 (base):**
> Botany, a bus and bicycle crash for Botany Road closing a southbound lane at Lorde Street. All lanes are back open on the M5 motorway westbound through Hammondville after a multi-car crash has cleared off around Heathcote Road. Still expect some very heavy congestion with a travel time of about 50 minutes, Kingsgrove into Preston's. The M4 motorway tunnel under speed westbound from Anzac Bridge heading out towards the exit after an earlier breakdown in Roselle. We've had a multi-car crash in Homebush West southbound Centenary Drive at the off-ramp to Marlborough Road. That's Sydney's latest traffic.

**ws1017-syd-1640-v1 — 16:40 (v1):**
> The M5 motorway is still quite a struggle westbound past King George's Road out towards Morbank following a multi-car crash. A bit of a knock-on effect as you move southbound towards Raby down the Hugh Motorway 2. Now on the Hugh Motorway, Campbelltown northbound a truck breakdown in the shoulder approaching Norelland Road. Botany, a bicycle and bus have collided. Botany Road at Lord Street southbound. Ingleside, Monavale Road, a crash, two cars at Tumburra Street. And emergency works in Punchbowl Westbound, Canterbury Road near Bonds Road. That's Sydney's latest traffic.

**ws1017-syd-1640-base — 16:40 (base):**
> There's some pretty heavy traffic on the M5 motorway. City bound through Kossula after a multi-car crash has cleared off, leaving quite a backlog of traffic down the Hugh Motorway through Bardia. Balkham Hills, a two-car crash northbound for Windsor Road at Seven Hills Road. In French is Forest, a car and motorcycle collision closing a southbound lane where Koss Parkway, French is Forest Road. In Blacktown, a car and pedestrian incident. Bungaraby Road eastbound Patrick Street. And for motorway, it's a bit of a hot spot. As well this afternoon, westbound from the tunnel exit through to the M7. That's Sydney's latest traffic.

**ws1017-syd-1659-base — 16:59 (base):**
> The M5 motorway is still very heavy westbound from King George's Road to Moorbank following a multi-car crash at Heathcote Road earlier. Now we've got delays northbound on the Hugh motorway with a truck that's broken down through Campbelltown towards Norelland Road. Crews have now shut off the left-hand lane into Botany. All lanes back open. Botany Road at Lord Street after a bus and motorcycle collided southbound. Ingalls Side Motivale Road, Tumburra Street, two-car crash westbound. M4 motorway tunnel, very under speed this afternoon westbound from Waddell Street right out the M4 towards Prospect. That's Sydney's latest traffic.

**ws1017-syd-1715-v1 — 17:15 (v1):**
> The M4 motorway tunnel, it is still a massive struggle from Waddell Street heading all the way out the motorway towards the M7 this afternoon. Expect some pretty heavy traffic too for the M2 motorway, we're crawling basically from Lane Cove Road right up to the M7 at Sunnyholt Road. Bump and bumper for much of the M5 westbound moving towards Heathcote Road from that earlier multi-car crash. That's Sydney's latest traffic.

**ws1017-syd-1715-base — 17:15 (base):**
> Annandale, the M4 M8 tunnel link a breakdown for your southbound run. A car and motorcycle collision through French's Forest and Waikers Parkways shot a southbound lane at French's Forest Road. Still looking pretty busy around the southwest, following a multi-car crash citybound on the M5 in Kasula. Stacks of traffic approaching the motorway for the Hume Highway through Kasula and Liverpool. And we're bump to bumper outbound for the M4 motorway from the tunnel exit right through to the M7. That stretch is going to take you just under 40 minutes. That's city's latest traffic.

**ws1017-syd-1723-v1 — 17:23 (v1):**
> Hurstville, King George's Road passing Waniura Road, a car breakdown northbound. Ingleside, Motivale Road passing Powderworks Road westbound, a car breakdown here. A crash in Punchbowl, two cars involved, Punchbowl Road near Viola Street. Looking at Uran, outbound for the M5 motorway from Kingsgrove into Preston's 45 minutes. The M4 motorway tunnel exit westbound, taking 40 minutes to get to the M7, and we're still pretty stacked through the tunnel westbound coming in from the Anzac Bridge. That's City's latest traffic.

**ws1017-syd-1723-base — 17:23 (base):**
> M8 motorway tunnels southbound just coming in from the Anzac Bridge off-ramp. We've got a car that's broken down a couple of lanes blocked off in Annandale. Parramatta, Great Western Highway, Pitt Street, a set of traffic lights flashing. All lanes back open through Bulgham Hills. Windsor Road northbound at Seven Hills Road after a two- car crash. Coming into the area for the M2 motorway, quite a slow stretch westbound into Bulgham Hills from the Epping Tunnel. And we're busy northbound up the Hugh motorway, getting onto the M5 city bound in Casula following that earlier multi. That's Sydney's latest traffic.

**ws1017-syd-1740-v1 — 17:40 (v1):**
> M5 motorway off-ramp to Moorbank Avenue westbound. A crash here that's also impacting your southbound run for Moorbank Avenue and one-lane shut on the M5. M8 motorway tunnel delays southbound passing the M4 link after a breakdown through Annandale. Lights on the flash in Parramatta, Great Western Highway at Pitt Street all directions. Travel on the M4 motorway is still looking quite heavy westbound from the tunnel exit, getting out towards the M7 motorway 35 minutes. The M7 also underpays westbound towards Sunnyhalt Road. That's Sydney's latest traffic.

**ws1017-syd-1740-v2 — 17:40 (v2):**
> Emergency utility works in Punchbowl along Canterbury Road, the left westbound lane shut near Bonds Road. A collision in Punchbowl for Punchbowl Road around Viola Street, two cars involved. Delays for the southwest outbound on the M5 motorway coming to Heathcote Road after that earlier multi-car crash. Still pretty chock-a-block for Milpera Road and Newbridge Road and roads approaching the motorway like Heathcote Road and Henry Lawson Drive. Meanwhile, the M4 motorway tunnel still struggling with a particularly slow stretch out past Homebridge Bay Drive. That's Sydney's latest traffic.

**ws1017-syd-1740-base — 17:40 (base):**
> M5 motorway off-ramp to Moorbank Avenue westbound. A crash here that's also impacting your southbound run for Moorbank Avenue and one-lane shut on the M5. M8 motorway tunnel delays southbound passing the M4 link after a breakdown through Annandale. Lights on the flash in Parramatta, Great Western Highway at Pitt Street all directions. Travel on the M4 motorway is still looking quite heavy westbound from the tunnel exit getting out towards the M7 motorway 35 minutes. The M7 also underpace westbound towards Sunnyholt Road. That's Sydney's latest traffic.

**ws1017-syd-1759-base — 17:59 (base):**
> Heavy traffic through Greenacre on Roberts Road. A two-car crash where only one northbound lane's getting past Pelman Avenue. There's emergency utility works in Punch Bowl, Canterbury Road near Bonds Road. Westbound lane one's been out of action. Still looking really slow for the M4 motorway tunnel westbound Waddell Street out to the exit and quite a bunch up on the motorway itself between Church Street and Prospect Highway each direction. M5 motorway also struggling outbound towards Heathcote Road. That's city's latest traffic.

**ws1017-syd-1815-v1 — 18:15 (v1):**
> Mamrie Road in Kemps Creek, a struggle each direction following a three-car crash that's between Abbots Road and Curves Road. Green Acre A2 Car collision northbound Roberts Road near Pelman Avenue. Grey Stains, a crash westbound on the Great Western Highway just as you pass Grey Stains Road. There's some emergency works around Punch Bowl, Canterbury Road, shunning the left westbound lane near Bonds Road, checking around on the M4 motorway now, travelling out from Church Street to Moggoa Road. It's easing up to 24 minutes, still looking heavy though at Homewish Bay Drive. That's Sydney's latest traffic.

**ws1017-syd-1815-base — 18:15 (base):**
> Kellyville Ridge, Windsor Road at Maryville Road. A three-car crash. A northbound lane is blocked off. Southbound is also being impacted. Three-car crash. Bonnie Rigg Heights on Elizabeth Drive, Westbound near Aplin Road. Lights are playing up in Parramatta, Great Western Highway at Pitt Street. There's been a crash. The M5 westbound off-ramp through to Morbank Avenue. Expect a lane blocked on the M5 motorway and some intimate enclosures for a clean-up recovery. Traffic on the M4 is under pace around Grey Stains Road each way. That's Sydney's latest traffic.

**ws1017-syd-1824-base — 18:24 (base):**
> In Seven Hills, we've got a car crash that's brought wires down. Seven Hills Road is now blocked off both directions, just as you pass Abbott Road. Bonnie Rig Heights, Elizabeth Drive, near Applin Road, a three-car crash westbound, another three-car collision in Kellyville Ridge on Windsor Road at Maryville Road, then right northbound lanes blocked off in southbound traffic also being impacted, lights are back up and running again through Parramatta, Great Western Highway at Pitt Street, and the M4 motorway, a little slow westbound through to Silverwater Road. That's Sydney's latest traffic.

**ws1017-syd-1840-v1 — 18:40 (v1):**
> M4 motorway tunnel, a little slow westbound after a crash has cleared around the Concord Road exit. In Berkshire Park, the northern road near Carrington Road, two-car collision southbound. Another two-car crash in Yaguna, Rookwood Road, southbound just as you pass William Home Street. Green Acre, a crash on Roberts Road, northbound near Pelman Avenue. There's been another accident through Greystain slowing up westbounders on the Great Western Highway just as you pass Greystain's Road. M5 motorway still under speed outbound King George's Road to Moorbank. That's Sydney's latest traffic.

**ws1017-syd-1840-base — 18:40 (base):**
> In Seven Hills, a crash has brought down wires and closed off Seven Hills Road, just off the Abbott Road Prospect Highway intersection. In Bonnie Rigg Heights, a collision westbound. Three cars involved. Elizabeth Drive near Applin Road. Another three-car crash in Kellyville Ridge, Windsor Road at Maryville Road. Expect congestion each direction. Travel on the M5 motorway, still sitting just a little over 20 minutes westbound. Kingsgrove into Preston's and checking a run for the M4 motorway westbound from Church Street towards Malgoa Road. It's about the same 21. That's Sydney's latest traffic.
