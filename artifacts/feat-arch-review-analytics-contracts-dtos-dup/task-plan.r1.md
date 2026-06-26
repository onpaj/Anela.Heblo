Self-review against the spec:

- **FR-1 (unify ProductMarginSummary)** → Task 2 (Response edit deletes nested class, Handler edit collapses Select). ✓
- **FR-2 (unify CategoryMarginSummary)** → Task 2 (same commit, both pairs in one response file). ✓
- **FR-3 (unify MonthlyMarginBreakdown, no MarginPercentage)** → Task 3; out-of-scope reminder calls out the prohibition. ✓
- **FR-4 (regenerate clients + FE pass)** → Task 5. ✓
- **FR-5 (no behavioural change at API boundary)** → Task 5 Step 3 (JSON property names unchanged check), Task 6 Step 7 (optional JSON diff). ✓
- **NFR-4 (build & test gates)** → Tasks 1, 4, 6 (baseline + post-BE + final), Task 6 Step 2 (`dotnet format --verify-no-changes`). ✓
- **Arch-review amendments** (interface no-op, no test edits, no FE source edits) → reflected in "Files unchanged" section and verification commands. ✓
- **Placeholder scan** → no TBDs, no "add error handling", no "similar to Task N", every code block is concrete. ✓
- **Type consistency** → `ProductMarginSummaryDto`, `CategoryMarginSummaryDto`, `MonthlyMarginBreakdownDto` used consistently throughout. ✓

Plan saved to `docs/superpowers/plans/2026-06-04-consolidate-analytics-dtos.md`. Six tasks total: 1 baseline, 2 refactor tasks (Response + Handler per use case, one commit each), 1 BE gate, 1 FE regenerate + verify, 1 final verification. Three commits expected; zero new files; zero hand-written FE edits.