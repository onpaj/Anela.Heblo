### task: test-helpers

**Files:**
- Create: `frontend/src/pages/__tests__/stockOperationsUtils.test.ts`

- [ ] Write tests with fixed `now = new Date("2026-01-01T12:00:00Z")` and a helper `minutesAgo(n)`:
  - Submitted: 5 min ago -> false, 6 -> true; no submittedAt -> false; createdAt old but submittedAt recent -> false.
  - Pending: 10 -> false, 11 -> true; old submittedAt ignored.
  - Completed and Failed, 1000 min old -> false.
  - `canRetry`: Failed/Submitted/Pending true; Completed false; undefined false.
- [ ] Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/stockOperationsUtils.test.ts` -> all pass.
- [ ] Sanity (mutation check, do not commit): change threshold 5 to 6 temporarily and confirm a test fails, then revert.
- [ ] Commit: `test(frontend): cover stock operation stuck and retry helpers`

