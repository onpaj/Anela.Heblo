Plan saved to `artifacts/feat-arch-review-expeditionlist-direct-depend/plan.r1.md`.

**Summary:**
- 10 sequential tasks, TDD-driven for the adapter (Task 2 RED → Task 3 GREEN), with the production refactor split into surgical migrations per file (Tasks 5–7).
- Task 9 includes a non-vacuous-rule verification step (deliberate violation must fail) to satisfy FR-6's "verifies the rule is active" acceptance criterion.
- Full acceptance cross-check table at the end maps every spec FR/NFR and every arch-review amendment to a specific task/step.
- Follows Option B from the arch review: adapter in `Features/Logistics/Infrastructure/`, DI in `LogisticsModule`, Logistics-namespaced types left untouched per spec Out-of-Scope; Shoptet binding unchanged.