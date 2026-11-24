---
title: "End-to-End Testing"
description: "Runbook for deterministic backend and frontend E2E tests plus gated external-provider smoke checks"
tags:
  - "operations"
  - "e2e"
  - "testing"
  - "ci"
aliases:
  - "e2e testing"
  - "end-to-end tests"
  - "playwright tests"
  - "backend e2e"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "scripts/e2e-backend.sh, scripts/e2e-external-smoke.sh, Playwright config, E2E test projects"
verified_by:
  - "./scripts/docs.py check"
  - "re-verified 2026-07-24: npm run e2e/e2e:backend/e2e:serial commands and scripts/e2e-backend.sh still match scripts/verify.sh"
related_code:
  - "scripts/e2e-backend.sh"
  - "scripts/e2e-external-smoke.sh"
  - "test/ArnBulletinGenerator.E2E.Tests"
  - "web/e2e"
  - "web/playwright.config.ts"
  - "test/ArnBulletinGenerator.ExternalSmoke.Tests"
common_queries:
  - "How do I run Arn E2E tests?"
  - "How do fake external providers work?"
  - "How do I run provider contract smoke tests?"
see_also:
  - "test/ArnBulletinGenerator.E2E.Tests/RouteCoverage.md"
  - "web/e2e/RouteCoverage.md"
---

# End-to-End Testing

Arn now has three E2E layers:

1. **Backend deterministic E2E** — `test/ArnBulletinGenerator.E2E.Tests` drives the real Azure Functions host over HTTP while `Providers__ExternalProviderMode=Fake` replaces HERE, Azure OpenAI, ElevenLabs, Power Automate, and Zetta edges with deterministic in-process providers.
2. **Frontend behavioral E2E** — `web/e2e` uses Playwright against `npm run dev:mock`, exercising operator-visible workflows through the existing mutable mock API layer.
3. **External provider smoke** — `test/ArnBulletinGenerator.ExternalSmoke.Tests` is gated by `ARN_EXTERNAL_SMOKE=1` and checks live HERE, Azure OpenAI, and ElevenLabs API contract drift without running in the constant PR path.

## Local commands

```bash
npm run e2e:backend
cd web && npm run e2e
npm run e2e
npm run e2e:serial
ARN_EXTERNAL_SMOKE=1 npm run e2e:external-smoke
```

The full deterministic gate is `npm run e2e`: it runs the backend fake-provider E2E and the frontend Playwright mock-mode E2E **concurrently** (independent ports `7071` and `5173`), so wall time is roughly the slower of the two rather than their sum. Use `npm run e2e:serial` for sequential, interleave-free output (CI runs the two as separate steps/jobs). Neither must require live HERE/OpenAI/ElevenLabs credentials.

### Faster local iteration

`scripts/e2e-backend.sh` honours `ARN_E2E_REUSE_HOST=1`: when a healthy host is already listening on `:7071`, it skips Azurite setup, the build, and `func start`, and runs `dotnet test` straight against the running host. Start a host once (`npm run e2e:backend` leaves one only if interrupted, or run the Functions host yourself), then:

```bash
ARN_E2E_REUSE_HOST=1 npm run e2e:backend
```

This trades a clean-slate guarantee for speed: it assumes the existing host and its storage are healthy. Run without the flag for a clean cold start.

## Backend fake-provider mode

Set fake mode with:

```bash
Providers__ExternalProviderMode=Fake
```

Equivalent JSON:

```json
{
  "Providers": {
    "ExternalProviderMode": "Fake"
  }
}
```

Default is `Real`; deployed behavior is unchanged unless fake mode is explicitly selected. Unknown enum values should fail options binding/startup validation rather than silently falling back.

`scripts/e2e-backend.sh`:

- starts Azurite on blob `10000`, queue `10001`, table `10002`;
- creates `broadcast-queue`, `regeneration-queue`, both poison queues, `broadcasts`, and `audit-exports`;
- starts `func start` with fake provider settings plus a fake fallback cart URL, and writes logs to `/tmp/arn-e2e-func.log` by default;
- writes fake Zetta output under `/tmp/arn-e2e-zetta` by default;
- runs `dotnet test test/ArnBulletinGenerator.E2E.Tests/ArnBulletinGenerator.E2E.Tests.csproj -c Debug`.

Inspect backend failures with:

- `/tmp/arn-e2e-func.log` for Functions host output;
- `/tmp/azurite-arn-e2e` for emulator data, unless `ARN_E2E_AZURITE_DIR` overrides it;
- `/tmp/arn-e2e-zetta` for fake SMB/Zetta writes, unless `E2E__ZettaOutputPath` overrides it;
- job IDs printed by failing E2E assertions and the HTTP `GET /api/jobs/{jobId}` payload.

## Frontend Playwright E2E

`web/playwright.config.ts` starts:

```bash
npm run dev:mock -- --host 127.0.0.1
```

It runs Chromium desktop `1440x900` and mobile `390x844`, freezes time at `2026-06-22T09:00:00+10:00`, disables animations, and retains trace/screenshot/video only on failure.

Inspect frontend failures with:

- Playwright trace attachments under `web/test-results`;
- screenshots/videos retained on failed tests;
- the route/workflow coverage map in `web/e2e/RouteCoverage.md`.

The mock store exposes `window.__ARN_MOCK_RESET__()` only in mock mode so tests can reset mutable fixtures before each case.

## External provider smoke

External smoke is opt-in:

```bash
export ARN_EXTERNAL_SMOKE=1
export AZURE_OPENAI_ENDPOINT=...
export AZURE_OPENAI_API_KEY=...
export AZURE_OPENAI_MODEL=...
export HERE_API_KEY=...
export ELEVENLABS_API_KEY=...
npm run e2e:external-smoke
```

Optional TTS synthesis quota burn is additionally gated:

```bash
export ARN_EXTERNAL_SMOKE_TTS=1
```

Without `ARN_EXTERNAL_SMOKE=1`, `scripts/e2e-external-smoke.sh` prints a skip message and exits `0`.

## CI behavior

- `azure-pipelines.yml` runs backend E2E before publishing the function app and frontend Playwright before building the web artifact.
- `azure-pipelines.yml` mirrors those deterministic gates because both pipeline files remain valid Azure DevOps entry points.
- `azure-pipelines.yml` has a nightly scheduled external-provider smoke job (`19:00 UTC`) guarded by `Build.Reason == Schedule`.

## Do not

- Do **not** point deterministic E2E at live HERE, Azure OpenAI, or ElevenLabs.
- Do **not** use process-private fake-provider memory as the primary backend assertion surface; assert through HTTP responses, job state, storage/queue/blob observations, and persisted logs.
- Do **not** downgrade backend E2E to in-process trigger tests; the value is the real Functions host, middleware, queue, storage, and HTTP behavior.
