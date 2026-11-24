#!/usr/bin/env python3
"""Audience classification for the documents this repository publishes.

`docs/` is the single source of truth. This module records who each document is
for, so the documentation-site build and any future publishing step cannot
disagree about what the client receives.

    AUD_ARN  operator and support documentation published to the client
    AUD_DEV  developer and maintainer documentation published with the repository
    AUD_INT  internal engineering material that is not published to the client

SCOPE, stated plainly because the previous wording claimed to cover "every
document in this repository" and did not: PACK is the curated site pack. It
classifies a subset of `docs/**`, and the leak guard only sees what is in it.
A document absent from PACK is not published to the site, but it is also
unguarded — so anything commercially or personally sensitive belongs here with
AUD_INT even though an internal entry renders no page. Adding a document to
PACK is the only way to bring it under the guard.
"""

from __future__ import annotations

from dataclasses import dataclass, field

# leak guard allow implementation vocabulary on developer pages only.
AUD_ARN = "arn"        # published to ARN: operator and support documentation
AUD_DEV = "dev"        # published to ARN: developer and maintainer documentation
AUD_INT = "int"        # Arinco internal: never handed over in this form

PUBLISHED = (AUD_ARN, AUD_DEV)

AUD_LABEL = {
    AUD_ARN: "Published to ARN",
    AUD_DEV: "Published to ARN (developers)",
    AUD_INT: "Arinco internal",
}


@dataclass
class Entry:
    """One document in the pack."""

    path: str
    group: str
    note: str = ""
    audience: str = AUD_ARN
    new: bool = False
    lang: str = "markdown"
    title: str = ""
    description: str = ""
    status: str = ""
    verified: str = ""
    body: str = ""
    missing: bool = False
    tags: list[str] = field(default_factory=list)


# Order here is the order in the sidebar. Groups render in first-seen order.
PACK: list[Entry] = [
    # ── Acceptance testing ───────────────────────────────────────────────
    # Group was "UAT" until 2026-09-03, which framed these as a one-off
    # project gate and invited the reasonable question of why a sign-off
    # form is in ARN's permanent operations reference. It is not one. The
    # plan describes itself as "a repeatable capability that ARN reruns
    # whenever a station, market or release changes", and the sheet is the
    # "Acceptance Scenario Template" ARN copies into its own records for
    # each of those runs. Both are things ARN needs in order to own the
    # platform, so both stay published — under a name that says so.
    Entry("docs/operations/uat-plan.md", "Acceptance testing",
          "The repeatable acceptance capability: environment, participants, entry and "
          "exit criteria, and the 17 operator scenarios. Rerun on any station, market "
          "or release change.", AUD_ARN),
    Entry("docs/operations/uat-scenario-sheet.md", "Acceptance testing",
          "The reusable scenario template. Copy it into ARN's records and complete the "
          "run fields per run; it is the evidence form for every rerun, not a one-off "
          "sign-off sheet.",
          AUD_ARN, new=True),
    Entry("docs/operations/defect-log.md", "Acceptance testing",
          "The register ARN records acceptance and integration findings in, "
          "with the finding workflow and severity scheme. Empty at handover.",
          AUD_ARN, new=True),
    Entry("docs/operations/first-time-sign-in.md", "Acceptance testing",
          "Participant walkthrough for anyone who has never opened the console.",
          AUD_ARN, new=True),
    Entry("docs/operations/integration-test-plan.md", "Acceptance testing",
          "Per-leg integration cases I1 to I6 and who executes each. Re-run after a "
          "change to any external leg.", AUD_ARN),

    # ── Findings ─────────────────────────────────────────────────────────
    Entry("docs/operations/platform-overview.md", "Handover",
          "What the platform does, for a reader who has never seen it. Site landing read.",
          AUD_ARN, new=True),
    Entry("docs/operations/glossary.md", "Handover",
          "Every term the rest of the documentation assumes. Alphabetical.",
          AUD_ARN, new=True),
    Entry("docs/operations/first-day.md", "Handover",
          "Ordered checklist for ARN on day one after Arinco leaves.",
          AUD_ARN, new=True),
    Entry("docs/operations/operator-guide.md", "Handover",
          "Task-shaped guide for the content team. Closes the SG3 user-guide gap.",
          AUD_ARN, new=True),
    Entry("docs/operations/dashboard-guide.md", "Handover",
          "What each deployed workbook actually plots. Closes the SC-06 dashboard-guide gap.",
          AUD_ARN, new=True),
    Entry("docs/operations/support-model.md", "Handover",
          "Three support tiers, escalation matrix, hypercare, SC-10 resourcing recommendation.", AUD_ARN),
    Entry("docs/operations/access-and-accounts.md", "Handover",
          "Every identity, credential and endpoint ARN must own by engagement end.", AUD_ARN),
    Entry("docs/operations/run-costs.md", "Handover",
          "Measured Azure run-cost envelope, vendor spend, cost levers.", AUD_ARN),
    Entry("docs/operations/runbook.md", "Operations",
          "The 3am doc: alert catalogue, first responses, deploy and rollback, secrets, audit.", AUD_ARN),
    Entry("docs/operations/as-built.md", "Operations",
          "Deployed reality per environment: inventory, hostnames, network, identity, secrets.", AUD_ARN),
    Entry("docs/operations/slo.md", "Operations",
          "SLIs, SLOs, error budgets and the KQL behind them.", AUD_ARN),
    Entry("docs/operations/notifications.md", "Operations",
          "Teams plumbing, card catalogue, re-consent procedure. Re-verified 28 Jul.", AUD_ARN),
    Entry("docs/operations/disaster-recovery.md", "Operations",
          "Single-region posture and its limits. Re-verified 28 Jul; invented RPO/RTO figures removed.",
          AUD_ARN),
    # The three harness documents were withdrawn from the operator set because
    # an operator has no use for them, not because ARN should not have them.
    # They are the maintainer's test story and belong in the developer group.
    Entry("docs/operations/load-testing.md", "Developers",
          "The backend load harness: scenarios, cost controls, how to run it.",
          AUD_DEV),
    Entry("docs/operations/performance-testing.md", "Developers",
          "The console performance harness and how to diagnose a slow deployed page.",
          AUD_DEV),
    Entry("docs/operations/e2e-testing.md", "Developers",
          "Deterministic end-to-end tests and the gated external-provider checks.",
          AUD_DEV),
    Entry("docs/operations/local-development.md", "Developers",
          "Prerequisites, build, test and run the platform on a workstation.",
          AUD_DEV),
    Entry("docs/operations/solution-structure.md", "Developers",
          "How the code is organised: layers, projects and the rules between them.",
          AUD_DEV, new=True),
    Entry("docs/operations/delivery-pipeline.md", "Developers",
          "The build and release pipeline stage by stage, and how to read a failed run.",
          AUD_DEV, new=True),
    Entry("docs/reference/prompt-iteration-guide.md", "Operations",
          "How ARN changes bulletin quality without a developer. Re-verified 28 Jul.", AUD_ARN),
    Entry("docs/reference/per-station-configuration.md", "Operations",
          "Per-station configuration reference.", AUD_ARN),
    Entry("docs/operations/quick-reference.md", "Operations",
          "Health check, KQL, deploy, park and resume, secret rotation, fake-provider mode.",
          AUD_ARN, new=True),
    Entry("docs/operations/dependencies.md", "Operations",
          "Every external service, what breaks without it, and how to rotate its credential.",
          AUD_ARN, new=True),
    Entry("docs/operations/known-limitations.md", "Operations",
          "What the platform deliberately does not do, and what is accepted for the pilot.",
          AUD_ARN, new=True),
    Entry("docs/operations/faq.md", "Operations",
          "Question-and-answer routing index for operators and support. Closes SC-15.",
          AUD_ARN, new=True),
    Entry("docs/operations/proven-capacity.md", "Operations",
          "The measured load, timings and limits, replacing three harness documents ARN has no use for.",
          AUD_ARN, new=True),

    # ── Architecture ─────────────────────────────────────────────────────
    Entry("docs/canonical/architecture.md", "Architecture",
          "How the platform is built: components, data flow, and why each choice was made.",
          AUD_ARN, new=True),
    Entry("docs/reference/architecture-diagrams.md", "Architecture",
          "The ten published figures, each with what it shows and when to read it.",
          AUD_ARN, new=True),
    Entry("docs/operations/solution-overview.md", "Architecture",
          "The ARN-facing solution description, rewritten from the SG2 design document.",
          AUD_ARN, new=True),
    Entry("docs/operations/design-decisions.md", "Architecture",
          "The dozen design choices that constrain how ARN operates the platform, and why.",
          AUD_ARN, new=True),

    # ── Governance ───────────────────────────────────────────────────────
    Entry("docs/canonical/decisions.md", "Governance", "Our engineering decision log. The ARN-facing subset is rewritten as the design-decisions page.",
          AUD_INT),
    Entry("docs/operations/open-items.md", "Governance",
          "What ARN must action after handover, with owner and by-when.",
          AUD_ARN, new=True),
    Entry("docs/operations/acceptance.md", "Governance",
          "What acceptance means and what the signed cover records.",
          AUD_ARN, new=True),
]
