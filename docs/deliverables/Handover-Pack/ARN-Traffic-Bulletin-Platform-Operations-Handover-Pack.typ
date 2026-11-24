// Arinco-styled Operations Handover Pack. Compile with the bundled brand font:
//   typst compile --font-path fonts "ARN-Traffic-Bulletin-Platform-Operations-Handover-Pack.typ" "ARN Traffic Bulletin Platform - Operations Handover Pack.pdf"
//
// This is a SIGNED COVER, not the documentation. Every operational, testing and
// governance detail lives on the handover documentation site, which is built
// from docs/ by scripts/build-docs-site.py and shipped offline by
// scripts/package-docs-offline.sh. Keeping detail here as well would create a
// second copy to fall out of date, which is exactly what happened to the
// hand-copied figures this file used to carry.

// ---- Document metadata (single source of truth) ----
#let doc_version = "2.6 draft"

// ---- Arinco brand palette (from the corporate template theme) ----
#let navy = rgb("#001A31")
#let blue = rgb("#0572A8")
#let titleblue = rgb("#003E75")
#let skyblue = rgb("#00B0F0")
#let yellow = rgb("#FBCA0E")
#let slate = rgb("#5b6b7a")
#let border = rgb("#cbd5e1")
#let band = rgb("#eef4f8")
#let zebra = rgb("#dfe9ef")

#set document(title: "ARN Traffic Bulletin Platform - Operations Handover Pack", author: "Arinco")
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

// Branded tables: blue header row, zebra body, white bold header text.
#set table(
  fill: (x, y) => if y == 0 { blue } else if calc.even(y) { zebra } else { white },
  stroke: 0.5pt + border, inset: 7pt, align: horizon,
)
#show table.cell.where(y: 0): set text(fill: white, weight: "bold", size: 9.5pt)

// Callouts.
#let callout(title, body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: band, stroke: (left: 3pt + blue))[#text(weight: "bold", fill: blue)[#title] #v(2pt) #body]
#let attention(body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: rgb("#fff8e1"), stroke: (left: 3pt + yellow))[#text(weight: "bold", fill: rgb("#8a6d00"))[Attention] #v(2pt) #body]
#let acceptbox(title, body) = block(width: 100%, inset: 11pt, radius: 3pt,
  fill: rgb("#e8eef4"), stroke: (left: 4pt + navy))[#text(size: 12pt, weight: "bold", fill: navy)[#title] #v(3pt) #body]

// Placeholder cell for values completed at finalisation.
#let tbc = text(style: "italic", fill: slate)[TBC]
#let tbcsession = text(style: "italic", fill: slate)[TBC at the handover session]
#let tbcfinal = text(style: "italic", fill: slate)[Confirmed at finalisation]

// ================================================================ COVER
#page(margin: 0pt, header: none, footer: none,
  background: image("brand/arinco-cover.png", width: 100%, height: 100%))[
  #pad(left: 24mm, right: 24mm, top: 30mm, bottom: 24mm)[
    #image("brand/arinco-logo.png", width: 54mm)
    #v(52mm)
    #text(size: 30pt, weight: "bold", fill: titleblue)[ARN Traffic Bulletin Platform]
    #v(6pt)
    #text(size: 19pt, weight: "bold", fill: blue)[Operations Handover Pack]
    #v(10pt)
    #text(size: 12pt, fill: slate)[What ARN receives at handover, and the acceptance record]
    #v(14mm)
    #grid(columns: (34mm, auto), row-gutter: 6pt, column-gutter: 8pt,
      text(weight: "bold")[Prepared for], [Australian Radio Network],
      text(weight: "bold")[Prepared by], [Delivery representative],
      text(weight: "bold")[Milestone], [Operational handover],
      text(weight: "bold")[Date], [4 September 2026],
      text(weight: "bold")[Version], [#doc_version],
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
      [Operations Handover Pack], image("brand/arinco-logo.png", width: 24mm))
    #v(-4pt)
    #line(length: 100%, stroke: 0.5pt + border)
  ],
  footer: context [
    #line(length: 100%, stroke: 0.5pt + border)
    #v(-3pt)
    #set text(size: 8pt, fill: slate)
    #grid(columns: (1fr, auto),
      [ARN Traffic Bulletin Platform  |  Operations Handover Pack  v#doc_version],
      [Page #counter(page).display()])
  ],
)
#counter(page).update(1)

// ================================================================ REVISION SHEET
#heading(numbering: none, outlined: false, level: 1)[Revision and sign-off]
#heading(level: 2, numbering: none, outlined: false)[Change record]
#table(columns: (auto, auto, 1.4fr, 2.6fr),
  table.header([*Date*], [*Version*], [*Author*], [*Change description*]),
  [20 Jul 2026], [1.0 draft], [Delivery representative], [Initial pack: platform overview, operations, service levels, resilience, support model, run costs, access transfer, quality evidence, session plan, documentation index, known risks, quick reference.],
  [28 Jul 2026], [2.0 draft], [Delivery representative], [Restructured as a signed cover. The operational, testing and governance detail moved to the handover documentation site, which is now the received documentation set; this document keeps only what the signatories accept: what is handed over, where to read it, the open items, the top limitations, and the sign-off block.],
  [10 Aug 2026], [2.1 draft], [Delivery representative], [Documentation site published and its address recorded. Documentation set corrected to seven groups with the addition of the developer group, cross-references re-pointed to the current group names, production state re-trued to deployed and parked between pilot runs, and the open items re-trued against the alert-routing decision and the outstanding penetration-test decision.],
  [3 Sep 2026], [2.2 draft], [Delivery representative], [Release state re-trued after the final promotion: production runs the accepted release with its station schedules switched off, test runs the release that was accepted in development, and the offline documentation bundle was rebuilt from the published site.],
  [3 Sep 2026], [2.3 draft], [Delivery representative], [Deliverable accuracy pass against the live platform. The technical session's architecture statement corrected: the model route differs by environment, with production on the raw-model route and test and development on versioned Foundry agents, rather than one raw-model route everywhere. The Key Vault list corrected to the secrets that exist, two named secrets recorded as not yet created, and one recorded as inert and to be left empty so managed-identity authentication is preserved. Recorded that the test and production vaults refuse a caller outside the virtual network, so secret rotation cannot be performed from a workstation. All sixteen console screenshots recaptured, and two that had silently captured a page instead of a dialog were fixed at source. The platform diagram's AI component relabelled, and the deep-dive deck and cost estimate marked where they describe the superseded pipeline.],
  [3 Sep 2026], [2.4 draft], [Delivery representative], [The defect log handed over as an empty register rather than as a snapshot of open faults. The behaviours identified before handover are stated as current platform behaviour with their workarounds under Known limitations, since they are characteristics of the delivered platform rather than findings awaiting triage, and the register keeps the finding workflow, severity scheme and close-out rules for ARN to record each acceptance run in.],
  [3 Sep 2026], [2.5 draft], [Delivery representative], [Open items reduced to what the platform actually depends on: the two authority feeds that need ARN-held access, the station clocks that decide whether delivered audio airs, and the transfer of the access the delivery representative holds today. ARN's staffing, governance and content decisions were removed from the register and the cover, since they are ARN's to make rather than items a supplier hands over.],
  [4 Sep 2026], [2.6 draft], [Delivery representative], [Access transfer stated as the four verified actions rather than a pointer: the platform administrator group, the release approvers, the Teams notification consent and the sign-in secret expiry, each with the consequence of leaving it. Environment state re-trued against the live platform on 3 September 2026, and the cover's description of the access page corrected to the accountable role it actually records.],
)

The pack is reissued once, after the handover sessions. At finalisation the
documentation site address is confirmed, the escalation contact matrix is
completed, session recordings and attendance are linked, the ARN dependencies are
re-trued, the offline documentation bundle is rebuilt and attached, and the
version becomes 3.0 final, approved by the ARN technology lead. (The rule read
"2.0 final" until 2026-09-03, by which point the change record had already
reached 2.2 draft through two reissues, so the stated target version was one
the document had passed.)

#heading(level: 2, numbering: none, outlined: false)[Reviewers]
#table(columns: (1.4fr, auto, 1.4fr, 1.6fr),
  table.header([*Date*], [*Version reviewed*], [*Reviewer*], [*Position*]),
  [], [], [ARN technology lead], [Technology Lead, ARN],
  [], [], [ARN support nominees], [Platform support (named at the handover session)],
)

// ================================================================ BODY

= What is being handed over

The platform generates radio traffic bulletins end to end. On each station's
schedule it pulls live traffic conditions, drafts a bulletin script in the
station's editorial style, has an independent reviewer pass judge the draft,
synthesises broadcast audio in the station's voice, and presents the result to
an operator for review. Approved bulletins are delivered to the playout
system; anything that cannot be produced safely falls back to the most recent
same-day bulletin for a clear road or approved standby audio, so nothing false
or empty airs.

Three environments run in a dedicated Azure subscription in the ARN tenant,
all in Australia East.

#table(columns: (auto, 1.4fr, 2.6fr),
  table.header([*Environment*], [*Purpose*], [*State at handover*]),
  [Dev], [Engineering], [Running],
  [Test], [Pilot and UAT, pilot stations KIIS 90s Sydney and Star 102.7 FM Cairns], [Running. Both stations generate on their schedules, and delivery to playout is switched off on both, so bulletins are produced for review without reaching a station library],
  [Prod], [Live broadcast], [Running the accepted release, proven end to end, with its station schedules currently switched off so no bulletin airs until ARN turns one on],
)

The documentation is handed over in two forms, which carry identical content:

#table(columns: (1.2fr, 2.8fr),
  table.header([*Form*], [*Where*]),
  [Documentation site], [Azure Static Web App #raw("IBP-AUE-PRD-SWA-DOC01") in the platform subscription. Address: #link("https://red-pond-03b957f00.7.azurestaticapps.net")[red-pond-03b957f00.7.azurestaticapps.net]. A permanent ARN address replaces this one when the custom domain is bound.],
  [Offline bundle], [#raw("arn-documentation-offline.zip"), attached to this pack. A complete self-contained copy that needs no network and no Azure access, so the documentation survives supplier identities lapsing after handover.],
)

Also handed over: the source repository in Azure DevOps (Information Bulletin
project, #raw("ARN-Traffic") repository), which is the system of record and
carries the infrastructure-as-code and delivery pipeline; and the two recorded
handover sessions, Session A for operators and content and Session B for ops
and support engineers.

= The documentation set

The documentation site is the received documentation set. It is organised by
who reads it, in seven groups.

#table(columns: (1.1fr, 3.2fr),
  table.header([*Group*], [*What it covers*]),
  [Start here], [What the platform does, the vocabulary the rest of the site uses, how to sign in, the questions asked most often, and a checklist for the first day of ownership.],
  [Using the console], [For the content team. Reviewing and approving a bulletin before it airs, changing what bulletins say without a developer, and the settings that differ from one station to the next. Nothing in this group needs Azure access.],
  [Running the platform], [For platform support. Incident response, the command reference, monitoring, alerts and notifications, service levels, the deployed environments, external services, accounts and access, resilience and recovery, and running costs.],
  [How it works], [The solution in one read, the platform architecture, the published diagrams, and the design choices that shape how it has to be operated.],
  [Assurance], [The capacity and performance the platform has been proven at, what it deliberately does not do, the acceptance testing, and the register ARN records its own findings in.],
  [Handover], [The support model, the items ARN needs to action, and what accepting the handover means.],
  [For developers], [For whoever changes the code. How the solution is laid out, building and running it on a workstation, the delivery pipeline, and the end-to-end, load and performance test harnesses. Last in the navigation: most readers can skip it.],
)

#callout("Reading it offline")[Open #raw("index.html") in the bundle to read
every page, diagram and screenshot with no network. Full-text search needs the
bundled #raw("serve.sh") (macOS or Linux) or #raw("serve.cmd") (Windows),
because the search index is fetched as a file rather than embedded in the
page. The bundle's #raw("README.txt") states this and the exact command.]

= Where the platform depends on ARN
Three places the platform reaches something only ARN holds, plus the transfer
of the access we hold today. ARN's own staffing, governance and content
decisions are not recorded here: they are ARN's to make, and the platform
behaviour they relate to is documented on the site. The full detail is under
Handover.

#table(columns: (1.3fr, 2.3fr, 1.3fr, 2.2fr),
  table.header([*Needed by*], [*Action*], [*Owner*], [*Consequence if incomplete*]),
  [Before relying on Queensland authority coverage], [Request a dedicated QLDTraffic authority key and store it in each environment's Key Vault], [ARN technology lead with the vendor account owner], [The shared demonstration key can be throttled, leaving Queensland bulletins on commercial evidence only],
  [Before relying on Traffic SA coverage], [Ask South Australia's road-operations team to allowlist the platform's Azure egress address], [ARN technology lead], [Traffic SA continues refusing the platform's address, so South Australian bulletins fall back to the commercial feed],
  [Before each station's next slot], [Schedule the delivered bulletins into each pilot station's clock], [ARN broadcast engineering with the station content teams], [Delivered bulletins do not air until scheduled],
  [On or before the last engagement day], [Transfer the access held through the delivery representative's identities: add named ARN administrators to the platform's Owner and Contributor groups, add ARN approvers to the test and production release environments, re-authorise each environment's Teams notification connection as a lasting ARN account, and record the sign-in secret expiry for the test and production application registrations], [ARN technology lead with the identity, pipeline and M365 owners], [Three of these fail silently. Without ARN approvers no release can be approved, including an emergency fix; without re-consent every alert and approval card stops arriving while the alert rules keep firing; an unrecorded sign-in secret expiry ends console access for every operator at once],
)

#attention[Supplier-held access is held through the delivery representative's
two identities, which lapse after handover. Verified on 3 September 2026: the
platform's own administrator group holds only those identities, the sole
approver on both release environments is one of them, and all three Teams
notification connections are consented as one of them. ARN's all-subscription
administrator groups still hold the platform team, so nobody is locked out of
Azure, but no one is named for this platform until ARN acts. The Access and
accounts page on the documentation site lists each asset, the accountable ARN
role, the ARN action and the consequence if it is not actioned; the first day
page opens with these four transfer steps and how to verify each one.]


= Known limitations and top risks

The entries that most affect running the platform after handover. Full detail
is on the documentation site: Known limitations under Assurance, and the
recovery position under Running the platform.

#table(columns: (auto, 3.4fr),
  table.header([*Entry*], [*What it means for ARN*]),
  [Single region, no automatic failover], [The platform runs in Australia East only. In a regional outage, bulletin processing and the console stop until the region recovers or the platform is redeployed elsewhere; playout keeps airing whatever fallback content the stations hold locally. Accepted for the pilot.],
  [Vendor single points of failure], [HERE, Azure OpenAI and ElevenLabs have no fallback provider. A sustained outage stops new bulletin production while fallback content airs. Candidate alternatives are identified; a post-pilot decision.],
  [ACMA regulatory review not yet done], [Regulatory review of AI-generated broadcast content is an ARN-led timeboxed assessment, needed before production broadcast.],
  [Content-safety rule lists not yet populated], [The enforcement framework is live but the banned-word and substitution lists wait on ARN editorial and compliance review.],
  [Production playout ingest confirmation], [On 30 July 2026, two production bulletins for KIIS 90s Sydney and Star 102.7 FM Cairns were written to the live playout share and confirmed in the station libraries. Zetta provides no receipt or on-air confirmation; ARN content teams must still schedule delivered bulletins in the station clock.],
  [No independent security assurance], [The deployed controls are stated in Known limitations: fail-closed authentication, private endpoints, encrypted and version-protected storage, and dependency and secret scanning in the delivery pipeline. No independent penetration test has been performed against the platform. Commissioning one, or formally accepting its absence, is an open item.],
  [Post-handover support not yet staffed], [The support model and resourcing recommendation on the documentation site are the input; the decision is ARN's at the handover session.],
)

= Acceptance and sign-off

#acceptbox("Handover acceptance", [Acceptance of this pack means ARN confirms
it has received the documentation set and environments described here, at
at least two named platform support contacts have completed the handover session
with access to the runbook, dashboards and alerts, and the open items above carry
named ARN owners.])

#v(4pt)

After acceptance, ARN owns routine operation and defect handling. Any further
remediation follows ARN's support and change process.

#v(6pt)
#grid(columns: (1.6fr, 1.6fr, 1.4fr, 1fr), row-gutter: 14pt, column-gutter: 10pt,
  text(weight: "bold")[Name], text(weight: "bold")[Position], text(weight: "bold")[Signature], text(weight: "bold")[Date],
  line(length: 90%, stroke: 0.5pt + slate), [Technology Lead, ARN], line(length: 90%, stroke: 0.5pt + slate), line(length: 90%, stroke: 0.5pt + slate),
  line(length: 90%, stroke: 0.5pt + slate), [Platform support contact 1], line(length: 90%, stroke: 0.5pt + slate), line(length: 90%, stroke: 0.5pt + slate),
  line(length: 90%, stroke: 0.5pt + slate), [Platform support contact 2], line(length: 90%, stroke: 0.5pt + slate), line(length: 90%, stroke: 0.5pt + slate),
  line(length: 90%, stroke: 0.5pt + slate), [Delivery representative], line(length: 90%, stroke: 0.5pt + slate), line(length: 90%, stroke: 0.5pt + slate),
)
