---
title: "Local Development"
description: "Commands and setup steps for building, testing, and running the ARN Information Bulletin Platform locally"
tags: [operations, local-development, commands]
purpose: "runbook"
status: "current"
last_verified: 2026-09-04
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "Current repository scripts, project files, and local app configuration"
verified_by:
  - "./scripts/verify.sh"
  - "./scripts/verify.sh --full"
  - "./scripts/docs.py check"
  - "manual command review"
aliases:
  - local setup
  - developer workflow
  - build commands
  - test commands
related_code:
  - package.json
  - web/package.json
  - ArnBulletinGenerator.slnx
  - scripts/verify.sh
  - scripts/docs.py
  - src/ArnBulletinGenerator.Functions/
  - src/ArnBulletinGenerator.Functions/local.settings.json.example
  - scripts/e2e-backend.sh
common_queries:
  - "How do I run Arn locally?"
  - "Which build and test commands should I use?"
  - "How do I validate documentation changes?"
see_also:
  - docs/operations/solution-structure.md
---

# Local Development

## Prerequisites

- .NET SDK 10.0.301 (the `global.json` pin; `latestFeature` roll-forward)
- Node.js 22.x
- Azure Functions Core Tools v4+
- Azurite for local Azure Storage emulation
- Bun for the root convenience scripts (`npm run local`)

## Configure Local Settings

The Functions host reads `local.settings.json` first: its `Values` become environment variables, which override `appsettings.json` and `appsettings.Development.json` (`Program.cs` loads the files first, environment last). Copying the example is the only configuration step a workstation needs:

```bash
cp src/ArnBulletinGenerator.Functions/local.settings.json.example src/ArnBulletinGenerator.Functions/local.settings.json
```

Do not commit `local.settings.json` or `appsettings.Development.json`; both are git-ignored. `KeyVaultUri` stays empty on a workstation, which makes the host skip the Key Vault step at startup.
When Azurite is not running, the host shuts back down with `Connection refused (127.0.0.1:1000x)` in its log — start Azurite and run `func start` again.

Start Azurite before the host (`azurite --silent`, or `npm run local` for the whole stack): `AzureWebJobsStorage` points at the emulator.

The two example files cover different layers. `local.settings.json.example` is the bring-up path: `func start` loads it as environment variables, which win over both JSON files. `appsettings.json.example` documents the file-layer shape (including the `Here` section) for overrides in `appsettings.Development.json`. For a first boot, the `local.settings.json` copy is sufficient on its own.

### Settings the host demands at boot

Options validation (`ValidateOnStart`) runs on every boot, including Fake mode. A missing or empty boot-mandatory value stops the host with an `OptionsValidationException` before any function listens:

| Setting (`Values` key) | Boot-mandatory | Where the real value comes from |
+|---|---|---|
+| `FUNCTIONS_WORKER_RUNTIME` | Yes — the host starts the .NET isolated worker with it | Fixed value `dotnet-isolated`; not a secret |
+| `AzureWebJobsStorage` | Yes — the Functions runtime itself requires it | Azurite on a workstation (`UseDevelopmentStorage=true`); a real connection string only exists in Azure |
+| `AadClientId` | The host boots without it, but every endpoint except `/api/health` then answers `503` | The Entra application registration for the API; the Entra application owner holds it (see `access-and-accounts.md`) |
+| `Auth__DevFallbackRole` | Required for local API calls, unless every request carries an `X-MS-CLIENT-PRINCIPAL` header (next section) | A workstation-only bypass; always empty in test and production — the host refuses to boot on a test or production site with it set |
+| `Providers__ExternalProviderMode` | No — defaults to `Real` | `Fake` substitutes deterministic offline providers for traffic, routing, geocoding, weather, fuel, script generation, voice and synthesis; `Real` calls the vendors below |
+| `Providers__OutputProvider` | No — defaults to `Console` | `Console` keeps audio output on the workstation; `AzureBlob` writes it to blob storage |
+| `Storage__AccountName` | Yes — `[Required]` validation | `devstoreaccount1` routes the storage SDK clients to Azurite |
+| `AiSettings__AzureAI__Endpoint`, `AiSettings__AzureAI__Model` | Yes — `[Required]` validation, and the endpoint must be a URL | The Azure AI Services resource in the matching environment; the subscription administrator holds it. Deployed environments receive the endpoint and key from Key Vault, never from the repo |
+| `AiSettings__AzureAI__ApiKey` | No — an empty key selects the managed-identity chain | The `AiSettings--AzureAI--ApiKey` vault secret |
+| `AiSettings__ElevenLabs__ApiKey` | Yes — `[Required]` validation | The ElevenLabs workspace; the vendor account owner holds it |
+| `Here__ApiKey` | Yes — `[Required]` validation with no default | The HERE developer console, server-side REST key; the vendor account owner holds it |
+| `Here__MapApiKey` | No — empty shows "Map unavailable" in every console map (no keyless fallback) | A separate browser-safe HERE key, domain-restricted to the console hostnames; `Here--MapApiKey` in Key Vault when deployed |
+| `Webhook__Enabled`, `Zetta__Enabled`, `AuditExport__Enabled` | No | Workstation safety switches; `false` stops the workstation posting notifications, delivering to playout, or running the audit-export tick |

### Running without vendor keys (offline)

The example ships `Providers__ExternalProviderMode: Fake` with `fake` placeholders for every vendor value — the same combination `scripts/e2e-backend.sh` boots the host with. Validation still runs in Fake mode, so the placeholders must stay non-empty and the endpoint must stay a URL. Going live on a workstation means setting the mode to `Real` and replacing the HERE key, the ElevenLabs key, the Azure AI endpoint plus model, and `AadClientId` with real values; keep `Auth__DevFallbackRole` set while calling the API by hand. Never commit the file.

### Calling the local API

- `GET /api/health` answers without authentication; it is the readiness probe (`scripts/local-start.sh` polls it for `200` or `503`).
- Every other endpoint enforces a role. There are two ways to present identity locally:
  1. Nothing at all: with `Auth__DevFallbackRole` set (the example uses `Admin`), the middleware synthesises a dev identity with that role for any request without a principal header.
  2. An `X-MS-CLIENT-PRINCIPAL` header: base64 JSON in either the Easy Auth shape (`auth_typ: aad` with a `claims` array) or the Static Web App shape (`identityProvider: aad` with `userId`, `userDetails`, `userRoles`). Roles come from the `roles` claims / `userRoles`; the fallback role is added on top when set. `scripts/local-start.sh` carries a ready-made Admin blob in its `PRINCIPAL` variable:

```bash
PRINCIPAL=$(grep '^PRINCIPAL=' scripts/local-start.sh | cut -d'"' -f2)
curl -H "X-MS-CLIENT-PRINCIPAL: $PRINCIPAL" http://localhost:7071/api/schedules
```

- Roles: `Operator` covers market- and job-scoped reads and actions plus the shared speech libraries; `Admin` covers global settings, the operator register, bulk config transfer, and every `DELETE`.
- No Easy Auth layer checks tokens on a workstation, so a local header is self-asserted. That is the point of the bypass — and the reason the fallback role stays empty outside a workstation.

## Build and Test

```bash
./scripts/verify.sh          # default: every gate, scoped to the areas you changed, in parallel lanes
./scripts/verify.sh --quick  # same scoping, minus the slow tail (backend E2E, web E2E, web perf, Slow-trait tests)
./scripts/verify.sh --full   # every gate, every area, unscoped — the exact CI mirror
./scripts/verify.sh --explain  # show which gates would run, and why, without running them
```

Gate selection comes from the paths you touched: a docs-only change runs the
documentation checks and secret scan, a web-only change never builds .NET, and
any path the classifier does not recognise escalates to every area. Backend,
web, eslint, bicep, docs and gitleaks run as concurrent lanes, so wall time is
the slowest lane rather than the sum of every phase. The pre-push hook runs the
default mode, scoped to the exact commit range being pushed.

Equivalent to the fast gate's backend step directly:

```bash
dotnet build ArnBulletinGenerator.slnx -c Release
dotnet test ArnBulletinGenerator.slnx -c Release --no-build --filter "Category!=Integration&Category!=E2E&Category!=Slow"
```

## Language Server (LSP) Setup

If your .NET SDK is managed by a version manager (e.g. mise/asdf) rather than a
system-wide install, `csharp-ls` can fail with `hostfxr_resolve_sdk2: No .NET
SDKs were found` even though `dotnet` itself works fine on `PATH`. `csharp-ls`
uses `Microsoft.Build.Locator`, which calls the native `hostfxr` resolver
directly — this reads `DOTNET_ROOT` from the environment and does not go
through the version manager's shell shim or `PATH`.

Fix: point your editor's LSP config at a small wrapper that exports
`DOTNET_ROOT` before delegating to `csharp-ls`, instead of at the raw binary.
A wrapper that resolves the SDK correctly reports `SDK instances found` and
loads `ArnBulletinGenerator.slnx`.

## Run Everything

```bash
npm run local
```

This starts Azurite, Azure Functions, and the Vite frontend through the root
`package.json` scripts. Bun must be installed because those scripts invoke it.

## Run Components Separately

Run these commands in separate terminals so each long-running process can stay
active:

```bash
azurite --silent

cd src/ArnBulletinGenerator.Functions
func start

cd web
npm ci
npm run dev
```

The Vite dev server runs on `http://localhost:5173` and proxies `/api/*` to `http://localhost:7071`.

## Frontend Commands

```bash
cd web
npm run build
npm run test
npm run lint
npm run format
```

## Documentation Harness

Route a task to relevant docs:

```bash
./scripts/docs.py route "deployment configuration safety"
```

Create a new live doc:

```bash
./scripts/docs.py new-doc \
  --purpose reference \
  --title "Example Reference" \
  --description "When to read this reference" \
  --tags example,reference \
  --source-of-truth "Implementation or source material that owns the facts"
```

Append a complete durable decision:

```bash
./scripts/docs.py add-decision \
  --title "Example Decision" \
  --context "What forced the choice." \
  --decision "What was chosen." \
  --rationale "Why this option won." \
  --consequences "What changes because of it."
```

Regenerate all derived indexes:

```bash
./scripts/docs.py generate
```

Validate docs, generated indexes, decisions, skills, links, review dates, and context budgets:

```bash
./scripts/docs.py check
```
