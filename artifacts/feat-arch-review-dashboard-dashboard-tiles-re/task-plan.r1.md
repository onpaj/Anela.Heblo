Plan saved to `docs/superpowers/plans/2026-06-02-decouple-dashboard-tile-drilldowns.md`.

**Summary** — 11 tasks, TDD throughout:

- **Task 1** — Add `DashboardTileDrillDown` DTO (plain class, not record).
- **Tasks 2–4** — Refactor each of the three backend tiles RED→GREEN. `DqtYesterdayStatusTile` first (template), then `FailedJobsTile` (keeps `tooltip`, uses anonymous projection because tooltip isn't on the DTO), then `DataQualityStatusTile` (with a new test file since none existed).
- **Task 5** — Audit other backend tiles for forbidden URL literals.
- **Task 6** — Add `drillDownRoutes.ts` registry + `resolveDrillDown()` helper with tests. External-strategy URLs are prefixed with `${apiUrl}` to preserve the cross-origin Hangfire UX flagged in the arch-review.
- **Tasks 7–9** — Update each frontend tile component to call the resolver instead of hardcoding paths. Includes unknown-key safety tests.
- **Task 10** — `memory/patterns/dashboard-tile-drilldown.md` documenting the two coexisting drill-down shapes.
- **Task 11** — Final cross-stack validation: backend build/format/tests, frontend build/lint/tests, literal sweep.

The plan honors all six arch-review amendments (OpenAPI claim removed, Hangfire cross-origin handled, correct frontend path, drift note, two-shape divergence, error-state drill-down preserved) and adds the `DataQualityStatusTile` test file the arch-review recommended.