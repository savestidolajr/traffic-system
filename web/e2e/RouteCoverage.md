# Frontend E2E Coverage

Playwright runs against `npm run dev:mock` and validates visible behavior, not backend schemas.

| Spec                     | Covered workflows                                                                                                                                    |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `navigation.spec.ts`     | Desktop/mobile route reachability, command palette, notifications, user menu                                                                         |
| `jobs-review.spec.ts`    | All fixture job states (`arn-job-00007`, `00008`, `00005`, `00009`, `00010`, `00013`, `00012`), approve, revise, adjust tab, terminal-state controls |
| `stations.spec.ts`       | Station list, create studio, edit/studio entry, manual trigger, delete/disable/archive control reachability                                          |
| `voices.spec.ts`         | Voice library, new variant studio, preview/save/version/restore/remix control reachability, v3 setting visibility                                    |
| `operators.spec.ts`      | Admin operator page and assign/update/delete control reachability                                                                                    |
| `guidance-rules.spec.ts` | Guidance, banned terms, priority-road page controls                                                                                                  |
| `library.spec.ts`        | Delivery style and pronunciation dictionary visible library pages                                                                                    |
| `history.spec.ts`        | History filters/search and historical job navigation                                                                                                 |
| `a11y-smoke.spec.ts`     | Top-level route smoke for landmarks/headings/buttons and critical console errors                                                                     |

Backend-only routes stay in `test/ArnBulletinGenerator.E2E.Tests/RouteCoverage.md`.
