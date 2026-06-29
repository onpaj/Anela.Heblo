### task: build-and-lint-verification

**Files:**
- No file changes — verification only

**Context:**

The project rules require `npm run build` and `npm run lint` to pass before a task is declared done. The TypeScript compile check in task 1 step 7 catches type errors early; this task runs the full build to catch any issues missed by `tsc --noEmit` alone (e.g. Vite/Rollup transform errors) and the ESLint pass to catch `as any` that may have been accidentally left behind.

- [ ] Step 1: Run the frontend build:
  ```
  cd /home/user/Anela.Heblo/frontend && npm run build
  ```
  Resolve any errors. Do not modify generated files. If a type narrowing error surfaces from `GetBankStatementListResponse.totalCount` being `number | undefined`, the fix is in `ImportTab.tsx` (add `?? 0`) — not in the generated client.

- [ ] Step 2: Run the linter:
  ```
  cd /home/user/Anela.Heblo/frontend && npm run lint
  ```
  Resolve any `@typescript-eslint/no-explicit-any` violations. If any `as any` patterns remain from the old fetch code, remove them. Do not introduce new `as any` casts.

- [ ] Step 3: Confirm the deleted interfaces are not re-introduced by the build step (OpenAPI client generation runs during build). The generated `api-client.ts` is regenerated from the backend OpenAPI spec — it will re-emit the generated types but will not re-create the hand-written interfaces that were in `useBankStatements.ts`. Verify by checking that `useBankStatements.ts` still imports from `'../generated/api-client'` after the build completes.
