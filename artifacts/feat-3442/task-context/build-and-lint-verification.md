### task: build-and-lint-verification

**Files:**
- No file changes — verification only

**Context:**

The project rules require `npm run build` and `npm run lint` to pass before a task is declared done. The TypeScript compile check in task 1 step 10 catches type errors early; this task runs the full build to catch any issues missed by `tsc --noEmit` alone (e.g. Vite/CRA transform errors) and the ESLint pass to catch any `as any` accidentally left behind. It also re-checks the two consumer components and their existing tests, since the arch review and design flagged `DashboardSettings.tsx` / `Dashboard.tsx` / `DashboardGrid.tsx` / `DashboardTile.tsx` as requiring no changes but this must be verified, not assumed.

- [ ] Step 1: Run the frontend build:
  ```
  cd frontend && npm run build
  ```
  Resolve any errors. Do not modify `frontend/src/api/generated/api-client.ts`. If a type error surfaces from consumer components (`DashboardSettings.tsx`, `Dashboard.tsx`, `DashboardGrid.tsx`, `DashboardTile.tsx`), the fix belongs in the mapping functions in `useDashboard.ts` (ensuring all required local-interface fields stay non-optional), not in the consumer files, per the design's Decision 1.

- [ ] Step 2: Run the linter:
  ```
  cd frontend && npm run lint
  ```
  Resolve any `@typescript-eslint/no-explicit-any` violations introduced by this change. Do not introduce new `as any` casts. The pre-existing `data?: any` field on `DashboardTile` (and the corresponding `dto.data` passthrough in `toDashboardTile`) is not an `as any` cast and predates this refactor — leave it unchanged.

- [ ] Step 3: Run the full existing test suites for the two consumer components to confirm no regressions from the widened `DashboardTile.size` type or the `Promise<FileResponse>` mutation return type:
  ```
  cd frontend && npx jest src/components/dashboard/__tests__/DashboardSettings.test.tsx src/components/dashboard/__tests__/DashboardTile.test.tsx src/components/dashboard/__tests__/DashboardGrid.test.tsx src/components/pages/__tests__/Dashboard.test.tsx --no-coverage
  ```
  Expected: all pass with no changes required to these test files. If any fail, diagnose whether the failure stems from the `size` widening (unlikely, since `DashboardTile.tsx`'s `getSizeClasses()` has a `default` case) or from a mutation return-type mismatch (unlikely, since neither caller consumes the mutation's resolved value) before making any test-file edits — do not silently adjust assertions to force a pass.

- [ ] Step 4: Run the full dashboard hook test file one more time alongside the two verification steps above to confirm the whole slice is green:
  ```
  cd frontend && npx jest src/api/hooks/__tests__/useDashboard.test.tsx --no-coverage
  ```
