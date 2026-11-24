#!/usr/bin/env python3
"""Sync the ARN-facing documentation into the Starlight site.

`docs/` stays the single source of truth. This script projects the ARN-facing
subset into `docs-site/src/content/docs/`, which is generated and gitignored:
never edit that tree by hand, it is overwritten on every run.

Which documents are published is not decided here. It is read from the audience
classification in `docs-audience.py`, so every publishing step reads one fact
about each document. Two audiences are published: operator and support
documentation, and the developer documentation that comes with the repository.

    ./scripts/build-docs-site.py [--out docs-site/src/content/docs]
"""

from __future__ import annotations

import argparse
import importlib.util
import re
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
AUDIENCE_MODULE = ROOT / "scripts" / "docs-audience.py"
DEFAULT_OUT = ROOT / "docs-site" / "src" / "content" / "docs"
CORPUS_OUT = ROOT / "docs-site" / "public" / "arn-documentation.md"


def load_pack_module():
    """Import the audience classification despite its hyphenated filename.

    Sharing its PACK list is deliberate: the audience classification is a
    single fact about the engagement, and duplicating it here would let the
    site and the classification drift into disagreeing about what ARN receives.
    """
    spec = importlib.util.spec_from_file_location("docs_audience", AUDIENCE_MODULE)
    if spec is None or spec.loader is None:
        sys.exit(f"cannot import {AUDIENCE_MODULE}")
    mod = importlib.util.module_from_spec(spec)
    # @dataclass resolves annotations via sys.modules, so register before exec.
    sys.modules[spec.name] = mod
    spec.loader.exec_module(mod)
    return mod


PB = load_pack_module()

# Site placement is a navigation concern, not an audience one, so it lives here
# rather than in the shared manifest. Key is the source path; value is
# (directory, sidebar order, sidebar label, one-line note for the site index).
#
# Groups are organised by WHO reads them. A content operator has no use for the
# incident runbook and a platform engineer has no use for the review workflow,
# so the two never share a group. Labels say what the page is for in the
# reader's words, not what the document is called internally.
PLACEMENT: dict[str, tuple[str, int, str, str]] = {
    # Orientation. Everything a newcomer needs before they can use anything else.
    "docs/operations/platform-overview.md":
        ("start-here", 1, "What the platform does",
         "What a bulletin is, where the data comes from, and what reaches air."),
    "docs/operations/glossary.md":
        ("start-here", 2, "Glossary",
         "Every term the rest of the site assumes you already know."),
    "docs/operations/first-time-sign-in.md":
        ("start-here", 3, "Signing in",
         "Getting into the console for the first time."),
    "docs/operations/faq.md":
        ("start-here", 4, "Common questions",
         "Short answers, each pointing at the page that covers it properly."),
    "docs/operations/first-day.md":
        ("start-here", 5, "Taking ownership",
         "An ordered checklist for the first day of owning the platform."),

    # The content team. Nothing in this group requires Azure access.
    "docs/operations/operator-guide.md":
        ("console", 1, "Reviewing and approving bulletins",
         "Reviewing a bulletin, deciding on it, and what the listener hears either way."),
    "docs/reference/prompt-iteration-guide.md":
        ("console", 2, "Changing what bulletins say",
         "Adjusting wording, style and pronunciation without a developer."),
    "docs/reference/per-station-configuration.md":
        ("console", 3, "Station settings",
         "The voice, delivery, content and coverage settings that differ per station."),

    # Platform support. Everything in this group assumes Azure access.
    "docs/operations/runbook.md":
        ("operate", 1, "Responding to an incident",
         "First response for every alert, and what airs while you work."),
    "docs/operations/quick-reference.md":
        ("operate", 2, "Command reference",
         "The commands themselves: health, logs, releases, park and resume, rotation."),
    "docs/operations/dashboard-guide.md":
        ("operate", 3, "Monitoring",
         "The six workbooks, what each one plots, and what it does not."),
    "docs/operations/notifications.md":
        ("operate", 4, "Alerts and notifications",
         "How Teams cards are delivered, and how to reauthorise them."),
    "docs/operations/slo.md":
        ("operate", 5, "Service levels",
         "The three service levels, their targets, and the queries behind them."),
    "docs/operations/as-built.md":
        ("operate", 6, "Environments",
         "What is deployed in each environment, down to the resource names."),
    "docs/operations/dependencies.md":
        ("operate", 7, "External services",
         "Each external service, what breaks without it, and who to call."),
    "docs/operations/access-and-accounts.md":
        ("operate", 8, "Accounts and access",
         "Who must own what, by role, before the supplier's access ends."),
    "docs/operations/disaster-recovery.md":
        ("operate", 9, "Resilience and recovery",
         "The current recovery position and its limits, stated plainly."),
    "docs/operations/run-costs.md":
        ("operate", 10, "Running costs",
         "Measured Azure spend, and the levers that change it."),

    # Understanding, not operating. Read once, referred to occasionally.
    "docs/operations/solution-overview.md":
        ("how-it-works", 1, "Solution overview",
         "The whole solution in one read."),
    "docs/canonical/architecture.md":
        ("how-it-works", 2, "Platform architecture",
         "How it runs, integrates and protects itself."),
    "docs/reference/architecture-diagrams.md":
        ("how-it-works", 3, "Diagrams",
         "Seven figures, and when you would reach for each."),
    "docs/operations/design-decisions.md":
        ("how-it-works", 4, "Why it works this way",
         "Twelve choices that constrain how it has to be operated, and why."),

    # Evidence: what was proven, what was not, and what is still open.
    "docs/operations/proven-capacity.md":
        ("assurance", 1, "Proven capacity",
         "The load it was measured at, and what remains unproven."),
    "docs/operations/known-limitations.md":
        ("assurance", 2, "Known limitations",
         "What it deliberately does not do."),
    "docs/operations/uat-plan.md":
        ("assurance", 3, "Acceptance testing",
         "Scope, roles, and the scenarios ARN runs."),
    "docs/operations/uat-scenario-sheet.md":
        ("assurance", 4, "Acceptance test record",
         "The sheet that records outcomes and evidence."),
    "docs/operations/integration-test-plan.md":
        ("assurance", 5, "Integration testing",
         "One case per external service."),
    "docs/operations/defect-log.md":
        ("assurance", 6, "Open defects",
         "Known faults, their effects, and the workarounds."),

    # The transfer itself.
    "docs/operations/support-model.md":
        ("handover", 1, "Support model",
         "Three tiers, who holds each, and the workload to expect."),
    "docs/operations/open-items.md":
        ("handover", 2, "Open items",
         "What ARN must action, with an owner and a consequence."),
    "docs/operations/acceptance.md":
        ("handover", 3, "Acceptance",
         "What accepting the handover means."),

    # For whoever maintains the code. This group is the only one whose reader
    # opens an editor, which is why it is the only one allowed implementation
    # vocabulary (see DEV_EXEMPT below).
    "docs/operations/solution-structure.md":
        ("develop", 1, "How the code is organised",
         "Layers, projects, and the dependency rules between them."),
    "docs/operations/local-development.md":
        ("develop", 2, "Building and running it",
         "Prerequisites, and the commands to build, test and run on a workstation."),
    "docs/operations/delivery-pipeline.md":
        ("develop", 3, "The delivery pipeline",
         "How a change reaches an environment, and how to read a failed run."),
    "docs/operations/e2e-testing.md":
        ("develop", 4, "End-to-end tests",
         "The deterministic suite, and the checks that call real vendors."),
    "docs/operations/load-testing.md":
        ("develop", 5, "Load testing",
         "The backend harness, its scenarios, and its cost controls."),
    "docs/operations/performance-testing.md":
        ("develop", 6, "Console performance",
         "Front-end budgets, and diagnosing a slow deployed page."),
}

# Shell scripts and other non-prose artefacts ship with the repository rather
# than as site pages; listing them keeps the coverage check honest about why
# they are absent instead of silently dropping them.
NOT_A_PAGE: set[str] = set()

# Images are build output, never tracked content. `architecture/images/` is
# regenerated by the diagram export and `web/audit/after/` by the console
# audit; both are gitignored. Copying them at build time is the only
# arrangement in which a published figure cannot silently go stale behind a
# hand-copied duplicate.
#
# Only presentation-grade figures are published, and they all share one visual
# language: orthogonal routing, light background, a legend that means
# something. Three auto-laid-out exports were published to the client once by
# mistake and are now withheld: `data-plane` and `delivery-path`, which the
# diagram registry marks as engineering exploration, and `bulletin-lifecycle`,
# whose content the bulletin state machine covers in the reader's own terms.
DIAGRAMS = [
    "system-context.png",
    "platform-architecture.png",
    "network-topology.png",
    "security-overview.png",
    "cicd-pipeline.png",
    "bulletin-state-machine.png",
    "per-station-configuration.png",
]

# Desktop light variants only. The audit corpus also holds three other
# viewports, a dark theme and Set-of-Mark overlays; none of them help a reader.
SCREENSHOTS = [
    "00-home.desktop.png",
    "01-onair.desktop.png",
    "02-review-waiting.desktop.png",
    "03-review-adjust.desktop.png",
    "04-review-audit.desktop.png",
    "09-fallback.desktop.png",
    "10-stations.desktop.png",
    "13-guidance.desktop.png",
    "16-station-studio.desktop.png",
    # 19-revise-dialog was excluded while the audit harness captured it
    # byte-identical to 02-review-waiting. That was not a harness limitation
    # but a silent failure: clickByText probed for the button once, before the
    # job detail had mounted its action bar, so the target captured the base
    # page under a dialog's caption. The probe now polls (2026-09-03), the
    # capture shows the real dialog, and the screenshot is published again.
    "19-revise-dialog.desktop.png",
    "20-delivery.desktop.png",
    "22-pronunciation.desktop.png",
    "24-activity.desktop.png",
    "25-config-transfer.desktop.png",
    "26-bookends.desktop.png",
    "27-bookend-studio.desktop.png",
]

# Azure Portal captures. Unlike the two sources above these are TRACKED, not
# build output: producing them needs an interactive Entra sign-in against real
# deployed resources, so no CI agent can regenerate them and there is nothing
# for a cache to stand in for. `scripts/capture-portal-screenshots.mjs` refreshes
# them from a signed-in browser.
PORTAL_SHOTS = [
    "workbooks-gallery.png",
    "workbook-operations-overview.png",
    "workbook-operations-overview-lower.png",
    "workbook-pipeline-latency.png",
    "logs-trace-one-bulletin.png",
    "alert-rules.png",
]

ASSET_SOURCES = {
    "diagram": (Path("architecture/images"), "diagrams", DIAGRAMS,
                "run ./scripts/export-diagrams.sh from the repo root"),
    "screenshot": (Path("web/audit/after"), "screenshots", SCREENSHOTS,
                   "run: cd web && npm run audit"),
    "portal": (Path("docs/assets/azure-portal"), "portal", PORTAL_SHOTS,
               "run: node scripts/capture-portal-screenshots.mjs "
               "(needs a signed-in Azure Portal tab)"),
}

# Schemes whose source is tracked in the repository, so the gitignored-build-
# output cache in sync_assets() does not apply to them.
TRACKED_SCHEMES = {"portal"}

FRONTMATTER = re.compile(r"^---\n(.*?)\n---\n", re.DOTALL)
MD_LINK = re.compile(r"\[([^\]]+)\]\(([^)\s]+?)(?:\s+\"[^\"]*\")?\)")
LEADING_H1 = re.compile(r"^\s*#\s+.+?\n+", re.DOTALL)


@dataclass
class Page:
    src: str
    directory: str
    order: int
    label: str
    note: str
    title: str = ""
    description: str = ""
    status: str = ""
    verified: str = ""
    source_of_truth: str = ""
    body: str = ""

    @property
    def slug(self) -> str:
        return Path(self.src).stem

    @property
    def route(self) -> str:
        return f"/{self.directory}/{self.slug}/"


def yaml_scalar(block: str, key: str) -> str:
    m = re.search(rf"^{key}:\s*(.+)$", block, re.MULTILINE)
    return m.group(1).strip().strip("\"'") if m else ""


def esc_yaml(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def resolve_doc_links(p: Page, body: str, routes: dict[str, str]) -> str:
    """Turn a bare `docs/…/foo.md` code span into a plain link to the page.

    The source documents cite each other as repository paths, which reads fine
    in a checkout and is meaningless to a client. Where the path resolves to a
    published page the reference becomes an ordinary link carrying the page's
    title; where it does not, it is a leak and the guard below stops the build.
    """
    def sub(m: re.Match[str]) -> str:
        route = routes.get(m.group(1))
        return f"[{TITLES.get(route, route)}]({route})" if route else m.group(0)

    out, in_fence = [], False
    for line in body.split("\n"):
        if FENCE.match(line):
            in_fence = not in_fence
            out.append(line)
            continue
        out.append(line if in_fence else CODE_PATH.sub(sub, line))
    return "\n".join(out)


def build_route_map(pages: list[Page]) -> dict[str, str]:
    """Map every reachable spelling of a source path to its site route."""
    routes: dict[str, str] = {}
    for p in pages:
        routes[p.src] = p.route
        routes["/" + p.src] = p.route
        routes["./" + p.src] = p.route
        # Sibling references inside the same source directory.
        routes[Path(p.src).name] = p.route
        routes["./" + Path(p.src).name] = p.route
        # Cross-directory relative hops, e.g. ../reference/foo.md
        parts = Path(p.src).parts
        if len(parts) >= 2:
            routes["../" + "/".join(parts[-2:])] = p.route
    return routes


def rewrite_links(p: Page, routes: dict[str, str], dropped: list[tuple[str, str]]) -> str:
    """Point internal links at site routes; defuse links that would 404.

    A link from an ARN-facing page to an Arinco-internal document cannot
    resolve on this site. Silently leaving it would ship a broken link to the
    client, so it is downgraded to plain text and reported.
    """
    def sub(m: re.Match[str]) -> str:
        text, target = m.group(1), m.group(2)
        # `diagram:`, `screenshot:` and `portal:` are this pipeline's own
        # schemes, resolved later by rewrite_asset_refs. Left alone here they
        # would be mistaken for an unresolvable repository path and demoted to a
        # code span, which silently drops the image and its alt text.
        if target.startswith(("http://", "https://", "mailto:", "#",
                              "diagram:", "screenshot:", "portal:")):
            return m.group(0)
        anchor = ""
        if "#" in target:
            target, anchor = target.split("#", 1)
            anchor = "#" + anchor
        clean = target.strip()
        if clean in routes:
            return f"[{text}]({routes[clean]}{anchor})"
        if clean.endswith(".md"):
            dropped.append((p.src, clean))
            return f"{text} (not published to this site)"
        # Code and infrastructure paths: keep the reference, drop the link.
        return f"`{clean}`" if clean else text

    return MD_LINK.sub(sub, p.body)


CODE_PATH = re.compile(r"`([A-Za-z0-9._/-]+\.md)`")
FENCE = re.compile(r"^\s*(```|~~~)")

# Titles by route, filled once pages are loaded, so a resolved reference reads
# as the page's name rather than as a file path.
TITLES: dict[str, str] = {}

# ── The leak guard ────────────────────────────────────────────────────────
# This site is the client deliverable. Its source documents were written as
# engineering working notes, so every class of leak below has already reached a
# published page at least once. Catching them by reading is not reliable at 30
# pages and will not survive the next author, so the build fails instead.
#
# The authority for what is and is not allowed is the language contract agreed
# for this deliverable; these patterns are its enforceable subset.
LEAKS: list[tuple[str, str, str]] = [
    ("repository path",
     r"(?<![\w-])\.?/?(?:docs|src|scripts|deployment|architecture|web|tools|test|\.agents)/[A-Za-z0-9_./{}-]+",
     "name the page or the Azure resource, never a path in our repository"),
    ("repository file",
     r"(?<![\w./-])(?:CONTEXT|AGENTS|README|llms)\.(?:md|txt)|azure-pipelines\.yml|[A-Za-z0-9_-]+\.(?:bicep|bicepparam|csproj|sln|cs|tsx?|mjs)\b",
     "describe the thing, not the file we keep it in"),
    ("our organisation",
     r"\bArinco(?:'s)?\b|\b(?:we|our|us)\b(?=[^\n]*\b(?:built|wrote|found|decided|verified|recommend|deliver|scope)\b)",
     "write for ARN in the second person or impersonally"),
    ("named individual",
     r"\b(?:Julian Codespoti|Julian|Nick Werba|Gerry Pyne|Sammy Wessel|Mat Williams|Fiona Ellis-Jones|Annabelle|Tim Fowler|Ben Cunningham|Jake Bradley|Matt McDonald|Mandy Page|Lucy Formosa|Will Aplin|Yong Cao|Vasko)\b",
     "name the role, not the person: roles outlive people"),
    ("internal identifier",
     r"\b(?:DEF-\d+|RISK-\d+|SC-\d{2}|SG[123]\b|QT-\d+|CC-\d+|WALK3-\d+)\b",
     "describe the defect, risk or requirement in words"),
    ("engagement mechanics",
     r"\b(?:statement of work|SoW|work order|stage gate|consultant days?|hypercare|FTE)\b",
     "commercial terms belong in the signed cover, not in operational documentation"),
    ("dated engineering note",
     r"\b(?:re-?verified|re-?snapshot(?:ted)?|re-?queried|last checked|cross-checked|as at)\b[^\n.]{0,40}\d",
     "the site carries one currency statement, in the footer"),
    # A PascalCase token in a code span is almost always a C# type. The
    # exceptions are Key Vault secret names and app settings, which an ARN
    # engineer types into the Portal, so they are operational facts rather
    # than implementation detail. SAFE_IDENTIFIERS below carries them.
    ("code identifier",
     r"`(?:I?[A-Z][a-z0-9]+){2,}(?:<[^`]*>)?(?:\.[A-Za-z]+)*`|\b(?:MarketSchedule|SchedulePollerService|BroadcastQueueProcessor|FallbackKind|JobStatus|GoodRunPolicy|BulletinContentSelection|GuidanceEntry|SafetyTerm|DeliveryStylePolicy|PronunciationLocator|IScriptDirector|BroadcastContext|CorridorTiering|StatusHistoryEntry|AudioPreReview)\b",
     "describe the concept; a C# type name is not a reader-facing term"),
    # Hyphenated forms are deliberate: "filler-cart chain" shipped to the
    # client once because the pattern only matched the spaced form.
    ("project jargon",
     r"\b(?:pollers?|good[ -]runs?|fill[ -]order|grounding[ -]gate|safety[ -]gate|"
     r"pre-?review|filler[ -]statements?|filler[ -]carts?|corridor[ -]tiering|PVC)\b",
     "use the agreed term from the language contract"),
    ("unfinished content",
     r"\bTBC\b|\bTBD\b|\[ARN input\]|\[INFERENCE\]|not yet (?:written|confirmed|decided)|to be confirmed",
     "fill it in or remove the row; a client document ships with no blanks"),
    ("internal tooling",
     r"\b(?:LikeC4|render\.py|likec4|Playwright|vitest|oxlint|k6|Pagefind|gitleaks|npm run|dotnet (?:test|build)|gpt-5[.\w-]*|Foundry|golden examples?)\b",
     "how the platform was built and tested is not how ARN runs it"),
]

# Narrow, justified exemptions. Each needs a reason, and the reason has to be
# about the reader, never about it being tedious to fix.
ALLOW: dict[str, set[str]] = {
    # The support page carries the one paragraph that must state the transition
    # into ARN's own support, which cannot be written without naming it.
    "docs/operations/support-model.md": {"engagement mechanics"},
    # Acceptance is the client-facing half of a commercial document.
    "docs/operations/acceptance.md": {"engagement mechanics"},
}

# The developer group's reader opens an editor, so the five classes that exist
# to keep implementation detail off an operator's page would block every useful
# sentence on theirs: a page about the code cannot avoid naming files, types and
# the commands that build them.
#
# What stays enforced there is everything that is not about the code. Naming a
# consultant, our own organisation, an internal defect or risk id, a commercial
# term, or shipping a TBD is still a failure on a developer page, because none of
# those become appropriate just because the reader can program.
DEV_DIRECTORY = "develop"
DEV_EXEMPT = {
    "repository path",
    "repository file",
    "code identifier",
    "project jargon",
    "internal tooling",
}

# Key Vault secret names and app settings an ARN engineer types into the
# Portal. They look like C# types to the pattern above but they are the
# opposite of implementation detail: getting them wrong is an outage.
SAFE_IDENTIFIERS = {
    "AadClientId", "AadClientSecret", "AiSettings", "ApiKey",
    "ExternalProviderMode", "ConfigTouch", "FillerCartAudioUrl",
    "WebhookEndpointUrl", "AzureWebJobsStorage", "FunctionAppLogs",
    "AppEvents", "AppRequests", "AppMetrics", "TimeGenerated",
}


def check_leaks(pages: list[Page]) -> list[tuple[str, str, str, str]]:
    """Report every published line that would expose internal material."""
    found: list[tuple[str, str, str, str]] = []
    for p in pages:
        exempt = ALLOW.get(p.src, set())
        if p.directory == DEV_DIRECTORY:
            exempt = exempt | DEV_EXEMPT
        for kind, pattern, advice in LEAKS:
            if kind in exempt:
                continue
            for m in re.finditer(pattern, p.body):
                text = m.group(0).strip()
                if kind == "code identifier" and text.strip("`") in SAFE_IDENTIFIERS:
                    continue
                line = p.body.count("\n", 0, m.start()) + 1
                found.append((p.src, f"line {line}", kind, text))
    return found


ASSET_REF = re.compile(r"\]\((diagram|screenshot|portal):([^)\s]+)\)")


def sync_assets(out: Path) -> dict[str, set[str]]:
    """Copy the published images into the site and report what landed.

    The live sources are gitignored build output, so a CI agent does not have
    them: `architecture/images/` needs the LikeC4 toolchain and
    `web/audit/after/` needs a browser audit run. The published subset is
    therefore mirrored into a tracked cache at `docs-site/assets/`, which this
    function refreshes from the live source whenever the live source is
    present. A developer who regenerates a diagram and rebuilds the site
    updates the cache as a side effect, so the cache is never hand-copied and
    there is only ever one refresh path.

    Returns the filenames published per scheme so a `diagram:` or
    `screenshot:` reference naming something that is not there can fail the
    build instead of shipping a broken image to the client.
    """
    assets = out.parent.parent / "assets"
    cache = out.parent.parent.parent / "assets"
    if assets.exists():
        shutil.rmtree(assets)
    copied: dict[str, set[str]] = {}
    from_cache: list[str] = []

    for scheme, (src_dir, sub, names, fix) in ASSET_SOURCES.items():
        dest = assets / sub
        dest.mkdir(parents=True, exist_ok=True)
        copied[scheme] = set()
        tracked = scheme in TRACKED_SCHEMES
        if not tracked:
            (cache / sub).mkdir(parents=True, exist_ok=True)
        for name in names:
            live = ROOT / src_dir / name
            if tracked:
                # Nothing to fall back to: the source is committed, so a missing
                # file means it was deleted, not that this machine cannot build it.
                if not live.is_file():
                    sys.exit(f"missing {scheme}: {src_dir / name} does not exist\n"
                             f"  to regenerate it, {fix}")
                shutil.copy2(live, dest / name)
                copied[scheme].add(name)
                continue
            cached = cache / sub / name
            if live.is_file():
                shutil.copy2(live, cached)
            elif not cached.is_file():
                sys.exit(f"missing {scheme}: neither {src_dir / name} nor its "
                         f"cached copy {cached.relative_to(ROOT)} exists\n"
                         f"  to regenerate it, {fix}")
            else:
                from_cache.append(name)
            shutil.copy2(cached, dest / name)
            copied[scheme].add(name)

    if from_cache:
        print(f"{len(from_cache)} image(s) served from the tracked cache "
              f"(live source not present on this machine)")
    return copied


def rewrite_asset_refs(p: Page, body: str, copied: dict[str, set[str]],
                       unknown: list[tuple[str, str, str]]) -> str:
    """Resolve the `diagram:`/`screenshot:` scheme to a site-relative path.

    Source documents cannot write a relative image path: they live at
    `docs/<area>/<page>.md` and are synced to
    `src/content/docs/<group>/<page>.md`, so any relative path is wrong in one
    place or the other. They name the image instead, and this resolves it. The
    prefix is a constant because every synced page sits at exactly one depth.

    A reference to an image that is not published is collected rather than
    raised, so one run reports every problem in the site at once. Failing on
    the first bad token made the whole report unreachable behind it.
    """
    def sub(m: re.Match[str]) -> str:
        scheme, name = m.group(1), m.group(2)
        if name not in copied[scheme]:
            unknown.append((p.src, scheme, name))
            return m.group(0)
        # src/content/docs/<group>/<page>.md -> src/assets/<sub>/<name>
        return f"](../../../assets/{ASSET_SOURCES[scheme][1]}/{name})"

    return ASSET_REF.sub(sub, body)


def load_pages() -> tuple[list[Page], list[str]]:
    pages: list[Page] = []
    unplaced: list[str] = []
    published = [e for e in PB.PACK if e.audience in PB.PUBLISHED]

    for entry in published:
        if entry.path in NOT_A_PAGE:
            continue
        placement = PLACEMENT.get(entry.path)
        if placement is None:
            unplaced.append(entry.path)
            continue
        directory, order, label, note = placement
        f = ROOT / entry.path
        if not f.is_file():
            unplaced.append(f"{entry.path} (missing on disk)")
            continue
        raw = f.read_text(encoding="utf-8")
        page = Page(entry.path, directory, order, label, note)
        fm = FRONTMATTER.match(raw)
        if fm:
            block, page.body = fm.group(1), raw[fm.end():]
            page.title = yaml_scalar(block, "title") or page.slug
            page.description = yaml_scalar(block, "description")
            page.status = yaml_scalar(block, "status")
            page.verified = yaml_scalar(block, "last_verified")
            page.source_of_truth = yaml_scalar(block, "source_of_truth")
        else:
            page.body, page.title = raw, page.slug
        # Starlight renders the title as the page h1; a second one duplicates it.
        page.body = LEADING_H1.sub("", page.body.lstrip("\n"), count=1)
        pages.append(page)
    return pages, unplaced


# Starlight has no site-wide banner option: it is per-page frontmatter, so it
# is emitted on every page here. This is the site's ONLY currency statement.
# Per-page verification dates used to be rendered in a metadata strip, which
# told the reader more about how this documentation is produced than about the
# platform, so the strip is gone and this line carries the whole job.
BANNER = "Operations documentation for the ARN Information Bulletin Platform. Current as at September 2026."


# The landing page. A documentation home's job is to route two audiences to
# the right page, so the page IS the index: one short opening, then every
# destination as a row under the group that owns it.
#
# It is generated from PLACEMENT rather than written out, so the index and the
# sidebar cannot disagree about what the site contains. To change a row's
# wording, edit its note in PLACEMENT.
GROUPS: dict[str, tuple[str, str]] = {
    "start-here": ("Start here",
                   "Read these before anything else. The rest of the site assumes them."),
    "console": ("Using the console",
                "For the content team. Nothing here needs Azure access."),
    "operate": ("Running the platform",
                "For platform support. Everything here assumes Azure access."),
    "how-it-works": ("How it works",
                     "Read once, then referred to when a decision needs explaining."),
    "assurance": ("Assurance",
                  "What was proven, what was not, and what is still open."),
    "handover": ("Handover", "The transfer itself."),
    # Last deliberately: it is the only group most readers should skip, and
    # putting it first would make the site look like a codebase tour.
    "develop": ("For developers",
                "For whoever changes the code. Everything here assumes the repository."),
}

INDEX_HEAD = """---
title: Operations documentation
description: Operations documentation for the ARN Information Bulletin Platform.
template: splash
banner:
  content: __BANNER__
pagefind: false
---

<div class="arn-index not-content">

<div class="arn-open">
<h1>Running the bulletin platform.</h1>
<p>Everything ARN needs to run the traffic bulletin platform: what it does, how
to work it, how to keep it up, what it will not do, and how to change it. The
index below is grouped by who each page is written for.</p>
<div class="arn-jump">
<a href="/start-here/platform-overview/">New here, start with what it does <span aria-hidden="true">&rarr;</span></a>
<a href="/operate/runbook/">Something is wrong right now <span aria-hidden="true">&rarr;</span></a>
</div>
</div>
"""

INDEX_TAIL = """
<div class="arn-close">
<h2>Keeping a copy</h2>
<p>The same content ships as a self-contained offline bundle,
<code>arn-documentation-offline.zip</code>, attached to the signed handover
pack. It needs no network and no Azure access, so it stays readable whatever
happens to this site.</p>
</div>

</div>
"""


def build_index(pages: list[Page]) -> str:
    """Compose the landing page as a real index of every published page."""
    rows: dict[str, list[Page]] = {}
    for p in pages:
        rows.setdefault(p.directory, []).append(p)

    parts = [INDEX_HEAD.replace("__BANNER__", BANNER)]
    for directory, (heading, blurb) in GROUPS.items():
        group = sorted(rows.get(directory, []), key=lambda x: x.order)
        if not group:
            continue
        parts.append(f'\n<div class="arn-group">\n<div class="arn-group__label">\n'
                     f"<h2>{heading}</h2>\n<p>{blurb}</p>\n</div>\n"
                     f'<div class="arn-rows">\n')
        for p in group:
            parts.append(
                f'<a class="arn-row" href="{p.route}">'
                f'<span class="arn-row__title">{p.label}'
                f'<span class="arn-row__note">{p.note}</span></span>'
                f'<span class="arn-row__go" aria-hidden="true">&rarr;</span></a>\n')
        parts.append("</div>\n</div>\n")
    parts.append(INDEX_TAIL)
    return "".join(parts)


# Starlight serves this for any unmatched route. It cannot be a tracked file:
# the sync wipes src/content/docs on every run, so it is emitted here.
NOT_FOUND = """---
title: Page not found
description: That page does not exist on this site.
template: splash
editUrl: false
banner:
  content: Handover documentation for the ARN Information Bulletin Platform. Content is current as at the handover date shown on each page.
hero:
  tagline: That page does not exist on this site.
  actions:
    - text: Go to the start
      link: /
      icon: right-arrow
---

The address you followed does not match a page here. The documentation moves as
the platform does, so a link from an older email or document may have changed.

Two ways forward:

- Use the search box at the top of any page, or press `/` to jump straight to
  it. Search covers the full text of every page, not just the titles.
- Start from [the site home](/), which lists the seven groups: start here,
  using the console, running the platform, how it works, assurance, handover,
  and for developers.
"""


def write_corpus(rendered: list[tuple[Page, str]]) -> None:
    """Write one offline-safe Markdown corpus for the site-wide exporter."""
    documents = [
        "# ARN Information Bulletin Platform documentation",
        "",
        "This corpus contains every published source document in the "
        "documentation site.",
        "",
    ]
    for page, body in rendered:
        documents.extend(
            [
                f"## {page.title}",
                "",
                f"- Source: `{page.src}`",
                f"- Site path: `{page.route}`",
                "",
                body.strip(),
                "",
                "---",
                "",
            ]
        )
    CORPUS_OUT.parent.mkdir(parents=True, exist_ok=True)
    CORPUS_OUT.write_text("\n".join(documents), encoding="utf-8")


def write_site(pages: list[Page], out: Path, routes: dict[str, str],
               assets: dict[str, set[str]],
               unknown: list[tuple[str, str, str]]) -> list[tuple[str, str]]:
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    dropped: list[tuple[str, str]] = []
    rendered: list[tuple[Page, str]] = []

    (out / "index.mdx").write_text(build_index(pages), encoding="utf-8")
    (out / "404.md").write_text(NOT_FOUND, encoding="utf-8")

    for p in pages:
        body = rewrite_links(p, routes, dropped)
        body = rewrite_asset_refs(p, body, assets, unknown)
        body = resolve_doc_links(p, body, routes)
        rendered.append((p, body))
        fm = [
            "---",
            f'title: "{esc_yaml(p.title)}"',
        ]
        if p.description:
            fm.append(f'description: "{esc_yaml(p.description)}"')
        fm.append("banner:")
        fm.append(f'  content: "{esc_yaml(BANNER)}"')
        fm.append("sidebar:")
        fm.append(f"  order: {p.order}")
        if p.label:
            fm.append(f'  label: "{esc_yaml(p.label)}"')
        fm.append("---")
        target = out / p.directory / f"{p.slug}.md"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("\n".join(fm) + "\n\n" + body, encoding="utf-8")
    write_corpus(rendered)
    return dropped


def check_sidebar_coverage() -> list[str]:
    """Report groups the sidebar config would not render.

    Starlight's sidebar is declared in `astro.config.mjs`, so a group added to
    GROUPS and PLACEMENT here builds its pages and then silently fails to appear
    in the navigation: reachable only by search or a direct link. That is a worse
    failure than a build error because everything looks fine.
    """
    config = ROOT / "docs-site" / "astro.config.mjs"
    if not config.is_file():
        return []
    declared = set(re.findall(r"autogenerate:\s*\{\s*directory:\s*'([^']+)'", config.read_text(encoding="utf-8")))
    return sorted(set(GROUPS) - declared)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--allow-leaks", action="store_true",
                    help="report leaks without failing, for use mid-refactor only")
    args = ap.parse_args()
    out = args.out

    pages, unplaced = load_pages()
    routes = build_route_map(pages)
    TITLES.update({p.route: p.title for p in pages})
    assets = sync_assets(out)
    unknown: list[tuple[str, str, str]] = []
    dropped = write_site(pages, out, routes, assets, unknown)

    by_dir: dict[str, int] = {}
    for p in pages:
        by_dir[p.directory] = by_dir.get(p.directory, 0) + 1
    summary = ", ".join(f"{k} {v}" for k, v in sorted(by_dir.items()))
    shown = out.relative_to(ROOT) if out.is_relative_to(ROOT) else out
    print(f"synced {len(pages)} pages to {shown}  ({summary})")
    print("copied " + ", ".join(f"{len(v)} {k}s" for k, v in sorted(assets.items()))
          + " into src/assets")

    if unplaced:
        print("\nARN-facing but not placed in the site nav:", file=sys.stderr)
        for path in unplaced:
            print(f"  {path}", file=sys.stderr)
        sys.exit(1)

    missing_nav = check_sidebar_coverage()
    if missing_nav:
        print("\ngroup(s) placed here but absent from the site sidebar:",
              file=sys.stderr)
        for group in missing_nav:
            print(f"  {group}", file=sys.stderr)
        print("  add an autogenerate entry for each in "
              "docs-site/astro.config.mjs, or the pages build unreachable",
              file=sys.stderr)
        sys.exit(1)

    # A link to an unpublished document is a content bug, not a formatting one:
    # an ARN-facing page leaning on internal material means the page is not
    # finished. Defusing it to plain text ships "(not published to this site)"
    # to the client, which reads as broken.
    if dropped:
        print(f"\n{len(dropped)} link(s) point at unpublished documents:",
              file=sys.stderr)
        for src, target in sorted(set(dropped)):
            print(f"  {src} -> {target}", file=sys.stderr)
        print("  rewrite the sentence so it does not depend on an internal "
              "document", file=sys.stderr)

    if unknown:
        print(f"\n{len(unknown)} reference(s) to an image that is not published:",
              file=sys.stderr)
        for src, scheme, name in sorted(set(unknown)):
            print(f"  {src}: {scheme}:{name}", file=sys.stderr)
        for scheme in sorted({s for _, s, _ in unknown}):
            print(f"  published {scheme}s: "
                  f"{', '.join(sorted(assets[scheme]))}", file=sys.stderr)

    leaks = check_leaks(pages)
    if leaks:
        by_kind: dict[str, int] = {}
        for _, _, kind, _ in leaks:
            by_kind[kind] = by_kind.get(kind, 0) + 1
        advice_for = {k: a for k, _, a in LEAKS}
        print(f"\n{len(leaks)} internal leak(s) in published pages:", file=sys.stderr)
        for kind, n in sorted(by_kind.items(), key=lambda kv: -kv[1]):
            print(f"\n  {kind} ({n}) — {advice_for[kind]}", file=sys.stderr)
            shown_ex = [x for x in leaks if x[2] == kind][:6]
            for src, where, _, text in shown_ex:
                print(f"      {src} {where}: {text[:90]}", file=sys.stderr)
            if n > len(shown_ex):
                print(f"      ... and {n - len(shown_ex)} more", file=sys.stderr)

    if (dropped or leaks or unknown) and not args.allow_leaks:
        sys.exit(1)


if __name__ == "__main__":
    main()
