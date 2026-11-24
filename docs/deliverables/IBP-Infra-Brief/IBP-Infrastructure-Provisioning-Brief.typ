// Arinco-styled Infrastructure Provisioning Brief. Compile with the bundled brand font:
//   typst compile --font-path fonts "IBP-Infrastructure-Provisioning-Brief.typ" "IBP Infrastructure Provisioning Brief.pdf"

// ---- Arinco brand palette (from the corporate template theme) ----
#let navy = rgb("#001A31")
#let blue = rgb("#0572A8")
#let titleblue = rgb("#003E75")
#let skyblue = rgb("#00B0F0")
#let yellow = rgb("#FBCA0E")
#let slate = rgb("#5b6b7a")
#let border = rgb("#cbd5e1")
#let band = rgb("#eef4f8")

#set document(title: "Information Bulletin Platform - Infrastructure Provisioning Brief", author: "Arinco")
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
#set table(
  fill: (x, y) => if y == 0 { blue } else if calc.even(y) { band } else { white },
  stroke: 0.5pt + border, inset: 6pt, align: horizon,
)
#show table.cell.where(y: 0): set text(fill: white, weight: "bold", size: 9.5pt)

// Callouts.
#let box(title, body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: band, stroke: (left: 3pt + blue))[#text(weight: "bold", fill: blue)[#title] #v(2pt) #body]
#let recommend(body) = block(width: 100%, inset: 9pt, radius: 3pt,
  fill: rgb("#fff8e1"), stroke: (left: 3pt + yellow))[#text(weight: "bold", fill: rgb("#8a6d00"))[Recommendation] #v(2pt) #body]

// ================================================================ COVER
#page(margin: 0pt, header: none, footer: none,
  background: image("brand/arinco-cover.png", width: 100%, height: 100%))[
  #pad(left: 24mm, right: 24mm, top: 30mm, bottom: 24mm)[
    #image("brand/arinco-logo.png", width: 54mm)
    #v(54mm)
    #text(size: 30pt, weight: "bold", fill: titleblue)[Information Bulletin Platform]
    #v(6pt)
    #text(size: 19pt, weight: "bold", fill: blue)[Infrastructure Provisioning Brief]
    #v(10pt)
    #text(size: 12pt, fill: slate)[What ARN provisions, and what Arinco deploys]
    #v(16mm)
    #grid(columns: (34mm, auto), row-gutter: 6pt, column-gutter: 8pt,
      text(weight: "bold")[Prepared for], [Australian Radio Network, Infrastructure and Security],
      text(weight: "bold")[Prepared by], [Julian Codespoti, Arinco],
      text(weight: "bold")[Date], [18 June 2026],
      text(weight: "bold")[Status], [For ARN action],
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
      [Infrastructure Provisioning Brief], image("brand/arinco-logo.png", width: 24mm))
    #v(-4pt)
    #line(length: 100%, stroke: 0.5pt + border)
  ],
  footer: context [
    #line(length: 100%, stroke: 0.5pt + border)
    #v(-3pt)
    #set text(size: 8pt, fill: slate)
    #grid(columns: (1fr, auto),
      [Information Bulletin Platform  |  Infrastructure Provisioning Brief],
      [Page #counter(page).display()])
  ],
)
#counter(page).update(1)

// ================================================================ CONTENTS
#text(size: 17pt, weight: "bold", fill: navy)[Contents]
#v(2pt)
#line(length: 100%, stroke: 1pt + blue)
#v(7pt)
#outline(title: none, depth: 1)

= Purpose

The platform's Azure resource naming convention is agreed, and the deployment automation already matches it. It has been validated end to end in a sandbox subscription. Two things remain before ARN can begin provisioning: agreement on which work sits with ARN and which with Arinco, and a small set of inputs from ARN. Both are set out below.

#box("In short", [
ARN provisions the foundation, which is the subscription, the resource groups, deployment access, sign-in identity, the network, and notifications. Arinco deploys everything inside the resource groups through its deployment pipeline. A development environment can be stood up as soon as a subscription, the resource groups, deployment access, and the sign-in registration exist. Nothing else holds it up.
])

= Responsibilities

#table(
  columns: (1.5fr, 1.2fr, 2.6fr),
  table.header([*Area*], [*Owner*], [*What it involves*]),
  [Subscription and resource groups], [ARN], [Confirm the subscription, then create the resource groups, or grant subscription-scoped access so the deployment creates them.],
  [Deployment access], [ARN], [Contributor access, with just-in-time elevation to Owner for the one-time role assignments, and Key Vault Administrator on the Key Vault group.],
  [Sign-in and identity], [ARN identity team], [Two application registrations and the two role groups.],
  [Network], [ARN network team], [Virtual network, subnets, Private Link DNS zones, and hub routing.],
  [Notifications], [ARN], [A Teams channel for approval and failure notices, and on-call contacts, per environment.],
  [Security acceptance], [ARN security], [Review and accept the security posture in section 8.],
  [Platform resources], [Arinco], [Function App, storage, Key Vault, AI service, monitoring, web app, notification logic, alerts, and private endpoints, deployed into the groups above, together with application configuration, secrets, and the application itself.],
)

ARN does not deploy the platform resources. ARN provides the subscription, the resource groups, access, identity, and the network, and the deployment pipeline fills them.

= Environments and naming

The naming convention agreed on 11 June is in use. The deployment computes every resource name from the environment, so there is nothing to name by hand. There are three environments, development, test, and production, all in Australia East.

#table(
  columns: (1.5fr, 2.6fr),
  table.header([*Resource group*], [*Contents*]),
  [`IBP-AUE-{ENV}-RSG-APP01`], [Function App, plan, web app, notification logic, monitoring, AI service, and search.],
  [`IBP-AUE-{ENV}-RSG-NTW01`], [Private endpoints, with the virtual network and subnets. Private endpoints sit here; there is no separate private-endpoint group.],
  [`IBP-AUE-{ENV}-RSG-KYV01`], [Key Vault and its secrets, certificates, and keys.],
  [`IBP-AUE-{ENV}-RSG-STA01`], [Storage account.],
  [`IBP-AUE-{ENV}-RSG-LAW01`], [Log Analytics, only if logging stays platform-side (see section 9).],
)

= Inputs required from ARN

#table(
  columns: (1.4fr, 1.2fr, 2.7fr),
  table.header([*Input*], [*Owner*], [*Purpose*]),
  [Subscription], [ARN platform], [The subscription the platform will use, its identifier, and a deployment service connection.],
  [Resource groups], [ARN platform], [The five groups in section 3, or subscription-scoped access so the deployment creates them.],
  [Deployment access], [ARN platform], [Contributor, with brief Owner elevation for the role assignments, and Key Vault Administrator on the Key Vault group.],
  [Application registrations], [ARN identity], [A sign-in registration for the web app, and an API registration that defines the Operator and Admin roles. Return the two application identifiers, the tenant identifier, and the API scope.],
  [Role groups], [ARN identity], [Two directory groups mapped to the Operator and Admin roles, with their object identifiers. Membership stays with ARN.],
  [Network], [ARN network], [Per environment, the application-integration subnet and the private-endpoint subnet, and the five Private Link DNS zones (blob, table, queue, vault, and Cognitive Services).],
  [Logging], [ARN platform], [Whether telemetry writes to a central Log Analytics workspace, with its resource identifier, or to a platform-side workspace.],
  [Alert routing], [ARN platform], [The production distribution lists and action-group identifiers, or the on-call email and mobile contacts.],
  [Notifications], [ARN platform], [The Teams team and channel for approval and failure notices, per environment.],
  [Capacity], [ARN platform], [Confirmation of Azure OpenAI capacity in Australia East for the model deployment.],
)

Only the first three are needed to begin the development deployment. The application registration is required for sign-in rather than for deployment, and the remaining items run in parallel.

= Fastest path to a development environment

+ Confirm the subscription. This is ARN's decision. Development can sit in an existing subscription to get moving, and the permanent home for test and production can be decided later.
+ Create the resource groups and grant access, or grant subscription-scoped access so the deployment creates them.
+ Deploy development with public service endpoints first, so it does not wait on the private network. The private posture, with private endpoints and public access closed, is built for test and production.
+ In parallel, the identity team begins the application registrations, and the network team begins the test and production network.

One confirmation is needed: whether development may run with public endpoints while the private network is built for test and production.

#box("On deployment access", [
The deployment runs at subscription scope, so the deployment identity needs Contributor across the subscription, with brief Owner elevation for the role assignments. Those assignments can instead be made by ARN if preferred. If ARN would rather pre-create the resource groups and keep access scoped to them, a short call will confirm the change to the deployment scope.
])

= Network design and sizing

For the network team.

- One virtual network per environment. A `/24` is ample, and a `/25` is enough if address space is tight.
  - Application-integration subnet, `/26`. The Function App needs its own delegated subnet.
  - Private-endpoint subnet, `/27`. One address per endpoint, for storage, Key Vault, and the AI service.
- Five Private Link DNS zones, for blob, table, queue, vault, and Cognitive Services, created or linked through the hub.
- All outbound traffic egresses through the hub firewall. The external services are HERE, ElevenLabs, and Azure OpenAI, plus playout (see section 7).

= Connectivity to playout

The platform's only outbound link to ARN's on-premises broadcast environment is delivery of approved audio to the Zetta ingest. The current direction agreed with broadcast engineering is a single ingest location that each site collects its own files from. The likely effect on the network is one outbound rule from the application subnet, through the hub, to that ingest location, rather than inbound access or a connection to each studio. The transport is being confirmed, and the exact rule will follow. No action is needed yet.

= Security posture

For ARN security review and acceptance.

- Sign-in is enforced at the Function App through Azure AD and fails closed. There is no anonymous access.
- Service-to-service authentication uses managed identity. Keys are not held in application settings.
- Operator and Admin access is controlled by role, through the API registration and the two directory groups.
- Storage, Key Vault, and the AI service are reached through private endpoints in test and production, with public data-plane access closed.
- Key Vault uses role-based access. Partner credentials are held only in Key Vault.
- Retention follows the June compliance position. Bulletin audio is kept for at least 90 days, and approval and audit records for two to three years or more, with a read-only group over the evidence.

ARN security to confirm acceptance, or to note any conditions to close first.

= Open items

#table(
  columns: (1.9fr, 1.3fr, 1.1fr, 1.2fr),
  table.header([*Item*], [*Type*], [*Owner*], [*Needed by*]),
  [Subscription model: a dedicated platform subscription, or platform-named groups in an existing subscription], [Decision at this gate], [ARN platform], [Development deploy],
  [Log Analytics location: central or platform-side], [Decision], [ARN platform], [Monitoring sign-off],
  [Production alert distribution lists and action groups], [Pre-go-live dependency], [ARN platform], [Go-live],
  [Network inputs, the subnets and DNS zones for test and production], [Pre-build dependency], [ARN network], [Test and production deploy],
  [Application registrations and role groups], [Pre-build dependency], [ARN identity], [Sign-in],
)

= Architecture overview

The diagrams that follow show the platform structure, its placement in the network, and the security boundaries.

#block(breakable: false)[
  #image("assets/platform-architecture.png", width: 100%)
  #v(5pt)
  #align(center)[#text(size: 8pt, fill: slate)[Figure 1: Platform components and the main data flows. Operators and admins sign in through Microsoft Entra ID, the Function App runs the bulletin pipeline, and approved audio is delivered to the Zetta playout system.]]
]

#v(12pt)

#block(breakable: false)[
  #image("assets/network-topology.png", width: 100%)
  #v(5pt)
  #align(center)[#text(size: 8pt, fill: slate)[Figure 2: Network and deployment topology, one set per environment. The platform runs in its own virtual network. The Function App sits in the compute subnet, and storage, Key Vault, and the AI service are reached through private endpoints in the data subnet.]]
]

#v(12pt)

#block(breakable: false)[
  #image("assets/security-overview.png", width: 100%)
  #v(5pt)
  #align(center)[#text(size: 8pt, fill: slate)[Figure 3: Security architecture. Sign-in fails closed through Microsoft Entra ID, Operator and Admin access is enforced server side, secrets stay in Key Vault, and service-to-service access uses managed identity.]]
]

= Sequence

+ Confirm the subscription.
+ Create the resource groups and grant access.
+ Deploy development with public endpoints.
+ In parallel, begin the application registrations and the test and production network.
+ Build the private posture for test and production.
+ Confirm security acceptance before production.

Anything here that ARN would prefer to handle differently can be adjusted. A short call to walk through it is welcome.
