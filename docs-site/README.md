# ARN documentation site

**This site is the handover deliverable.** It is the documentation set ARN
receives: sidebar navigation, full-text search, seven presentation-grade
architecture figures and a current console screenshot set, dark and light
themes, and a printable page per document. The signed Operations Handover Pack
is a short cover PDF that names this site and carries the acceptance and
sign-off block.

## The one rule

**`src/content/docs/` and `src/assets/` are generated. Never edit them.** Both
are deleted and rebuilt on every sync, and both are gitignored. `docs/` in the
repository root remains the single source of truth; edit there.

## Commands

```sh
npm install          # once
npm run dev          # sync + dev server with hot reload
npm run build        # sync + static build into dist/
npm run preview      # serve the built site
npm run sync         # regenerate content and assets only
```

`sync` runs `../scripts/build-docs-site.py`.

To produce the offline copy that ships with the signed cover, run
`./scripts/package-docs-offline.sh` from the repository root. It builds the
site and writes `docs/deliverables/Handover-Pack/arn-documentation-offline.zip`
with a launcher for macOS, Linux and Windows.

## The design system

The visual layer is a single, documented system. It is not decoration: an
operations runbook read at 3am has to be legible before it is attractive.

| Layer | Where | Rule |
|---|---|---|
| Tokens | `src/styles/tokens.css` | Every colour, font, space, easing and z-index. Nothing is inlined at point of use. |
| Page and chrome | `src/styles/arn.css` | Consumes tokens by name only. Opens with a stamp recording the design decisions and the audit results. |
| Footer | `src/components/Footer.astro` | The one component override: currency and supplier attribution. |

**Type.** Geist (400 body, 700 display) with Geist Mono as the outlier. Mono
carries exactly two roles: code, and the small-caps label register (wordmark,
sidebar group headings, table headings, colophon). Body prose is never mono.
Both faces are **self-hosted** via `@fontsource-variable`, because this site
also ships as an offline bundle with no network; a font CDN would leave ARN
reading a fallback stack.

**Colour.** Cool neutrals tinted toward the accent hue, never pure black or
white. One accent, ARN purple at hue 271, used only on links, focus rings, the
current-page marker and hover states. It covers well under 5% of any viewport
by design: accent is emphasis, not fill. Every text pair was computed in both
themes; the lowest is 4.70:1 against a 4.5:1 floor.

**Motion.** Almost none, deliberately. Nothing reveals on scroll. Interactive
elements get one or two signals, never four, on named easings with a
`prefers-reduced-motion` collapse.

**The landing page is generated, not written.** `build_index()` in
`scripts/build-docs-site.py` composes it from `PLACEMENT`'s group, order,
label and note fields, so the index and the sidebar cannot disagree about what
the site contains. To change a row's wording, edit its note in `PLACEMENT`.

Two patterns were removed from Starlight's defaults and should not come back:
the fake macOS terminal window it draws around shell blocks (the reader already
has a real terminal, and redrawing one is the strongest "generated" tell), and
the per-page metadata strip, which exposed our repository layout on every page.

The sidebar marks the current page with weight and ink only. Starlight's default
adds an inset accent stripe on the inline start edge; against the sublist's own
nesting hairline that read as a shadow hanging off the item, so it is removed.
Do not restore it.

If you change the visual layer, re-verify: contrast on both themes, no
horizontal scroll at 320/375/414/768, and no clickable label wrapping to two
lines. All three have shipped broken before.

## What gets published, and what does not

The site publishes only documents classified **ARN-facing**. That classification
is not made here: it is read from the audience field in
`scripts/docs-audience.py`, so every publishing step and this site cannot
disagree about what ARN receives. Arinco-internal material (working briefs, the
SoW self-audit, dry-run records, session run-sheets) is excluded by
construction rather than by remembering to exclude it.

Two consequences worth knowing:

- Adding an ARN-facing document means adding it to that shared manifest, then
  giving it a home in `PLACEMENT` in the sync script. The sync **exits non-zero**
  if an ARN-facing document has no placement, so a new document cannot be
  silently dropped from the site.
- A link from a published page to an unpublished one cannot resolve. Rather than
  ship a 404 to the client, the sync downgrades it to plain text marked
  *(not published to this site)* and prints every instance it defused. Read that
  output; a long list means an ARN-facing document leans on internal material.

## Information architecture

The site has seven groups. They are organised by **who reads them and what they
need to do**, not by our internal filing structure:

| Group | Directory | Audience | What the group is for |
|---|---|---|---|
| **Start here** | `start-here/` | New owners and first-time readers | Orientation and first steps before operating the platform. |
| **Using the console** | `console/` | Content operators | Review, configure and improve bulletins without Azure access. |
| **Running the platform** | `operate/` | Platform support engineers | Monitor, troubleshoot, operate and recover the running service. |
| **How it works** | `how-it-works/` | Technical owners and support engineers | Architecture, design rationale and system behaviour. |
| **Assurance** | `assurance/` | Acceptance and support teams | Proven capacity, test evidence, limitations and defects. |
| **Handover** | `handover/` | Incoming support owners | Support ownership, open actions and acceptance. |
| **For developers** | `develop/` | Whoever changes the code | Code layout, local build and test, the delivery pipeline, the test harnesses. |

ARN receives the repository as well as the operator documentation, so the
received set has two readers. `develop/` is the second one, and it is the only
group whose pages may use implementation vocabulary — see the leak guard below.
It sits last in the navigation deliberately: it is the group most readers should
skip, and leading with it would make the site read as a codebase tour.

Three places must stay in step when the navigation changes: this table, the
sidebar in `astro.config.mjs`, and the signed cover's documentation-set table.
The sync fails the build if a placed group is missing from the sidebar, because
the pages otherwise build and are simply unreachable.

## Images

Source documents never write a relative image path. `docs/<area>/<page>.md` is
synced to `src/content/docs/<group>/<page>.md`, so a relative path would be
wrong in one place or the other. They write a scheme instead, which the sync
resolves and validates:

```markdown
![System context](diagram:system-context.png)
![The review cockpit](screenshot:03-review-adjust.desktop.png)
```

A reference naming an image that is not published fails the build and names the
missing token and the regeneration command.

Most images are build output, never hand-copied:

| Class | Scheme | Live source | Regenerate with |
|---|---|---|---|
| Diagrams | `diagram:` | `architecture/images/` | `./scripts/export-diagrams.sh` |
| Screenshots | `screenshot:` | `web/audit/after/` | `cd web && npm run audit` |
| Azure Portal | `portal:` | `docs/assets/azure-portal/` | `node scripts/capture-portal-screenshots.mjs` |

The first two live sources are gitignored. The sync copies the published subset
into `src/assets/` at build time, so a regenerated figure or screenshot cannot be
silently hidden behind a hand-maintained duplicate. If a required source file
is missing, the build stops and names the command that produces it.

**Portal captures are the exception: they are tracked, not build output.**
Producing one needs an interactive Entra sign-in against real deployed
resources, so no CI agent can regenerate them and there is nothing for a cache
to stand in for. The capture script drives an already-signed-in Chromium over
its DevTools endpoint and redacts the signed-in account from the Portal header
before every shot, because that header would otherwise name an individual on
every published image. Do not hand-crop or hand-edit these files; re-run the
script.

## Leak guard

The client-readiness guard runs against every published page during sync. Its
eleven pattern classes are defined in `LEAKS` in
`scripts/build-docs-site.py`: repository paths, repository files, our
organisation, named individuals, internal identifiers, engagement mechanics,
dated engineering notes, code identifiers, project jargon, unfinished content,
and internal tooling.

Run it as part of the normal site commands:

```sh
npm run sync
npm run build
```

The build fails when a published page matches a pattern. For mid-refactor
investigation only, `python3 scripts/build-docs-site.py --allow-leaks` reports
findings without failing. Never use that mode to publish.

The narrow `ALLOW` map contains page-specific pattern exemptions, and
`SAFE_IDENTIFIERS` contains the Key Vault secret names and app settings that an
ARN operator must type into the Portal. Both are maintained in
`scripts/build-docs-site.py`. A new exemption needs a reader-facing
justification in the code; weakening the guard merely to make a build pass is
the wrong move. Fix the page instead.

### The developer group's exemption

Pages in `develop/` are exempt from five classes, listed as `DEV_EXEMPT`:
repository paths, repository files, code identifiers, project jargon and
internal tooling. A page about the code cannot avoid naming files, types and the
commands that build them, and its reader is someone holding the repository.

The other six classes stay enforced there, and that boundary is the point of the
exemption rather than an oversight. Naming a consultant, our own organisation, an
internal defect or risk identifier, a commercial term, or shipping a `TBD` is
still a build failure on a developer page: none of those become appropriate just
because the reader can program. The three harness pages were withdrawn from the
operator set in July for audience reasons, and promoting them to this group
required exactly one round of removing engagement identifiers from their prose.

## Language contract

Published pages use the agreed reader-facing vocabulary below. New pages must
follow the same contract rather than reintroducing engineering shorthand:

| Do not write | Write |
|---|---|
| poller | scheduler |
| good run | clear road |
| filler cart | standby audio |
| fill order | content priority order |
| grounding gate | the source check |
| safety gate | the content-safety check |
| pre-review | the automated audio check |
| market | station and licence area (the telemetry dimension is still named `market`) |

This contract keeps the site understandable to an incoming operator or support
engineer and prevents internal implementation terms becoming client concepts.

## Diagram set

Seven figures are published: `system-context`, `platform-architecture`,
`security-overview`, `cicd-pipeline`, `network-topology`,
`bulletin-state-machine`, and `per-station-configuration`. They are all manual
orthogonal renders with one visual style, and the model-backed figures are
validated against the architecture model.

Three figures are deliberately withdrawn:

- `data-plane` and `delivery-path` were auto-laid-out engineering exploration
  views with crossing edges and a repeated legend label.
- `bulletin-lifecycle` was an auto-laid-out figure that was 6,904 pixels wide
  and unreadable; the state machine covers the useful content in reader terms.

A figure destined for this site must be a manual render. Never add an
auto-layout export to the published asset list.

## Page currency and branding

The per-page provenance strip was removed. It exposed our repository layout and
documentation-harness vocabulary on every page, which was internal detail rather
than useful client context. Source frontmatter still records verification data
for maintainers; the site now uses one currency statement in the site-wide
banner and a footer colophon instead of repeating the strip.

There is no supplier logo in the site header. Supplier attribution belongs in
the footer colophon; ARN remains the subject of the site.

## Deployment

Two routes, both intentional. `docs/operations/access-and-accounts.md` records
that the Arinco identities holding this engagement's access lapse at engagement
end, so ARN must not lose the documentation with them.

| Route | How |
|---|---|
| Hosted | Azure Static Web App `IBP-AUE-{ENV}-SWA-DOC01`, provisioned by `deployment/main.bicep` behind `deployDocsSite` (true on dev and prod, false on test) and published by the `DocsSite` job in `deployment/pipelines/deploy-stage.yml`. |
| Offline | `arn-documentation-offline.zip`, attached to the signed cover. |

The hosted site has no authentication: the content is operational, not secret,
and auth would break the offline-parity story. A `noindex` meta tag keeps it out
of search engines.

Ordering note for the first deploy: the pipeline job needs
`DOCS_SWA_DEPLOYMENT_TOKEN` in the environment's variable group, and that token
only exists once the Static Web App does. The first run after enabling this
provisions the app and skips the docs deploy; the second run publishes.

**Current state (10 Aug 2026): both apps have published and serve the site.**
Dev (`IBP-AUE-DEV-SWA-DOC01`, host `white-forest-0e4647200`) serves
https://white-forest-0e4647200.7.azurestaticapps.net and production
(`IBP-AUE-PRD-SWA-DOC01`, host `red-pond-03b957f00`) serves
https://red-pond-03b957f00.7.azurestaticapps.net; both were verified serving the
documentation with working full-text search and the site's own 404 page. The one
outstanding item is a custom domain: neither app has one bound, so `site:` in
`astro.config.mjs` still names a host that does not resolve — which affects the
generated sitemap only.

```sh
az staticwebapp secrets list -n IBP-AUE-DEV-SWA-DOC01 --query properties.apiKey -o tsv
```

The pipeline job is gated by a `deployDocsSite` variable-group variable
(`'true'`/`'false'`), because a Bicep parameter cannot gate a pipeline job. Keep
it in step with the matching bicepparam or a deploy will either skip silently or
fail on a missing token.

### The site carries its own Static Web App config

`docs-site/public/staticwebapp.config.json` exists so the deploy cannot pick up
the console app's `web/public/staticwebapp.config.json`. The CLI searches for
that file from its working directory, so a deploy run from the repository root
finds the console's config first: a single-page-app navigation fallback that
rewrites every unknown URL to `index.html` and a 401 redirect to the console's
sign-in, both wrong for a static documentation site. The symptom was every
unknown URL returning the home page with a 200 instead of the site's own 404.
The docs-site config instead maps the 404 to the site's own `404.html`, forbids
indexing, and caches the Astro assets. The deploy must therefore run from inside
`docs-site/` (`swa deploy dist`), where the CLI finds this file; the pipeline's
DocsSite job does exactly that.

## Relationship to the other two artefacts

| Artefact | What it is |
|---|---|
| This site | The primary received documentation set, deployed to the hosted site and shipped as the offline bundle. |
| `docs/deliverables/Handover-Pack/` | The signed cover PDF: where to read the site, the documentation-set table naming all seven groups, open items, top limitations, acceptance and sign-off. |
| `docs/handover-uat-pack.html` | An Arinco-side working index covering ARN-facing and internal material. It is not a client deliverable. |

## Not yet done

- Bind a custom domain so `site:` in `astro.config.mjs` names a host that
  resolves. Confirm the `DOC01` suffix with the infrastructure owner before
  production naming is treated as settled. This is the only deployment item
  left outstanding — see Deployment above.
- Portal captures are development-environment images. If ARN would rather the
  handover set showed production, re-run the capture script against the
  production resource ids once that environment is resumed.
