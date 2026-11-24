# ARN Bulletin Generator — Web Dashboard

Operator console for the ARN Traffic Bulletin pipeline. A split workstation ("On air") surfaces every bulletin needing human sign-off against its air deadline (coverage board + triage queue + review pane with audio/script/incident provenance); station profiles carry schedules, voices, and coverage, with one-click ad-hoc triggers.

## Tech Stack

| Layer         | Technology                                               |
| :------------ | :------------------------------------------------------- |
| Framework     | React 19, TypeScript 6                                   |
| Build         | Vite 8 (rolldown)                                        |
| Server State  | TanStack Query v5 (React Query)                          |
| Tables        | TanStack Table v8                                        |
| Routing       | React Router v7                                          |
| UI Primitives | shadcn/ui (Radix UI + Tailwind CSS 4)                    |
| Forms         | React Hook Form + Zod v4 validation                      |
| Styling       | Tailwind CSS 4, tailwind-merge, class-variance-authority |
| Maps          | Leaflet / React Leaflet                                  |
| HTTP          | Axios                                                    |
| Testing       | Vitest                                                   |

## Getting Started

### Prerequisites

- Node.js 22+ (pinned via `.nvmrc` / `.tool-versions`; matches CI). The repo's `engines` field enforces `>=22`.
- Backend Azure Functions running locally (`func start` on port 7071) or accessible remotely

### Installation

```bash
cd web
npm install
```

### Environment

Copy `.env.example` to `.env`:

```bash
cp .env.example .env
```

| Variable              | Description                                                    | Default |
| :-------------------- | :------------------------------------------------------------- | :------ |
| `VITE_API_BASE_URL`   | Backend API base path                                          | `/api`  |
| `VITE_API_TOKEN`      | API auth token (backend function key)                          | —       |
| `VITE_USE_DEV_AUTH`   | Skip auth in dev (`true` auto-enabled in dev mode)             | `false` |
| `VITE_DEV_USER_NAME`  | Dev auth user display name (when `VITE_USE_DEV_AUTH=true`)     | —       |
| `VITE_DEV_USER_ROLES` | Comma-separated dev auth roles (when `VITE_USE_DEV_AUTH=true`) | `admin` |

The dev server proxies `/api` requests to `http://localhost:7071` (see `vite.config.ts`).

### Scripts

```bash
npm run dev        # Start dev server (port 5173)
npm run build      # Type-check + production build
npm run preview    # Preview production build locally
npm run test       # Run tests (Vitest)
npm run test:watch # Run tests in watch mode
npm run lint       # ESLint with auto-fix
npm run lint:tw    # Tailwind conformance gate (part of lint)
npm run perf       # Lighthouse over 12 routes (builds the mock bundle first)
npm run audit      # Deterministic UI crawl: targets × variants + axe + screenshots
npm run review     # Subjective UX findings keyed to Set-of-Mark numbers
npm run ui:inventory # Regenerate docs/UI-INVENTORY.md (verify.sh fails when stale)
npm run format     # Prettier format all files
```

## Architecture

```
src/
├── features/         # Feature-sliced modules (primary code location)
│   ├── onair/        # Workstation: coverage board (slot computation), queue rail, review pane
│   ├── jobs/         # Job model, review cockpit, provenance, status tracking, audio player
│   ├── schedules/    # Schedule model, cron math, edit/detail sheets
│   ├── stations/     # Station profile cards + trigger-now dialog
│   ├── notifications/# Notification panel + alarm engine (chime, desktop alerts, title countdown)
│   └── voices/       # Voice selection and calibration
├── components/       # Shared UI (NavigationBar, ErrorBoundary, data-table, etc.)
│   └── ui/           # shadcn/ui primitives (Button, Dialog, Select, etc.)
├── hooks/            # Cross-cutting hooks (useDocumentTitle, useIsMobile)
├── lib/              # Infrastructure (API client, schemas, utils, routes)
│   └── api/          # Axios client, query key factory, Zod schemas, mock adapter
├── pages/            # Route-level pages: OnAirPage, HistoryPage, StationsPage, NotFoundPage
```

**Feature modules** follow Feature-Sliced Design: each has `api/`, `components/`, `hooks/`, `types.ts`, and an `index.ts` barrel. Features must not import from another feature's internals.

### State Management

- **Server state**: TanStack Query — all API data fetching, caching, and mutations
- **Form state**: React Hook Form with Zod schema validation
- **No global client state store** — component-local state + query cache is sufficient

### Key Patterns

- **Optimistic updates** on approve/reject/publish actions
- **Polling** on job detail pages (5s interval until terminal state)
- **Zod schema validation** on all API responses (`lib/api/schemas/`)
- **Path alias**: `@/` maps to `src/` (configured in `tsconfig.json` and `vite.config.ts`)

## Deployment

Built artifacts are deployed to **Azure Static Web Apps**. The SWA proxies `/api/*` to the backend Azure Functions app. See `staticwebapp.config.json` for routing rules.

## Additional Documentation

Some one-off analysis documents may be generated during audits or agent-assisted reviews, but they are intentionally gitignored and are not treated as live repository documentation.
