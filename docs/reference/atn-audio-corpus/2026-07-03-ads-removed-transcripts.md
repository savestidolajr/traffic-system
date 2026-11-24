---
title: "ATN Expanded Ads-Removed Audio Corpus — 2026-07-03"
description: "Machine transcripts and manifest pointer for 97 ARN-supplied ads-removed ATN airchecks: KIIS 101.1 Melbourne and Gold/WSFM 101.7 Sydney."
tags: [atn, corpus, audio, transcription, style, samples]
aliases: [expanded ATN corpus, ads removed ATN audio, KIIS 101.1 corpus, WSFM 101.7 corpus]
purpose: "reference"
status: "current"
last_verified: 2026-07-03
review_after: 2026-09-30
volatility: "low"
source_of_truth: "ARN-supplied Downloads zips: KIIS 1011 [MELB-Ads removed].zip and Gold 1017 [SYD-Ads removed].zip, downloaded 2026-07-03"
verified_by:
  - "zip inventory + SHA-256 manifest generation, 2026-07-03"
  - "faster-whisper small.en transcription, 2026-07-03"
common_queries:
  - "what is in the expanded ATN audio corpus"
  - "what do ads-removed KIIS Melbourne ATN reads say"
  - "what do ads-removed WSFM Sydney ATN reads say"
see_also:
  - docs/reference/atn-bulletin-style-samples.md
  - docs/reference/atn-audio-corpus/2026-07-03-ads-removed-manifest.csv
---

# ATN Expanded Ads-Removed Audio Corpus — 2026-07-03

ARN supplied two expanded ads-removed ATN aircheck archives on 2026-07-03:

- `KIIS 1011 [MELB-Ads removed].zip` — KIIS 101.1 Melbourne, 52 MP3 files across 26 clock-time groups.
- `Gold 1017 [SYD-Ads removed].zip` — Gold/WSFM 101.7 Sydney, 45 MP3 files across 29 clock-time groups.

Raw audio is kept locally under `docs/reference/atn-audio-corpus/_local-raw/2026-07-03-ads-removed/` and is intentionally gitignored to avoid adding ~47 MB of binary MP3s to repo history. The tracked corpus layer is this transcript doc plus the SHA-256 manifest at `2026-07-03-ads-removed-manifest.csv`.

Transcripts are machine-generated with `faster-whisper small.en` and should be treated as draft text for style analysis. Re-listen to the raw MP3 before relying on disputed road names, presenter names, or station tags.

## Summary

| Station | Market | Files | Clock groups | Duration range | Word-count range | Mean pace |
|---|---|---:|---:|---|---|---:|
| KIIS 101.1 | Melbourne | 52 | 26 | 19.0–32.0s | 66–117 | 3.68 w/s |
| Gold / WSFM 101.7 | Sydney | 45 | 29 | 19.2–29.1s | 69–104 | 3.33 w/s |

## KIIS 101.1 — Melbourne

### kiis1011-melb-0559-v1 — 05:59 (v1)

- Source file: `kiis1011_0559 (1).mp3`
- Duration: 20.31s; words: 73; pace: 3.59 w/s
- SHA-256: `86ec380dfa0255c3966d3d1d775bd4f4b18ef5b54149612b84ef3ec3079fce98`

> G'day there, looking over Melbourne on this cloudy, rainy morning. Heading for a top
> of 17 degrees today, crews are out attending a building fire in Mentone. Lower
> Dandenong Road closed between the Napeen Highway and Boundary Road, giving a missed
> head north takes scent to Dandenong Road instead, brake lights forming on the Western
> Freeway City bound from Leakes Road to Hopkins Road. It's handy first, with KIIS
> traffic and weather together.

### kiis1011-melb-0559-base — 05:59 (base)

- Source file: `kiis1011_0559.mp3`
- Duration: 19.28s; words: 71; pace: 3.68 w/s
- SHA-256: `4de81d9c22454b461cf5026d3450eef9f08959f47536be12aa17e241a748cc41`

> G'day there, looking over Melbourne on this cloudy morning, heading for a top of 16
> degrees today. Just a few brake lights forming now on the Western Freeway, city bound
> from Lakes Road to Hopkins Road, level crossing works Newport, Maddox Road close to
> the rail line, give it a miss and take Millers Road or Melbourne Road, clear about the
> CBD. It's Andy first, with KIIS traffic and weather together.

### kiis1011-melb-0625-v1 — 06:25 (v1)

- Source file: `kiis1011_0625 (1).mp3`
- Duration: 22.10s; words: 78; pace: 3.53 w/s
- SHA-256: `c9de2d7576a7fce16c6745b763d04a0d1285a73680eeda7ece69a2cddffa7035`

> Good idea, looking over Melbourne on this cloudy, wet morning. Cruiser in, Mentone
> attending a building fire. Lower Dandenong Road closed between the Dapin Highway and
> Boundary Road. Giving a miss head north takes scent to Dandenong Road instead. An
> incident for the Princess Freeway Citybound near Palmers Road, sickening up on the
> Depark bypass approaching Robinson Road Citybound. The KIIS cast cloudy and rain atop
> of 17 degrees. It's Andy first with KIIS traffic and weather together.

### kiis1011-melb-0625-base — 06:25 (base)

- Source file: `kiis1011_0625.mp3`
- Duration: 22.51s; words: 83; pace: 3.69 w/s
- SHA-256: `8116dbf99358e6fcd23d9062d7ec1444d532b44d6caab2bb75ac2ed8a300ee28`

> G'day there, looking over Melbourne on this cloudy morning, heading for a top of 16
> degrees today, a burst water main in Footscray. Footscray Road outbound at Moreland
> Street. The left-hand lane closed, speeds down to 40, with a crash in Deremette, Mount
> Dermot Road, just near the Deer Park Bypass and Lindbrooke, the South Gipps Lane
> Highway, near the South Gipps Lane Freeway. The KIIS cars cloudier shower to a top of
> 16. It's Andy first, with KIIS traffic and weather together.

### kiis1011-melb-0659-v1 — 06:59 (v1)

- Source file: `kiis1011_0659 (1).mp3`
- Duration: 23.51s; words: 97; pace: 4.13 w/s
- SHA-256: `934f35e49e86a4a6e4f4e7d28a8e6f9421ebff9bc3218abcc82e9d91837dd371`

> G'day there, looking over Melbourne on this cloudy, wet morning. A crash for the ring
> ride out toner bound just after the quarter. The right-hand lane closed. Speeds down,
> of course, and delays back past Pasco Vow Road. That's where a broken-down truck for
> the Monash City Bannock 2 Act Road. The left-hand lane closed. Speeds are 40. A truck
> fired Coburg, Bell Street, East Bannock Nicholson Street. Search West Pack F post to
> get started. Tees and C supply. The KIIS cars cloudy rain atop of 17. It's Andy Furst
> with KIIS traffic and weather together.

### kiis1011-melb-0659-base — 06:59 (base)

- Source file: `kiis1011_0659.mp3`
- Duration: 22.74s; words: 90; pace: 3.96 w/s
- SHA-256: `bb80cb28af76583ffab4200994966db8760dfff2b258ae553ba5dd4c83fc0b37`

> G'day there, looking over Melbourne on this partly cloudy morning. Flooding, following
> a burst water main in Footscray. Footscray road outbound at Moreland Street, the left-
> hand lane closed, and underwater speeds at 40 in the remaining lane. Elsewhere, word
> of a crash in Lindbrook, the South Gippsland Highway, Citybound before the South
> Gippsland Freeway, thickening up on the Prince's Citybound from Forsyth Road to the
> Point Cook Bend. The KIIS cars cloudy, a shower at 2, a top of 16 degrees. It's Andy
> Furst with KIIS traffic and weather together.

### kiis1011-melb-0724-v1 — 07:24 (v1)

- Source file: `kiis1011_0724 (1).mp3`
- Duration: 22.27s; words: 93; pace: 4.18 w/s
- SHA-256: `f0bde3a65a6f1cb71bf55d332d4ea94f6af79c831cb47d6b39dc913a50e829ff`

> G'day there, looking over Melbourne on this cloudy, wet morning. Delays and lots of
> them for the ring-ride Outona banner crash after the Court of Freeway, the right-hand
> lane closed and delays back to Sydney Road, just for good measure. Where did the
> crash, Greens, rebound before Keilor Park Drive? That's where it broke into our truck
> for the Monash City banner 2 rack road in the left lane. The KIIS cast at Cloudy 1
> rained about, heading for a top of 17 degrees. It's Andy Furst with KIIS traffic and
> weather together.

### kiis1011-melb-0724-base — 07:24 (base)

- Source file: `kiis1011_0724.mp3`
- Duration: 21.94s; words: 76; pace: 3.46 w/s
- SHA-256: `2775389a63a9947e86d7c2b7866b4c80c105d1dbef7f281c6dc0507c193db90d`

> G'day there looking over Melbourne on this partly cloudy morning. Debris for the Ring
> Road Greens rebound just after the Deer Park bypass, a lane closed. Speeds at 40, wind
> of a crash in Bundoora, plenty roads southbound just after McKimmes Road, Waterworks,
> Doncaster, Doncaster Road, Citybound after Rose Street, the left-hand lane closed. The
> KIIS cars cloudy, a shower too developing, a top of 16 degrees. It's Andy first with
> KIIS traffic and weather together.

### kiis1011-melb-0739-v1 — 07:39 (v1)

- Source file: `kiis1011_0739 (1).mp3`
- Duration: 23.16s; words: 84; pace: 3.63 w/s
- SHA-256: `0fa84a408cfe1d4995dc9a42fbf8b2f33ca60fc9a67a61e2f1bd9116b79e9ee4`

> G'day there, looking over Melbourne on this cloudy, wet morning. It's a shambles on
> the ring road. A crash green's profound on the EJ Witten Bridge. Two right-hand lanes
> close. Speeds at 40, somewhat redundant of course. Delays back past Ballarat Road,
> recovering our tone about approaching the Calder Freeway following an earlier crash
> and a crash for the Hume Freeway City Ban after Donnybrook Road. The KIIS cars cloudy,
> rain atop of 17 degrees. It's Andy first with KIIS traffic and weather together.

### kiis1011-melb-0739-base — 07:39 (base)

- Source file: `kiis1011_0739.mp3`
- Duration: 22.68s; words: 84; pace: 3.70 w/s
- SHA-256: `8c4198cb3330528bb2768a376673cb1a4cb768996d7217564382e9c5ef106364`

> Today they're looking over Melbourne on this cloudy morning. A crashing Epping.
> O'Hearns right at Edgar's Road, crews attending multiple lanes closed at the
> intersection elsewhere flooding following a burst water main in Footscray. Footscrow
> road outbound at Mall and Street, the left-hand lane closed, speeds down at 40,
> thickening out for the Princes, city bound from Forsyth Road to the Point Cook Bend.
> The KIIS cast cloudy a shower to a top of 16. It's Andy Furst with KIIS traffic and
> weather together.

### kiis1011-melb-0749-v1 — 07:49 (v1)

- Source file: `kiis1011_0749 (1).mp3`
- Duration: 21.49s; words: 85; pace: 3.96 w/s
- SHA-256: `701460353d7e8e9a9590ab25e0fa96c8c0799e24789c2128baadbb41c489fa79`

> Good idea looking over Melbourne. On this cloudy and wet morning, a crash of the Ring
> Road Greens rebound on the EJ Witten Bridge causing grief. Two right-hand lanes
> closed, speeds down of course, somewhat redundant traffic crawling well back to the
> Deer Park Bypass, elsewhere a broken-down truck for the Monash City-Bannock 2 Act
> Road. Left-hand lane closed, delays back to East Link take the Princes Highway. The
> KIIS cars cloudy, rain atop of 17. It's Andy Furst with KIIS traffic and weather
> together.

### kiis1011-melb-0749-base — 07:49 (base)

- Source file: `kiis1011_0749.mp3`
- Duration: 21.78s; words: 76; pace: 3.49 w/s
- SHA-256: `0cd2a3b136309f75e5eb3b33b1f0ebc9ef7f1a06be50d1930bcda6a687826140`

> G'day there, looking over Melbourne on this cloudy morning. Crews are attending a
> crash in Epping, O'Hern's right at Edgar's Road, multiple lanes closed at the
> intersection. Another collision in Narry Warren, Narry Warren-Crammon Road, northbound
> before Footlight Road, Waterworks Doncaster, Doncaster Road, City Pan after Rose
> Street, the left-hand lane closed. The KIIS cast cloudy with a shower to developing a
> top of 16 degrees. It's Andy Furst with KIIS traffic and weather together.

### kiis1011-melb-0816-v1 — 08:16 (v1)

- Source file: `kiis1011_0816 (1).mp3`
- Duration: 19.04s; words: 74; pace: 3.89 w/s
- SHA-256: `e6b761ec227ba55ce57e77a5e2ef63f4ba3d5db6ddd96f3d566dfb098877aee8`

> G'day there, looking over Melbourne on this cloudy, wet morning. A broken-down truck
> causing nothing but grief for the Monash City, bound at Toorak Road, a lane closed and
> delays back to East Link, giving a mistake to Princes Highway. Elsewhere a collision
> for the Ring Road Greens, profound on the EJ Witten Bridge, two lanes closed and
> delays back to the Deer Park Bypass. It's Andy first, with KIIS traffic and weather
> together.

### kiis1011-melb-0816-base — 08:16 (base)

- Source file: `kiis1011_0816.mp3`
- Duration: 22.60s; words: 79; pace: 3.50 w/s
- SHA-256: `5df451d9aae93ada8bbf27ffff190620ed367ffd7fa0740c5eb3f639e35fc23c`

> Good day then. Looking over Melbourne on this cloudy morning. Crews are attending a
> crash in Narry Warren. Narry Warren Cranburn Road northbound before Footard Road
> delays back to Golf, Lenx Road, a collision Epping, O'Hearn's Road, Edger's Road,
> multiple lanes closed at the intersection. The Ring Road recovering Greensborough
> Bount approaching Ballarat Road following a crash. The KIIS cast cloudy, a shower to
> developing a top of 16. It's Andy Furst with KIIS traffic and weather together.

### kiis1011-melb-0829-base — 08:29 (base)

- Source file: `kiis1011_0829.mp3`
- Duration: 22.86s; words: 83; pace: 3.63 w/s
- SHA-256: `6f3b3c39bdbcc6a79f4ed3cf38f774e11cdfbdee5e930b065e240d758ccea9bb`

> G'day there, looking over Melbourne on this cloudy morning. A breakdown being attended
> to when the ring rode out toned about at Pasco Vow Road. The left-hand lane closed.
> Speech down, of course. Delays back to the Hume Freeway. Wait a bit of accident. St
> Kilda East. Dantherong Road, Citybound before Hotham Street. Waterworks, Doncaster.
> Doncaster Road, Citybound after Rose Street. The KIIS cast cloudy, a shower to
> developing a top of 16 degrees. It's Andy first, with KIIS traffic and weather
> together.

### kiis1011-melb-0850-v1 — 08:50 (v1)

- Source file: `kiis1011_0850 (1).mp3`
- Duration: 22.45s; words: 83; pace: 3.70 w/s
- SHA-256: `83016c15709f077c4024cf18df555d2f5df4d8b1c27431491bbf9cbe98177044`

> G'day there, looking over Melbourne on this cloudy, wet morning. It's a mess on the
> Monash, a broken-down truck, citybound at Two Rack Road, the left-hand lane closed and
> traffic crawling back to East Link. Give it a miss, take the Princes Highway instead.
> The Ring Road recovering Greensborobound, approaching the EJ Witten Bridge following a
> crash, still full back to the Deer Park bypass. The KIIS cast cloudy rain atop of 17
> degrees. It's Andy first with KIIS traffic and weather together.

### kiis1011-melb-0850-base — 08:50 (base)

- Source file: `kiis1011_0850.mp3`
- Duration: 22.02s; words: 79; pace: 3.59 w/s
- SHA-256: `aa7c75fecac82925db83e12eaf82d1c51b20615e03e1b94c0850aab184c3df1d`

> G'day there, looking over Melbourne on this cloudy morning. Break lights for the ring-
> right out-toner bound. It's recovering, approaching Pasco Valley Road, following an
> earlier breakdown. Airswear breakdown for the Monash outbound at Jackson's Road in the
> emergency lane. A collision, Narry Warren. Narry Warren-Crammon Road northbound before
> Fulart Road. Patchy for the western city bound around Hopkins Road. The KIIS cars
> cloudy, a shower or two atop of 16. It's Andy Furst with KIIS traffic and weather
> together.

### kiis1011-melb-0906-v1 — 09:06 (v1)

- Source file: `kiis1011_0906 (1).mp3`
- Duration: 21.88s; words: 83; pace: 3.79 w/s
- SHA-256: `767e8623faf0baf83e92ac2d6d11f39b674aaec3e1f2b4df656668fcbb14ba0d`

> G'day there. Looking over Melbourne on this cloudy, wet morning. The Monash in
> Recovery Mode City bound from East Link to Two Rack Road following an earlier broken-
> down truck. All lanes just reopened, but the damage done. Take the Princes Highway.
> Elsewhere a collision. Berwick Clyde Road southbound at the Princes Highway and a
> broken-down truck Clayton, the Princes Highway outbound at Clayton Road. The KIIS cars
> cloudy and rain today atop of 17. It's Andy Furst with KIIS traffic and weather
> together.

### kiis1011-melb-0906-base — 09:06 (base)

- Source file: `kiis1011_0906.mp3`
- Duration: 21.99s; words: 79; pace: 3.59 w/s
- SHA-256: `20e36f98d7c0f4b9b7d1fa3207666382e3b6c14b609c328c576f8ba653366be0`

> G'day there. Looking over Melbourne on this cloudy morning. Flooding. Following a
> burst water main. Footscray. Footscray right outbound at Moreland Street. The left-
> hand lane closed. Speed's down of course and just for good measure, no access to
> Moreland Street either. Take Whitehall Street. Waterworks Doncaster. Doncaster roads
> City Ban after Rose Street. The KIIS cast cloudy with a shower to developing. A top of
> 16 degrees on the way. It's Andy first with KIIS traffic and weather together.

### kiis1011-melb-0934-v1 — 09:34 (v1)

- Source file: `kiis1011_0934 (1).mp3`
- Duration: 22.75s; words: 81; pace: 3.56 w/s
- SHA-256: `7e1b12f6782dd53b3a35e67c6ef8d08d847cf7985316550791652b59976c5b16`

> G'day there, looking over Melbourne on this cloudy, wet morning. Braked lights remain
> for the Monash City bound from Wellington Road to Turak Road following an early
> broken-down truck patchy for the Westgate passing Todd Road into the Burnley, a
> collision barrack Clyde Road southbound after the Princess Highway, and a broken-down
> truck Clayton, the Princess Highway outbound at Clayton Road. The KIIS cars cloudy and
> rain are top of 17 degrees. It's Andy first with KIIS traffic and weather together.

### kiis1011-melb-0934-base — 09:34 (base)

- Source file: `kiis1011_0934.mp3`
- Duration: 22.41s; words: 82; pace: 3.66 w/s
- SHA-256: `919de7dfbb2c524c6470ccb584b03849248e1bb7368219598ea345976ed316d6`

> G'day there, looking over Melbourne on this cloudy morning, word of a crash in
> Northcote, St George's Road City bound just after Arthurton Road, elsewhere, flooding,
> following a burst water main, Footscray, Footscray Road out, down at Moreland Street,
> the left-hand lane close speeds at 40 with crews on the scene. No access to Moreland
> Street, take Whitehall Street instead. The KIIS cast cloudy, a shower too, developing
> a top of 16 degrees. It's Andy Furst with KIIS traffic and weather together.

### kiis1011-melb-1158-v1 — 11:58 (v1)

- Source file: `kiis1011_1158 (1).mp3`
- Duration: 21.89s; words: 77; pace: 3.52 w/s
- SHA-256: `a8c1c3f861a74026f2079d5cb738dab8249a7fa803da7727ac711588e73d3752`

> Looking over Melbourne this rainy afternoon, it should be easing 17 the top, right now
> 14 degrees. In Glen Waverly, a crash on Fern Tree Gully Road westbound at Springvale
> Road in the middle lane. In Wonturna, a crash on Barania Road eastbound at Wonturna
> Road. Three lanes are closed, traffic using the right-hand turning lane to get past
> the northbound left-hand lane on Wonturna Road also closed. It's KIIS first, with
> KIIS, traffic and weather together.

### kiis1011-melb-1158-base — 11:58 (base)

- Source file: `kiis1011_1158.mp3`
- Duration: 23.04s; words: 77; pace: 3.34 w/s
- SHA-256: `07288718b37ba49a938d9d7146be62716b6b720b6af318802220ee543d90dcc3`

> Looking over Melbourne this cloudy afternoon, a shower or two expected. 16 the top,
> right now it's 14. It's a broken down truck on the Monash outbound at Burke Road. It's
> in the left-hand emergency lane with speed back to 40. Then further along works on the
> Monash outbound just after Warriggle Road. This also works on the Tullar Freeway
> Citybound past the airport in the right-hand lane. It's KIIS first, with KIIS, traffic
> and weather together.

### kiis1011-melb-1345-v1 — 13:45 (v1)

- Source file: `kiis1011_1345 (1).mp3`
- Duration: 22.03s; words: 74; pace: 3.36 w/s
- SHA-256: `c105d83fe3af39c1f4d2d3fbe60d077235dc03d2a49d8db72fea3395be5f44d4`

> Looking over Melbourne this cloudy afternoon, rain will be easing 17 that's up right
> now 14 degrees. A crash on the Ring Road altitude about before the West Gate Princes
> Interchange. Two right-hand lanes are blocked, delays are heavy. Backpass Fitzgerald
> Road and onto the Deepak Bypass. And in Ringwood, a crash on the Ringwood Bypass at
> Warrandyte Road. Multiple lanes close through the intersection. It's KIIS first with
> KIIS, traffic and weather together.

### kiis1011-melb-1345-base — 13:45 (base)

- Source file: `kiis1011_1345.mp3`
- Duration: 20.95s; words: 69; pace: 3.29 w/s
- SHA-256: `96ec8629da0f0fe79d9e1123a98412432a56a55daf5ea18ea084a7f94653a675`

> Looking over Melbourne, this partly cloudy afternoon, a shower or two expected. 16 the
> top, right now it's 14. In Cranmer, North, a crash to watch out for on Narry Warren
> Road, north of Thompson's Road, there are delays in both directions. Works continue in
> Berwick, Clyde Road is down to one lane in both directions at the Monash Freeway. It's
> KIIS first, with KIIS, traffic and weather together.

### kiis1011-melb-1440-v1 — 14:40 (v1)

- Source file: `kiis1011_1440 (1).mp3`
- Duration: 20.95s; words: 76; pace: 3.63 w/s
- SHA-256: `1ea739d5fc5fc6d091b831e14e7571827e973b6bdf56bf6f097a6f7d2a45aeba`

> Looking over Melbourne this cloudy afternoon, rain is easing, 17 that's up right now,
> 15 degrees. On the Calder Freeway, a crash in Mount Behoek, Kilo Park Drive in the
> left-hand lane with speed back to 40 delays back past Green Galley Road. Also, a crash
> to watch out for in Cranburn on the Princess Highway City ballot in Harry Warren
> Cranburn Road in the right-hand lane. It's Krizz first, with KIIS, traffic and weather
> together.

### kiis1011-melb-1440-base — 14:40 (base)

- Source file: `kiis1011_1440.mp3`
- Duration: 19.40s; words: 66; pace: 3.40 w/s
- SHA-256: `f1cd9b1053c04292418eb2e9e4a510f8e89e510504c8713f7e2ca623f74a378a`

> Looking over Melbourne this cloudy afternoon, a shower or two expected. 16 that's up
> right now. 15 degrees. There's works currently underway on the Monash app at Sirac
> Road. They're in the left-hand lane with speed back to 40. Also works in Berrycon
> Clyde Road at the Monash Freeway. It's down to one lane in both directions. It's KIIS
> first with KIIS, traffic and weather together.

### kiis1011-melb-1459-v1 — 14:59 (v1)

- Source file: `kiis1011_1459 (1).mp3`
- Duration: 21.55s; words: 87; pace: 4.04 w/s
- SHA-256: `50da3e621565da7a91a9f696e0dce8ec71479c19542b72b7b00379145fb005c9`

> Hello there, a couple of crashes to watch out for at the moment. One in Parram,
> Melbourne roads closed. As a result, both ways east of Williams Road. There may be
> used Turok Road or High Street. A crash in South Yarra, Alexandra Avenue, West Bamber
> 4 Park Road in the left-hand lane. A crash to a Nariwarren North, Hetherton Road
> eastbound at Beallgrave, Hallam Road. The KIIS cars, rain easing today, rain
> increasing tomorrow in 17, right now 15. First with KIIS traffic and weather together.

### kiis1011-melb-1459-base — 14:59 (base)

- Source file: `kiis1011_1459.mp3`
- Duration: 22.21s; words: 89; pace: 4.01 w/s
- SHA-256: `4ce53305e96085acd54eff147ffcf8d03ae787a8bbb3e5b4baeb58343466b7df`

> Hello there, it's a pretty good one on our roads at the moment with no major issues as
> we speak. We're excellent and breakdown free. Slow at the moment on the Hymn though,
> outbound off the Ring Road and the Ring Road itself is slow. Greensboro bound past,
> the Deer Park bypass, also slow at the moment on the Monash Freeway outbound from
> Turek Road to Warrigal Road. The KISS cars to shower to today, showers tomorrow in 17,
> right now 16, first with KISS traffic and weather together.

### kiis1011-melb-1515-v1 — 15:15 (v1)

- Source file: `kiis1011_1515 (1).mp3`
- Duration: 23.11s; words: 80; pace: 3.46 w/s
- SHA-256: `07fc68029bda7136cf9ac16264b2ad821ef3084f0592e2714791e43e46e652a7`

> Looking over Melbourne, rain easing today, but rain increasing tomorrow at the top of
> 17 expected. Right now it's 15. A couple crashes to watch out for. One in Peran,
> Malvern Road's closed as a result. Both ways east of Williams Road there. Maybe use
> Turek Road or High Street instead. And a crash in south Yarra, Alexandra Avenue
> Westbound before Punt Road in the left-hand lane. It's Steve, first with KIIS traffic
> and weather together on The Smallsy Show.

### kiis1011-melb-1515-base — 15:15 (base)

- Source file: `kiis1011_1515.mp3`
- Duration: 21.40s; words: 90; pace: 4.21 w/s
- SHA-256: `567c63b6408f43a2cff2a5f75c87cc9f77a0ae85c4ecb4ffcd8d440626577f84`

> Looking over Melbourne, a shower or two today. Then more showers tomorrow in a top of
> 17. Right now it's 16. Gotta break down to watch out for the moment on the Ring Road
> Greensboro bound ramp to the Deer Park bypass. Left-hand lanes closed as a result. It
> speeds it down to 40 kms. And keep in mind that speed camera on the King's Way, the
> intersection of Kingsway and Park Street in South Melbourne. It's Steve Fes with KIIS
> traffic and weather together on The Sm Aussie Show.

### kiis1011-melb-1540-v1 — 15:40 (v1)

- Source file: `kiis1011_1540 (1).mp3`
- Duration: 20.60s; words: 79; pace: 3.83 w/s
- SHA-256: `8049f0f994a48c5a84e9b4747ae6d7e48930e536b1c05698c4141f39d8958910`

> We can go for Melbourne. Rain easing today, but rain increasing tomorrow and up to 17
> for a max. Right now it's 15. Got a breakdown to watch out for at the moment in
> Preston, Bell Street, eastbound at St George's Road in the left-hand lane there. And
> in Roeville, reports of a crash to watch out for on Stud Road, southbound before
> Wellington Road. It's Steve, first with KIIS traffic and weather together on The Small
> Z Show.

### kiis1011-melb-1540-base — 15:40 (base)

- Source file: `kiis1011_1540.mp3`
- Duration: 22.18s; words: 85; pace: 3.83 w/s
- SHA-256: `b111ee268049ba063eb0294511273b39746c9c616fd865f92251f309e60cabda`

> Looking over Melbourne, a shower or two about the place this afternoon, and more
> showers tomorrow, 17 for your Tuesday. Right now it's 15. No major issues on the roads
> at the moment. It's slow though on the Hume outbound off the Ring Road, then before
> Donnybrook Road, an inbound past Cooper Street. And slowing at the moment too,
> citybound on the Westgate Freeway from the vaulty through the Boonee Tunnel there.
> It's Steve, first with KIIS traffic and weather together on The Smallsy Show.

### kiis1011-melb-1559-v1 — 15:59 (v1)

- Source file: `kiis1011_1559 (1).mp3`
- Duration: 23.27s; words: 90; pace: 3.87 w/s
- SHA-256: `5ecdd824669f6deeae181f14b2510f916c090df687d80f2cd4e1e6daffcdd880`

> Looking over Melbourne. Rain easing today, then rain increasing tomorrow and 17 for
> your Wednesday. Right now it's 14. Got a crash to watch out for on Croydon. Maroondah
> Highway outbound at Clindar Road. Heavy back to the Ringwood bypass. Keep that in
> mind. Reports for crash two in Roeville on Stud Road. South Bama for Wellington Road.
> Got a breakdown to look out for in Preston Bell Street. Eastbound at St George's Road.
> It's Steve first with KIIS traffic and weather together. Will and Woody driving you
> home next.

### kiis1011-melb-1559-base — 15:59 (base)

- Source file: `kiis1011_1559.mp3`
- Duration: 22.57s; words: 88; pace: 3.90 w/s
- SHA-256: `d71837848770efc5b7f01810671510f09938207ed0aa94cdb84b64ffdf43f175`

> Looking over Melbourne, a shower or two for today. Then more showers tomorrow in 17.
> Right now it's 15. Reports for a couple of crashes at the moment. One in Broadmeadows,
> Pascovar Road northbound before Barrie Road and also one on Pascovar Road in Glenroy.
> This one's at Glenroy Road. Slight at the moment too on the Hume outbound off the Ring
> Road then before Donnybrook Road and inbound past Cooper Street. It's Steve first with
> KIIS traffic and weather together. Will and Woody driving you home next.

### kiis1011-melb-1624-v1 — 16:24 (v1)

- Source file: `kiis1011_1624 (1).mp3`
- Duration: 21.91s; words: 76; pace: 3.47 w/s
- SHA-256: `7189152449a223ad1a1841ad35f415a2bc86bfb078b8e92ed1289abe70b68ff5`

> Looking over Melbourne. Rain easing today but rain increasing tomorrow in 17. Right
> now it's 14. A crash in Croydon causing some issues. Maroondah Highway outbound at
> Clinde Road. It's in the left-hand lane there. It speeds down to 40 kms. Traffic is
> heavy back to the Ringwood bypass. And a breakdown in Preston, Bell Street, eastbound
> at St George's Road. It's Steve. First with KIIS traffic and weather together. Will
> and Woody driving you home.

### kiis1011-melb-1624-base — 16:24 (base)

- Source file: `kiis1011_1624.mp3`
- Duration: 32.02s; words: 117; pace: 3.65 w/s
- SHA-256: `c28b5ae67dc582d42bbb732af9781626add135307fe8ebac31a069d90b260fe4`

> Looking over Melbourne, a shower or two today. Then more showers tomorrow in 17. Right
> now it's 15. No major issues and no roads at the moment. We are accident and
> breakdown-free. This is like the moment they're on the Hume, outbound off the Ring
> Road, past O'Hearns Road, then from Craggyburn Road to Donnybrook Road. Also, it's
> like the moment on the West Gate Citybound from the Bolty through the Burnley Tunnel.
> It's the final week to enjoy the Lounge Lovers end of financial year sale. 20-50% off
> store-wide ends Tuesday. Visit a Lounge Lovers showroom or shop online. Terms apply.
> It stays first with KIIS traffic and weather together. Will and Woody, driving you
> home.

### kiis1011-melb-1639-v1 — 16:39 (v1)

- Source file: `kiis1011_1639 (1).mp3`
- Duration: 21.80s; words: 77; pace: 3.53 w/s
- SHA-256: `17b19660e82e8fbcf59e119e2f69019bfe86a003f9f184eb1a0757ec37ea97d2`

> Looking over Melbourne and rain easing today, but rain increasing tomorrow and 17
> expected max. Right now it's 14. A crash in Baronia to look out for the moment. Dorset
> Road southbound after the mountain highway. One lane is closed as a result. Look out
> for a breakdown in Preston, Bell Street, eastbound at St George's Road, affecting the
> left-hand lane there. It's Steve, first with KIIS traffic and weather together, Will
> and Woody driving you home.

### kiis1011-melb-1639-base — 16:39 (base)

- Source file: `kiis1011_1639.mp3`
- Duration: 21.40s; words: 92; pace: 4.30 w/s
- SHA-256: `b26caa5bf8afe5c633dc717054a0d27d846208a7d3f3fb84fb56f8b48f4dd744`

> Looking over Melbourne, a shower or two for today, then showers easing tomorrow in 17.
> Right now it's 15 and it's a good run on our roads at the moment for the drive home,
> no major issues. Slight at the moment though, on the Hume Freeway outbound off the
> Ring Road past O'Hearns Road, then approaching Donnybrook Road and also slight at the
> moment on the West Gate City bound from the Bolte through the Burling Tunnel. It's
> Steve, first with KIIS traffic and weather together, Will and Woody, driving you home.

### kiis1011-melb-1654-v1 — 16:54 (v1)

- Source file: `kiis1011_1654 (1).mp3`
- Duration: 21.72s; words: 76; pace: 3.50 w/s
- SHA-256: `29053ec11ddcf2420a57d65a0ebcea62ee358f34db58513ae78b65cdc406c8ff`

> Looking over Melbourne. Rain easing today, but rain increasing tomorrow in 17. Right
> now it's 14. Some crashes to watch out for at the moment. In Coburg, Moreland Road at
> Fifth Avenue. On the Ring Road, our tone abound before the quarter freeway. Two left-
> hand lanes affected there. And in Baronia, Dorset Road southbound after the mountain
> highway, one lanes closed. It's Steve. First with KIIS traffic and weather together,
> Will and Woody driving you home.

### kiis1011-melb-1654-base — 16:54 (base)

- Source file: `kiis1011_1654.mp3`
- Duration: 21.79s; words: 88; pace: 4.04 w/s
- SHA-256: `e6ee9440355cb48c46b68ebaba81e569d16044bed552d302a4b39154be1ac6ca`

> Looking over Melbourne, a shower or two today, then showers easing in 17 tomorrow.
> Right now it's 15 degrees. Reports for crash at the moment in Hallam, Hallam Road at
> Centre Road. Delays in both directions there as a result. Apart from that, no other
> major issues at the moment. It is slow at the moment on the West Gate Freeway, slowing
> anyway, city bound from the Balti through the Boonley Tunnel there. It's Steve first
> with KIIS traffic and weather together. Will and Woody, driving you home.

### kiis1011-melb-1724-v1 — 17:24 (v1)

- Source file: `kiis1011_1724 (1).mp3`
- Duration: 22.13s; words: 86; pace: 3.89 w/s
- SHA-256: `4c432bdaf947100da309a27a8c13a6df965823bc8b222bce80f2788567ae383d`

> Looking over Melbourne, a shower or two today, then more showers tomorrow easing
> though to top of 17 expected after a low tonight of 12. Right now 15. No major issues
> on those roads at the moment, which is good news. We are accident and breakdown free.
> It is slightly over though on the Hume outbound off the Ring Road past O'Hoon's Road,
> then from Craggyroon Road to Donnybrook Road. It's tea, first with KIIS traffic and
> weather together, Will and Woody driving you home.

### kiis1011-melb-1724-v2 — 17:24 (v2)

- Source file: `kiis1011_1724 (2).mp3`
- Duration: 23.11s; words: 84; pace: 3.63 w/s
- SHA-256: `e5d093aa7f072ed3468c45ee9f635fc72af5615404d79d093ed4bb4dc4716218`

> Looking over Melbourne, rain easing. Tomorrow, rain increasing and 17 right now, 14.
> Couple of crashes to watch out for in Coburg, Moreland Road at Fifth Avenue in Narry
> Warren, Princess Highway, Citybound before Narry Warren Road and in Cranbin West,
> Thompson's Road, eastbound at Evans Road. Reports of a crash on the Ring Road are
> toned about after Keilor Park Drive delays their back to the Calder Freeway. It's
> Steve, first with KIIS traffic and weather together, Will and Woody driving you home.

### kiis1011-melb-1724-base — 17:24 (base)

- Source file: `kiis1011_1724.mp3`
- Duration: 21.96s; words: 88; pace: 4.01 w/s
- SHA-256: `68f4071873ad5677b72b95582487ce5f26e17551e7ccde47cc2802ca9b339365`

> Looking over Melbourne, a shower or two today, then more showers tomorrow, easing
> though, and a top of 17 expected after a low tonight of 12, right now 15. No major
> issues on those roads at the moment, which is good news, we are accident and breakdown
> free. This is like the moment though on the Hume outbound off the Ring Road past
> O'Hoon's Road, then from Craggyroon Road to Donnybrook Road. It's Steve, first with
> KIIS traffic and weather together, Will and Woody driving you home.

### kiis1011-melb-1739-v1 — 17:39 (v1)

- Source file: `kiis1011_1739 (1).mp3`
- Duration: 22.89s; words: 78; pace: 3.41 w/s
- SHA-256: `d8cbb2112e654f3a2f812a85ce082e2c315e08851729f150b2838eac9bebf1a9`

> Looking over Melbourne. Rain easing today, but rain increasing tomorrow and 17 for a
> max. Right now, it's 14. Some crashes to watch out for at the moment. In Hallam,
> Princess Highway, outbound after Belgrave Hallam Road. In Nariwarran, Princess
> Highway, city bound before Nariwarran Road. In Lindhurst, Evans Road, southbound after
> Thompson's Road. And in Brunswick, Glendline Road, west of Ligon Street. It's Steve,
> first with KIIS traffic and weather together. Will and Woody, driving you home.

### kiis1011-melb-1739-base — 17:39 (base)

- Source file: `kiis1011_1739.mp3`
- Duration: 23.20s; words: 85; pace: 3.66 w/s
- SHA-256: `2cd03afc9eec941de97cf83819f84207a58e1731121fb4bbae90a5f238293930`

> Looking over Melbourne, a shower or two today, then showers easing tomorrow in 17.
> Right now it's 15. Got a crash to watch out for at the moment in one turn, a mountain
> highway westbound before Baronea Road there. It's like the moment on the Hume, as
> you'd expect at this time of the day, outbound off the Ring Road past O'Hearns Road,
> then from Craggyburn Road to Donnybrook Road. It's Steve, first with KIIS traffic and
> weather together, Will and Woody driving you home.

### kiis1011-melb-1753-v1 — 17:53 (v1)

- Source file: `kiis1011_1753 (1).mp3`
- Duration: 22.22s; words: 82; pace: 3.69 w/s
- SHA-256: `5ec58ca57ad4ae761f5265eab3a342ca1a63ebb91150e10a95654caaa5517371`

> Looking over Melbourne, rain easing today and tonight, but rain increasing tomorrow
> and up to 17. Right now, it's 14. Plenty of crashes to watch out for at the moment. In
> Hallam on the Princess Highway, outbound after Belgrave Hallam Road. In Nariwaran,
> Princess Highway, city bound before Nariwaran, North Road. In Lindhurst, Evans Road,
> southbound after Thompsons Road. And reports for crash on the Call to Freeway outbound
> after Keilor Park Drive. It's Steve, first with KIIS traffic and weather together.

### kiis1011-melb-1753-base — 17:53 (base)

- Source file: `kiis1011_1753.mp3`
- Duration: 21.45s; words: 77; pace: 3.59 w/s
- SHA-256: `3561a0c4bd801b8ca992aff345aadfe620c19da864c1f392f8b762f6d32238f8`

> Looking over Melbourne, a shower or two today, then showers easing in 17 tomorrow.
> Right now, it's 15. Gonna crash to watch out for the moment in one turn at Mountain
> Highway, westbound before Baronia Road, and reports for crashing Coburg-Belle Street
> eastbound just before Sydney Road there. Slide the moment to on the Hume, outbound off
> the Ring Road past O'Hoon's Road, then approaching Donnybrook Road. It's Steve, first
> with KIIS traffic and weather together.

### kiis1011-melb-1824-v1 — 18:24 (v1)

- Source file: `kiis1011_1824 (1).mp3`
- Duration: 21.26s; words: 71; pace: 3.34 w/s
- SHA-256: `32ce8d3e07c2ff279bfc4a3619f8c8764e7326a9316569507cd3fda15e0f19d9`

> Looking over Melbourne this partly cloudy evening, going down to 11 overnight. Right
> now it's 14 degrees. A crash in Hellam on the Princess Highway outbound after
> Belgrave-Hellam Road. It's in the left-hand lane with speed back to 40. Also a crash
> in Narry Warren on the Princess Highway citybound approaching Narry Warren North Road
> to left-hand lanes out of action there. It's KIIS first with KIIS, traffic and weather
> together.

### kiis1011-melb-1824-base — 18:24 (base)

- Source file: `kiis1011_1824.mp3`
- Duration: 21.19s; words: 70; pace: 3.30 w/s
- SHA-256: `fe4f80c68554a0e3e44cfc93f128e687adfdf441ebf7cdb1d170d03aeb4c88c5`

> Looking over Melbourne this cloudy evening, going down to 12 overnight. Tomorrow
> showers easing in 17, that's up right now it's 14 degrees. There's a breakdown on the
> Monash outbound before Burke Road, it's in the left-hand lane. A multi-car collision
> in 1 turn hour has the mountain highway closed westbound before Baronia Road, you
> start road in Baronia Road instead. It's KIIS first, with KIIS, traffic and weather
> together.

### kiis1011-melb-1839-v1 — 18:39 (v1)

- Source file: `kiis1011_1839 (1).mp3`
- Duration: 32.02s; words: 103; pace: 3.22 w/s
- SHA-256: `bc7e3b9dbb44034744ff30509dda9fd425328c2f66639a3cc6d5d07567f54d46`

> Looking over Melbourne this partly cloudy evening, going down to 11 overnight. Right
> now it's 14 degrees. A crash in Hellam on the Princess Highway outbound after Belgrave
> Hellam Road, the left-hand lane blocked with speed back to 40. Also watch out for a
> breakdown in the city outbound over the Swan Street Bridge, the left-hand lane out of
> action there. Get 10% of your purchase back in an AMART gift card when you spend 999
> or more. This offer must end today. Stores open until 9pm, so hurry into AMART. Tease
> and seize apply. It's Christmas with KIIS traffic and weather together.

### kiis1011-melb-1839-base — 18:39 (base)

- Source file: `kiis1011_1839.mp3`
- Duration: 20.81s; words: 69; pace: 3.32 w/s
- SHA-256: `782b6eed19db72da0b14a13aa2784d37733fb4db2f5761015dd1107d60cffa33`

> Looking over Melbourne this cloudy evening, going down to 12 overnight, right now it's
> 15 degrees. Breakdown to watch out for on the Monash, outbound before Burke Road, it's
> in the left-hand lane with speed set to 40. A multi-car collision in one turn hour has
> the mountain highway closed westbound approaching Baronia Road, Eustard Road and
> Baronia Road instead. It's KIIS first, with KIIS, traffic and weather together.

## Gold / WSFM 101.7 — Sydney

### ws1017-syd-0539-v1 — 05:39 (v1)

- Source file: `ws1017_0539 (1).mp3`
- Duration: 24.98s; words: 81; pace: 3.24 w/s
- SHA-256: `04d6b3e02be5567cf43ab964b26ae43d937fea4e52972f6bed3af409720f69ea`

> Reevesby, two cars have collided on the River Road above the M5 motorway. At Pimball,
> there's a broken-down truck on Mona Vale Road. It's northbound, just past Telegraph
> Road in the left lane. Travel time on the M4 motorway from Margauxa Road to Church
> Street is about 20 minutes at the moment. The M7 getting from Richmond Road to the M2
> in 10 minutes and the M5 from Preston's to the M5 tunnel is a 14-minute trip. That's
> Sydney's latest traffic.

### ws1017-syd-0539-base — 05:39 (base)

- Source file: `ws1017_0539.mp3`
- Duration: 27.94s; words: 101; pace: 3.61 w/s
- SHA-256: `9f5599da1e77318a2a9ffbbdf645d3273999121502cb1537b9976aa7f7f19cbd`

> This morning we've got a truck and car crash into Ramesby on Milcara Road, that's
> Eastbound near Mont Street, closing lane 1. Erskine Park now with two cars, Smarsh,
> Erskine Park Road, corner of Swallow Drive, with delays building all directions. To
> the M5 at Milcara, watch out for the Citybound breakdown approaching Henry Lawson
> Drive, the Sydney Harbour Tunnel and Bridge getting a good run in both directions.
> Dettie on the M4 is building Citybound from Easton Creek towards the Prospect Highway.
> Trouble time on Lower Road to Church Street will take you now 15 minutes. That's
> Sydney's latest traffic.

### ws1017-syd-0559-base — 05:59 (base)

- Source file: `ws1017_0559.mp3`
- Duration: 25.62s; words: 76; pace: 2.97 w/s
- SHA-256: `37ef1a6c96c9ac40af71e25459685a77d8c24852af2d1e47db51bb2228bda2b0`

> In reef speed, traffic light repairs underway on the river road above the M5. It
> follows an earlier crash, but the lights themselves are working. At Pimball, a truck
> has broken down on Monavale Road, northbound just past Telegraph Road in the left
> lane. Ludnam break down the northern road southbound just after the M12, and Lightfog
> in Sydney's south-western suburbs at Picton, Appen and over the Hugh motorway at
> Douglas Park. That's Sydney's latest traffic.

### ws1017-syd-0614-v1 — 06:14 (v1)

- Source file: `ws1017_0614 (1).mp3`
- Duration: 29.10s; words: 89; pace: 3.06 w/s
- SHA-256: `0fe81bb75f01e792daeefa1fd4a86292cce1ceed46d50ffc3e1cfb3ec1564948`

> Fantastic furniture's end-of-financial-year sale just got eat. Pimple, a broken-down
> truck on Monavale Road northbound, just past Telegraph Road, over to Rees V. Traffic
> light repairs underway on the River Road just above the M5 motorway following an
> earlier crash. Windsor Downs, a reported crash on Richmond Road southbound after St
> Mary's Road and went out for light fog in Sydney's south-western suburbs at Picton,
> Appen and over the Hugh Motorway at Douglas Park. The M4 Malgoa road to Church Street
> taking 27 minutes. That's Sydney's latest traffic.

### ws1017-syd-0614-base — 06:14 (base)

- Source file: `ws1017_0614.mp3`
- Duration: 27.74s; words: 95; pace: 3.42 w/s
- SHA-256: `6d3707048cd181c06f2c54d08114d3a07ebdfd86485f1647189d4b9d99f17b64`

> Inside the M11 motorway tunnel at Thornley, there's a broken-down truck northbound in
> the left lane that speeds down to 40kms in the tunnel. Mass got a breakdown on
> Southern Cross Drive, a southbound approaching Botany Road in the left lane. The M5
> motorway at Liverpool has a breakdown westbound at Moorbank Avenue. Travel time
> citybound along the M5 though, from Preston's to the main tunnel not too bad,
> currently 22 minutes. Sydney Harbour Bridge Harbour Tunnel also clear both ways, and
> the M4 building quickly eastbound through the M7 interchange. That's Sydney's latest
> traffic.

### ws1017-syd-0628-base — 06:28 (base)

- Source file: `ws1017_0628.mp3`
- Duration: 25.54s; words: 82; pace: 3.21 w/s
- SHA-256: `04ad01ae723fe6b533698387a56bae78ba29f09963445a42f7525adcf80fd0e9`

> Botany. There's a broken-down B double truck on Foreshore Road westbound. It's taking
> up the left-hand lane at Hales Street, building city-bound through the M5 motorway
> tunnel as well. Fairfield reports for crash on Vine Street near Lawson Street in the
> Sports Centre, building southbound from North Sydney along the Warringah Freeway
> toward the Harbour Bridge. The M1 Pacific motorway steady-run central coast to Sydney
> though. Travel time from the Hawke Spree River Bridge to Warrunga, 13 minutes. That's
> Sydney's latest traffic.

### ws1017-syd-0728-v1 — 07:28 (v1)

- Source file: `ws1017_0728 (1).mp3`
- Duration: 26.48s; words: 89; pace: 3.36 w/s
- SHA-256: `98e1b7260fde42ce6bcfccfb79e6ecab0ff4c94f2f82928c92460806e672f8d0`

> On the M1 Pacific Moto at Bar Point, a truck has broken down northbound approaching
> Jolles Bridge. On the M5 in Riverwood, an earlier three-car smash has cleared
> Citybound before Belmore Road. Travel time is 36 minutes from Preston's to Kingsgrove,
> the M4 slowing Citybound from the M7 to Prospect Highway. Travel time from Eastern
> Creek to Homewish Bay Drive, 28 minutes, and in Windsor Downs, watch out for roadworks
> on Richmond Road just south of St. Mary's Road, a bit of heavy traffic southbound.
> That's Sydney's latest traffic.

### ws1017-syd-0728-base — 07:28 (base)

- Source file: `ws1017_0728.mp3`
- Duration: 23.13s; words: 77; pace: 3.33 w/s
- SHA-256: `9bc203c0302687d1ffe7d115e57a745da1141fb6286569e1607e62f6ac8cdb98`

> A Carlingford, a car's broken down on Carlingford Road, eastbound at Hepburn Avenue in
> the left lane. Campbelltown, a two-car crash. Campbelltown Road, northbound at Queen
> Street. A Botany, broken down truck on Foreshore Road is westbound in the left lane at
> Hales Street. Rosebury, an earlier incident on Gardiners Road, eastbound near Botany
> Road, still has delays back along Gardiners Road toward Kent Road and the exit from
> the M8 motorway tunnel. That's Sydney's latest traffic.

### ws1017-syd-0744-v1 — 07:44 (v1)

- Source file: `ws1017_0744 (1).mp3`
- Duration: 24.38s; words: 71; pace: 2.91 w/s
- SHA-256: `4a96a094410e51f3fe57469dc8a4af9bd949d9a60345792d7c2350bfee7e5993`

> On the M1 Pacific motorway in Barre Point, a truck has broken down northbound
> approaching Jolls Bridge. On the M5 in Riverwood, an earlier three-car crash has now
> cleared Citybound before Bellmoor Road with travel time 36 minutes from Preston's into
> Kingsgrove and the M4 motorway slowing Citybound from the M7 over to Prospect Highway.
> Travel time Eastern Creek to Homewish Bay Drive is roughly 28 minutes. That's Sydney's
> latest traffic.

### ws1017-syd-0744-base — 07:44 (base)

- Source file: `ws1017_0744.mp3`
- Duration: 24.51s; words: 86; pace: 3.51 w/s
- SHA-256: `ac31fd8ff4a11aa2a7db46e81d6d351803ab20f010206f42f97ecb96c21e9349`

> Botany. A truck's broken down westbound on Forshaw Road. It's in the left-hand lane at
> Hales Street, picking up pace now through Rosebury on Gardiners Road eastbound after
> an earlier incident near Botany Road. There are queues at Masse, got a long airport
> drive at the International Airport Terminal entry. At Rosell, still a slow run onto
> the Anzac Bridge from City West Link at the M4 Tunnel and the M4 Travel Time Eastern
> Creek to Homewish Bay Drive now 26 minutes at Sydney's latest traffic.

### ws1017-syd-0814-base — 08:14 (base)

- Source file: `ws1017_0814.mp3`
- Duration: 25.69s; words: 86; pace: 3.35 w/s
- SHA-256: `d739153ccfcf777df4e95ddb6a9f3d55e944fa965c0f1561f90889eb29403530`

> At Ermington, there's been a two-car crash on Silverwater Road southbound before
> Holkiss Street. Heavy delays now back past Victoria Road. Botany are broken down
> truckers on Foreshore Road westbound in the left lane at Hales Street. Padstow Heights
> reports of a crash on Davies Road northbound at Prosser Avenue. The M4 motorway is
> slowing now eastbound from the M7 to the Prospect Highway and again at Silverwater
> Road. Travel time from Eastern Creek to Homebush Bay Drive is currently 21 minutes.
> That's Sydney's latest traffic.

### ws1017-syd-0828-v1 — 08:28 (v1)

- Source file: `ws1017_0828 (1).mp3`
- Duration: 29.00s; words: 86; pace: 2.97 w/s
- SHA-256: `3087f185cfb113dca750ce86f880cc9cce86ffcb4831e1da254c61f9413e00db`

> The M5 motorway now easing citybound after an earlier crash was cleared just before
> Belmore Road. Travel time is 28 minutes from Preston's into Kingsgrove. It's also
> quite slow on the M5 alternative of Newbridge Road Citybound through Milpera. M2
> congested eastbound from Deli Road to the Lane Cove Tunnel. Sydney Harbour Bridge
> looking quite steady in both directions. And the M4 slowing citybound from the M7 to
> Prospect Highway. Travel time is 24 minutes from Eastern Creek to Homebush Bay Drive.
> That's Sydney's latest traffic.

### ws1017-syd-0828-base — 08:28 (base)

- Source file: `ws1017_0828.mp3`
- Duration: 27.60s; words: 89; pace: 3.22 w/s
- SHA-256: `0188a90274e6034dba8c58664c521a3d8fedc69efba529558b0eebefc29f0542`

> At Ermington, a two-car crash on Silverwater Road southbound before Holkers Street,
> slow back toward Victoria Road. Inside the M11 motorway north connects Tunnel at
> Pennett Hills, broken down track northbound in the left-hand lane with speed reduced
> through the tunnel. Botany broken down track is on Foreshore Road westbound in the
> left lane at Hales Street. The M5 motorway still slows Citybound Casula to Moorbank
> and the M4 slowing Citybound past the Prospect Highway. Travel time for Homebush Bay
> Drive is currently 21 minutes. That's Sydney's latest traffic.

### ws1017-syd-0844-v1 — 08:44 (v1)

- Source file: `ws1017_0844 (1).mp3`
- Duration: 25.20s; words: 70; pace: 2.78 w/s
- SHA-256: `7d70ecf09ca9b12489b4ef65877bb7eed442fa501f2de6d2ed21929b2b06e485`

> On the Yemon Pacific motorway in Asquith, police operation is closing two of three
> southbound lanes just past Keringai Chase Road. Anzac Bridge slowing city-bound and
> the western distributor congested past Darling Harbour and toward the King Street exit
> after an earlier breakdown in the CBD. The M5 easing city-bound after an earlier crash
> cleared before Belmore Road. Travel time 28 minutes from Preston's into Kingsgrove.
> That's Sydney's latest traffic.

### ws1017-syd-0844-base — 08:44 (base)

- Source file: `ws1017_0844.mp3`
- Duration: 24.18s; words: 87; pace: 3.60 w/s
- SHA-256: `57d73e27932d4ac8974321ad0e39e11d19978747d9a0216069be45b3d1e58239`

> Atson Ives. There's been a truck and car crash on Motivale Road at Woodbury Road. Two
> of the three northbound lanes are closed and heavy delays along Motivale Road. The M2
> motorway at Bulcombe Hills. There's a broken-down bus. It's westbound at Windsor Road
> but it's blocking the right-hand lane and the M2 queuing back now toward Pienaet Hills
> Road. Plus, at Ermington a two-car crash still holding up Silver Water Road southbound
> before Holkers Street. Some heavy delays back to Victoria Road. That's Sydney's latest
> traffic.

### ws1017-syd-0858-base — 08:58 (base)

- Source file: `ws1017_0858.mp3`
- Duration: 25.95s; words: 85; pace: 3.28 w/s
- SHA-256: `cebcf15fb4324251b4fae6fe87c6f7a8d686d4c09e3c6cddf76d17e80fb56d11`

> At St Ives, a truck and car crash on Motobale Road, northbound at Woodbury Road, left
> lane remains closed and heavy delays. On the M2 motorway, a broken-down bus is
> westbound at Windsor Road, the two right-hand lanes are blocked and heavy delays back
> toward Penard Hills Road. Inside the M11 north connects tunnel at Penard Hills, a
> broken-down truck northbound with speed reduced in the tunnel, and Lucas Heights, a
> broken-down truck on Heathcote Road, eastbound approaching New Illawarra Road. That's
> Sydney's latest traffic.

### ws1017-syd-0958-v1 — 09:58 (v1)

- Source file: `ws1017_0958 (1).mp3`
- Duration: 22.13s; words: 77; pace: 3.48 w/s
- SHA-256: `1441cc444a0ffeb733c4a6afcd81f26f4bf2a13ed9ebd73999bbd43962e393c4`

> In Midgenbury, a crash on Wargrove Road, Lane 1 northbounders block just past the M4
> on-ramps. Leftover delays at Asquith after an earlier police operation on the M1
> Pacific motorway, but all lanes are open now near Coringay Chase Road. There's a
> broken-down bus at Mount Collar on the Pacific Highway at Excelsior Road, and it banks
> down the trucks broken down on Stacey Street. It's blocking a northbound lane at
> Myrtle Road. That's Sydney's latest traffic.

### ws1017-syd-0958-base — 09:58 (base)

- Source file: `ws1017_0958.mp3`
- Duration: 25.87s; words: 80; pace: 3.09 w/s
- SHA-256: `2db05d0a1c814f70a08d284c67451436fdcde495ab9a747359d58bb7c9cae5f0`

> A truck's broken down on the M4 motorway at Grey Stains. It's westbound at Eta Long
> Road. In Liverpool, emergency roadwork underway. Elizabeth's drive at McLean Street
> that's affecting eastbounders. We've got delays coming through Jamoine and Gladesville
> after a crash at Huntley's Point, westbound on Victoria Road at Huntley's Point Road.
> In Cowan, an earlier breakdown has been cleared from the M1 Pacific motorway.
> Northbound traffic getting back on track past the Pacific Highway overpass. That's
> Sydney's latest traffic.

### ws1017-syd-1058-base — 10:58 (base)

- Source file: `ws1017_1058.mp3`
- Duration: 25.52s; words: 77; pace: 3.02 w/s
- SHA-256: `06c4a96db4077d22e7841fa7e48d6b2ed35bc4b22d27e50b82c23b034ec02eb3`

> It's been a crash at Blacktown involving a motorbike on Richmond Road, eastbound at
> Kent Street. In Waterloo, a cyclist and a car have collided on South Dowlings Street,
> northbound at O'Day Avenue. There's a breakdown on the Sydney Harbour Bridge, that's
> northbound, and at North Taramura, crews are dealing with a fallen tree. Bobbinhead
> Road affected at Lure Crescent both ways. Emergency roadwork at Liverpool, Elizabeth
> Drive at McLean Street, that's eastbound. That's Sydney's latest traffic.

### ws1017-syd-1158-base — 11:58 (base)

- Source file: `ws1017_1158.mp3`
- Duration: 25.26s; words: 84; pace: 3.33 w/s
- SHA-256: `63bb305d824ee726e068a1c3657ca3aed040b2fd76ed15fdcaef7dd1cba239fd`

> A two-car crash at Borkham Hills, Windsor Road, northbound at Marinda Road. Emergency
> crews have taken out the right northbound lane. In Mentionbury, we had an oil spill on
> the M4 that's been tidied up now, and eastbound is getting back on track now past
> Roper Road. In the Blue Mountains at Mount Victoria, the Great Western Highway remains
> closed both ways. The diversions via Bell through Shiffley Road and Darling Causeway
> add at least 30 minutes to the trip. That's Sydney's latest traffic.

### ws1017-syd-1358-base — 13:58 (base)

- Source file: `ws1017_1358.mp3`
- Duration: 24.95s; words: 69; pace: 2.77 w/s
- SHA-256: `a460a56ad024d9b34e3347c0817e9541d942375cefe55df0cf429a039ca1b352`

> Mount Georgia Crash, Woodstock Avenue, Carlisle Avenue. Northmeader breakdown for
> Southbound. Windsor Road approaching James, Roost Drive and Hursville. Crash, Croydon
> Road at Queen's Road. Summer Hill wires are down, affecting Lane 3 Westbound. Pameter
> Road between Sloane Street and Liverpool Road. Castle Hill, lights are out. Old
> Northern Road, Coors Road. Roselands are broken down semi-Southbound. Lane 1, King
> George's Road approaching Canary's Road. That's Sydney's latest traffic.

### ws1017-syd-1459-v1 — 14:59 (v1)

- Source file: `ws1017_1459 (1).mp3`
- Duration: 25.52s; words: 91; pace: 3.57 w/s
- SHA-256: `433ca0f9e84aeea1edd75eb5ffde07e333f08364fb19670d1ad2492c58f4ef41`

> M5 motorway through Hammondville. A multi-car crash where two or three westbound lanes
> are blocked off on the approach to Heathcote Road. Chatswood, a van's broken down
> westbound Fullitts Road just passing Pacific Highway. M2 motorway, Balkham Hills, a
> truck crash eastbound that's in the median strip just as you pass Windsor Road. To the
> Hume Highway, Kessula multi-car crash northbound as you pass Old Carajong Road. Trucks
> broken down in the Sydney Harbour Tunnel northbound near the exit into Milsons Point
> with a left-hand lane shut. That's Sydney's latest traffic.

### ws1017-syd-1459-base — 14:59 (base)

- Source file: `ws1017_1459.mp3`
- Duration: 28.66s; words: 103; pace: 3.59 w/s
- SHA-256: `fc9e054a4ea56b5209efe1c4298936e7cba827113209e7b71afb6bcb762b45b5`

> Banks Meadow. There's a truck and car collision on Foreshore Road. Blocking an
> eastbound lane approaching Botany Road. Delays southbound down General Holmes Drive
> through Brighton Lissands after a truck breakdown is cleared just from before Bay
> Street. Liverpool Newbridge Road passing Speed Street 3. Car crash with an eastbound
> lane shut. In the North Connex Tunnel through Warrunga at the Offramp to Pennant Hills
> Road northbound a truck breakdown. Traffic light repairs in Castle Hill. Old Northern
> Road occurs Road. A northbound lane being blocked off here. And the umphil motorway is
> filling quickly westbound at Church Street. That's Sydney's latest traffic.

### ws1017-syd-1524-base — 15:24 (base)

- Source file: `ws1017_1524.mp3`
- Duration: 28.01s; words: 94; pace: 3.36 w/s
- SHA-256: `05378ac39a621b39abafa229d12f3d47d3191cac024ca461deb72680403ed60c`

> The M5 motorway crawling westbound from Fairford Road towards Heathcote Road. There's
> been a multi-car crash in Hammondville. Couple of lanes are blocked off around the
> area and a city-bound lane as well. Another multi-car crash just clearing from
> Kasulah-Hume Highway passing Karajong Road northbound. Into Balkam Hills, a sideline
> truck crash eastbound for the M2 motorway past Windsor Road. Delays north in the
> Sydney Harbour Tunnel towards the exit of Milsons Point. A truck breakdown has moved
> off. A van's broken down Chatswood fullers Road westbound past Pacific Highway. That's
> city's latest traffic.

### ws1017-syd-1540-base — 15:40 (base)

- Source file: `ws1017_1540.mp3`
- Duration: 26.26s; words: 91; pace: 3.47 w/s
- SHA-256: `c9bd3cce770d2d36fa78ac39ae59c5768bbe2640bc32d4681e7c5d8d350a3a02`

> Heavy conditions outbound for the M4 motorway from Church Street towards a Prospect
> Highway after a multi-car crash has cleared from Hunting Wood. We've eased up to
> eastbound, entering the Lane Cove tunnel following an earlier incident. Expect some
> very heavy delays outbound for General Holmes Drive. The airport tunneling mascot
> right into Brighton-less Sands and earlier truck breakdowns moved off. Looking to Iran
> in Castle Hill, traffic light repairs Old Northern Road at Kurz Road. Crews are on
> scene closing off the left at Northbound Lane. That's Sydney's latest traffic.

### ws1017-syd-1559-v1 — 15:59 (v1)

- Source file: `ws1017_1559 (1).mp3`
- Duration: 27.23s; words: 90; pace: 3.31 w/s
- SHA-256: `e7bc6dfbeccc3cdbd27666f1ab1e6d0074d40c9bdb23fdc6aa3784b6a519da4c`

> Homebush West, a multi-car crash. Centenary Drive, southbound off-ramp towards Austin
> Avenue. Roselle, the M4 motorway tunnel-a-bus breakdown. It's westbound in the
> breakdown lane just as you come off the Anzac Bridge, a 40k speed drop. An absolute
> shocker for the M5 right now. A multi-car crash westbound through Hammondville towards
> Heathcote Road. Delays are extensive back-passing King George's Road and city-bound
> pressure coming up from the Hugh motorway with a lane also blocked off. Weatherill
> Park, the Horsley Drive, a breakdown near Ferris Road. That's Sydney's latest traffic.

### ws1017-syd-1559-base — 15:59 (base)

- Source file: `ws1017_1559.mp3`
- Duration: 28.40s; words: 101; pace: 3.56 w/s
- SHA-256: `159ce5b52a0caacbf2ebe34644bb2c815982d67cc861e44d1cd5c4b06579431c`

> Heavy outbound pressure for General Holmes Drive heading towards the M5 tunnel in
> Mascot. We've had a car that's broken down and still expect delays continuing south
> along General Holmes Drive to Brightless Sands onto the Grand Parade after an earlier
> truck breakdown here. Blacktown Bungaraby Road closed off eastbound following a car
> and pedestrian incident at Patrick Street. Crews are on scene. North Connect's tunnel
> northbound off ramp to Pennant Hills Road are trucks broken down and traffic lights
> are being repaired in Castle Hill. Old northern road at Currs Road, the left
> northbound lane shut. That's Sydney's latest traffic.

### ws1017-syd-1615-base — 16:15 (base)

- Source file: `ws1017_1615.mp3`
- Duration: 26.01s; words: 86; pace: 3.31 w/s
- SHA-256: `b3770d2224f5ba3338ba4fd8d4dbb98d559092985878e828e994e2e9ee12ecb2`

> All westbound lanes have reopened on the M5 motorway through Hammondville approaching
> Heathcote Road following a multi-car crash. Still expect extensive delays back past
> King George's Road. Your run time's been sitting at about one hour exiting the main
> tunnel through Kingsgrove into Preston's and there's plenty of citybound pressure to
> approaching Homebush West, a multi-car crash southbound Centenary Drive off-ramp to
> Austin Avenue. Delays in Weatherill Park after a breakdown is now cleared from the
> Halsey Drive westbound approaching Ferris Road. That's Sydney's latest traffic.

### ws1017-syd-1624-base — 16:24 (base)

- Source file: `ws1017_1624.mp3`
- Duration: 27.31s; words: 97; pace: 3.55 w/s
- SHA-256: `d75c220dd0b97d85b0c1c86e64cf6748dc74d4e99f7dbc438f8247ad28d409f9`

> Botany, a bus and bicycle crash for Botany Road closing a southbound lane at Lorde
> Street. All lanes are back open on the M5 motorway westbound through Hammondville
> after a multi-car crash has cleared off around Heathcote Road. Still expect some very
> heavy congestion with a travel time of about 50 minutes, Kingsgrove into Preston's.
> The M4 motorway tunnel under speed westbound from Anzac Bridge heading out towards the
> exit after an earlier breakdown in Roselle. We've had a multi-car crash in Homebush
> West southbound Centenary Drive at the off-ramp to Marlborough Road. That's Sydney's
> latest traffic.

### ws1017-syd-1640-v1 — 16:40 (v1)

- Source file: `ws1017_1640 (1).mp3`
- Duration: 27.94s; words: 91; pace: 3.26 w/s
- SHA-256: `529aaddd4be5061104ae0aa06653497d7c0c5659bdf46da87b2b3c41a5c90e1c`

> The M5 motorway is still quite a struggle westbound past King George's Road out
> towards Morbank following a multi-car crash. A bit of a knock-on effect as you move
> southbound towards Raby down the Hugh Motorway 2. Now on the Hugh Motorway,
> Campbelltown northbound a truck breakdown in the shoulder approaching Norelland Road.
> Botany, a bicycle and bus have collided. Botany Road at Lord Street southbound.
> Ingleside, Monavale Road, a crash, two cars at Tumburra Street. And emergency works in
> Punchbowl Westbound, Canterbury Road near Bonds Road. That's Sydney's latest traffic.

### ws1017-syd-1640-base — 16:40 (base)

- Source file: `ws1017_1640.mp3`
- Duration: 28.52s; words: 104; pace: 3.65 w/s
- SHA-256: `9c55ea029ed0452ba2209fc5ed0bf4ddbd050f4d678e506862d72b7bb8e7b487`

> There's some pretty heavy traffic on the M5 motorway. City bound through Kossula after
> a multi-car crash has cleared off, leaving quite a backlog of traffic down the Hugh
> Motorway through Bardia. Balkham Hills, a two-car crash northbound for Windsor Road at
> Seven Hills Road. In French is Forest, a car and motorcycle collision closing a
> southbound lane where Koss Parkway, French is Forest Road. In Blacktown, a car and
> pedestrian incident. Bungaraby Road eastbound Patrick Street. And for motorway, it's a
> bit of a hot spot. As well this afternoon, westbound from the tunnel exit through to
> the M7. That's Sydney's latest traffic.

### ws1017-syd-1659-base — 16:59 (base)

- Source file: `ws1017_1659.mp3`
- Duration: 29.02s; words: 99; pace: 3.41 w/s
- SHA-256: `3e8c96c27e2a664ace5a2ee284102eae2fcb8289a4b63710896c4364691653e1`

> The M5 motorway is still very heavy westbound from King George's Road to Moorbank
> following a multi-car crash at Heathcote Road earlier. Now we've got delays northbound
> on the Hugh motorway with a truck that's broken down through Campbelltown towards
> Norelland Road. Crews have now shut off the left-hand lane into Botany. All lanes back
> open. Botany Road at Lord Street after a bus and motorcycle collided southbound.
> Ingalls Side Motivale Road, Tumburra Street, two-car crash westbound. M4 motorway
> tunnel, very under speed this afternoon westbound from Waddell Street right out the M4
> towards Prospect. That's Sydney's latest traffic.

### ws1017-syd-1715-v1 — 17:15 (v1)

- Source file: `ws1017_1715 (1).mp3`
- Duration: 19.22s; words: 72; pace: 3.75 w/s
- SHA-256: `d8c0ee2540a84e674cd3f4db6ec16f7da14c5e993606f0123b29b2759690d423`

> The M4 motorway tunnel, it is still a massive struggle from Waddell Street heading all
> the way out the motorway towards the M7 this afternoon. Expect some pretty heavy
> traffic too for the M2 motorway, we're crawling basically from Lane Cove Road right up
> to the M7 at Sunnyholt Road. Bump and bumper for much of the M5 westbound moving
> towards Heathcote Road from that earlier multi-car crash. That's Sydney's latest
> traffic.

### ws1017-syd-1715-base — 17:15 (base)

- Source file: `ws1017_1715.mp3`
- Duration: 27.07s; words: 96; pace: 3.55 w/s
- SHA-256: `1051d06002a62009a2a3781ef9fc601651cfd370d821ce086a9b757e1f37e3e4`

> Annandale, the M4 M8 tunnel link a breakdown for your southbound run. A car and
> motorcycle collision through French's Forest and Waikers Parkways shot a southbound
> lane at French's Forest Road. Still looking pretty busy around the southwest,
> following a multi-car crash citybound on the M5 in Kasula. Stacks of traffic
> approaching the motorway for the Hume Highway through Kasula and Liverpool. And we're
> bump to bumper outbound for the M4 motorway from the tunnel exit right through to the
> M7. That stretch is going to take you just under 40 minutes. That's city's latest
> traffic.

### ws1017-syd-1723-v1 — 17:23 (v1)

- Source file: `ws1017_1723 (1).mp3`
- Duration: 26.70s; words: 81; pace: 3.03 w/s
- SHA-256: `4a830086c8d358d9c5d942e4661cc9cd49a4eef741a722ef664e22cfebbfec4e`

> Hurstville, King George's Road passing Waniura Road, a car breakdown northbound.
> Ingleside, Motivale Road passing Powderworks Road westbound, a car breakdown here. A
> crash in Punchbowl, two cars involved, Punchbowl Road near Viola Street. Looking at
> Uran, outbound for the M5 motorway from Kingsgrove into Preston's 45 minutes. The M4
> motorway tunnel exit westbound, taking 40 minutes to get to the M7, and we're still
> pretty stacked through the tunnel westbound coming in from the Anzac Bridge. That's
> City's latest traffic.

### ws1017-syd-1723-base — 17:23 (base)

- Source file: `ws1017_1723.mp3`
- Duration: 26.48s; words: 101; pace: 3.81 w/s
- SHA-256: `854252f30b1b5483e449b14593a4b8a1ad071b7969a4aee90cd86aa2e89c6f2b`

> M8 motorway tunnels southbound just coming in from the Anzac Bridge off-ramp. We've
> got a car that's broken down a couple of lanes blocked off in Annandale. Parramatta,
> Great Western Highway, Pitt Street, a set of traffic lights flashing. All lanes back
> open through Bulgham Hills. Windsor Road northbound at Seven Hills Road after a two-
> car crash. Coming into the area for the M2 motorway, quite a slow stretch westbound
> into Bulgham Hills from the Epping Tunnel. And we're busy northbound up the Hugh
> motorway, getting onto the M5 city bound in Casula following that earlier multi.
> That's Sydney's latest traffic.

### ws1017-syd-1740-v1 — 17:40 (v1)

- Source file: `ws1017_1740 (1).mp3`
- Duration: 28.45s; words: 88; pace: 3.09 w/s
- SHA-256: `49b89276857aa4512f0c3b8ef7996ef1564669e1c4ebe21ffa8a85bef06b2c6b`

> M5 motorway off-ramp to Moorbank Avenue westbound. A crash here that's also impacting
> your southbound run for Moorbank Avenue and one-lane shut on the M5. M8 motorway
> tunnel delays southbound passing the M4 link after a breakdown through Annandale.
> Lights on the flash in Parramatta, Great Western Highway at Pitt Street all
> directions. Travel on the M4 motorway is still looking quite heavy westbound from the
> tunnel exit, getting out towards the M7 motorway 35 minutes. The M7 also underpays
> westbound towards Sunnyhalt Road. That's Sydney's latest traffic.

### ws1017-syd-1740-v2 — 17:40 (v2)

- Source file: `ws1017_1740 (2).mp3`
- Duration: 27.14s; words: 90; pace: 3.32 w/s
- SHA-256: `c0e88153726091d56cff38118f04731745acf2296ec95733d4c494a713275fcd`

> Emergency utility works in Punchbowl along Canterbury Road, the left westbound lane
> shut near Bonds Road. A collision in Punchbowl for Punchbowl Road around Viola Street,
> two cars involved. Delays for the southwest outbound on the M5 motorway coming to
> Heathcote Road after that earlier multi-car crash. Still pretty chock-a-block for
> Milpera Road and Newbridge Road and roads approaching the motorway like Heathcote Road
> and Henry Lawson Drive. Meanwhile, the M4 motorway tunnel still struggling with a
> particularly slow stretch out past Homebridge Bay Drive. That's Sydney's latest
> traffic.

### ws1017-syd-1740-base — 17:40 (base)

- Source file: `ws1017_1740.mp3`
- Duration: 28.38s; words: 88; pace: 3.10 w/s
- SHA-256: `f46c31352287b57df9cfefc1d5c869e52688f7bc0513335ff5c492edcd5d6333`

> M5 motorway off-ramp to Moorbank Avenue westbound. A crash here that's also impacting
> your southbound run for Moorbank Avenue and one-lane shut on the M5. M8 motorway
> tunnel delays southbound passing the M4 link after a breakdown through Annandale.
> Lights on the flash in Parramatta, Great Western Highway at Pitt Street all
> directions. Travel on the M4 motorway is still looking quite heavy westbound from the
> tunnel exit getting out towards the M7 motorway 35 minutes. The M7 also underpace
> westbound towards Sunnyholt Road. That's Sydney's latest traffic.

### ws1017-syd-1759-base — 17:59 (base)

- Source file: `ws1017_1759.mp3`
- Duration: 25.81s; words: 83; pace: 3.22 w/s
- SHA-256: `302236fdc81621aebf10a04795f823540e2858d195086b34ef073027e2dcf0bb`

> Heavy traffic through Greenacre on Roberts Road. A two-car crash where only one
> northbound lane's getting past Pelman Avenue. There's emergency utility works in Punch
> Bowl, Canterbury Road near Bonds Road. Westbound lane one's been out of action. Still
> looking really slow for the M4 motorway tunnel westbound Waddell Street out to the
> exit and quite a bunch up on the motorway itself between Church Street and Prospect
> Highway each direction. M5 motorway also struggling outbound towards Heathcote Road.
> That's city's latest traffic.

### ws1017-syd-1815-v1 — 18:15 (v1)

- Source file: `ws1017_1815 (1).mp3`
- Duration: 26.70s; words: 98; pace: 3.67 w/s
- SHA-256: `2798772440ac629643b6f2a450370329c4dd2c601981bbaad031f456ef3fe06c`

> Mamrie Road in Kemps Creek, a struggle each direction following a three-car crash
> that's between Abbots Road and Curves Road. Green Acre A2 Car collision northbound
> Roberts Road near Pelman Avenue. Grey Stains, a crash westbound on the Great Western
> Highway just as you pass Grey Stains Road. There's some emergency works around Punch
> Bowl, Canterbury Road, shunning the left westbound lane near Bonds Road, checking
> around on the M4 motorway now, travelling out from Church Street to Moggoa Road. It's
> easing up to 24 minutes, still looking heavy though at Homewish Bay Drive. That's
> Sydney's latest traffic.

### ws1017-syd-1815-base — 18:15 (base)

- Source file: `ws1017_1815.mp3`
- Duration: 26.79s; words: 90; pace: 3.36 w/s
- SHA-256: `58e91116dd44a74a3701e174bf72d9777aa5a60c402237ae3b7ce39e5f47aca4`

> Kellyville Ridge, Windsor Road at Maryville Road. A three-car crash. A northbound lane
> is blocked off. Southbound is also being impacted. Three-car crash. Bonnie Rigg
> Heights on Elizabeth Drive, Westbound near Aplin Road. Lights are playing up in
> Parramatta, Great Western Highway at Pitt Street. There's been a crash. The M5
> westbound off-ramp through to Morbank Avenue. Expect a lane blocked on the M5 motorway
> and some intimate enclosures for a clean-up recovery. Traffic on the M4 is under pace
> around Grey Stains Road each way. That's Sydney's latest traffic.

### ws1017-syd-1824-base — 18:24 (base)

- Source file: `ws1017_1824.mp3`
- Duration: 26.68s; words: 94; pace: 3.52 w/s
- SHA-256: `1590e6a1792e1a5e7e56b67f12367c1d64bbaaab8ed09391fd248e8e129e123a`

> In Seven Hills, we've got a car crash that's brought wires down. Seven Hills Road is
> now blocked off both directions, just as you pass Abbott Road. Bonnie Rig Heights,
> Elizabeth Drive, near Applin Road, a three-car crash westbound, another three-car
> collision in Kellyville Ridge on Windsor Road at Maryville Road, then right northbound
> lanes blocked off in southbound traffic also being impacted, lights are back up and
> running again through Parramatta, Great Western Highway at Pitt Street, and the M4
> motorway, a little slow westbound through to Silverwater Road. That's Sydney's latest
> traffic.

### ws1017-syd-1840-v1 — 18:40 (v1)

- Source file: `ws1017_1840 (1).mp3`
- Duration: 27.52s; words: 90; pace: 3.27 w/s
- SHA-256: `d00fb8e80349bb7dd4f524b6954f417ddee13f08944e967ea57071d348b334b7`

> M4 motorway tunnel, a little slow westbound after a crash has cleared around the
> Concord Road exit. In Berkshire Park, the northern road near Carrington Road, two-car
> collision southbound. Another two-car crash in Yaguna, Rookwood Road, southbound just
> as you pass William Home Street. Green Acre, a crash on Roberts Road, northbound near
> Pelman Avenue. There's been another accident through Greystain slowing up westbounders
> on the Great Western Highway just as you pass Greystain's Road. M5 motorway still
> under speed outbound King George's Road to Moorbank. That's Sydney's latest traffic.

### ws1017-syd-1840-base — 18:40 (base)

- Source file: `ws1017_1840.mp3`
- Duration: 27.54s; words: 94; pace: 3.41 w/s
- SHA-256: `9c93426580b182fe026c342cf19c985c6b837ad7f5ecb2d8680fb8a86834f7f6`

> In Seven Hills, a crash has brought down wires and closed off Seven Hills Road, just
> off the Abbott Road Prospect Highway intersection. In Bonnie Rigg Heights, a collision
> westbound. Three cars involved. Elizabeth Drive near Applin Road. Another three-car
> crash in Kellyville Ridge, Windsor Road at Maryville Road. Expect congestion each
> direction. Travel on the M5 motorway, still sitting just a little over 20 minutes
> westbound. Kingsgrove into Preston's and checking a run for the M4 motorway westbound
> from Church Street towards Malgoa Road. It's about the same 21. That's Sydney's latest
> traffic.
