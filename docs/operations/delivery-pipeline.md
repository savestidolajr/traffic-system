---
title: "The Delivery Pipeline"
description: "How a change reaches an environment: the build and release stages, the gates and approvals, and how to read a failed run"
tags:
  - "developer"
  - "ci-cd"
  - "pipeline"
  - "deployment"
  - "azure-devops"
aliases:
  - "pipeline"
  - "ci/cd"
  - "build pipeline"
  - "release pipeline"
  - "azure devops pipeline"
purpose: "runbook"
status: "current"
last_verified: 2026-09-03
review_after: 2026-12-03
volatility: "medium"
source_of_truth: "azure-pipelines.yml and the stage templates in deployment/pipelines/"
verified_by:
  - "Stage graph, compile-time target contract, gates and jobs read from azure-pipelines.yml and deployment/pipelines/, 2026-09-03"
related_code:
  - "azure-pipelines.yml"
  - "deployment/pipelines/"
common_queries:
  - "How does a change reach production?"
  - "What runs in CI?"
  - "Why did the pipeline not deploy?"
  - "How do I roll back?"
see_also:
  - "docs/operations/solution-structure.md"
  - "docs/operations/local-development.md"
  - "docs/operations/runbook.md"
---

# The Delivery Pipeline

One pipeline, defined in `azure-pipelines.yml` with the repeated stages pulled
out into templates under `deployment/pipelines/`. It builds, tests, gates, and
promotes the same versioned artefacts through development, test and production.

![Delivery pipeline](diagram:cicd-pipeline.png)

## What starts a run

| Trigger | Effect |
|---|---|
| Push to `master` or `main` | Starts a batched CI run. By default it compiles to Build and Package only; deployment and evaluation stages are parameter-gated |
| Pull request to `master` or `main` | Build and validation only; also publishes code coverage |
| Nightly at 18:30 UTC (04:30 AEST) | Drift watch only, after the Australian broadcast day |
| Sunday 19:30 UTC | Weekly drift watch with the full judge run |

Changes to `*.md` and `docs/*` are excluded from both the push and pull-request
triggers, so a documentation-only change does not consume a build.

Scheduled runs skip Build and Package entirely, and a `ScheduleNotice` stage
states plainly that such a run verifies nothing. That is deliberate: a green
scheduled run is not evidence that the code builds.

## The stage graph

```
Build & Test  ─┐
               ├─→ Deploy_Dev ──→ Deploy_Test ──→ Deploy_Prod
Package       ─┘    target=dev|test|prod   target=test|prod   target=prod
                                      (ADO approval)       (ADO approval)

EvalGate_Golden        (enableEvalGate; independent of Build and Package)

EvalDrift, EvalQualityPanel   (enableEvalGate; scheduled runs only)
NotifyOnFailure               (on any failure)
```

Build and Package have no dependency on each other and run concurrently: the
console build needs nothing from the backend. The compile-time
`targetEnvironment` parameter selects the deployment chain:

- `none` (the default) compiles no deployment stage.
- `dev` compiles `Deploy_Dev`.
- `test` compiles `Deploy_Dev` followed by `Deploy_Test`.
- `prod` compiles `Deploy_Dev` followed by `Deploy_Test` and `Deploy_Prod`.

Every deployment stage also has a `master`/`main` branch condition. The
deployment stages consume the same Build and Package artefacts in sequence;
they do not rebuild per environment. `Deploy_Test` and `Deploy_Prod` each
wait for their ADO environment approval.

`deployInfrastructure` does not select stages. It controls the jobs inside
every compiled deployment stage: `true` (the default) includes Bicep and
autoscale; `false` omits only those infrastructure jobs for an app-only
promotion. Function App, Static Web App, documentation site (when enabled by
the environment variable group), and Smoke still run, and the ADO approvals
still apply.

### Build & Test

Four jobs:

- **Backend** — restore, build, run the unit and integration tests, then run the
  backend end-to-end suite against a real Functions host with fake external
  providers. Publishes the function app, zips it, and uploads it as the artefact
  the deploy stages consume. NuGet packages and the Functions Core Tools are
  cached.
- **Bicep lint & build** — compiles `main.bicep` and every `.bicepparam`. This
  catches a template error before a deployment does.
- **Secret scan** — `gitleaks` over the repository.
- **Dependency vulnerability scan** — the .NET dependency audit.

### Package

One job for the console: install, lint, unit tests, npm vulnerability audit,
Playwright end-to-end tests in mock mode, a Lighthouse performance audit gated
against budgets, an informational runtime performance probe, then the
production build and artefact upload. npm and the Playwright browsers are
cached.

The performance audit is budget-gated, which means a change that regresses the
console's load performance past the budget fails the build rather than
producing a report nobody reads.

`EvalGate_Golden` runs only when `enableEvalGate=true` (default `false`), so it
does not block an ordinary push. When enabled, it runs the deterministic
bulletin-quality suites against the golden corpus, including a check that no
bulletin invents a road name. It costs no model tokens for the deterministic
graders.

Two further eval stages run only on the schedules: `EvalDrift` harvests the
previous day's bulletins and applies the drift floors, and
`EvalQualityPanel` generates fresh bulletins through the real generation path
and grades them.

`EvalQualityPanel` used to block production promotion and was moved to the
schedule. The reasoning is recorded in a comment above the production deploy
stage and is worth reading before putting it back: it had never passed a
complete run, its one hard check is already enforced deterministically by the
golden gate, and two panels ninety minutes apart scored the same corpus 0.50
and 0.12. A deploy gate has to be deterministic. This one is a monitoring
instrument, and it needs a pass history before it can be a gate.

### Deploy

Each environment runs the same template, `deployment/pipelines/deploy-stage.yml`,
with different parameters. Five jobs, in order:

| Job | What it does |
|---|---|
| `Infrastructure` | Deploys the Bicep template with that environment's parameter file |
| `FunctionApp` | Zip-deploys the backend artefact |
| `StaticWebApp` | Deploys the console artefact |
| `DocsSite` | Builds and publishes this documentation site |
| `Smoke` | Checks the health endpoint and that authentication fails closed |

Per-environment differences that matter:

| | Dev | Test | Production |
|---|---|---|---|
| Variable group | `arn-traffic-dev` | `arn-traffic-test` | `arn-traffic-variables` |
| Service connection | `IBP-DEV` | `IBP-TST` | `IBP-PRD` |
| Parameter file | `main.dev.bicepparam` | `main.test.bicepparam` | `main.prod.bicepparam` |
| Console authentication | Built-in provider plus the development role fallback | Custom Entra provider configuration | Custom Entra provider configuration |
| Approval before deploy | No | Yes | Yes |
| Documentation site | Published | Not published | Published |

The documentation site is deliberately not published to test: test is a UAT
environment with no reason to host documentation, so its `deployDocsSite`
parameter and the matching variable-group entry are both `false`. Dev and
production publish the same site from the same source.

Test and production are faithful mirrors of each other by design; development
differs deliberately, and the difference is the console's authentication
configuration.

## Deployment and evaluation parameters

Deployment stages are selected at compile time, so they do not merely skip
inside an ordinary run: an explicit `targetEnvironment` is required for them
to exist. The parameter is a string enum with these values:

| Parameter | Default | Values | Effect |
|---|---|---|---|
| `targetEnvironment` | `none` | `none`, `dev`, `test`, `prod` | `none` compiles no deploy stage; `dev` compiles Dev; `test` compiles Dev → Test; `prod` compiles Dev → Test → Prod |
| `deployInfrastructure` | `true` | Boolean | In each compiled deploy stage, `true` runs Bicep and autoscale; `false` skips only those jobs while Function App, Static Web App, documentation site (when enabled), and Smoke still run |
| `enableEvalGate` | `false` | Boolean | Compiles the golden gate and scheduled eval stages |

The target chain and `deployInfrastructure` choice are independent. An
app-only promotion still uses the same environment approvals for Test and
Production. `enableEvalGate` is also independent of deployment and can be
enabled on a manual run without compiling any deploy stage.

The default `targetEnvironment=none` is intentional: automatic pushes compile
to Build and Package only. The pipeline contract is source-verified from
`azure-pipelines.yml` on 2026-09-03; historical green runs dated earlier
predate this target-parameter contract and are evidence for the gates they
actually exercised, not live proof of these new queue combinations.

## How to read a failed run

Open the run in Azure DevOps, then work in this order:

1. **Which stage failed.** Build or Package means the change itself. A deploy
   stage means the change is fine and the environment or the template is not.
2. **Which job inside it.** The four Build jobs fail for unrelated reasons; a
   `gitleaks` failure and a unit-test failure need completely different
   responses.
3. **Read the first failing step, not the last.** A failed run usually ends with
   a cascade of skipped and cancelled steps that say nothing.
4. **Reproduce it locally.** Every gate in Build and Package has a local
   equivalent on the [building and running page](/develop/local-development/).
   If it fails locally, stop reading pipeline logs.

Some failures that look alike but are not:
| Symptom | Usual cause |
|---|---|
| Build and Package ran but no deployment stage exists | `targetEnvironment=none` (the default), or the selected target was not one of `dev`, `test`, `prod` |
| `Deploy_Dev` is absent | The target is `none`; `targetEnvironment=test` and `targetEnvironment=prod` both compile Dev |
| `Deploy_Test` is absent | The target is `none` or `dev`; only `test` and `prod` compile Test |
| `Deploy_Prod` is absent | Only `targetEnvironment=prod` compiles Production |
| Infrastructure is absent but application jobs run | `deployInfrastructure=false` intentionally selected the app-only path; Bicep and autoscale are skipped, but Function App, web, docs (when enabled) and Smoke still run |
| A deploy stage is not running on the selected target | The queue used a non-`master`/`main` branch, or an ADO environment approval is waiting for a human |
| The documentation site job is skipped | The `deployDocsSite` variable is not `true` in that environment's variable group |
| The documentation site job fails on a missing token | `DOCS_SWA_DEPLOYMENT_TOKEN` is absent. The token only exists once the site's hosting resource does, so the first run provisions it and the second publishes |
| Smoke fails while every deploy job passed | The application deployed but is unhealthy. Follow the [operations runbook](/operate/runbook/) |

A failure on any stage posts to the operations channel through the
`NotifyOnFailure` stage. If no notification arrives, the notification webhook
is unset for that pipeline rather than the run being fine.

## Releasing and rolling back

A release or deployment is never caused by a push. Queue Pipeline 108
deliberately on `master` or `main` and choose one target:

- `targetEnvironment=dev` deploys Dev only.
- `targetEnvironment=test` deploys the same Build and Package artefacts through
  Dev, then Test, stopping at Test after its ADO approval.
- `targetEnvironment=prod` deploys those same artefacts through Dev, Test and
  Production, with the Test and Production approvals.

`deployInfrastructure=true` is the default and runs Bicep plus autoscale in
each compiled stage. Set `deployInfrastructure=false` only for an app-only
promotion: it skips Bicep/autoscale but still deploys Functions, the console,
the documentation site where enabled, and Smoke, with approvals unchanged.
Set `enableEvalGate=true` only when the golden and scheduled eval stages are
also required. Promotion moves the same built artefacts forward; nothing is
rebuilt per environment, so what Production gets is byte-identical to what
Test approved.

**Rollback is a rerun of the last successful run**, not a revert commit and not
a manual deployment. The runbook covers this from the operator's side under
release handling; the mechanics are the same from here.

Two things to check before promoting:

1. The run you are promoting is green on the commit you think it is. The commit
   is on the run summary; confirm it matches the head of `master` or `main`.
2. The service levels look normal after the development deploy, before approving
   test. The [service levels page](/operate/slo/) has the queries.
