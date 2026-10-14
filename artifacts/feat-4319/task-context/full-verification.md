### task: full-verification

**Files:**
- None (verification only, no new files)

- [ ] **Step 1: Run the frontend build**

Run: `cd frontend && npm run build`
Expected: build succeeds with no TypeScript errors.

- [ ] **Step 2: Run the frontend lint**

Run: `cd frontend && npm run lint`
Expected: no new lint errors introduced by the change in `useRecurringJobs.ts` or its test file.

- [ ] **Step 3: Run the full frontend test suite for the touched files**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useRecurringJobs.test.ts src/pages/__tests__/RecurringJobsPage.test.tsx src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx`
Expected: all tests pass — the two consumer test files (`RecurringJobsPage.test.tsx`, `ExpeditionJobControlsBar.test.tsx`) must still pass unmodified, confirming the invalidation change did not alter any consumer-visible behavior they assert on.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && npm test -- --watchAll=false`
Expected: PASS — no regressions anywhere else in the frontend suite.

- [ ] **Step 5: Commit (if verification uncovered any fix-up changes)**

```bash
git add -A
git commit -m "chore(background-jobs): verification pass for CRON detail-invalidation fix"
```

If verification required no code changes, skip this commit — Task 1's commit already covers the full fix.
