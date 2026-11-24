// Arinco-styled SDD. Compile with the bundled brand font:
//   typst compile --font-path fonts "ARN-Traffic-Bulletin-Platform-Solution-Design-Document.typ" "ARN Traffic Bulletin Platform - Solution Design Document - SG2 Review.pdf"

// ---- Arinco brand palette (from the corporate template theme) ----
#let navy = rgb("#001A31")
#let blue = rgb("#0572A8")
#let titleblue = rgb("#003E75")
#let skyblue = rgb("#00B0F0")
#let yellow = rgb("#FBCA0E")
#let slate = rgb("#5b6b7a")
#let border = rgb("#cbd5e1")
#let band = rgb("#eef4f8")
#let pale = band        // alias kept for content compatibility
#let light_blue = band  // alias kept for content compatibility

#set document(title: "ARN Traffic Bulletin Platform - Solution Design Document", author: "Arinco")
#set text(font: "Montserrat", size: 10.5pt, fill: navy)
#set par(justify: false, leading: 0.6em)
#set heading(numbering: "1.1")

#show heading.where(level: 1): it => {
  pagebreak(weak: true)
  v(6pt)
  text(size: 17pt, weight: "bold", fill: navy)[#it]
  v(2pt)
  line(length: 100%, stroke: (paint: blue, thickness: 1pt))
  v(7pt)
}
#show heading.where(level: 2): it => { v(9pt); text(size: 12.5pt, weight: "bold", fill: blue)[#it]; v(2pt) }
#show heading.where(level: 3): it => { v(6pt); text(size: 10.5pt, weight: "bold", fill: blue)[#it]; v(1pt) }

// Branded tables: blue header row, zebra body, white bold header text.
// Applies to every table call; cover metadata uses grid to stay unbranded.
#set table(
  fill: (x, y) => if y == 0 { blue } else if calc.even(y) { band } else { white },
  stroke: 0.5pt + border, inset: 6pt, align: horizon,
)
#show table.cell.where(y: 0): set text(fill: white, weight: "bold", size: 9.5pt)

// Callouts.
#let box(title, body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: band, stroke: (left: 3pt + blue))[#text(weight: "bold", fill: blue)[#title] #v(2pt) #body]
#let callout(title, body) = box(title, body)
#let decision(body) = box("Design decision", body)
#let recommend(body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: rgb("#fff8e1"), stroke: (left: 3pt + yellow))[#text(weight: "bold", fill: rgb("#8a6d00"))[Recommendation] #v(2pt) #body]

// Column-spec helpers (inherit the branded fill / stroke from #set table).
#let decision_table = table.with(columns: (0.7fr, 1.6fr, 2.5fr))
#let four_col_table = table.with(columns: (1.2fr, 1.6fr, 2fr, 1.3fr))

// ================================================================ COVER
#page(margin: 0pt, header: none, footer: none,
  background: image("brand/arinco-cover.png", width: 100%, height: 100%))[
  #pad(left: 24mm, right: 24mm, top: 30mm, bottom: 24mm)[
    #image("brand/arinco-logo.png", width: 54mm)
    #v(58mm)
    #text(size: 30pt, weight: "bold", fill: titleblue)[ARN Traffic Bulletin Platform]
    #v(6pt)
    #text(size: 19pt, weight: "bold", fill: blue)[Solution Design Document]
    #v(10pt)
    #text(size: 12pt, fill: slate)[Stage Gate 2 Review Package]
    #v(16mm)
    #grid(columns: (34mm, auto), row-gutter: 6pt, column-gutter: 8pt,
      text(weight: "bold")[Prepared for], [Australian Radio Network],
      text(weight: "bold")[Prepared by], [Julian Codespoti, Arinco],
      text(weight: "bold")[Review gate], [Stage Gate 2: Production Solution Design Approval],
      text(weight: "bold")[Review date], [12 June 2026],
    )
  ]
]

// ================================================================ BODY CHROME
#set page(
  paper: "a4",
  margin: (top: 24mm, right: 20mm, bottom: 22mm, left: 20mm),
  header: context [
    #set text(size: 8pt, fill: slate)
    #grid(columns: (1fr, auto), align: (left + horizon, right + horizon),
      [Solution Design Document], image("brand/arinco-logo.png", width: 24mm))
    #v(-4pt)
    #line(length: 100%, stroke: 0.5pt + border)
  ],
  footer: context [
    #line(length: 100%, stroke: 0.5pt + border)
    #v(-3pt)
    #set text(size: 8pt, fill: slate)
    #grid(columns: (1fr, auto),
      [ARN Traffic Bulletin Platform  |  Solution Design Document],
      [Page #counter(page).display()])
  ],
)
#counter(page).update(1)

// ================================================================ SIGN-OFF
#heading(numbering: none, outlined: false, level: 1)[Sign-off]
#table(columns: (1.4fr, 1.6fr, 1.8fr),
  table.header([*Date*], [*Name*], [*Position*]),
  [], [Marc Tricksey], [Account Manager, Arinco],
  [], [ARN Business Owners], [Stage Gate 2 approvers],
)

// ================================================================ CONTENTS
#pagebreak()
#text(size: 17pt, weight: "bold", fill: navy)[Contents]
#v(2pt)
#line(length: 100%, stroke: 1pt + blue)
#v(7pt)
#outline(title: none, depth: 2)

= Review guide

== What ARN is being asked to approve

#decision_table(
  table.header([*No.*], [*Decision*], [*Requested position*]),
  [1], [Overall solution design], [Approve the Release 1 architecture and delivery approach so Phase A build can proceed.],
  [2], [Scheduling and delivery reliability], [Ratify fixed-event slot anchoring, a small multi-candidate generation approach where needed, and a mandatory fallback-fill cart.],
  [3], [Multi-station design direction], [Ratify shared pure-traffic bulletin bodies across co-located same-slot stations, with station tags or credits handled separately at the delivery layer where ARN requires them.],
  [4], [Voice strategy], [Endorse a cloned (Professional Voice Clone) Australian voice as the pilot target for the most natural, consistent broadcast quality, with a voice we design and tune in ElevenLabs as the capable fallback if the clone is not ready by 4 July. Confirm ARN will resolve voice talent, consent, and IP for the clone.]
)


== Review timetable

#table(
  columns: (1fr, 2.6fr, 1.5fr),
  inset: 5pt,
  stroke: border,
  table.header([*Date*], [*Activity*], [*Owner*]),
  [5 Jun 2026], [Client-ready SDD package prepared for Arinco review.], [Julian Codespoti],
  [8 Jun 2026], [Internal Arinco review, then circulation to ARN reviewers.], [Julian Codespoti, Marc Tricksey],
  [10 Jun 2026], [Requested feedback cut-off.], [ARN reviewers via Sammy Wessel],
  [12 Jun 2026], [Stage Gate 2 review.], [ARN Business Owners and Steer Co],
  [15 Jun 2026], [Final tidy-up and formal sign-off if feedback is raised.], [Julian Codespoti and ARN Business Owners]
)

= Executive summary

ARN is replacing a material traffic-bulletin dependency with an in-house AI traffic bulletin capability. The existing proof of concept has already shown the concept working end to end, from live HERE traffic through to delivery into Zetta.

The productionisation engagement turns that proof of concept into a production-ready internal pilot. The 4 July decision is a board-confidence gate for ARN's Australian Traffic Network (ATN) contract path. The system must be demonstrably working well enough for ARN reviewers to judge voice quality, script quality, safety controls, and operational feasibility.

In this engagement, a _production-ready internal pilot_ means the platform runs on production-grade Azure infrastructure with mandatory, audited human approval and is exercised by real ARN operators reviewing live bulletins internally. It does not mean unattended on-air automation, public broadcast, or rollout beyond the pilot market; those remain ARN-led, post-engagement steps.

#box("Recommended approval", [
Approve the solution design at Stage Gate 2 so the Phase A build can proceed. Remaining work is captured as owned, sequenced dependencies in the risks and dependencies section.
])

== What the proof of concept already proves

The platform does not start from a blank page. The proof of concept already runs the full bulletin pipeline:

- Live traffic data is pulled from HERE for a market.
- A timed radio script is generated from that traffic.
- The script is voiced as broadcast audio through ElevenLabs.
- A human operator reviews the script and audio and approves it before anything is published.
- Approved audio is delivered to Zetta over SMB file delivery.

This proves the parts that are hardest to de-risk: the external integrations, the script-to-audio generation, and the Zetta delivery path. The productionisation work carries this proof of concept into an Azure-native solution and adds the security, identity, environment, deployment, and monitoring design needed to run it as a supervised pilot, set out in the sections that follow.

= Business context

ARN historically relied on ATN for traffic inventory across metro and regional radio. The renegotiated ATN arrangement has created a significant revenue and strategic exposure. ARN's objective is to bring traffic bulletin capability in-house, protect commercial value, and create foundations for future product growth.

#table(
  columns: (1.4fr, 2.2fr),
  inset: 5pt,
  stroke: border,
  table.header([*Outcome*], [*Value*]),
  [Mitigated revenue loss], [\$37.8M over four years],
  [New commercial inventory opportunity], [\$29M per year at full sell-out],
  [Strategic positioning], [Positioned as one of the first AI information bulletin products in Australia],
  [Future revenue diversification], [Potential white-label or SaaS opportunity after this engagement]
)

#text(size: 8pt, fill: slate)[Figures are ARN commercial estimates, not Arinco projections.]

== Engagement structure

#table(
  columns: (1.2fr, 1.3fr, 1.4fr, 2.1fr),
  inset: 5pt,
  stroke: border,
  table.header([*Phase*], [*Window*], [*Hard date*], [*Goal*]),
  [Phase A: Pilot proof], [Weeks 1 to 6], [4 Jul 2026], [ARN board has enough confidence to authorise the ATN cancellation notice path.],
  [Phase B: Production handover], [Weeks 7 to 14], [26 Aug 2026], [Pilot production deployment, UAT, hypercare, handover, and Stage Gate 3 sign-off.]
)

= Release 1 scope

== In scope for Release 1

#table(
  columns: (1.4fr, 3fr),
  inset: 5pt,
  stroke: border,
  table.header([*Area*], [*Position*]),
  [Pilot market], [Leading candidate is Hot Tomato, Gold Coast, pending ARN internal approval. A second small regional market may be used to observe low or no-traffic behaviour.],
  [Traffic bulletin generation], [Traffic is the core Release 1 data source. HERE traffic incidents feed the generation pipeline.],
  [Human approval], [Mandatory. No bulletin is published without an explicit operator decision.],
  [Content safety], [A compliance-aware safety layer is required before go-live. Current framework supports banned terms and substitution rules.],
  [Zetta delivery], [Core integration path. The proof-of-concept and test-environment mechanics are already known through SMB file delivery. Production and test environment differences still need confirmation.],
  [Fallback], [If a slot is missed or rejected with no time to regenerate, reuse the most recent same-day approved bulletin; for first-of-day slots or when no recent approved bulletin is suitable, play a generic fallback-fill cart supplied by ARN. Reuse-suitability rules (for example, excluding a bulletin dominated by since-cleared incidents) are tuned with ARN content during the pilot.],
  [Operator dashboard], [Operators review scripts, audio, selected incidents, timings, and failure states.],
  [Monitoring and alerts], [Dashboard, Teams notification, shared operator escalation, and operational metrics.]
)

== Candidate or deferred items

#table(
  columns: (1.3fr, 3fr),
  inset: 5pt,
  stroke: border,
  table.header([*Item*], [*Treatment*]),
  [Weather and fuel], [Candidate supplementary HERE-backed content sources only. They are not assumed as committed Release 1 build scope unless ARN confirms. Architecture impact is low because they follow the same source-data pattern.],
  [Ads, Aquira, and live-read sponsorship], [Open ARN commercial and sales-process question. Architecture keeps a seam for future commercial content, but implementation is not committed for Release 1.],
  [Voice approach], [A cloned (Professional Voice Clone) Australian voice is the pilot target for the most natural, consistent result, subject to ARN resolving voice talent, consent, and IP. If the clone is not ready in time, the fallback is a voice we design and tune in ElevenLabs, a custom Australian voice rather than an off-the-shelf one. Per-region pronunciation is handled the same way for either, so cloning does not reduce pronunciation control.],
  [Full dev/test/prod private-network deployment], [In scope for the engagement. All environments are private; the private-endpoint and network inputs are an ARN dependency across dev, test, and production.],
  [Full network rollout], [Post-engagement ARN-led rollout. Not part of this Stage Gate 2 approval.]
)

== Explicitly out of scope

- SaaS or multi-tenant implementation.
- Full rollout to all ATN-represented stations.
- Commercial sales structure, pricing, GTM collateral, or ATN contract termination execution.
- Native mobile applications.
- Historical traffic data migration.
- Custom model training.
- Ongoing managed service after hypercare.
- Ads or Aquira implementation unless separately scoped and approved.

= Target solution architecture

The target architecture keeps the proof-of-concept shape but hardens the production controls around it.

#page(flipped: true)[
  #v(1fr)
  #image("assets/platform-architecture.png", width: 100%)
  #v(8pt)
  #align(center)[#text(size: 8pt, fill: slate)[Figure 1: Target architecture. External services feed the Azure Functions processing layer, operators review through the dashboard, approved audio is delivered to Zetta, and operational signals flow to monitoring and notifications.]]
  #v(1fr)
]

== Architecture principles

- Preserve the existing clean architecture rather than rewriting the system.
- Keep station, market, voice, schedule, and destination details as configuration.
- Make human approval mandatory and auditable.
- Use identity-based access for Azure services wherever possible.
- Keep secrets in Key Vault.
- Separate generated traffic content from station tags, sponsor reads, and commercial content.
- Keep the delivery layer extensible so future Aquira, sponsorship, or multi-station patterns can be added without changing the core generation pipeline.

== Component responsibilities

#table(
  columns: (1.2fr, 3fr),
  inset: 5pt,
  stroke: border,
  table.header([*Component*], [*Responsibility*]),
  [Operator dashboard], [Review queue, script/audio review, approve, reject, edit, regenerate, publish, failure visibility, and schedule/configuration management.],
  [Function processing layer], [Scheduling, traffic fetch, script generation, content checks, audio synthesis, job lifecycle, Zetta delivery, metrics, and notifications.],
  [Azure Storage], [Persistent job state, generated scripts, generated audio, queue work, and schedule state.],
  [HERE], [Traffic incidents and related traffic data for the selected market. Candidate weather/fuel extensions would use the same source-data pattern if ARN confirms them.],
  [Azure OpenAI], [Script drafting, review, and refinement.],
  [ElevenLabs], [Text-to-speech generation and voice configuration.],
  [Zetta], [Broadcast playout destination for approved bulletin audio.],
  [Teams and monitoring], [Operational alerts, escalation notifications, and system visibility.]
)

= Bulletin lifecycle

The lifecycle is:

1. A schedule or manual trigger starts a bulletin job for a station and market.
2. HERE traffic incidents are fetched for the configured coverage area.
3. Stale traffic is removed before script generation.
4. The system selects relevant incidents and context.
5. Azure OpenAI drafts, reviews, and refines a timed Australian radio script.
6. The script is checked against content safety rules.
7. ElevenLabs generates the audio.
8. The audio and script are saved for review.
9. An operator reviews the incident list, script, and audio.
10. The operator approves, edits and regenerates, rejects, or sends the slot to fallback.
11. Approved audio is delivered to Zetta and notifications are sent.
12. If no safe fresh bulletin exists in time, the fallback path is used.

Each slot has an approval cut-off ahead of its delivery time. If an operator does not approve a bulletin before the cut-off, the slot falls back automatically rather than airing unreviewed content: the system reuses the most recent suitable same-day bulletin or plays the fallback-fill cart, and raises an operator alert. The deadline only ever triggers fallback; it never auto-publishes an unreviewed bulletin.

#page(flipped: true)[
  #v(1fr)
  #image("assets/bulletin-lifecycle.png", width: 100%)
  #v(8pt)
  #align(center)[#text(size: 8pt, fill: slate)[Figure 2: Bulletin lifecycle from scheduled trigger through script generation, safety check, audio synthesis, operator approval, Zetta delivery, and Teams notification.]]
  #v(1fr)
]

== Operator review requirements

The operator-workflow discovery established that the operator primarily checks the script against selected incidents and listens for local pronunciation, naturalness, and correctness. The review screen should support:

- Click a word in the script to jump to the relevant point in the audio.
- Inline script edits followed by regeneration.
- Reordering, including, excluding, or omitting traffic items before regeneration.
- Reject-to-fallback when the bulletin is wrong and there is no time to fix it.
- Positive status reporting for priority arterials, not only incident reporting.
- A per-market priority roads list that operators can refine over time.

The editorial rule is simple: information only. The bulletin states facts and avoids advice or assumptions about the listener's situation.

= Integration design

== HERE

HERE Traffic API v7 is the Release 1 data source. HERE provides traffic incident data that can be converted into a radio script. Current known limits are acceptable for the pilot:

- Incident type, location, severity, description, and timing can support a useful bulletin.
- Cause may appear in free text but is not consistently structured.
- Lane counts, diversion details, and traffic-camera imagery are not available through HERE in the current design.
- Camera imagery or road-authority feeds would be separate future integrations, not Release 1 scope.

Weather and fuel should not change the architecture if ARN later confirms them. They would be additional source-data inputs using the same source ingestion pattern.

== Azure OpenAI and ElevenLabs

Azure OpenAI produces the timed traffic script. ElevenLabs produces the broadcast audio. The pilot targets a cloned (Professional Voice Clone) Australian voice, which testing and ElevenLabs' own guidance indicate gives the most natural and consistent broadcast result; this depends on ARN resolving voice talent, consent, and IP. If the clone is not ready in time, the fallback is a voice we design and tune in ElevenLabs rather than an off-the-shelf one. Either way, local place and road names are driven by per-region alias pronunciation rules, with native International Phonetic Alphabet (IPA) pronunciation available on newer models, so the choice of voice does not affect pronunciation control.

== Zetta

Zetta is a core Release 1 integration. The proof-of-concept and test-environment mechanics are already known: approved audio is written to Zetta through SMB file delivery. The productionised design retains this path and adds retry, timeout, and post-write size verification.

The remaining Zetta work is not to discover the integration from zero. It is to confirm environment differences and production mechanics:

- Test and production target server, share, path, and credentials.
- Network path from Azure to the production Zetta environment.
- Whether VPN, ExpressRoute, or another private route is required.
- File naming and metadata expectations per station or market.
- Whether Zetta exposes delivery acknowledgement, status files, API confirmation, or actual on-air time.
- Fallback-fill cart setup for the pilot station clock.

== Ads, Aquira, and live-read sponsorship

Ads and Aquira remain an ARN business and sales-process question. They are not committed Release 1 build scope. The architecture keeps the delivery/content seam open so future sponsor reads, appended credits, or Aquira-driven commercial content can be added without changing the core traffic-generation pipeline.

For SG2, the right decision is to acknowledge the seam and keep implementation out of Release 1 unless ARN separately confirms the sales model and timing requirements.

= Security, identity, and compliance

== Identity and access

The solution uses Microsoft Entra ID for access control. The pilot design uses two roles:

#table(
  columns: (1fr, 3fr),
  inset: 5pt,
  stroke: border,
  table.header([*Role*], [*Permissions*]),
  [Operator], [Review, approve, reject, publish, and regenerate bulletins for assigned markets or stations.],
  [Admin], [Operator permissions plus schedule, configuration, and assignment management.]
)

Operators should be scoped to the markets or stations they own. This prevents an operator from approving the wrong station's bulletin and keeps accountability clear.

== Secrets and service access

- Secrets are stored in Key Vault.
- Managed identity is used for Azure service access where possible.
- Production access is intended to be identity-based rather than connection-string based.
- Key Vault, Storage, and AI Services are designed for private endpoint protection in test and production.

== Compliance and audit posture

The system treats human approval as the publishing decision. Each meaningful operator action records user identity, timestamp, decision, and reason where applicable.

The design already provides the compliance controls as configurable capabilities:

- Per-action audit capture of operator identity, timestamp, decision, and reason.
- A content-safety filter framework supporting banned-term and substitution rules, applied before audio synthesis.
- Configurable storage retention, with immutable (WORM) audit storage available if ARN requires it.

Retention and audit storage were resolved during this review cycle:

- Immutable (WORM) audit storage is *not required* (ARN technology review, 10 June; consistent with the ARN legal review, 9 June). Standard storage with soft delete and restricted audit access is the agreed posture.
- ARN's existing broadcast logging already captures all aired broadcast traffic, but it does not capture scripts or approval evidence. The platform is therefore the system of record for scripts and approval decisions: bulletin audio is retained for at least 90 days, and approval and audit records for two to three years or longer, with the exact figure confirmed by ARN legal (9 June legal review).

What remains are ARN policy decisions that configure these controls, not changes to the design:

- Banned-term sources and the editorial banned-term list.
- Defamation guardrail patterns to encode as content-safety rules.
- ACMA accuracy posture for generated traffic bulletins.

Because these decisions populate or configure controls that already exist in the design, they do not block Stage Gate 2 design approval; they must be confirmed with ARN before live operation.

= Environment, deployment, and operations

== Environment model

The production target is Development, Test, and Production. The design is parameterised per environment.

#table(
  columns: (1fr, 2.2fr, 2.2fr),
  inset: 5pt,
  stroke: border,
  table.header([*Environment*], [*Purpose*], [*Current dependency*]),
  [Development], [Early build, configuration, and functional verification.], [Requires ARN resource group, permissions, app registration, secrets, and private-network inputs.],
  [Test], [Integration and UAT preparation with closer production controls.], [Requires network and private endpoint inputs.],
  [Production], [Pilot operation and handover.], [Requires network, Zetta production path, security, compliance, and operations readiness.]
)

== Deployment model

The deployment model uses Azure DevOps with a multi-stage pipeline: build and test, environment promotion, and a guarded production path. A push to the development branch deploys to Dev. A push to the main branch runs a Bicep what-if preview, then passes two manual approval gates: the first authorises the production deploy, the second authorises the staging-slot swap. The release is deployed to a staging slot, swapped into production, and verified by a post-swap health check that automatically rolls back on failure.

#pagebreak(weak: true)
#align(center)[#image("assets/cicd-pipeline.png", height: 240mm)]
#align(center)[#text(size: 8pt, fill: slate)[Figure 3: Azure DevOps delivery pipeline. Build and test, promotion to Dev, and a two-gate production path with staging-slot swap, post-swap health check, and automatic rollback.]]

== Monitoring and reliability

The monitoring design covers:

- Pipeline health and latency.
- External API health for HERE, ElevenLabs, and Azure OpenAI.
- Approval workflow status and time-in-state.
- Schedule poller catch-up and missed-window events.
- Cost telemetry for tokens and TTS characters.
- Operational alerts for function health, Key Vault access failures, poison queue depth, AI throttling, bulletin heartbeat, and synthesis failures.

Primary operator visibility is the dashboard. Secondary notification is Teams with click-through to the review page. Phone and SMS escalation are not required for the pilot design unless ARN later decides otherwise.

== Non-functional and operational posture

The pilot runs to a best-effort operational standard. Automated health checks, retries, and the fallback paths above keep air covered through short interruptions, and operators are alerted when intervention is needed.

Formal service levels (availability and latency targets), disaster recovery and business continuity, and the steady-state run-cost envelope are confirmed in Phase B, alongside operations runbooks and hypercare, once production load and the final environment topology are known.

= Delivery plan

#table(
  columns: (1fr, 2.8fr, 1.2fr),
  inset: 5pt,
  stroke: border,
  table.header([*Date*], [*Milestone*], [*Owner*]),
  [5 Jun 2026], [SDD review package prepared for Arinco review.], [Julian Codespoti],
  [8 Jun 2026], [Internal Arinco review, then circulation to ARN Business Owners and Steer Co.], [Julian Codespoti, Marc Tricksey],
  [10 Jun 2026], [Requested ARN feedback cut-off.], [ARN reviewers],
  [12 Jun 2026], [Stage Gate 2 review.], [ARN],
  [15 Jun 2026], [Feedback tidy-up and formal sign-off if required.], [ARN and Arinco],
  [4 Jul 2026], [Board-confidence gate for ATN cancellation notice path.], [ARN],
  [1 Aug 2026], [UAT commences.], [ARN and Arinco],
  [21 Aug 2026], [Stage Gate 3: Pilot Successful Completion.], [Steer Co],
  [26 Aug 2026], [Arinco engagement closes and handover is complete.], [Arinco]
)

= Risks and dependencies

== Top risks
#table(
  columns: (1fr, 2fr, 2.5fr),
  inset: 5pt,
  stroke: border,
  table.header([*Rating*], [*Risk*], [*Mitigation*]),
  [High], [External service dependency. HERE, Azure OpenAI, and ElevenLabs have no alternate provider, so an outage can interrupt bulletin generation.], [Health checks and retries on each service, a fallback-fill cart, and reuse of the most recent same-day approved bulletin keep air covered during a short outage.],
  [High], [Pilot quality bar. Voice, pronunciation, and script quality must be judged same or better than ATN by the ARN review group, and that judgement gates the 4 July board-confidence decision.], [Target a cloned Australian voice, with a voice we design and tune in ElevenLabs as a fallback that needs no voice talent, consent, or IP; demo to the ARN review group early, tune local-name pronunciation with per-region alias dictionaries, and run a rejection feedback loop so corrections accumulate.],
  [Medium], [Scope expansion. Pressure to pull ads, Aquira, weather, fuel, or broader rollout into the pilot would stretch the fixed timeline.], [Hold the candidate, deferred, and out-of-scope classification in this document; route any addition through ARN as an explicit scope decision.],
  [Medium], [Content accuracy and defamation. A generated bulletin could state an incident incorrectly or refer to a party in a way that creates editorial or legal exposure.], [Every bulletin requires explicit operator approval before publish, a content-safety filter applies banned-term and substitution rules before synthesis, and each approval is recorded with operator identity, timestamp, and reason for audit.],
)
== Open dependencies and actions
#four_col_table(
  table.header([*Needed by*], [*Dependency*], [*Owner*], [*Status*]),
  [This review (SG2)], [Approve the SDD and ratify the three design postures: scheduling and delivery, multi-station sharing, voice.], [Business Owners and Steer Co], [Requested at this review],
  [Before first deploy], [Entra ID app registration and client ID, dev/test/prod resource groups.], [ARN (Nick Werba)], [Requested; awaiting ARN],
  [Before first deploy], [Azure OpenAI quota and Teams notification channel configuration.], [ARN (Nick Werba)], [To initiate],
  [Before go-live], [Entra group mapping, named operators, content safety rules, approval framing.], [ARN (Nick Werba, Fiona Ellis-Jones, business owners)], [Audit retention and WORM resolved (9 to 10 Jun); remainder sequenced for go-live],
  [Before production cutover], [Production and test Zetta server, share, path, credentials, network route, delivery confirmation capability, fallback-fill cart configuration.], [ARN broadcast engineering (Gerry Pyne)], [Requested; awaiting ARN],
  [Before pilot go-live], [Voice talent, consent, and IP for the cloned Australian voice (the designed-voice fallback needs none of these).], [ARN (content and legal)], [Open (ARN decision)],
  [Phase B or later], [Operations runbooks, hypercare detail, broader rollout planning.], [ARN and Arinco], [Scheduled for Phase B],
  [Not committed], [Ads and Aquira build, weather and fuel implementation, SaaS, full rollout, commercial GTM.], [ARN decision if pursued], [Out of scope unless ARN confirms]
)

= Decision appendix A: Scheduling and delivery reliability

The pilot must fit a human approval step into a tight radio timing window. The operator-workflow discovery established the practical constraints: generation starts before the slot, audio is ready quickly, Zetta needs buffer time, and a rejection can consume most of the available window.

#table(
  columns: (1.2fr, 2.4fr, 2.4fr),
  inset: 5pt,
  stroke: border,
  table.header([*Design area*], [*Recommendation*], [*Reason*]),
  [Time anchoring], [Use fixed-event delivery slot anchoring with generation lead time tuned per station.], [Matches radio operations better than a pure wall-clock trigger and handles live drift.],
  [Generation strategy], [Generate a small bucket of candidates where needed, with configurable lead time.], [Avoids waiting for serial reject-and-regenerate in an eight-minute approval window.],
  [Delivery reliability], [Use a dedicated fallback-fill cart plus delivered-status confirmation from file write verification.], [Prevents dead air and gives operators a clearer state even if full on-air poll-back is unavailable.],
  [Deferred detail], [Full Zetta poll-back and exact cadence decisions remain to be confirmed.], [These depend on Zetta capability and ARN content policy.]
)

*SG2 ask:* Ratify fixed-event anchoring, multi-candidate generation where justified, and a mandatory fallback-fill cart as the design direction.

= Decision appendix B: Multi-station sharing and station tags

The pilot is single-station, but the architecture should not force the wrong model for later rollout. The operator-workflow discovery established that the traffic body itself can be pure traffic and therefore shareable across co-located stations.

#table(
  columns: (1.3fr, 2.5fr, 2.2fr),
  inset: 5pt,
  stroke: border,
  table.header([*Area*], [*Recommendation*], [*Reason*]),
  [Traffic body], [Share one approved pure-traffic body across co-located same-slot stations by default.], [Reduces operator load at peak times and keeps market traffic consistent.],
  [Station tag or credit], [Handle tags or credits as separate appended carts where ARN requires them.], [Preserves station identity without forcing separate generated bodies for every station.],
  [Exceptions], [Generate per-station bodies only where content, format, or persona genuinely differs.], [Cost is manageable, but operator review capacity is the real constraint.],
  [Open decision], [ARN confirms whether per-station tags remain required.], [This is a content/commercial policy decision.]
)

*SG2 ask:* Ratify shared traffic body plus tag-as-separate-cart as the design direction, with tag policy and Zetta mechanics confirmed after SG2.

= Decision appendix C: Voice strategy

Voice and script quality are core to the 4 July confidence gate. The pilot must sound Australian, handle local place names, and avoid sounding synthetic. The most natural, consistent result comes from a cloned (Professional Voice Clone) Australian voice, so that is the pilot target; it depends on ARN resolving voice talent, consent, and IP. We do not rely on an off-the-shelf voice: if the clone is not ready in time, the fallback is a custom voice we design and tune in ElevenLabs.

#table(
  columns: (1.2fr, 2.5fr, 2.2fr),
  inset: 5pt,
  stroke: border,
  table.header([*Area*], [*Recommendation*], [*Reason*]),
  [Pilot voice], [Target a cloned (Professional Voice Clone) Australian voice.], [ElevenLabs' guidance and our testing show a clone gives the most natural and consistent result, and the best chance of meeting the same-or-better-than-ATN bar.],
  [Pronunciation], [Use per-region alias pronunciation rules for road and suburb names.], [Reliable across voice models and unaffected by whether the voice is designed or cloned; easy to tune during pilot review.],
  [Feedback loop], [Capture rejection reasons and local pronunciation corrections.], [Improves output without retraining the model.],
  [Fallback], [If the clone is not ready by 4 July, use a custom voice we design and tune in ElevenLabs.], [A designed voice needs no talent, consent, or IP, so it protects the timeline; clone quality remains the goal.]
)

*SG2 ask:* Endorse a cloned (Professional Voice Clone) Australian voice as the pilot target and commit to resolving voice talent, consent, and IP. A custom voice we design in ElevenLabs is the fallback if the clone is not ready by 4 July, and it needs no voice talent, consent, or IP.

= Approval summary

#callout("Approval requested", [
ARN is asked to approve this SDD as the Stage Gate 2 solution design. Open dependencies and sequenced decisions are tracked in the risks and dependencies section.
])

#table(
  columns: (1.4fr, 2.2fr),
  inset: 6pt,
  stroke: border,
  table.header([*Approver group*], [*Decision*]),
  [Product and content owners], [Confirm Release 1 scope, human approval, fallback posture, and content-quality bar.],
  [Technology owner], [Confirm architecture, Azure deployment approach, identity/security posture, and integration boundaries.],
  [Commercial owner], [Confirm Ads/Aquira remains an open business question and not committed Release 1 build scope.],
  [Steer Co], [Confirm the design supports the 4 July confidence gate and 26 August handover path.]
)
