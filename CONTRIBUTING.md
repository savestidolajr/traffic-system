# Contributing to ArnBulletinGenerator

Handover guide for developers joining the project. Covers setup, conventions, architecture, and day-to-day development workflow.

---
## What this repository deliberately does not contain

This repository ships only what ARN needs to run, operate, and extend the
platform. Deliberately absent, and not to be re-added:

- Agent-harness configuration and local skill libraries (developer aids, not product).
- Internal working notes, session memory, and meeting captures.
- Evaluation labs, corpora, and one-off research tooling.
- Arinco commercial material (status reports, decks, case studies, video production).

`.gitignore` keeps `.agents/`, `.omp/`, `.foundry/`, and
`tools/showcase-video/` local so these cannot drift back in. When a working
note grows into a durable platform fact, record the fact in `docs/` — do not
commit the note.


## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| **.NET SDK** | 10.0.301 | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) |
| **Node.js** | 22.x | [nodejs.org](https://nodejs.org/) |
| **Azure Functions Core Tools** | v4+ | [Install guide](https://learn.microsoft.com/azure/azure-functions/functions-run-local) |
| **Azurite** | latest | `npm install -g azurite` (local Azure Storage emulator) |
| **Bun** *(required for `npm run local`)* | latest | The root convenience scripts invoke `bun x`; without Bun, start each component individually as below |

> **`global.json` exists** — pinned to `10.0.301` with `rollForward: latestFeature`.

---

## Getting Started

### 1. Clone & Restore

```bash
git clone <repo-url> && cd <repo-directory>
dotnet restore ArnBulletinGenerator.slnx
```

### 2. Configure

```bash
cp src/ArnBulletinGenerator.Functions/local.settings.json.example src/ArnBulletinGenerator.Functions/local.settings.json
```

The copied file boots the host offline: it selects the fake providers and carries `fake` placeholders for the Azure AI, ElevenLabs and HERE settings that options validation demands at startup. For real vendor calls, replace those placeholders and set `Providers__ExternalProviderMode` to `Real`. See [Configuration](#environment-variables) below and `docs/operations/local-development.md` for the full setting table.

### 3. Run Everything (one command)

```bash
# Starts Azurite + Azure Functions + Vite dev server
npm run local
```

Or start each component individually:

```bash
# Terminal 1 — Azurite
azurite --silent

# Terminal 2 — Backend
cd src/ArnBulletinGenerator.Functions && func start

# Terminal 3 — Frontend
cd web && npm install && npm run dev
```

The Vite dev server runs on `http://localhost:5173` and proxies `/api/*` to `http://localhost:7071`.

### 4. Build

```bash
# Backend
dotnet build ArnBulletinGenerator.slnx

# Frontend
cd web && npm run build
```

---

## Project Structure

The backend follows **Clean Architecture** with strict dependency inversion — inner layers never reference outer layers.

```
src/
├── ArnBulletinGenerator.Domain/            # Pure C# — entities, value objects, interfaces
│                                           # Zero external dependencies
├── ArnBulletinGenerator.Application/       # Business logic orchestration
│                                           # Depends on: Domain
├── ArnBulletinGenerator.Infrastructure/    # External service adapters (AI, storage, APIs)
│                                           # Depends on: Domain
└── ArnBulletinGenerator.Functions/         # Azure Functions entry points (HTTP/Queue/Timer)
                                            # Depends on: all layers (composition root)

test/
├── ArnBulletinGenerator.Domain.Tests/
├── ArnBulletinGenerator.Application.Tests/
├── ArnBulletinGenerator.Infrastructure.Tests/
├── ArnBulletinGenerator.Functions.Tests/
├── ArnBulletinGenerator.ArchitectureTests/
├── ArnBulletinGenerator.E2E.Tests/
└── ArnBulletinGenerator.ExternalSmoke.Tests/
web/                    # React 19 + TypeScript 5.9 + Vite + shadcn/ui + Tailwind CSS
deployment/             # Azure Bicep IaC, provisioning scripts, AI agent policies
```

### Dependency Graph

```
Domain  ← Application  ← Functions (composition root)
   ↑                         ↓
   └──── Infrastructure ─────┘
```

Domain and Infrastructure both depend on Domain only. Functions wires everything via DI.

---

## Development Workflow

### AI-Assisted Development

`AGENTS.md` is the canonical instruction file for AI coding agents. `.github/copilot-instructions.md` only adapts agents back to that source.
Before substantial agent-assisted work, run `./scripts/docs.py route "<task summary>"`, then open only the relevant docs or sections returned by the route command. Agent-harness configuration and any local skill library are developer aids and are not part of this repository.

After documentation, prompt, skill, or agent-harness changes, run:

```bash
./scripts/docs.py generate
./scripts/docs.py check
```

### Environment Variables

Override `appsettings.json` values via environment variables:

| Variable | Maps To |
|----------|---------|
| `AZURE_OPENAI_ENDPOINT` | `AiSettings:AzureAI:Endpoint` |
| `AZURE_OPENAI_API_KEY` | `AiSettings:AzureAI:ApiKey` |
| `ELEVENLABS_API_KEY` | `AiSettings:ElevenLabs:ApiKey` |
| `HERE_API_KEY` | `Here:ApiKey` |

In production, secrets are loaded from Azure Key Vault (configured via `KeyVaultUri`).

### appsettings Layering

- `appsettings.json` — defaults (checked in, no secrets)
- `appsettings.Development.json` — local overrides (git-ignored, copy from `.example`)
- Environment variables override both

### Storage Emulation

Azurite emulates Table Storage, Blob Storage, and Storage Queues locally. Set `AzureWebJobsStorage` to `UseDevelopmentStorage=true` (already default in the example config).

---

## Code Conventions

### C# Style (`.editorconfig` enforced)

| Rule | Setting |
|------|---------|
| Namespaces | **File-scoped** (`namespace Foo;`) — warning level |
| Constructors | **Primary constructors** preferred |
| `var` usage | Explicit types for built-in types; `var` when type is apparent |
| Using directives | Outside namespace, `System` first |
| Nullable | Enabled globally via `Directory.Build.props` |
| Implicit usings | Enabled globally |

### Result and Option Patterns

The codebase uses **monadic `Result<T>` and `Option<T>`** types instead of exceptions for control flow:

```csharp
// Result<T> — represents success or failure
Result<Job> result = await jobRepository.GetJobAsync(jobId);
return result.Match(
    onSuccess: job => Results.Ok(job),
    onFailure: error => Results.NotFound(error)
);

// Option<T> — represents presence or absence
Option<string> voiceName = Option.OfWhitespace(input);
string name = voiceName.GetValueOr("DefaultVoice");
```

Key methods: `Map`, `Bind`/`BindAsync`, `Match`, `Tap`/`TapAsync`, `GetValueOr`, `Or`.

**Convention**: return `Result.Failure(...)` instead of throwing exceptions in service/repository code. Reserve exceptions for truly exceptional situations (infrastructure failures caught at boundaries).

### Frontend

- **React 19** with TypeScript (strict mode)
- **Zod** for API response validation
- **React Query** (`@tanstack/react-query`) for server state
- **React Hook Form** + `@hookform/resolvers` for form handling
- **shadcn/ui** + Tailwind CSS for components
- **ESLint** + **Prettier** for formatting (`npm run lint` / `npm run format`)

---

## Testing

### Backend

```bash
# All tests
dotnet test ArnBulletinGenerator.slnx

# Specific layer
dotnet test test/ArnBulletinGenerator.Domain.Tests
dotnet test test/ArnBulletinGenerator.Application.Tests
dotnet test test/ArnBulletinGenerator.Functions.Tests
dotnet test test/ArnBulletinGenerator.Infrastructure.Tests
```

| Project | Scope | Mocking |
|---------|-------|---------|
| `Domain.Tests` | Pure domain logic (Result, Option, CorridorAggregator) | None required |
| `Application.Tests` | Orchestrator, services, model selection | NSubstitute for interfaces |
| `Functions.Tests` | HTTP trigger validation and routing | Custom `MockHttpRequestData` |
| `Infrastructure.Tests` | Integration / E2E | In-memory repos; real-service tests gated by credentials |

**Stack**: xUnit · NSubstitute · Shouldly
**Pattern**: Arrange–Act–Assert (AAA), Result/Option-aware assertions.

### Frontend

```bash
cd web
npx vitest run         # Single run
npx vitest             # Watch mode
```

**Stack**: Vitest

---

## Deployment

### Infrastructure (Bicep)

All Azure resources are defined in `deployment/`:

```bash
az deployment group create \
  --resource-group <rg-name> \
  --template-file deployment/main.bicep \
  --parameters deployment/main.dev.bicepparam
```

Pick the parameter file per environment: `main.dev.bicepparam`, `main.test.bicepparam` or `main.prod.bicepparam`.

Resources provisioned: Storage Account, Key Vault, Application Insights, Azure AI Services, Static Web App, and Azure Functions (PremiumV3 P0v3).

Bicep modules live in `deployment/modules/` — each resource type has its own module.

### Backend

```bash
dotnet publish src/ArnBulletinGenerator.Functions -c Release -o publish
cd publish && zip -r /tmp/deploy.zip .
az functionapp deployment source config-zip \
  --resource-group <rg-name> \
  --name <function-app-name> \
  --src /tmp/deploy.zip
```

Or use the deploy script: `deployment/scripts/deploy.sh`

### Frontend

Deployed via Azure Static Web Apps (linked to the repository or SWA CLI).

```bash
cd web && npm run build
# Deploy via SWA CLI or CI/CD pipeline
```

### AI Agent Policies

Provisioning scripts in `deployment/scripts/` define the behaviour of the 3-stage LLM pipeline (draft → review → refine). Update and publish with:

```bash
deployment/scripts/provision-foundry-agents.sh
deployment/scripts/provision-workflow-agents.sh
```

---

## Key Architecture Decisions

### 1. Monadic Result/Option Types

All service and repository methods return `Result<T>` or `Option<T>` instead of throwing exceptions. This makes error paths explicit, composable via `Bind`/`Map`, and pattern-matchable. Defined in `Domain/Models/`.

### 2. Azure Functions Isolated Worker Model

The backend runs on the **.NET isolated worker** model (not in-process), targeting `net10.0`. This provides full control over middleware, DI, and configuration. Entry point is `Program.cs` in the Functions project.

### 3. Queue-Based Async Processing

Broadcast requests are enqueued to Azure Storage Queues and processed asynchronously by queue-triggered functions. This decouples the HTTP response from the long-running pipeline (traffic fetch → LLM scripting → TTS synthesis) and enables retry/recovery.

### 4. 3-Stage LLM Pipeline

Script generation uses Azure AI Foundry agents with Semantic Kernel prompt templates:
1. **Draft** — generates initial script from traffic data
2. **Review** — evaluates script quality with structured verdict
3. **Refine** — revises script if review fails (loops up to `MaxGenerationRetries`)

### 5. Duration-Targeted Audio Synthesis

ElevenLabs TTS output is fine-tuned to hit target broadcast durations via speed adjustment and silence padding. Voice model selection is configurable per schedule.

### 6. Clean Architecture with Dependency Inversion

All external dependencies (Azure Storage, OpenAI, ElevenLabs, HERE Maps) are abstracted behind interfaces in the Domain layer and implemented in Infrastructure. The Functions project is the composition root that wires DI.
