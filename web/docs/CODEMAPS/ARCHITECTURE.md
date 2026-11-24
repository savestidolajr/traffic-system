# Frontend Architecture

## Data Flow

```
Browser ──► SWA EasyAuth or VITE_API_TOKEN ──► src/lib/auth.ts (centralized parseAuthPayload + shared useAuthData TanStack Query cache)
                                           │
                                           ▼
                                   src/hooks/use-current-user.ts
                                   (useCurrentUser / useRoles / useIsAdmin)

Browser ──► axios (apiClient) ──► /api/...
              │
              ├─ Interceptor: prod auth rides the SWA-injected X-MS-CLIENT-PRINCIPAL (no Bearer / no /.auth/me fetch); dev attaches optional VITE_API_TOKEN
              └─ Response interceptor wraps errors in ApiError (src/lib/api/client.ts)

              ▼
       src/lib/api/client.ts :: ApiError
              │
              ▼
       src/App.tsx — QueryClient retry(failureCount, error):
         • ApiError with 4xx status → false (no retry)
         • Otherwise → retry ≤ 3

Query invalidation → src/lib/api/use-mutation-factory.ts
  • Two modes: simple (invalidates on success) and optimistic (cancel/snapshot/rollback)
  • Domain-level mutation toasts (via toastAction per feature), not global mutation toasts
```

## Routing

```
/ ─────────────── OnAirPage (operator workstation: coverage board + queue rail + review pane)
/jobs/:jobId ──── OnAirPage (same workstation; the route param is the selected bulletin)
/history ──────── HistoryPage (audit log: filterable jobs data-table)
/stations ─────── StationsPage (station profile cards, trigger-now dialog, schedule sheets)
/activity ─────── ActivityPage (activity/change audit log: who changed what, when)
/jobs ─────────── redirect → /
/schedules ────── redirect → /stations
/trigger ──────── redirect → /stations
* ─────────────── NotFoundPage
```

The workstation (`features/onair/`) is a split surface: `CoverageBoard` (stations × upcoming
cron slots, slot-state machine in `features/onair/lib/slots.ts`), `QueueRail` (urgency-ordered
triage, j/k keyboard), and `ReviewPane` (decision bar + review cockpit; auto-advances to the
next waiting bulletin after approve/reject). `features/notifications/AlarmEffects` (mounted in
App) owns the chime, desktop notifications, and the title-bar countdown.

## Auth Flow

- `src/lib/auth.ts` — Parses SWA `/.auth/me` responses (both object and array formats). Provides `useAuthData()` backed by TanStack Query with 5-min stale time, `retry: false`.
- `src/hooks/use-current-user.ts` — Consumers `useAuthData()` and exposes `useCurrentUser()`, `useRoles()`, `useIsAdmin()`.
- `src/lib/api/client.ts` — No per-request auth fetch. The SWA linked backend injects `X-MS-CLIENT-PRINCIPAL` on every `/api/*` request (authenticated by the Functions `AuthorizationMiddleware`), so no Bearer token is attached in production. Dev attaches an optional `VITE_API_TOKEN`.

## Key Design Decisions

1. **ApiError wrapping** — All Axios HTTP errors become `ApiError` (status + message). QueryClient retry logic checks `error instanceof ApiError` to avoid retrying 4xx.
2. **No global mutation error toast** — Each feature owns its mutation success/error UX via `toastAction` (src/lib/toast-action.ts) or inline `toast.error()`.
3. **Library-free geocoding** — `use-geocoding.ts` uses `fetch` with `AbortController` (not axios) for Nominatim lookups, aborts stale/unmounted requests, and checks `response.ok`.
4. **Domain-specific pure functions** — Cron date math lives in schedule `lib/` as pure functions; actor-name fallback is a single `get-actor-name.ts` in jobs `lib/`.
